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

        private readonly int[] remainingAmmoCountsByColor = new int[(int)PigColor.DarkBlue + 1];
        private readonly int[] exposureLayerCountsByColor = new int[(int)PigColor.DarkBlue + 1];
        private bool[] occupiedBuffer = Array.Empty<bool>();
        private PigColor[] targetColorsBuffer = Array.Empty<PigColor>();
        private bool[] exposedTargetsBuffer = Array.Empty<bool>();

        public LevelOutcomeDecision Evaluate(
            int remainingTargetBlocks,
            bool isBurstActive,
            bool isHoldingContainerFilled,
            bool allowBurstEntry,
            IReadOnlyList<BlockVisual> spawnedBlocks,
            IReadOnlyList<PigController> spawnedPigs,
            IReadOnlyList<PigQueueEntry> pendingQueueEntries)
        {
            var hasPendingTargetResolution = HasPendingTargetResolution(spawnedBlocks);
            var hasPendingPigAction = HasPendingPigAction(spawnedPigs);
            var hasPigWithRemainingAmmo = HasPigWithRemainingAmmo(spawnedPigs, pendingQueueEntries);

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

            var remainingPigCount = CountPigsWithRemainingAmmo(spawnedPigs, pendingQueueEntries);
            if (!isBurstActive
                && allowBurstEntry
                && remainingPigCount > 0
                && remainingPigCount <= BurstRemainingPigThreshold
                && IsGuaranteedFromCurrentState(remainingTargetBlocks, spawnedBlocks, spawnedPigs, pendingQueueEntries))
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
            if (spawnedPigs == null)
            {
                return false;
            }

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

        private static bool HasPigWithRemainingAmmo(
            IReadOnlyList<PigController> spawnedPigs,
            IReadOnlyList<PigQueueEntry> pendingQueueEntries)
        {
            if (spawnedPigs != null)
            {
                for (int i = 0; i < spawnedPigs.Count; i++)
                {
                    var pig = spawnedPigs[i];
                    if (pig != null && pig.HasAmmo)
                    {
                        return true;
                    }
                }
            }

            if (pendingQueueEntries == null)
            {
                return false;
            }

            for (int i = 0; i < pendingQueueEntries.Count; i++)
            {
                var entry = pendingQueueEntries[i];
                if (entry.Color != PigColor.None && entry.Ammo > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountPigsWithRemainingAmmo(
            IReadOnlyList<PigController> spawnedPigs,
            IReadOnlyList<PigQueueEntry> pendingQueueEntries)
        {
            var count = 0;
            if (spawnedPigs != null)
            {
                for (int i = 0; i < spawnedPigs.Count; i++)
                {
                    var pig = spawnedPigs[i];
                    if (pig != null && pig.HasAmmo)
                    {
                        count++;
                    }
                }
            }

            if (pendingQueueEntries != null)
            {
                for (int i = 0; i < pendingQueueEntries.Count; i++)
                {
                    if (pendingQueueEntries[i].Color != PigColor.None && pendingQueueEntries[i].Ammo > 0)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private bool IsGuaranteedFromCurrentState(
            int remainingTargetBlocks,
            IReadOnlyList<BlockVisual> spawnedBlocks,
            IReadOnlyList<PigController> spawnedPigs,
            IReadOnlyList<PigQueueEntry> pendingQueueEntries)
        {
            if (remainingTargetBlocks <= 0)
            {
                return false;
            }

            if (!TryBuildTargetGrid(spawnedBlocks, out var width, out var height, out var remainingGridTargets)
                || remainingGridTargets <= 0)
            {
                return false;
            }

            Array.Clear(remainingAmmoCountsByColor, 0, remainingAmmoCountsByColor.Length);

            if (spawnedPigs != null)
            {
                for (int i = 0; i < spawnedPigs.Count; i++)
                {
                    var pig = spawnedPigs[i];
                    if (pig == null || !pig.HasAmmo || pig.Color == PigColor.None)
                    {
                        continue;
                    }

                    remainingAmmoCountsByColor[(int)pig.Color] += pig.Ammo;
                }
            }

            if (pendingQueueEntries != null)
            {
                for (int i = 0; i < pendingQueueEntries.Count; i++)
                {
                    var entry = pendingQueueEntries[i];
                    if (entry.Color == PigColor.None || entry.Ammo <= 0)
                    {
                        continue;
                    }

                    remainingAmmoCountsByColor[(int)entry.Color] += entry.Ammo;
                }
            }

            while (remainingGridTargets > 0)
            {
                Array.Clear(exposedTargetsBuffer, 0, width * height);
                MarkExposedTargets(width, height);
                if (!TryConsumeExposedLayer(width, height, ref remainingGridTargets))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryBuildTargetGrid(
            IReadOnlyList<BlockVisual> spawnedBlocks,
            out int width,
            out int height,
            out int remainingTargetCount)
        {
            width = 0;
            height = 0;
            remainingTargetCount = 0;
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

            width = Math.Max(1, maxX - minX + 1);
            height = Math.Max(1, maxY - minY + 1);
            var cellCount = width * height;
            EnsureGridCapacity(cellCount);
            Array.Clear(occupiedBuffer, 0, cellCount);
            Array.Clear(targetColorsBuffer, 0, cellCount);

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

                var index = gridX + (gridY * width);
                occupiedBuffer[index] = true;

                var previousColor = targetColorsBuffer[index];
                if (previousColor == PigColor.None && block.Color != PigColor.None)
                {
                    remainingTargetCount++;
                }
                else if (previousColor != PigColor.None && block.Color == PigColor.None)
                {
                    remainingTargetCount--;
                }

                targetColorsBuffer[index] = block.Color;
            }

            return true;
        }

        private void EnsureGridCapacity(int cellCount)
        {
            if (occupiedBuffer.Length >= cellCount)
            {
                return;
            }

            occupiedBuffer = new bool[cellCount];
            targetColorsBuffer = new PigColor[cellCount];
            exposedTargetsBuffer = new bool[cellCount];
        }

        private void MarkExposedTargets(int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                MarkFirstOccupiedInRow(y, width, leftToRight: true);
                MarkFirstOccupiedInRow(y, width, leftToRight: false);
            }

            for (int x = 0; x < width; x++)
            {
                MarkFirstOccupiedInColumn(x, width, height, bottomToTop: true);
                MarkFirstOccupiedInColumn(x, width, height, bottomToTop: false);
            }
        }

        private void MarkFirstOccupiedInRow(int rowIndex, int width, bool leftToRight)
        {
            if (leftToRight)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = x + (rowIndex * width);
                    if (!occupiedBuffer[index])
                    {
                        continue;
                    }

                    if (targetColorsBuffer[index] != PigColor.None)
                    {
                        exposedTargetsBuffer[index] = true;
                    }

                    break;
                }

                return;
            }

            for (int x = width - 1; x >= 0; x--)
            {
                var index = x + (rowIndex * width);
                if (!occupiedBuffer[index])
                {
                    continue;
                }

                if (targetColorsBuffer[index] != PigColor.None)
                {
                    exposedTargetsBuffer[index] = true;
                }

                break;
            }
        }

        private void MarkFirstOccupiedInColumn(int columnIndex, int width, int height, bool bottomToTop)
        {
            if (bottomToTop)
            {
                for (int y = 0; y < height; y++)
                {
                    var index = columnIndex + (y * width);
                    if (!occupiedBuffer[index])
                    {
                        continue;
                    }

                    if (targetColorsBuffer[index] != PigColor.None)
                    {
                        exposedTargetsBuffer[index] = true;
                    }

                    break;
                }

                return;
            }

            for (int y = height - 1; y >= 0; y--)
            {
                var index = columnIndex + (y * width);
                if (!occupiedBuffer[index])
                {
                    continue;
                }

                if (targetColorsBuffer[index] != PigColor.None)
                {
                    exposedTargetsBuffer[index] = true;
                }

                break;
            }
        }

        private bool TryConsumeExposedLayer(int width, int height, ref int remainingTargetCount)
        {
            Array.Clear(exposureLayerCountsByColor, 0, exposureLayerCountsByColor.Length);
            var exposedTargetCount = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = x + (y * width);
                    if (!exposedTargetsBuffer[index])
                    {
                        continue;
                    }

                    var color = targetColorsBuffer[index];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    exposureLayerCountsByColor[(int)color]++;
                    targetColorsBuffer[index] = PigColor.None;
                    occupiedBuffer[index] = false;
                    remainingTargetCount--;
                    exposedTargetCount++;
                }
            }

            if (exposedTargetCount <= 0)
            {
                return false;
            }

            for (int colorIndex = 0; colorIndex < exposureLayerCountsByColor.Length; colorIndex++)
            {
                var count = exposureLayerCountsByColor[colorIndex];
                if (count <= 0)
                {
                    continue;
                }

                if (remainingAmmoCountsByColor[colorIndex] < count)
                {
                    return false;
                }

                remainingAmmoCountsByColor[colorIndex] -= count;
            }

            return true;
        }
    }
}
