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

            var layers = ResolveExposureLayers(displayGrid);
            AppendLayerBuckets(layers, settings, result);
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

            var layers = ResolveExposureLayers(placedObjects, database);
            AppendLayerBuckets(layers, settings, result);
            return result;
        }

        private static void AppendLayerBuckets(
            IReadOnlyList<Dictionary<PigColor, int>> layers,
            PigQueueGenerationSettings settings,
            List<PigQueueEntry> result)
        {
            if (layers == null || settings == null || result == null)
            {
                return;
            }

            var slotCursor = 0;
            var holdingSlotCount = Mathf.Max(1, settings.HoldingSlotCount);
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var buckets = BuildBuckets(layers[layerIndex], settings);
                if (buckets.Count == 0)
                {
                    continue;
                }

                var hasRemaining = true;
                while (hasRemaining)
                {
                    hasRemaining = false;
                    for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                    {
                        var bucket = buckets[bucketIndex];
                        if (bucket.AmmoQueue.Count == 0)
                        {
                            continue;
                        }

                        hasRemaining = true;
                        var ammo = bucket.AmmoQueue.Dequeue();
                        result.Add(new PigQueueEntry(bucket.Color, ammo, slotCursor % holdingSlotCount));
                        slotCursor++;
                    }
                }
            }
        }

        private static List<Dictionary<PigColor, int>> ResolveExposureLayers(PigColor[,] displayGrid)
        {
            var layers = new List<Dictionary<PigColor, int>>();
            var width = displayGrid.GetLength(0);
            var height = displayGrid.GetLength(1);
            if (width <= 0 || height <= 0)
            {
                return layers;
            }

            var occupied = new bool[width, height];
            var targetColors = new PigColor[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var color = displayGrid[x, y];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    occupied[x, y] = true;
                    targetColors[x, y] = color;
                }
            }

            return ResolveExposureLayers(occupied, targetColors);
        }

        private static List<Dictionary<PigColor, int>> ResolveExposureLayers(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database)
        {
            var layers = new List<Dictionary<PigColor, int>>();
            if (!TryBuildPlacedObjectGrid(placedObjects, database, out var occupied, out var targetColors))
            {
                return layers;
            }

            return ResolveExposureLayers(occupied, targetColors);
        }

        private static List<Dictionary<PigColor, int>> ResolveExposureLayers(bool[,] occupied, PigColor[,] targetColors)
        {
            var layers = new List<Dictionary<PigColor, int>>();
            if (occupied == null || targetColors == null)
            {
                return layers;
            }

            var width = occupied.GetLength(0);
            var height = occupied.GetLength(1);
            if (width <= 0 || height <= 0)
            {
                return layers;
            }

            while (HasRemainingTargets(targetColors))
            {
                var exposedTargets = new bool[width, height];
                MarkExposedTargets(occupied, targetColors, exposedTargets, width, height);

                var layerCounts = CollectAndRemoveExposedTargets(occupied, targetColors, exposedTargets, width, height);
                if (layerCounts.Count == 0)
                {
                    var fallbackCounts = CountRemainingTargets(targetColors, width, height);
                    if (fallbackCounts.Count > 0)
                    {
                        layers.Add(fallbackCounts);
                    }

                    break;
                }

                layers.Add(layerCounts);
            }

            return layers;
        }

        private static void MarkExposedTargets(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int width,
            int height)
        {
            for (int y = 0; y < height; y++)
            {
                MarkFirstOccupiedInRow(occupied, targetColors, exposedTargets, y, width, leftToRight: true);
                MarkFirstOccupiedInRow(occupied, targetColors, exposedTargets, y, width, leftToRight: false);
            }

            for (int x = 0; x < width; x++)
            {
                MarkFirstOccupiedInColumn(occupied, targetColors, exposedTargets, x, height, bottomToTop: true);
                MarkFirstOccupiedInColumn(occupied, targetColors, exposedTargets, x, height, bottomToTop: false);
            }
        }

        private static void MarkFirstOccupiedInRow(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int rowIndex,
            int width,
            bool leftToRight)
        {
            if (leftToRight)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!occupied[x, rowIndex])
                    {
                        continue;
                    }

                    if (targetColors[x, rowIndex] != PigColor.None)
                    {
                        exposedTargets[x, rowIndex] = true;
                    }

                    break;
                }

                return;
            }

            for (int x = width - 1; x >= 0; x--)
            {
                if (!occupied[x, rowIndex])
                {
                    continue;
                }

                if (targetColors[x, rowIndex] != PigColor.None)
                {
                    exposedTargets[x, rowIndex] = true;
                }

                break;
            }
        }

        private static void MarkFirstOccupiedInColumn(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int columnIndex,
            int height,
            bool bottomToTop)
        {
            if (bottomToTop)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!occupied[columnIndex, y])
                    {
                        continue;
                    }

                    if (targetColors[columnIndex, y] != PigColor.None)
                    {
                        exposedTargets[columnIndex, y] = true;
                    }

                    break;
                }

                return;
            }

            for (int y = height - 1; y >= 0; y--)
            {
                if (!occupied[columnIndex, y])
                {
                    continue;
                }

                if (targetColors[columnIndex, y] != PigColor.None)
                {
                    exposedTargets[columnIndex, y] = true;
                }

                break;
            }
        }

        private static Dictionary<PigColor, int> CollectAndRemoveExposedTargets(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int width,
            int height)
        {
            var counts = new Dictionary<PigColor, int>();

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!exposedTargets[x, y])
                    {
                        continue;
                    }

                    var color = targetColors[x, y];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    IncrementCount(counts, color);
                    targetColors[x, y] = PigColor.None;
                    occupied[x, y] = false;
                }
            }

            return counts;
        }

        private static Dictionary<PigColor, int> CountRemainingTargets(
            PigColor[,] targetColors,
            int width,
            int height)
        {
            var counts = new Dictionary<PigColor, int>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var color = targetColors[x, y];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    IncrementCount(counts, color);
                }
            }

            return counts;
        }

        private static bool HasRemainingTargets(PigColor[,] targetColors)
        {
            var width = targetColors.GetLength(0);
            var height = targetColors.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (targetColors[x, y] != PigColor.None)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryBuildPlacedObjectGrid(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database,
            out bool[,] occupied,
            out PigColor[,] targetColors)
        {
            occupied = null;
            targetColors = null;
            if (placedObjects == null || database == null || placedObjects.Count == 0)
            {
                return false;
            }

            var hasBounds = false;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;

            for (int i = 0; i < placedObjects.Count; i++)
            {
                var placedObject = placedObjects[i];
                var definition = database.FindPlaceable(placedObject);
                if (definition == null)
                {
                    continue;
                }

                var size = definition.GridSize;
                minX = Mathf.Min(minX, placedObject.Origin.x);
                minY = Mathf.Min(minY, placedObject.Origin.y);
                maxX = Mathf.Max(maxX, placedObject.Origin.x + Mathf.Max(1, size.x) - 1);
                maxY = Mathf.Max(maxY, placedObject.Origin.y + Mathf.Max(1, size.y) - 1);
                hasBounds = true;
            }

            if (!hasBounds)
            {
                return false;
            }

            var width = Mathf.Max(1, maxX - minX + 1);
            var height = Mathf.Max(1, maxY - minY + 1);
            occupied = new bool[width, height];
            targetColors = new PigColor[width, height];

            for (int i = 0; i < placedObjects.Count; i++)
            {
                var placedObject = placedObjects[i];
                var definition = database.FindPlaceable(placedObject);
                if (definition == null)
                {
                    continue;
                }

                var size = definition.GridSize;
                for (int offsetX = 0; offsetX < size.x; offsetX++)
                {
                    for (int offsetY = 0; offsetY < size.y; offsetY++)
                    {
                        var gridX = (placedObject.Origin.x + offsetX) - minX;
                        var gridY = (placedObject.Origin.y + offsetY) - minY;
                        if (gridX < 0 || gridX >= width || gridY < 0 || gridY >= height)
                        {
                            continue;
                        }

                        occupied[gridX, gridY] = true;
                        targetColors[gridX, gridY] = definition.Kind == PlaceableKind.Block
                            ? definition.Color
                            : PigColor.None;
                    }
                }
            }

            return true;
        }

        private static void IncrementCount(Dictionary<PigColor, int> counts, PigColor color)
        {
            if (color == PigColor.None || counts == null)
            {
                return;
            }

            if (!counts.TryGetValue(color, out var current))
            {
                counts[color] = 1;
                return;
            }

            counts[color] = current + 1;
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

        private static int SnapUpToStep(int value, int step) => AmmoStepUtility.SnapUpToStep(value, step);

        private static int SnapToNearestStep(int value, int step) => AmmoStepUtility.SnapToNearestStep(value, step);
    }
}
