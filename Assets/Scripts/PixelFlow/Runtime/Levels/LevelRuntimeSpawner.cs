using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Levels
{
    internal sealed class LevelRuntimeSpawner
    {
        private const float QueuedPigVerticalOffset = 0.6f;

        private readonly IGameFactory gameFactory;
        private readonly LevelDatabase levelDatabase;
        private readonly EnvironmentContext environment;

        public LevelRuntimeSpawner(
            IGameFactory gameFactory,
            LevelDatabase levelDatabase,
            EnvironmentContext environment)
        {
            this.gameFactory = gameFactory;
            this.levelDatabase = levelDatabase;
            this.environment = environment;
        }

        public LevelRuntimeSpawnResult SpawnLevel(LevelData level, string boardRootName, string deckRootName)
        {
            var queueEntries = ResolveQueueEntries(level);
            var holdingSlotCount = ApplyHoldingSlotCount(level, queueEntries);
            EnsureRuntimeRoots(boardRootName, deckRootName, out var boardRoot, out var deckRoot);

            var spawnedBlocks = new List<BlockVisual>();
            var targetBlocks = new List<BlockVisual>();
            var spawnedPigs = new List<PigController>();

            SpawnBoard(level, boardRoot, spawnedBlocks, targetBlocks);
            var waitingLanes = SpawnDeck(level, deckRoot, holdingSlotCount, queueEntries, spawnedPigs);

            return new LevelRuntimeSpawnResult(
                boardRoot,
                deckRoot,
                spawnedBlocks,
                targetBlocks,
                spawnedPigs,
                waitingLanes);
        }

        private int ApplyHoldingSlotCount(LevelData level, IReadOnlyList<PigQueueEntry> queueEntries)
        {
            if (environment == null)
            {
                return 0;
            }

            var desiredCount = 1;
            if (level?.PigQueueGenerationSettings != null)
            {
                desiredCount = Mathf.Max(1, level.PigQueueGenerationSettings.HoldingSlotCount);
            }
            else if (level?.PigQueue != null && level.PigQueue.Count > 0)
            {
                for (int i = 0; i < level.PigQueue.Count; i++)
                {
                    desiredCount = Mathf.Max(desiredCount, level.PigQueue[i].SlotIndex + 1);
                }
            }

            if (queueEntries != null && queueEntries.Count > 0)
            {
                var requiredSlotCount = 1;
                for (int i = 0; i < queueEntries.Count; i++)
                {
                    requiredSlotCount = Mathf.Max(requiredSlotCount, queueEntries[i].SlotIndex + 1);
                }

                desiredCount = Mathf.Max(requiredSlotCount, desiredCount);
            }

            return environment.ApplyHoldingContainerCount(
                desiredCount,
                minCount: 1,
                maxCount: environment.HoldingContainerCapacity);
        }

        private void EnsureRuntimeRoots(
            string boardRootName,
            string deckRootName,
            out Transform boardRoot,
            out Transform deckRoot)
        {
            boardRoot = EnsureChildRoot(
                environment != null && environment.BlockContainer != null ? environment.BlockContainer : environment != null ? environment.transform : null,
                boardRootName);
            deckRoot = EnsureChildRoot(
                environment != null && environment.DeckContainer != null ? environment.DeckContainer : environment != null ? environment.transform : null,
                deckRootName);

            if (boardRoot != null)
            {
                boardRoot.localPosition = Vector3.zero;
                boardRoot.localRotation = Quaternion.identity;
                boardRoot.localScale = Vector3.one;
            }

            if (deckRoot != null)
            {
                deckRoot.localPosition = Vector3.zero;
                deckRoot.localRotation = Quaternion.identity;
                deckRoot.localScale = Vector3.one;
            }
        }

        private static Transform EnsureChildRoot(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            var existing = parent.Find(childName);
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void SpawnBoard(
            LevelData level,
            Transform boardRoot,
            ICollection<BlockVisual> spawnedBlocks,
            ICollection<BlockVisual> targetBlocks)
        {
            if (level == null || environment == null || boardRoot == null || levelDatabase == null || gameFactory == null)
            {
                return;
            }

            var placedObjects = level.PlacedObjects;
            if (placedObjects == null || placedObjects.Count == 0)
            {
                return;
            }

            var resolvedBlockData = level.BlockData != null ? level.BlockData : environment.DefaultBlockData;
            var cellSpacing = resolvedBlockData != null ? resolvedBlockData.CellSpacing : level.ImportSettings.CellSpacing;
            var verticalOffset = resolvedBlockData != null ? resolvedBlockData.VerticalOffset : level.ImportSettings.VerticalOffset;
            var blockScaleOverride = resolvedBlockData == null ? level.ImportSettings.BlockLocalScale : (Vector3?)null;
            var layout = BoardLayoutUtility.ResolvePlacedObjectLayout(
                environment,
                boardRoot,
                placedObjects,
                levelDatabase,
                level.GridSize,
                cellSpacing,
                preserveFullGridBounds: true,
                boardFill: level.ImportSettings.BoardFill);
            boardRoot.localScale = Vector3.one * layout.RootScale;

            var width = Mathf.Max(1, level.GridSize.x);
            var height = Mathf.Max(1, level.GridSize.y);

            for (int placementIndex = 0; placementIndex < placedObjects.Count; placementIndex++)
            {
                var placedObject = placedObjects[placementIndex];
                var definition = levelDatabase.FindPlaceable(placedObject);
                if (definition == null)
                {
                    continue;
                }

                var size = definition.GridSize;
                var blockColor = definition.Kind == PlaceableKind.Block ? definition.Color : PigColor.None;
                var countsAsTarget = definition.Kind == PlaceableKind.Block && definition.Color != PigColor.None;

                for (int offsetX = 0; offsetX < size.x; offsetX++)
                {
                    for (int offsetY = 0; offsetY < size.y; offsetY++)
                    {
                        var gridX = placedObject.Origin.x + offsetX;
                        var gridY = placedObject.Origin.y + offsetY;
                        if (gridX < 0 || gridX >= width || gridY < 0 || gridY >= height)
                        {
                            continue;
                        }

                        var block = gameFactory.CreateBlock(new BlockSpawnRequest(
                            blockColor,
                            ResolveBlockToneIndex(placedObject, blockColor),
                            new VisualSpawnPlacement(
                                parent: boardRoot,
                                position: new Vector3(
                                    layout.GetLocalX(gridX),
                                    verticalOffset,
                                    layout.GetLocalZFromBottom(gridY)),
                                rotation: Quaternion.identity,
                                localScale: blockScaleOverride)));
                        if (block == null)
                        {
                            continue;
                        }

                        block.SetRuntimeGridPosition(gridX, gridY);
                        block.name = $"Block_{gridX}_{gridY}_{definition.DisplayName}";
                        spawnedBlocks.Add(block);
                        if (countsAsTarget)
                        {
                            targetBlocks.Add(block);
                        }
                    }
                }
            }
        }

        private List<PigController>[] SpawnDeck(
            LevelData level,
            Transform deckRoot,
            int holdingSlotCount,
            IReadOnlyList<PigQueueEntry> queueEntries,
            ICollection<PigController> spawnedPigs)
        {
            if (level == null
                || environment == null
                || deckRoot == null
                || holdingSlotCount <= 0
                || queueEntries == null
                || queueEntries.Count == 0
                || gameFactory == null)
            {
                return Array.Empty<List<PigController>>();
            }

            var waitingLanes = new List<PigController>[holdingSlotCount];
            var laneEntryIndices = new List<int>[holdingSlotCount];
            for (int i = 0; i < holdingSlotCount; i++)
            {
                waitingLanes[i] = new List<PigController>();
                laneEntryIndices[i] = new List<int>();
            }

            for (int i = 0; i < queueEntries.Count; i++)
            {
                var entry = queueEntries[i];
                var slotIndex = Mathf.Clamp(entry.SlotIndex, 0, holdingSlotCount - 1);
                laneEntryIndices[slotIndex].Add(i);
            }

            var lanePositions = ResolveDeckLanePositions(deckRoot, holdingSlotCount, out var laneSpacing);
            var depthSpacing = Mathf.Max(0.9f, laneSpacing);
            var depthDirection = ResolveDeckDepthDirection(deckRoot);

            for (int laneIndex = 0; laneIndex < holdingSlotCount; laneIndex++)
            {
                var holdingSlot = environment.GetHoldingSlot(laneIndex, activeOnly: true)
                    ?? environment.GetHoldingSlot(laneIndex, activeOnly: false);
                for (int depthIndex = 0; depthIndex < laneEntryIndices[laneIndex].Count; depthIndex++)
                {
                    var queueIndex = laneEntryIndices[laneIndex][depthIndex];
                    var entry = queueEntries[queueIndex];
                    var pig = gameFactory.CreatePig(new PigSpawnRequest(
                        entry.Color,
                        entry.Ammo,
                        entry.Direction,
                        new VisualSpawnPlacement(
                            parent: deckRoot,
                            position: new Vector3(
                                lanePositions[laneIndex],
                                0f,
                                depthIndex * depthSpacing * depthDirection),
                            rotation: Quaternion.identity)));
                    if (pig == null)
                    {
                        continue;
                    }

                    pig.name = $"Pig_{queueIndex + 1}_{entry.Color}_{entry.Ammo}";
                    pig.SetQueued(false, snapImmediately: false);
                    pig.SetTrayVisible(false);
                    if (holdingSlot != null)
                    {
                        pig.AssignWaitingAnchor(
                            holdingSlot,
                            snapImmediately: true,
                            ResolveQueuedPigWorldOffset(holdingSlot, depthIndex, depthSpacing, depthDirection));
                        pig.SetQueued(true, snapImmediately: true);
                    }
                    else
                    {
                        pig.ClearWaitingAnchor();
                    }

                    spawnedPigs.Add(pig);
                    waitingLanes[laneIndex].Add(pig);
                }
            }

            return waitingLanes;
        }

        private List<PigQueueEntry> ResolveQueueEntries(LevelData level)
        {
            if (level == null || levelDatabase == null)
            {
                return new List<PigQueueEntry>();
            }

            var generatedQueueEntries = PigQueueGenerator.Generate(
                level.PlacedObjects,
                levelDatabase,
                level.PigQueueGenerationSettings);
            if (generatedQueueEntries.Count > 0)
            {
                return generatedQueueEntries;
            }

            return level.PigQueue != null
                ? new List<PigQueueEntry>(level.PigQueue)
                : new List<PigQueueEntry>();
        }

        private float[] ResolveDeckLanePositions(Transform deckRoot, int holdingSlotCount, out float laneSpacing)
        {
            var resolvedPositions = new float[holdingSlotCount];
            var validLaneCount = 0;
            var previousPosition = 0f;
            var totalSpacing = 0f;
            var spacingSamples = 0;

            for (int laneIndex = 0; laneIndex < holdingSlotCount; laneIndex++)
            {
                var slot = environment.GetHoldingSlot(laneIndex, activeOnly: true)
                    ?? environment.GetHoldingSlot(laneIndex, activeOnly: false);
                if (slot == null)
                {
                    resolvedPositions[laneIndex] = float.NaN;
                    continue;
                }

                var localPosition = deckRoot.InverseTransformPoint(slot.position);
                resolvedPositions[laneIndex] = localPosition.x;
                if (validLaneCount > 0)
                {
                    totalSpacing += Mathf.Abs(localPosition.x - previousPosition);
                    spacingSamples++;
                }

                previousPosition = localPosition.x;
                validLaneCount++;
            }

            laneSpacing = spacingSamples > 0
                ? Mathf.Max(0.6f, totalSpacing / spacingSamples)
                : 1.1f;

            if (validLaneCount == holdingSlotCount)
            {
                return resolvedPositions;
            }

            var halfWidth = (holdingSlotCount - 1) * 0.5f;
            for (int laneIndex = 0; laneIndex < holdingSlotCount; laneIndex++)
            {
                if (float.IsNaN(resolvedPositions[laneIndex]))
                {
                    resolvedPositions[laneIndex] = (laneIndex - halfWidth) * laneSpacing;
                }
            }

            return resolvedPositions;
        }

        private float ResolveDeckDepthDirection(Transform deckRoot)
        {
            if (deckRoot == null || environment?.TrayEquipPos == null)
            {
                return -1f;
            }

            var trayLocalPosition = deckRoot.InverseTransformPoint(environment.TrayEquipPos.position);
            return trayLocalPosition.z >= 0f ? -1f : 1f;
        }

        private Vector3 ResolveQueuedPigWorldOffset(
            Transform holdingSlot,
            int depthIndex,
            float depthSpacing,
            float depthDirection)
        {
            var deckContainer = environment != null ? environment.DeckContainer : null;
            if (holdingSlot == null || deckContainer == null)
            {
                return Vector3.up * QueuedPigVerticalOffset;
            }

            var slotPositionInDeckSpace = deckContainer.InverseTransformPoint(holdingSlot.position);
            var queuedWorldPosition = deckContainer.TransformPoint(new Vector3(
                slotPositionInDeckSpace.x,
                0f,
                depthIndex * depthSpacing * depthDirection));
            queuedWorldPosition.y += QueuedPigVerticalOffset;
            return queuedWorldPosition - holdingSlot.position;
        }

        private static int ResolveBlockToneIndex(PlacedObjectData placedObject, PigColor blockColor)
        {
            return placedObject.HasVisualToneOverride
                ? placedObject.VisualToneIndex
                : PigColorAtlasUtility.ResolveDefaultToneIndex(blockColor);
        }
    }
}
