using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;

namespace PixelFlow.Runtime.Levels
{
    internal enum LevelOutcomeDecision
    {
        None = 0,
        EnterBurst = 1,
        Win = 2,
        Lose = 3,
    }

    internal sealed class LevelOutcomeEvaluator
    {
        private const int BurstRemainingPigThreshold = 4;

        private readonly int[] remainingBlockCountsByColor = new int[(int)PigColor.White + 1];
        private readonly int[] remainingAmmoCountsByColor = new int[(int)PigColor.White + 1];

        public LevelOutcomeDecision Evaluate(
            int remainingTargetBlocks,
            bool isBurstActive,
            bool isHoldingContainerFilled,
            bool allowBurstEntry,
            IReadOnlyList<BlockVisual> spawnedBlocks,
            IReadOnlyList<PigController> spawnedPigs)
        {
            var hasPendingTargetResolution = HasPendingTargetResolution(spawnedBlocks);
            var hasPendingPigAction = HasPendingPigAction(spawnedPigs);
            var hasPigWithRemainingAmmo = HasPigWithRemainingAmmo(spawnedPigs);

            if (remainingTargetBlocks <= 0)
            {
                return hasPendingTargetResolution || hasPendingPigAction
                    ? LevelOutcomeDecision.None
                    : LevelOutcomeDecision.Win;
            }

            if (hasPendingTargetResolution)
            {
                return LevelOutcomeDecision.None;
            }

            var remainingPigCount = CountPigsWithRemainingAmmo(spawnedPigs);
            if (!isBurstActive
                && allowBurstEntry
                && remainingPigCount > 0
                && remainingPigCount <= BurstRemainingPigThreshold
                && IsGuaranteedFromCurrentState(remainingTargetBlocks, spawnedBlocks, spawnedPigs))
            {
                return LevelOutcomeDecision.EnterBurst;
            }

            if (isBurstActive)
            {
                return hasPendingPigAction || hasPigWithRemainingAmmo
                    ? LevelOutcomeDecision.None
                    : LevelOutcomeDecision.Lose;
            }

            return hasPendingPigAction || hasPigWithRemainingAmmo
                ? LevelOutcomeDecision.None
                : LevelOutcomeDecision.Lose;
        }

        internal static bool HasPendingTargetResolution(IReadOnlyList<BlockVisual> spawnedBlocks)
        {
            if (spawnedBlocks == null)
            {
                return false;
            }

            for (int i = 0; i < spawnedBlocks.Count; i++)
            {
                var block = spawnedBlocks[i];
                if (block == null
                    || !block.gameObject.activeInHierarchy
                    || block.Color == PigColor.None)
                {
                    continue;
                }

                if (block.IsDying || block.IsReserved)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasPendingPigAction(IReadOnlyList<PigController> spawnedPigs)
        {
            for (int i = 0; i < spawnedPigs.Count; i++)
            {
                var pig = spawnedPigs[i];
                if (pig == null)
                {
                    continue;
                }

                switch (pig.State)
                {
                    case PigState.DispatchingToBelt:
                    case PigState.FollowingSpline:
                    case PigState.OrbitingTarget:
                    case PigState.Firing:
                    case PigState.ReturningToWaiting:
                    case PigState.DespawningOnBelt:
                        return true;
                }
            }

            return false;
        }

        private static bool HasPigWithRemainingAmmo(IReadOnlyList<PigController> spawnedPigs)
        {
            for (int i = 0; i < spawnedPigs.Count; i++)
            {
                var pig = spawnedPigs[i];
                if (pig != null && pig.HasAmmo)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountPigsWithRemainingAmmo(IReadOnlyList<PigController> spawnedPigs)
        {
            if (spawnedPigs == null)
            {
                return 0;
            }

            var count = 0;
            for (int i = 0; i < spawnedPigs.Count; i++)
            {
                var pig = spawnedPigs[i];
                if (pig != null && pig.HasAmmo)
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsGuaranteedFromCurrentState(
            int remainingTargetBlocks,
            IReadOnlyList<BlockVisual> spawnedBlocks,
            IReadOnlyList<PigController> spawnedPigs)
        {
            if (remainingTargetBlocks <= 0)
            {
                return false;
            }

            if (!TryResolveExposureLayers(spawnedBlocks, out var exposureLayers) || exposureLayers.Count == 0)
            {
                return false;
            }

            Array.Clear(remainingBlockCountsByColor, 0, remainingBlockCountsByColor.Length);
            Array.Clear(remainingAmmoCountsByColor, 0, remainingAmmoCountsByColor.Length);

            for (int i = 0; i < spawnedPigs.Count; i++)
            {
                var pig = spawnedPigs[i];
                if (pig == null || !pig.HasAmmo || pig.Color == PigColor.None)
                {
                    continue;
                }

                remainingAmmoCountsByColor[(int)pig.Color] += pig.Ammo;
            }

            for (int layerIndex = 0; layerIndex < exposureLayers.Count; layerIndex++)
            {
                var layer = exposureLayers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                foreach (var pair in layer)
                {
                    if (pair.Key == PigColor.None || pair.Value <= 0)
                    {
                        continue;
                    }

                    var colorIndex = (int)pair.Key;
                    remainingBlockCountsByColor[colorIndex] += pair.Value;
                    if (remainingAmmoCountsByColor[colorIndex] < pair.Value)
                    {
                        return false;
                    }

                    remainingAmmoCountsByColor[colorIndex] -= pair.Value;
                }
            }

            return true;
        }

        private static bool TryResolveExposureLayers(
            IReadOnlyList<BlockVisual> spawnedBlocks,
            out List<Dictionary<PigColor, int>> layers)
        {
            layers = new List<Dictionary<PigColor, int>>();
            if (spawnedBlocks == null || spawnedBlocks.Count == 0)
            {
                return false;
            }

            var hasBounds = false;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;

            for (int i = 0; i < spawnedBlocks.Count; i++)
            {
                var block = spawnedBlocks[i];
                if (block == null
                    || !block.gameObject.activeInHierarchy
                    || block.IsDying)
                {
                    continue;
                }

                if (!block.HasRuntimeGridPosition)
                {
                    return false;
                }

                minX = Math.Min(minX, block.RuntimeGridX);
                minY = Math.Min(minY, block.RuntimeGridY);
                maxX = Math.Max(maxX, block.RuntimeGridX);
                maxY = Math.Max(maxY, block.RuntimeGridY);
                hasBounds = true;
            }

            if (!hasBounds)
            {
                return false;
            }

            var width = Math.Max(1, maxX - minX + 1);
            var height = Math.Max(1, maxY - minY + 1);
            var occupied = new bool[width, height];
            var targetColors = new PigColor[width, height];

            for (int i = 0; i < spawnedBlocks.Count; i++)
            {
                var block = spawnedBlocks[i];
                if (block == null
                    || !block.gameObject.activeInHierarchy
                    || block.IsDying
                    || !block.HasRuntimeGridPosition)
                {
                    continue;
                }

                var gridX = block.RuntimeGridX - minX;
                var gridY = block.RuntimeGridY - minY;
                if (gridX < 0 || gridX >= width || gridY < 0 || gridY >= height)
                {
                    return false;
                }

                occupied[gridX, gridY] = true;
                targetColors[gridX, gridY] = block.Color;
            }

            return TryResolveExposureLayers(occupied, targetColors, allowFallbackForRemainingTargets: false, out layers);
        }

        private static bool TryResolveExposureLayers(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool allowFallbackForRemainingTargets,
            out List<Dictionary<PigColor, int>> layers)
        {
            layers = new List<Dictionary<PigColor, int>>();
            if (occupied == null || targetColors == null)
            {
                return false;
            }

            var width = occupied.GetLength(0);
            var height = occupied.GetLength(1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            while (HasRemainingTargets(targetColors))
            {
                var exposedTargets = new bool[width, height];
                MarkExposedTargets(occupied, targetColors, exposedTargets, width, height);

                var layerCounts = CollectAndRemoveExposedTargets(occupied, targetColors, exposedTargets, width, height);
                if (layerCounts.Count == 0)
                {
                    if (!allowFallbackForRemainingTargets)
                    {
                        layers.Clear();
                        return false;
                    }

                    var fallbackCounts = CountRemainingTargets(targetColors, width, height);
                    if (fallbackCounts.Count > 0)
                    {
                        layers.Add(fallbackCounts);
                    }

                    break;
                }

                layers.Add(layerCounts);
            }

            return true;
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

                    if (!counts.TryGetValue(color, out var currentCount))
                    {
                        counts[color] = 1;
                    }
                    else
                    {
                        counts[color] = currentCount + 1;
                    }

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

                    if (!counts.TryGetValue(color, out var currentCount))
                    {
                        counts[color] = 1;
                    }
                    else
                    {
                        counts[color] = currentCount + 1;
                    }
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
    }
}
