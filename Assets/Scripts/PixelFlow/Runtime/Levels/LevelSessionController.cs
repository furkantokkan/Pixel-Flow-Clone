using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
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
using VContainer.Unity;

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
        private const float WinOutcomeDelaySeconds = 1f;

        [Header("Loading")]
        [SerializeField, FormerlySerializedAs("autoLoadFirstLevelOnStart")] private bool autoLoad = true;
        [SerializeField] private bool canSave = true;
        [SerializeField] private bool wrapLevelIndex = true;

        private readonly List<BlockVisual> spawnedBlocks = new();
        private readonly List<PigController> spawnedPigs = new();
        private readonly LevelSessionOutcomeTracker outcomeTracker = new();

        private GameSceneContext sceneContext;
        private GameManager gameManager;
        private IGameFactory gameFactory;
        private IVisualPoolService visualPoolService;
        private LevelDatabase levelDatabase;
        private Transform boardRoot;
        private Transform deckRoot;
        private bool hasLoadedInitialLevel;
        private bool isTransitioning;
        private bool isBurstActive;
        private bool hasResolvedTargetBlock;
        private bool isWinPresentationPending;
        private float pendingWinReadyTime = -1f;
        private bool hasSubscribedToGameManager;

        private static bool IsPlayModeActive => Application.isPlaying;

        public int CurrentLevelIndex { get; private set; } = -1;
        public int DisplayLevelIndex => ResolveDisplayLevelIndex();
        public LevelRunState CurrentRunState { get; private set; }
        public int RemainingTargetBlocks { get; private set; }
        public int LevelCount => levelDatabase?.Levels?.Count ?? 0;
        public bool HasLoadedInitialLevel => hasLoadedInitialLevel;
        public bool AcceptsInput => CurrentRunState == LevelRunState.Playing && !isTransitioning && !isBurstActive;
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
            SubscribeToGameManagerEvents();
        }

        private void Update()
        {
            if (!Application.isPlaying
                || isTransitioning
                || CurrentLevelIndex < 0)
            {
                return;
            }

            if (isWinPresentationPending)
            {
                TryPresentPendingWin();
                return;
            }

            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            EvaluateLevelOutcomeIfNeeded();
        }

        private void OnDestroy()
        {
            UnsubscribeFromSpawnedBlocks();
            outcomeTracker.UnsubscribeFromOutcomePigs(HandlePigOutcomeChanged);
            UnsubscribeFromGameManagerEvents();
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
            ResolveRuntimeDependencies();
            ResolveEditorOnlyDependencies();
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
            if (CurrentRunState == LevelRunState.Won || CurrentRunState == LevelRunState.Lost)
            {
                return true;
            }

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
        [EnableIf(nameof(IsPlayModeActive))]
        [Button("Previous", ButtonSizes.Medium)]
        [ContextMenu("Load Previous Level")]
        public void LoadPreviousLevel()
        {
            if (!Application.isPlaying)
            {
                return;
            }

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
        [EnableIf(nameof(IsPlayModeActive))]
        [Button("Next", ButtonSizes.Medium)]
        [ContextMenu("Load Next Level")]
        public void LoadNextLevel()
        {
            if (!Application.isPlaying)
            {
                return;
            }

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
        [EnableIf(nameof(IsPlayModeActive))]
        [Button("First", ButtonSizes.Medium)]
        [ContextMenu("Load First Level")]
        public void LoadFirstLevel()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            TryLoadLevel(0);
        }

        [HorizontalGroup("Debug Levels Bottom")]
        [EnableIf(nameof(IsPlayModeActive))]
        [Button("Restart", ButtonSizes.Medium)]
        [ContextMenu("Restart Current Level")]
        public void RestartCurrentLevel()
        {
            if (!Application.isPlaying)
            {
                return;
            }

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
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            if (RemainingTargetBlocks <= 0)
            {
                BeginWinPresentation();
                TryPresentPendingWin();
                return;
            }

            if (outcomeTracker.HasPendingTargetResolution(spawnedBlocks))
            {
                return;
            }

            if (outcomeTracker.HasPendingPigAction(ResolveOutcomePigs()))
            {
                return;
            }

            if (ResolvePendingQueueEntries().Count > 0)
            {
                return;
            }

            FailCurrentLevel();
        }

        public void ForceLevelFail()
        {
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            if (RemainingTargetBlocks <= 0)
            {
                BeginWinPresentation();
                TryPresentPendingWin();
                return;
            }

            if (outcomeTracker.HasPendingTargetResolution(spawnedBlocks))
            {
                return;
            }

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
                SetBurstActive(false);
                hasResolvedTargetBlock = false;
                isWinPresentationPending = false;
                pendingWinReadyTime = -1f;

                var level = database.Levels[resolvedLevelIndex];
                var runtimeSpawner = new LevelRuntimeSpawner(gameFactory, database, environment);
                var spawnResult = runtimeSpawner.SpawnLevel(level, RuntimeBoardRootName, RuntimeDeckRootName);
                RegisterSpawnedContent(spawnResult);

                gameManager?.Construct(environment);
                gameManager?.InitializeWaitingLanes(
                    spawnResult.WaitingLanes,
                    spawnResult.PendingLaneEntries,
                    spawnResult.DeckRoot);
                NotifyLevelChanged();
                outcomeTracker.Invalidate();
                EvaluateLevelOutcomeIfNeeded(force: true);
                return true;
            }
            finally
            {
                isTransitioning = false;
            }
        }

        private bool TryResolveLoadDependencies(out LevelDatabase database, out EnvironmentContext environment)
        {
            ResolveRuntimeDependencies();
            ResolveEditorOnlyDependencies();
            sceneContext?.InitializeRuntimeSessionIfNeeded();
            database = levelDatabase;
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
                Debug.LogWarning(
                    $"[LevelSessionController] Missing runtime dependencies. " +
                    $"GameManager={(gameManager != null)}, GameFactory={(gameFactory != null)}, VisualPool={(visualPoolService != null)}.",
                    this);
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
            SetBurstActive(false);
            hasResolvedTargetBlock = false;
            isWinPresentationPending = false;
            outcomeTracker.Invalidate();

            UnsubscribeFromSpawnedBlocks();
            outcomeTracker.UnsubscribeFromOutcomePigs(HandlePigOutcomeChanged);
            spawnedBlocks.Clear();
            spawnedPigs.Clear();
            pendingWinReadyTime = -1f;

            gameManager?.ClearQueue();
            visualPoolService?.ReturnAll();
        }

        private void UnsubscribeFromSpawnedBlocks()
        {
            for (int i = 0; i < spawnedBlocks.Count; i++)
            {
                if (spawnedBlocks[i] != null)
                {
                    spawnedBlocks[i].StateChanged -= HandleBlockStateChanged;
                    spawnedBlocks[i].Destroyed -= HandleBlockDestroyed;
                }
            }
        }

        private void RegisterSpawnedContent(LevelRuntimeSpawnResult spawnResult)
        {
            if (spawnResult == null)
            {
                boardRoot = null;
                deckRoot = null;
                RemainingTargetBlocks = 0;
                outcomeTracker.Invalidate();
                return;
            }

            boardRoot = spawnResult.BoardRoot;
            deckRoot = spawnResult.DeckRoot;
            RemainingTargetBlocks = spawnResult.TargetBlocks != null
                ? spawnResult.TargetBlocks.Count
                : 0;
            outcomeTracker.Invalidate();

            if (spawnResult.SpawnedBlocks != null)
            {
                spawnedBlocks.AddRange(spawnResult.SpawnedBlocks);
            }

            if (spawnResult.SpawnedPigs != null)
            {
                spawnedPigs.AddRange(spawnResult.SpawnedPigs);
                for (int i = 0; i < spawnResult.SpawnedPigs.Count; i++)
                {
                    outcomeTracker.SubscribeToOutcomePig(spawnResult.SpawnedPigs[i], HandlePigOutcomeChanged);
                }
            }

            if (spawnResult.TargetBlocks == null)
            {
                return;
            }

            for (int i = 0; i < spawnResult.TargetBlocks.Count; i++)
            {
                var block = spawnResult.TargetBlocks[i];
                if (block == null)
                {
                    continue;
                }

                block.StateChanged -= HandleBlockStateChanged;
                block.StateChanged += HandleBlockStateChanged;
                block.Destroyed -= HandleBlockDestroyed;
                block.Destroyed += HandleBlockDestroyed;
            }
        }

        private void HandleBlockDestroyed(BlockVisual block)
        {
            if (block != null)
            {
                block.StateChanged -= HandleBlockStateChanged;
                block.Destroyed -= HandleBlockDestroyed;
            }

            if (RemainingTargetBlocks > 0)
            {
                RemainingTargetBlocks--;
            }

            hasResolvedTargetBlock = true;
            if (RemainingTargetBlocks <= 0)
            {
                BeginWinPresentation();
            }

            outcomeTracker.Invalidate();
            EvaluateLevelOutcomeIfNeeded(force: true);
        }

        private void HandleBlockStateChanged(BlockVisual _)
        {
            outcomeTracker.Invalidate();
        }

        private void EvaluateLevelOutcomeIfNeeded(bool force = false)
        {
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            if (!force && !outcomeTracker.ShouldEvaluate())
            {
                return;
            }

            EvaluateLevelOutcome();
        }

        private void EvaluateLevelOutcome()
        {
            if (CurrentRunState != LevelRunState.Playing)
            {
                return;
            }

            switch (outcomeTracker.Evaluate(
                RemainingTargetBlocks,
                isBurstActive,
                IsHoldingContainerFilled(),
                hasResolvedTargetBlock,
                spawnedBlocks,
                ResolveOutcomePigs(),
                ResolvePendingQueueEntries()))
            {
                case LevelOutcomeDecision.Win:
                    BeginWinPresentation();
                    return;
                case LevelOutcomeDecision.EnterBurst:
                    SetBurstActive(true);
                    return;
                case LevelOutcomeDecision.Lose:
                    FailCurrentLevel();
                    return;
            }
        }

        private bool IsHoldingContainerFilled()
        {
            return gameManager != null && gameManager.IsHoldingContainerFull;
        }

        private void CompleteCurrentLevel()
        {
            if (CurrentRunState == LevelRunState.Lost || !isWinPresentationPending)
            {
                return;
            }

            isWinPresentationPending = false;
            pendingWinReadyTime = -1f;
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
            SetBurstActive(false);
            isWinPresentationPending = false;
            pendingWinReadyTime = -1f;
            outcomeTracker.Invalidate();
            LevelLost?.Invoke(CurrentLevelIndex);
        }

        private void ResetRuntimeState()
        {
            hasLoadedInitialLevel = false;
            isTransitioning = false;
            isBurstActive = false;
            hasResolvedTargetBlock = false;
            isWinPresentationPending = false;
            pendingWinReadyTime = -1f;
            CurrentLevelIndex = -1;
            CurrentRunState = LevelRunState.None;
            RemainingTargetBlocks = 0;
            outcomeTracker.Invalidate();
        }

        private void SetBurstActive(bool active)
        {
            isBurstActive = active;
            outcomeTracker.Invalidate();
            gameManager?.SetBurstModeActive(active);
        }

        public void ResetForPlaySession()
        {
            ResetRuntimeState();
            boardRoot = null;
            deckRoot = null;
            UnsubscribeFromSpawnedBlocks();
            outcomeTracker.UnsubscribeFromOutcomePigs(HandlePigOutcomeChanged);
            spawnedBlocks.Clear();
            spawnedPigs.Clear();
        }

        private bool IsWinPresentationReady()
        {
            return pendingWinReadyTime < 0f
                || !Application.isPlaying
                || Time.unscaledTime >= pendingWinReadyTime;
        }

        private void BeginWinPresentation()
        {
            if (CurrentRunState == LevelRunState.Lost)
            {
                return;
            }

            CurrentRunState = LevelRunState.Won;
            SetBurstActive(false);
            isWinPresentationPending = true;
            pendingWinReadyTime = Application.isPlaying
                ? Time.unscaledTime + WinOutcomeDelaySeconds
                : 0f;
            outcomeTracker.Invalidate();
        }

        private void TryPresentPendingWin()
        {
            if (!isWinPresentationPending || CurrentRunState != LevelRunState.Won)
            {
                return;
            }

            if (outcomeTracker.HasPendingTargetResolution(spawnedBlocks)
                || outcomeTracker.HasPendingPigAction(ResolveOutcomePigs())
                || !IsWinPresentationReady())
            {
                return;
            }

            CompleteCurrentLevel();
        }

        private IReadOnlyList<PigController> ResolveOutcomePigs()
        {
            return Application.isPlaying && gameManager != null
                ? gameManager.TrackedPigs
                : spawnedPigs;
        }

        private IReadOnlyList<PigQueueEntry> ResolvePendingQueueEntries()
        {
            return Application.isPlaying && gameManager != null
                ? gameManager.PendingQueueEntries
                : Array.Empty<PigQueueEntry>();
        }

        private void SubscribeToGameManagerEvents()
        {
            if (gameManager == null || hasSubscribedToGameManager)
            {
                return;
            }

            gameManager.OutcomeStateChanged += HandleGameManagerOutcomeStateChanged;
            gameManager.TrackedPigRegistered += HandleTrackedPigRegistered;
            gameManager.TrackedPigUnregistered += HandleTrackedPigUnregistered;
            hasSubscribedToGameManager = true;
            outcomeTracker.SubscribeToCurrentOutcomePigs(ResolveOutcomePigs(), HandlePigOutcomeChanged);
        }

        private void UnsubscribeFromGameManagerEvents()
        {
            if (gameManager == null || !hasSubscribedToGameManager)
            {
                return;
            }

            gameManager.OutcomeStateChanged -= HandleGameManagerOutcomeStateChanged;
            gameManager.TrackedPigRegistered -= HandleTrackedPigRegistered;
            gameManager.TrackedPigUnregistered -= HandleTrackedPigUnregistered;
            hasSubscribedToGameManager = false;
        }

        private void HandleGameManagerOutcomeStateChanged()
        {
            outcomeTracker.SubscribeToCurrentOutcomePigs(ResolveOutcomePigs(), HandlePigOutcomeChanged);
            outcomeTracker.Invalidate();
        }

        private void HandleTrackedPigRegistered(PigController pig)
        {
            outcomeTracker.SubscribeToOutcomePig(pig, HandlePigOutcomeChanged);
            outcomeTracker.Invalidate();
        }

        private void HandleTrackedPigUnregistered(PigController pig)
        {
            outcomeTracker.UnsubscribeFromOutcomePig(pig, HandlePigOutcomeChanged);
            outcomeTracker.Invalidate();
        }

        private void HandlePigOutcomeChanged(PigController _)
        {
            outcomeTracker.Invalidate();
        }

        private void ResolveEditorOnlyDependencies()
        {
            if (Application.isPlaying)
            {
                return;
            }

            sceneContext ??= GetComponent<GameSceneContext>();
            gameManager ??= GetComponent<GameManager>();
            SubscribeToGameManagerEvents();
            if (sceneContext == null)
            {
                return;
            }

            gameManager ??= sceneContext.GameManager;
            SubscribeToGameManagerEvents();
            gameFactory ??= sceneContext.GameFactory;
            visualPoolService ??= sceneContext.VisualPoolService;
            levelDatabase ??= ResolveEditorLevelDatabase();
        }

        private LevelDatabase ResolveEditorLevelDatabase()
        {
            if (levelDatabase != null)
            {
                return levelDatabase;
            }

            var projectScope = LifetimeScope.Find<ProjectLifetimeScope>() as ProjectLifetimeScope;
            if (projectScope != null && projectScope.LevelDatabase != null)
            {
                return projectScope.LevelDatabase;
            }

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(LevelDatabase)}");
            if (guids != null && guids.Length > 0)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                return UnityEditor.AssetDatabase.LoadAssetAtPath<LevelDatabase>(assetPath);
            }
#endif

            return null;
        }

        private void ResolveRuntimeDependencies()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            sceneContext ??= GetComponent<GameSceneContext>();
            gameManager ??= GetComponent<GameManager>();
            SubscribeToGameManagerEvents();
            if (sceneContext == null)
            {
                return;
            }

            gameManager ??= sceneContext.GameManager;
            SubscribeToGameManagerEvents();
            gameFactory ??= sceneContext.GameFactory;
            visualPoolService ??= sceneContext.VisualPoolService;
            levelDatabase ??= ResolveRuntimeLevelDatabase(sceneContext);
        }

        private static LevelDatabase ResolveRuntimeLevelDatabase(GameSceneContext sceneContext)
        {
            if (sceneContext == null || sceneContext.Container == null)
            {
                return null;
            }

            try
            {
                return sceneContext.Container.Resolve<LevelDatabase>();
            }
            catch
            {
                return null;
            }
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
