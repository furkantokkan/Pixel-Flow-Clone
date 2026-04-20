using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Managers;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Pooling;
using PixelFlow.Runtime.Visuals;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;

namespace PixelFlow.Runtime.Levels
{
    public enum LevelRunState
    {
        None = 0,
        Playing = 1,
        Won = 2,
        Lost = 3,
    }

    [DisallowMultipleComponent]
    public sealed class LevelSessionController : MonoBehaviour
    {
        private const string CurrentLevelPrefsKey = "pixelflow.level.current_index";
        private const int DefaultLevelIndex = 0;
        private const string RuntimeBoardRootName = "__RuntimeBoardRoot";
        private const string RuntimeDeckRootName = "__RuntimeDeckRoot";

        [Header("Loading")]
        [SerializeField, FormerlySerializedAs("autoLoadFirstLevelOnStart")] private bool autoLoad = true;
        [SerializeField] private bool canSave = true;
        [SerializeField] private bool wrapLevelIndex = true;

        private readonly List<BlockVisual> spawnedBlocks = new();
        private readonly List<PigController> spawnedPigs = new();

        private GameSceneContext sceneContext;
        private GameManager gameManager;
        private IGameFactory gameFactory;
        private IVisualPoolService visualPoolService;
        private LevelDatabase levelDatabase;
        private Transform boardRoot;
        private Transform deckRoot;
        private bool hasLoadedInitialLevel;
        private bool isTransitioning;

        public int CurrentLevelIndex { get; private set; } = -1;
        public int DisplayLevelIndex => ResolveDisplayLevelIndex();
        public LevelRunState CurrentRunState { get; private set; }
        public int RemainingTargetBlocks { get; private set; }
        public int LevelCount => levelDatabase?.Levels?.Count ?? 0;
        public bool HasLoadedInitialLevel => hasLoadedInitialLevel;
        public bool AcceptsInput => CurrentRunState == LevelRunState.Playing && !isTransitioning;
        public event Action<int> LevelChanged;
        public event Action<int> LevelWon;
        public event Action<int> LevelLost;

        [Inject]
        public void Construct(
            GameSceneContext injectedSceneContext,
            GameManager injectedGameManager,
            IGameFactory injectedGameFactory,
            IVisualPoolService injectedVisualPoolService,
            LevelDatabase injectedLevelDatabase)
        {
            sceneContext = injectedSceneContext;
            gameManager = injectedGameManager;
            gameFactory = injectedGameFactory;
            visualPoolService = injectedVisualPoolService;
            levelDatabase = injectedLevelDatabase;
        }

        private void Update()
        {
            if (!Application.isPlaying
                || isTransitioning
                || CurrentRunState != LevelRunState.Playing
                || CurrentLevelIndex < 0)
            {
                return;
            }

            EvaluateLevelOutcome();
        }

        private void OnDestroy()
        {
            UnsubscribeFromSpawnedBlocks();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                ResetRuntimeState();
            }
        }

        private void OnApplicationQuit()
        {
            ResetRuntimeState();
        }

        public bool LoadInitialLevelIfNeeded()
        {
            sceneContext ??= GetComponent<GameSceneContext>();
            if (hasLoadedInitialLevel
                && (CurrentLevelIndex < 0
                    || sceneContext == null
                    || sceneContext.EnvironmentInstance == null))
            {
                hasLoadedInitialLevel = false;
                CurrentLevelIndex = -1;
            }

            if (!autoLoad || hasLoadedInitialLevel)
            {
                return true;
            }

            return LoadSavedOrFirstLevel();
        }

        public bool LoadSavedOrFirstLevel()
        {
            return TryLoadLevel(ResolveInitialLevelIndex());
        }

        public bool EnsureCurrentLevelLoaded()
        {
            if (HasRuntimeLevelContent())
            {
                return true;
            }

            if (CurrentLevelIndex < 0)
            {
                return LoadSavedOrFirstLevel();
            }

            return TryLoadLevel(CurrentLevelIndex);
        }

        [HorizontalGroup("Debug Levels Top")]
        [Button("Previous", ButtonSizes.Medium)]
        [ContextMenu("Load Previous Level")]
        public void LoadPreviousLevel()
        {
            if (LevelCount <= 0)
            {
                return;
            }

            if (!wrapLevelIndex && CurrentLevelIndex <= 0)
            {
                return;
            }

            var previousLevelIndex = CurrentLevelIndex < 0
                ? (wrapLevelIndex ? LevelCount - 1 : 0)
                : CurrentLevelIndex - 1;
            TryLoadLevel(previousLevelIndex);
        }

        [HorizontalGroup("Debug Levels Top")]
        [Button("Next", ButtonSizes.Medium)]
        [ContextMenu("Load Next Level")]
        public void LoadNextLevel()
        {
            if (LevelCount <= 0)
            {
                return;
            }

            if (!wrapLevelIndex && CurrentLevelIndex >= LevelCount - 1)
            {
                return;
            }

            var nextLevelIndex = CurrentLevelIndex < 0 ? 0 : CurrentLevelIndex + 1;
            TryLoadLevel(nextLevelIndex);
        }

        [HorizontalGroup("Debug Levels Bottom")]
        [Button("First", ButtonSizes.Medium)]
        [ContextMenu("Load First Level")]
        public void LoadFirstLevel()
        {
            TryLoadLevel(0);
        }

        [HorizontalGroup("Debug Levels Bottom")]
        [Button("Restart", ButtonSizes.Medium)]
        [ContextMenu("Restart Current Level")]
        public void RestartCurrentLevel()
        {
            TryLoadLevel(CurrentLevelIndex >= 0 ? CurrentLevelIndex : 0);
        }

        [HorizontalGroup("Debug Levels Save")]
        [Button("Clear Save Data", ButtonSizes.Medium)]
        [ContextMenu("Clear Saved Level Data")]
        public void ClearSavedLevelData()
        {
            PlayerPrefs.DeleteKey(CurrentLevelPrefsKey);
            PlayerPrefs.Save();
        }

        public bool LoadLevel(int requestedLevelIndex)
        {
            return TryLoadLevel(requestedLevelIndex);
        }

        public void TriggerLevelFail()
        {
            FailCurrentLevel();
        }

        private bool TryLoadLevel(int requestedLevelIndex)
        {
            if (isTransitioning)
            {
                return false;
            }

            if (!TryResolveLoadDependencies(out var database, out var environment))
            {
                return false;
            }

            var resolvedLevelIndex = NormalizeLevelIndex(requestedLevelIndex, database.Levels.Count);
            if (resolvedLevelIndex < 0)
            {
                return false;
            }

            isTransitioning = true;
            try
            {
                hasLoadedInitialLevel = true;
                CurrentLevelIndex = resolvedLevelIndex;

                ClearCurrentLevel();
                CurrentRunState = LevelRunState.Playing;

                var level = database.Levels[resolvedLevelIndex];
                var queueEntries = ResolveQueueEntries(level, database);
                var appliedHoldingSlotCount = ApplyHoldingSlotCount(environment, level, queueEntries);
                gameManager?.Construct(environment);
                EnsureRuntimeRoots(environment);

                SpawnBoard(level, database, environment);
                var waitingLanes = SpawnDeck(level, environment, appliedHoldingSlotCount, queueEntries);
                gameManager?.InitializeWaitingLanes(waitingLanes);
                NotifyLevelChanged();
                EvaluateLevelOutcome();
                return true;
            }
            finally
            {
                isTransitioning = false;
            }
        }

        private bool TryResolveLoadDependencies(out LevelDatabase database, out EnvironmentContext environment)
        {
            sceneContext ??= GetComponent<GameSceneContext>();
            sceneContext?.InitializeRuntimeSessionIfNeeded();
            gameManager ??= GetComponent<GameManager>();
            gameManager ??= sceneContext != null ? sceneContext.GameManager : null;
            gameFactory ??= sceneContext != null ? sceneContext.GameFactory : null;
            visualPoolService ??= sceneContext != null ? sceneContext.VisualPoolService : null;
            levelDatabase ??= sceneContext != null ? sceneContext.LevelDatabase : null;

            database = levelDatabase != null ? levelDatabase : sceneContext != null ? sceneContext.LevelDatabase : null;
            environment = sceneContext != null ? sceneContext.EnsureEnvironment() : null;

            if (database == null)
            {
                Debug.LogWarning("[LevelSessionController] No LevelDatabase is configured.", this);
                return false;
            }

            if (database.Levels == null || database.Levels.Count == 0)
            {
                Debug.LogWarning("[LevelSessionController] LevelDatabase does not contain any levels.", database);
                return false;
            }

            if (gameFactory == null || visualPoolService == null || gameManager == null)
            {
                return false;
            }

            if (environment == null)
            {
                Debug.LogWarning("[LevelSessionController] Could not resolve an EnvironmentContext for level loading.", this);
                return false;
            }

            return true;
        }

        private int NormalizeLevelIndex(int requestedLevelIndex, int levelCount)
        {
            if (levelCount <= 0)
            {
                return -1;
            }

            if (wrapLevelIndex)
            {
                return ((requestedLevelIndex % levelCount) + levelCount) % levelCount;
            }

            return Mathf.Clamp(requestedLevelIndex, 0, levelCount - 1);
        }

        private void ClearCurrentLevel()
        {
            CurrentRunState = LevelRunState.None;
            RemainingTargetBlocks = 0;

            UnsubscribeFromSpawnedBlocks();
            spawnedBlocks.Clear();
            spawnedPigs.Clear();

            gameManager?.ClearQueue();
            visualPoolService?.ReturnAll();
        }

        private void UnsubscribeFromSpawnedBlocks()
        {
            for (int i = 0; i < spawnedBlocks.Count; i++)
            {
                if (spawnedBlocks[i] != null)
                {
                    spawnedBlocks[i].Destroyed -= HandleBlockDestroyed;
                }
            }
        }

        private static int ApplyHoldingSlotCount(EnvironmentContext environment, LevelData level, IReadOnlyList<PigQueueEntry> queueEntries)
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

            return environment.ApplyHoldingContainerCount(desiredCount, minCount: 1, maxCount: environment.HoldingContainerCapacity);
        }

        private void EnsureRuntimeRoots(EnvironmentContext environment)
        {
            boardRoot = EnsureChildRoot(environment.BlockContainer != null ? environment.BlockContainer : environment.transform, RuntimeBoardRootName);
            deckRoot = EnsureChildRoot(environment.DeckContainer != null ? environment.DeckContainer : environment.transform, RuntimeDeckRootName);
            boardRoot.localPosition = Vector3.zero;
            boardRoot.localRotation = Quaternion.identity;
            boardRoot.localScale = Vector3.one;
            deckRoot.localPosition = Vector3.zero;
            deckRoot.localRotation = Quaternion.identity;
            deckRoot.localScale = Vector3.one;
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

        private void SpawnBoard(LevelData level, LevelDatabase database, EnvironmentContext environment)
        {
            if (level == null || environment == null || boardRoot == null || database == null)
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
                database,
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
                var definition = database.FindPlaceable(placedObject);
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

                        var block = gameFactory?.CreateBlock(new BlockSpawnRequest(
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

                        block.name = $"Block_{gridX}_{gridY}_{definition.DisplayName}";
                        spawnedBlocks.Add(block);

                        if (!countsAsTarget)
                        {
                            continue;
                        }

                        block.Destroyed -= HandleBlockDestroyed;
                        block.Destroyed += HandleBlockDestroyed;
                        RemainingTargetBlocks++;
                    }
                }
            }
        }

        private List<PigController>[] SpawnDeck(
            LevelData level,
            EnvironmentContext environment,
            int holdingSlotCount,
            IReadOnlyList<PigQueueEntry> queueEntries)
        {
            if (level == null || environment == null || deckRoot == null || holdingSlotCount <= 0)
            {
                return Array.Empty<List<PigController>>();
            }

            if (queueEntries == null || queueEntries.Count == 0)
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

            var lanePositions = ResolveDeckLanePositions(deckRoot, environment, holdingSlotCount, out var laneSpacing);
            var depthSpacing = Mathf.Max(0.9f, laneSpacing);
            var depthDirection = ResolveDeckDepthDirection(deckRoot, environment);

            for (int laneIndex = 0; laneIndex < holdingSlotCount; laneIndex++)
            {
                for (int depthIndex = 0; depthIndex < laneEntryIndices[laneIndex].Count; depthIndex++)
                {
                    var queueIndex = laneEntryIndices[laneIndex][depthIndex];
                    var entry = queueEntries[queueIndex];
                    var pig = gameFactory?.CreatePig(new PigSpawnRequest(
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
                    pig.ClearWaitingAnchor();
                    spawnedPigs.Add(pig);
                    waitingLanes[laneIndex].Add(pig);
                }
            }

            return waitingLanes;
        }

        private static List<PigQueueEntry> ResolveQueueEntries(LevelData level, LevelDatabase database)
        {
            if (level == null || database == null)
            {
                return new List<PigQueueEntry>();
            }

            var generatedQueueEntries = PigQueueGenerator.Generate(level.PlacedObjects, database, level.PigQueueGenerationSettings);
            if (generatedQueueEntries.Count > 0)
            {
                return generatedQueueEntries;
            }

            return level.PigQueue != null
                ? new List<PigQueueEntry>(level.PigQueue)
                : new List<PigQueueEntry>();
        }

        private static float[] ResolveDeckLanePositions(
            Transform deckRoot,
            EnvironmentContext environment,
            int holdingSlotCount,
            out float laneSpacing)
        {
            var resolvedPositions = new float[holdingSlotCount];
            var validLaneCount = 0;
            var previousPosition = 0f;
            var totalSpacing = 0f;
            var spacingSamples = 0;

            for (int laneIndex = 0; laneIndex < holdingSlotCount; laneIndex++)
            {
                var slot = environment != null
                    ? environment.GetHoldingSlot(laneIndex, activeOnly: true) ?? environment.GetHoldingSlot(laneIndex, activeOnly: false)
                    : null;
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

        private static float ResolveDeckDepthDirection(Transform deckRoot, EnvironmentContext environment)
        {
            if (deckRoot == null || environment?.TrayEquipPos == null)
            {
                return -1f;
            }

            var trayLocalPosition = deckRoot.InverseTransformPoint(environment.TrayEquipPos.position);
            return trayLocalPosition.z >= 0f ? -1f : 1f;
        }

        private static int ResolveBlockToneIndex(PlacedObjectData placedObject, PigColor blockColor)
        {
            return placedObject.HasVisualToneOverride
                ? placedObject.VisualToneIndex
                : PigColorAtlasUtility.ResolveDefaultToneIndex(blockColor);
        }

        private void HandleBlockDestroyed(BlockVisual block)
        {
            if (block != null)
            {
                block.Destroyed -= HandleBlockDestroyed;
            }

            if (RemainingTargetBlocks > 0)
            {
                RemainingTargetBlocks--;
            }

            EvaluateLevelOutcome();
        }

        private void EvaluateLevelOutcome()
        {
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            if (RemainingTargetBlocks <= 0)
            {
                CompleteCurrentLevel();
                return;
            }

            if (HasPendingPigAction() || HasPigWithRemainingAmmo())
            {
                return;
            }

            FailCurrentLevel();
        }

        private bool HasPendingPigAction()
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
                    case PigState.FollowingSpline:
                    case PigState.OrbitingTarget:
                    case PigState.Firing:
                    case PigState.ReturningToWaiting:
                        return true;
                }
            }

            return false;
        }

        private bool HasPigWithRemainingAmmo()
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

        private void CompleteCurrentLevel()
        {
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            CurrentRunState = LevelRunState.Won;
            SaveNextLevelIndexIfNeeded();
            LevelWon?.Invoke(CurrentLevelIndex);
        }

        private void FailCurrentLevel()
        {
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            CurrentRunState = LevelRunState.Lost;
            LevelLost?.Invoke(CurrentLevelIndex);
        }

        private void ResetRuntimeState()
        {
            hasLoadedInitialLevel = false;
            isTransitioning = false;
            CurrentLevelIndex = -1;
            CurrentRunState = LevelRunState.None;
            RemainingTargetBlocks = 0;
        }

        public void ResetForPlaySession()
        {
            ResetRuntimeState();
            sceneContext = null;
            gameManager = null;
            gameFactory = null;
            visualPoolService = null;
            levelDatabase = null;
            boardRoot = null;
            deckRoot = null;
            spawnedBlocks.Clear();
            spawnedPigs.Clear();
        }

        private int ResolveInitialLevelIndex()
        {
            if (!PlayerPrefs.HasKey(CurrentLevelPrefsKey))
            {
                return DefaultLevelIndex;
            }

            return Mathf.Max(DefaultLevelIndex, PlayerPrefs.GetInt(CurrentLevelPrefsKey, DefaultLevelIndex));
        }

        private int ResolveDisplayLevelIndex()
        {
            var requestedLevelIndex = CurrentLevelIndex >= 0
                ? CurrentLevelIndex
                : ResolveInitialLevelIndex();

            return LevelCount > 0
                ? NormalizeLevelIndex(requestedLevelIndex, LevelCount)
                : Mathf.Max(DefaultLevelIndex, requestedLevelIndex);
        }

        private void SaveNextLevelIndexIfNeeded()
        {
            if (!canSave || !TryResolveNextLevelIndexForSave(out var nextLevelIndex))
            {
                return;
            }

            PlayerPrefs.SetInt(CurrentLevelPrefsKey, nextLevelIndex);
            PlayerPrefs.Save();
        }

        private bool TryResolveNextLevelIndexForSave(out int nextLevelIndex)
        {
            nextLevelIndex = DefaultLevelIndex;
            if (CurrentLevelIndex < 0 || LevelCount <= 0)
            {
                return false;
            }

            var sequentialNextLevelIndex = CurrentLevelIndex + 1;
            if (sequentialNextLevelIndex >= LevelCount)
            {
                return false;
            }

            nextLevelIndex = sequentialNextLevelIndex;
            return true;
        }

        private void NotifyLevelChanged()
        {
            LevelChanged?.Invoke(CurrentLevelIndex);
        }

        private bool HasRuntimeLevelContent()
        {
            return (boardRoot != null && boardRoot.childCount > 0)
                || (deckRoot != null && deckRoot.childCount > 0);
        }
    }
}
