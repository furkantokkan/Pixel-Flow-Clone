using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    public static class PigQueueGenerator
    {
        private sealed class ColorQueueBucket
        {
            public PigColor Color;
            public int BlockCount;
            public readonly Queue<int> AmmoQueue = new();
        }

        private readonly struct AmmoDistributionRules
        {
            public readonly int MinAmmoPerPig;
            public readonly int TargetAmmoPerPig;
            public readonly int MaxAmmoPerPig;
            public readonly int AmmoStep;
            public readonly int MinPigsPerColor;
            public readonly int MaxPigsPerColor;

            public AmmoDistributionRules(PigQueueGenerationSettings settings)
            {
                var normalizedSettings = settings?.Clone() ?? new PigQueueGenerationSettings();
                normalizedSettings.NormalizeAmmoDistributionSettings();

                MinAmmoPerPig = normalizedSettings.MinAmmoPerPig;
                MaxAmmoPerPig = normalizedSettings.MaxAmmoPerPig;
                AmmoStep = PigQueueGenerationSettings.ClampAmmoStep(normalizedSettings.AmmoStep);
                TargetAmmoPerPig = SnapToNearestStep(
                    Mathf.RoundToInt((MinAmmoPerPig + MaxAmmoPerPig) * 0.5f),
                    AmmoStep);
                TargetAmmoPerPig = Mathf.Clamp(TargetAmmoPerPig, MinAmmoPerPig, MaxAmmoPerPig);
                MinPigsPerColor = Mathf.Max(1, normalizedSettings.MinPigsPerColor);
                MaxPigsPerColor = Mathf.Max(MinPigsPerColor, normalizedSettings.MaxPigsPerColor);
            }
        }

        private readonly struct AmmoDistributionPlan
        {
            public readonly int PigCount;
            public readonly int BaseUnits;
            public readonly int RemainderUnits;
            public readonly int ConstraintPenalty;
            public readonly float TargetPenalty;
            public readonly int PigCountRangePenalty;

            public AmmoDistributionPlan(
                int pigCount,
                int baseUnits,
                int remainderUnits,
                int constraintPenalty,
                float targetPenalty,
                int pigCountRangePenalty)
            {
                PigCount = pigCount;
                BaseUnits = baseUnits;
                RemainderUnits = remainderUnits;
                ConstraintPenalty = constraintPenalty;
                TargetPenalty = targetPenalty;
                PigCountRangePenalty = pigCountRangePenalty;
            }
        }

        public static List<PigQueueEntry> Generate(PigColor[,] displayGrid, PigQueueGenerationSettings settings)
        {
            var result = new List<PigQueueEntry>();
            if (displayGrid == null || settings == null)
            {
                return result;
            }

            var blockCounts = CountBlocksByColor(displayGrid);
            var buckets = BuildBuckets(blockCounts, settings);

            if (buckets.Count == 0)
            {
                return result;
            }

            var slotCursor = 0;
            var hasRemaining = true;

            while (hasRemaining)
            {
                hasRemaining = false;

                for (int i = 0; i < buckets.Count; i++)
                {
                    var bucket = buckets[i];
                    if (bucket.AmmoQueue.Count == 0)
                    {
                        continue;
                    }

                    hasRemaining = true;
                    var ammo = bucket.AmmoQueue.Dequeue();
                    result.Add(new PigQueueEntry(bucket.Color, ammo, slotCursor % settings.HoldingSlotCount));
                    slotCursor++;
                }
            }

            return result;
        }

        public static List<PigQueueEntry> Generate(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database,
            PigQueueGenerationSettings settings)
        {
            var result = new List<PigQueueEntry>();
            if (placedObjects == null || database == null || settings == null)
            {
                return result;
            }

            var blockCounts = CountBlocksByColor(placedObjects, database);
            var buckets = BuildBuckets(blockCounts, settings);
            if (buckets.Count == 0)
            {
                return result;
            }

            var slotCursor = 0;
            var hasRemaining = true;
            while (hasRemaining)
            {
                hasRemaining = false;

                for (int i = 0; i < buckets.Count; i++)
                {
                    var bucket = buckets[i];
                    if (bucket.AmmoQueue.Count == 0)
                    {
                        continue;
                    }

                    hasRemaining = true;
                    var ammo = bucket.AmmoQueue.Dequeue();
                    result.Add(new PigQueueEntry(bucket.Color, ammo, slotCursor % settings.HoldingSlotCount));
                    slotCursor++;
                }
            }

            return result;
        }

        private static Dictionary<PigColor, int> CountBlocksByColor(PigColor[,] displayGrid)
        {
            var counts = new Dictionary<PigColor, int>();
            var width = displayGrid.GetLength(0);
            var height = displayGrid.GetLength(1);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var color = displayGrid[x, y];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    if (!counts.TryGetValue(color, out var current))
                    {
                        counts[color] = 1;
                        continue;
                    }

                    counts[color] = current + 1;
                }
            }

            return counts;
        }

        private static Dictionary<PigColor, int> CountBlocksByColor(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database)
        {
            var counts = new Dictionary<PigColor, int>();

            for (int i = 0; i < placedObjects.Count; i++)
            {
                var placedObject = placedObjects[i];
                var definition = database.FindPlaceable(placedObject);
                if (definition == null
                    || definition.Kind != PlaceableKind.Pig
                    || definition.Color == PigColor.None)
                {
                    continue;
                }

                var occupiedCellCount = Mathf.Max(1, definition.GridSize.x * definition.GridSize.y);
                if (!counts.TryGetValue(definition.Color, out var current))
                {
                    counts[definition.Color] = occupiedCellCount;
                    continue;
                }

                counts[definition.Color] = current + occupiedCellCount;
            }

            return counts;
        }

        private static List<ColorQueueBucket> BuildBuckets(Dictionary<PigColor, int> blockCounts, PigQueueGenerationSettings settings)
        {
            var buckets = new List<ColorQueueBucket>();
            if (blockCounts == null || blockCounts.Count == 0)
            {
                return buckets;
            }

            var ammoRules = new AmmoDistributionRules(settings);

            foreach (var pair in blockCounts)
            {
                var color = pair.Key;
                var blockCount = pair.Value;
                if (color == PigColor.None || blockCount <= 0)
                {
                    continue;
                }

                // Auto generation now uses the required block count directly and only snaps
                // upward when needed so each pig ammo value can stay on the configured step.
                var totalAmmo = SnapUpToStep(blockCount, ammoRules.AmmoStep);

                var bucket = new ColorQueueBucket
                {
                    Color = color,
                    BlockCount = blockCount,
                };

                var distributionPlan = BuildDistributionPlan(totalAmmo, ammoRules);
                for (int i = 0; i < distributionPlan.PigCount; i++)
                {
                    var ammoUnits = distributionPlan.BaseUnits + (i < distributionPlan.RemainderUnits ? 1 : 0);
                    bucket.AmmoQueue.Enqueue(ammoUnits * ammoRules.AmmoStep);
                }

                buckets.Add(bucket);
            }

            buckets.Sort(CompareBuckets);
            return buckets;
        }

        private static int CompareBuckets(ColorQueueBucket a, ColorQueueBucket b)
        {
            var blockCompare = b.BlockCount.CompareTo(a.BlockCount);
            if (blockCompare != 0)
            {
                return blockCompare;
            }

            return a.Color.CompareTo(b.Color);
        }

        private static AmmoDistributionPlan BuildDistributionPlan(int totalAmmo, AmmoDistributionRules rules)
        {
            var totalUnits = Mathf.Max(1, Mathf.CeilToInt(totalAmmo / (float)rules.AmmoStep));
            var minUnits = Mathf.Max(1, Mathf.CeilToInt(rules.MinAmmoPerPig / (float)rules.AmmoStep));
            var maxUnits = Mathf.Max(minUnits, Mathf.CeilToInt(rules.MaxAmmoPerPig / (float)rules.AmmoStep));
            var configuredMinimumPigCount = Mathf.Max(1, rules.MinPigsPerColor);
            var configuredMaximumPigCount = Mathf.Max(configuredMinimumPigCount, rules.MaxPigsPerColor);
            var minimumPigCount = Mathf.Max(1, Mathf.CeilToInt(totalUnits / (float)maxUnits));
            var maximumPigCount = Mathf.Max(minimumPigCount, Mathf.FloorToInt(totalUnits / (float)minUnits));

            var bestPlan = default(AmmoDistributionPlan);
            var hasPlan = false;

            for (int pigCount = minimumPigCount; pigCount <= maximumPigCount; pigCount++)
            {
                var baseUnits = totalUnits / pigCount;
                var remainderUnits = totalUnits % pigCount;
                var smallestAmmo = baseUnits * rules.AmmoStep;
                var largestAmmo = (baseUnits + (remainderUnits > 0 ? 1 : 0)) * rules.AmmoStep;
                var constraintPenalty = Mathf.Max(0, rules.MinAmmoPerPig - smallestAmmo)
                    + Mathf.Max(0, largestAmmo - rules.MaxAmmoPerPig);
                var averageAmmo = totalUnits * rules.AmmoStep / (float)pigCount;
                var targetPenalty = Mathf.Abs(averageAmmo - rules.TargetAmmoPerPig);
                var pigCountRangePenalty = Mathf.Max(0, configuredMinimumPigCount - pigCount)
                    + Mathf.Max(0, pigCount - configuredMaximumPigCount);
                var candidate = new AmmoDistributionPlan(
                    pigCount,
                    baseUnits,
                    remainderUnits,
                    constraintPenalty,
                    targetPenalty,
                    pigCountRangePenalty);

                if (!hasPlan || IsBetterPlan(candidate, bestPlan))
                {
                    bestPlan = candidate;
                    hasPlan = true;
                }
            }

            return hasPlan ? bestPlan : new AmmoDistributionPlan(1, totalUnits, 0, 0, 0f, 0);
        }

        private static bool IsBetterPlan(AmmoDistributionPlan candidate, AmmoDistributionPlan best)
        {
            if (candidate.ConstraintPenalty != best.ConstraintPenalty)
            {
                return candidate.ConstraintPenalty < best.ConstraintPenalty;
            }

            if (candidate.PigCountRangePenalty != best.PigCountRangePenalty)
            {
                return candidate.PigCountRangePenalty < best.PigCountRangePenalty;
            }

            if (!Mathf.Approximately(candidate.TargetPenalty, best.TargetPenalty))
            {
                return candidate.TargetPenalty < best.TargetPenalty;
            }

            if (candidate.RemainderUnits != best.RemainderUnits)
            {
                return candidate.RemainderUnits < best.RemainderUnits;
            }

            return candidate.PigCount < best.PigCount;
        }

        private static int SnapUpToStep(int value, int step)
        {
            var positiveStep = Mathf.Max(1, step);
            var positiveValue = Mathf.Max(1, value);
            return ((positiveValue + positiveStep - 1) / positiveStep) * positiveStep;
        }

        private static int SnapToNearestStep(int value, int step)
        {
            var positiveStep = Mathf.Max(1, step);
            var positiveValue = Mathf.Max(1, value);
            var lower = Mathf.Max(positiveStep, (positiveValue / positiveStep) * positiveStep);
            var upper = SnapUpToStep(positiveValue, positiveStep);
            return positiveValue - lower <= upper - positiveValue ? lower : upper;
        }
    }
}
