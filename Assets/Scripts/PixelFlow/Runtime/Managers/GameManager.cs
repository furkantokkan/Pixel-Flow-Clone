using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Dreamteck.Splines;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Levels;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Tray;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;

namespace PixelFlow.Runtime.Managers
{
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        private static int playSessionVersion;
        private const float QueuedPigVerticalOffset = 0.6f;
        private const float TargetSelectionEpsilon = 0.0001f;
        private const float DispatchEntryClearanceDistance = 1.25f;

        private float dispatchFollowSpeed = 7f;
        private float traySendDuration = 0.45f;
        private float trayReturnDuration = 0.6f;
        private float trayTransferArcHeight = 1.5f;
        private float beltShotOriginForwardOffset = 0.8f;
        private float beltShotRadius = 0.4f;
        private float beltShotDistance = 20f;
        private float burstFollowSpeedMultiplier = 1.85f;
        private float burstRampDuration = 0.35f;
        private float burstFireIntervalMultiplier = 0.7f;
        [SerializeField, FormerlySerializedAs("beltShotLayerMask")] private LayerMask pigShotLayerMask = 1 << 8;
        [SerializeField] private bool cullOffscreenPigRenderers = true;
        [SerializeField, Range(0f, 0.5f)] private float pigRendererViewportPadding = 0.18f;
        [SerializeField, Min(30)] private int targetFrameRate = 60;

        private readonly List<PigController> queuedPigs = new();
        private readonly List<PigController> holdingPigs = new();
        private readonly List<TrayController> trayStackVisuals = new();
        private readonly HashSet<PigController> pigsUsingTrayStack = new();
        private readonly List<List<PigController>> waitingLanes = new();
        private readonly Dictionary<PigController, int> pigLaneLookup = new();
        private readonly Dictionary<PigController, int> pigHoldingLookup = new();
        private readonly List<PigController> activeConveyorPigs = new();

        private EnvironmentContext environment;
        private SplineComputer dispatchSpline;
        private IGameFactory gameFactory;
        private GameManagerCollaboratorFactory collaboratorFactory;
        private GameSceneContext sceneContext;
        private LevelSessionController levelSessionController;
        private GameManagerTrayQueueCoordinator trayQueueCoordinator;
        private GameManagerTargetingCoordinator targetingCoordinator;
        private GameManagerBurstCoordinator burstCoordinator;
        private PigRendererVisibilityCoordinator pigRendererVisibilityCoordinator;
        private int observedPlaySessionVersion = -1;
        private CancellationTokenSource dispatchWarmupCts;

        public IReadOnlyList<PigController> QueuedPigs => queuedPigs;
        public int QueueCount => queuedPigs.Count;
        public int QueueCapacity => environment != null ? environment.ActiveHoldingContainerCount : 0;
        public int HoldingPigCount => trayQueueCoordinator != null ? trayQueueCoordinator.HoldingPigCount : 0;
        public bool IsHoldingContainerFull => QueueCapacity > 0 && HoldingPigCount >= QueueCapacity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            playSessionVersion = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void MarkNewPlaySession()
        {
            playSessionVersion++;
        }

        private void LateUpdate()
        {
            ApplyRuntimeFrameRateCap();
            BootstrapRuntimeIfNeeded();

            if (environment == null)
            {
                return;
            }

            EnsureGameplayCollaborators();
            var capacity = QueueCapacity;
            if (trayQueueCoordinator != null && capacity != trayQueueCoordinator.LastKnownCapacity)
            {
                RefreshQueueVisuals(snapWaitingPigs: true);
            }

            UpdateBurstDispatch();
            UpdateConveyorPigFiring();
            UpdatePigRendererVisibility();
        }

        [Inject]
        public void InjectProjectSettings(ProjectRuntimeSettings settings)
        {
            ConfigureFromProjectSettings(settings);
        }

        [Inject]
        public void InjectGameFactory(IGameFactory injectedGameFactory)
        {
            gameFactory = injectedGameFactory;
        }

        [Inject]
        private void InjectCollaboratorFactory(GameManagerCollaboratorFactory injectedCollaboratorFactory)
        {
            collaboratorFactory = injectedCollaboratorFactory;
        }

        [Inject]
        public void InjectSceneDependencies(GameSceneContext injectedSceneContext)
        {
            sceneContext = injectedSceneContext;
        }

        private void ConfigureFromProjectSettings(ProjectRuntimeSettings settings)
        {
            if (settings == null)
            {
                ApplyRuntimeFrameRateCap();
                EnsureGameplayLayerCollisionMatrix();
                return;
            }

            dispatchFollowSpeed = Mathf.Max(0.01f, settings.DispatchFollowSpeed);
            traySendDuration = Mathf.Max(0.01f, settings.TraySendDuration);
            trayReturnDuration = Mathf.Max(0.01f, settings.TrayReturnDuration);
            trayTransferArcHeight = Mathf.Max(0.01f, settings.TrayTransferArcHeight);
            beltShotOriginForwardOffset = Mathf.Max(0f, settings.BeltShotOriginForwardOffset);
            beltShotRadius = Mathf.Max(0.01f, settings.BeltShotRadius);
            beltShotDistance = Mathf.Max(0.01f, settings.BeltShotDistance);
            burstFollowSpeedMultiplier = Mathf.Max(1f, settings.BurstFollowSpeedMultiplier);
            burstRampDuration = Mathf.Max(0.01f, settings.BurstRampDuration);
            burstFireIntervalMultiplier = Mathf.Max(0.01f, settings.BurstFireIntervalMultiplier);
            EnsureGameplayCollaborators(refreshSettings: true);
            ApplyRuntimeFrameRateCap();
            EnsureGameplayLayerCollisionMatrix();
        }

        public void Construct(EnvironmentContext resolvedEnvironment)
        {
            environment = resolvedEnvironment;
            if (environment != null)
            {
                environment.ResolveMissingReferences();
            }

            ResolveEnvironmentReferences();
            EnsureGameplayCollaborators(refreshSettings: true);
            trayQueueCoordinator?.InitializeForEnvironment();
            environment?.EnsureTrayCounterText();
            EnsureGameplayLayerCollisionMatrix();
            ResetBurstMode();
            RefreshQueueVisuals(snapWaitingPigs: true);
            ScheduleDispatchWarmup();
        }

        public void InitializeWaitingLanes(IReadOnlyList<List<PigController>> lanes)
        {
            EnsureGameplayCollaborators();
            trayQueueCoordinator?.InitializeWaitingLanes(lanes);
            ScheduleDispatchWarmup();
        }

        public void SetBurstModeActive(bool active)
        {
            EnsureGameplayCollaborators();
            burstCoordinator?.SetActive(active);
        }

        public bool TryQueuePig(PigController pig)
        {
            EnsureGameplayCollaborators();
            return trayQueueCoordinator != null && trayQueueCoordinator.TryQueuePig(pig);
        }

        public bool CanDispatchPig(PigController pig)
        {
            return CanDispatchPig(pig, ignoreTrayAvailability: false);
        }

        private bool CanDispatchPig(PigController pig, bool ignoreTrayAvailability)
        {
            EnsureGameplayCollaborators();
            if (trayQueueCoordinator == null || IsDispatchEntryOccupied(pig))
            {
                return false;
            }

            return trayQueueCoordinator.CanDispatchPig(pig, ignoreTrayAvailability);
        }

        public bool TryResolveDispatchCandidate(PigController clickedPig, out PigController dispatchPig)
        {
            EnsureGameplayCollaborators();
            if (trayQueueCoordinator == null)
            {
                dispatchPig = null;
                return false;
            }

            return trayQueueCoordinator.TryResolveDispatchCandidate(clickedPig, out dispatchPig);
        }

        public bool TryDispatchPig(PigController pig)
        {
            return TryDispatchPig(pig, ignoreTrayAvailability: false);
        }

        private bool TryDispatchPig(PigController pig, bool ignoreTrayAvailability)
        {
            if (!CanDispatchPig(pig, ignoreTrayAvailability))
            {
                return false;
            }

            var resolvedDispatchSpline = ResolveDispatchSpline();
            if (resolvedDispatchSpline == null)
            {
                Debug.LogWarning("[GameManager] Dispatch spline could not be resolved. Pig dispatch was cancelled.", this);
                return false;
            }

            if (!TryResolveDispatchEntryPose(resolvedDispatchSpline, out var dispatchEntryPosition, out var dispatchEntryRotation))
            {
                Debug.LogWarning("[GameManager] Dispatch entry pose could not be resolved. Pig dispatch was cancelled.", this);
                return false;
            }

            if (!TryResolveDispatchSplineRange(resolvedDispatchSpline, out var dispatchStartPercent, out var dispatchEndPercent))
            {
                Debug.LogWarning("[GameManager] Dispatch spline range could not be resolved. Pig dispatch was cancelled.", this);
                return false;
            }

            EnsureGameplayCollaborators();
            if (trayQueueCoordinator == null
                || !trayQueueCoordinator.TryPrepareDispatch(
                    pig,
                    environment != null ? environment.transform : null,
                    ignoreTrayAvailability))
            {
                return false;
            }
            ApplyBurstModifiersToPig(pig);
            pig.BeginDispatchToSpline(
                resolvedDispatchSpline,
                dispatchEntryPosition,
                dispatchEntryRotation,
                dispatchStartPercent,
                dispatchEndPercent,
                dispatchFollowSpeed,
                environment.BlockContainer,
                dispatchDurationOverride: traySendDuration);

            RefreshQueueVisuals();
            return true;
        }

        public PigController DispatchNextPigToSpline()
        {
            return DispatchNextPigToSpline(ignoreTrayAvailability: false);
        }

        private PigController DispatchNextPigToSpline(bool ignoreTrayAvailability)
        {
            EnsureGameplayCollaborators();
            return trayQueueCoordinator != null
                ? trayQueueCoordinator.DispatchNextPig(pig => TryDispatchPig(pig, ignoreTrayAvailability))
                : null;
        }

        public void ClearQueue()
        {
            ResetBurstMode();
            EnsureGameplayCollaborators();
            trayQueueCoordinator?.ClearQueue();
            targetingCoordinator?.Clear();
        }

        private void RefreshQueueVisuals(bool snapWaitingPigs = false)
        {
            trayQueueCoordinator?.RefreshQueueVisuals(snapWaitingPigs);
        }

        private Vector3 ResolveTrayEquipPosition()
        {
            if (environment?.TrayEquipPos != null)
            {
                return environment.TrayEquipPos.position;
            }

            if (TryResolveDispatchEntryPose(ResolveDispatchSpline(), out var position, out _))
            {
                return position;
            }

            return environment?.TrayRoot != null
                ? environment.TrayRoot.position
                : transform.position;
        }

        private bool TryResolveDispatchEntryPose(
            SplineComputer spline,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            if (environment?.TrayEquipPos != null)
            {
                worldPosition = environment.TrayEquipPos.position;
                worldRotation = environment.TrayEquipPos.rotation;
                return true;
            }

            if (spline != null)
            {
                var sample = spline.Evaluate(0.0);
                worldPosition = sample.position;

                var forward = sample.forward.sqrMagnitude > TargetSelectionEpsilon
                    ? sample.forward.normalized
                    : transform.forward;
                var up = sample.up.sqrMagnitude > TargetSelectionEpsilon
                    ? sample.up.normalized
                    : Vector3.up;
                worldRotation = Quaternion.LookRotation(forward, up);
                return true;
            }

            worldPosition = transform.position;
            worldRotation = transform.rotation;
            return false;
        }

        private bool TryResolveDispatchSplineRange(
            SplineComputer spline,
            out double startPercent,
            out double endPercent)
        {
            startPercent = 0.0;
            endPercent = 1.0;

            if (spline == null)
            {
                return false;
            }

            if (environment?.TrayEquipPos != null)
            {
                startPercent = Mathf.Clamp01((float)spline.Project(environment.TrayEquipPos.position).percent);
            }

            if (environment?.TrayDropPos != null)
            {
                endPercent = Mathf.Clamp01((float)spline.Project(environment.TrayDropPos.position).percent);
            }

            if (Math.Abs(startPercent - endPercent) <= 0.0001d)
            {
                startPercent = 0.0;
                endPercent = 1.0;
            }

            return true;
        }

        private bool IsDispatchEntryOccupied(PigController dispatchPig)
        {
            if (activeConveyorPigs.Count == 0)
            {
                return false;
            }

            if (!TryResolveDispatchEntryPose(ResolveDispatchSpline(), out var dispatchEntryPosition, out _))
            {
                return HasPigDispatchingToBelt();
            }

            var clearanceDistanceSqr = DispatchEntryClearanceDistance * DispatchEntryClearanceDistance;
            for (var i = 0; i < activeConveyorPigs.Count; i++)
            {
                var activePig = activeConveyorPigs[i];
                if (activePig == null || activePig == dispatchPig)
                {
                    continue;
                }

                if (activePig.State == PigState.DispatchingToBelt)
                {
                    return true;
                }

                if (activePig.State != PigState.FollowingSpline)
                {
                    continue;
                }

                var delta = activePig.transform.position - dispatchEntryPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude <= clearanceDistanceSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPigDispatchingToBelt()
        {
            for (var i = 0; i < activeConveyorPigs.Count; i++)
            {
                var activePig = activeConveyorPigs[i];
                if (activePig != null && activePig.State == PigState.DispatchingToBelt)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureGameplayLayerCollisionMatrix()
        {
            var pigLayer = LayerMask.NameToLayer("Pig");
            var bulletLayer = LayerMask.NameToLayer("Bullet");
            var blockLayer = LayerMask.NameToLayer("Block");
            var defaultLayer = LayerMask.NameToLayer("Default");
            var transparentFxLayer = LayerMask.NameToLayer("TransparentFX");
            var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            var waterLayer = LayerMask.NameToLayer("Water");
            var uiLayer = LayerMask.NameToLayer("UI");

            EnsureLayerCollision(pigLayer, blockLayer, enabled: true);
            EnsureLayerCollision(pigLayer, pigLayer, enabled: false);
            EnsureLayerCollision(pigLayer, bulletLayer, enabled: false);
            EnsureLayerCollision(bulletLayer, bulletLayer, enabled: false);
            EnsureLayerCollision(bulletLayer, blockLayer, enabled: false);
            EnsureLayerCollision(blockLayer, blockLayer, enabled: false);

            EnsureLayerCollision(pigLayer, defaultLayer, enabled: false);
            EnsureLayerCollision(pigLayer, transparentFxLayer, enabled: false);
            EnsureLayerCollision(pigLayer, ignoreRaycastLayer, enabled: false);
            EnsureLayerCollision(pigLayer, waterLayer, enabled: false);
            EnsureLayerCollision(pigLayer, uiLayer, enabled: false);

            EnsureLayerCollision(bulletLayer, defaultLayer, enabled: false);
            EnsureLayerCollision(bulletLayer, transparentFxLayer, enabled: false);
            EnsureLayerCollision(bulletLayer, ignoreRaycastLayer, enabled: false);
            EnsureLayerCollision(bulletLayer, waterLayer, enabled: false);
            EnsureLayerCollision(bulletLayer, uiLayer, enabled: false);

            EnsureLayerCollision(blockLayer, defaultLayer, enabled: false);
            EnsureLayerCollision(blockLayer, transparentFxLayer, enabled: false);
            EnsureLayerCollision(blockLayer, ignoreRaycastLayer, enabled: false);
            EnsureLayerCollision(blockLayer, waterLayer, enabled: false);
            EnsureLayerCollision(blockLayer, uiLayer, enabled: false);
        }

        private static void EnsureLayerCollision(int layerA, int layerB, bool enabled)
        {
            if (layerA < 0 || layerB < 0)
            {
                return;
            }

            var currentlyIgnored = Physics.GetIgnoreLayerCollision(layerA, layerB);
            if (enabled == !currentlyIgnored)
            {
                return;
            }

            Physics.IgnoreLayerCollision(layerA, layerB, !enabled);
        }

        private void ApplyRuntimeFrameRateCap()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var resolvedTargetFrameRate = Mathf.Max(30, targetFrameRate);
            if (ShouldDisableVSyncForCurrentPlatform()
                && QualitySettings.vSyncCount != 0)
            {
                QualitySettings.vSyncCount = 0;
            }

            if (Application.targetFrameRate != resolvedTargetFrameRate)
            {
                Application.targetFrameRate = resolvedTargetFrameRate;
            }
        }

        private static bool ShouldDisableVSyncForCurrentPlatform()
        {
            return Application.isMobilePlatform
                || Application.isEditor
                || Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.LinuxPlayer;
        }

        private void UpdateConveyorPigFiring()
        {
            EnsureGameplayCollaborators();
            targetingCoordinator?.UpdateConveyorPigFiring(HandlePigDepleted);
        }

        private void UpdateBurstDispatch()
        {
            EnsureGameplayCollaborators();
            burstCoordinator?.Update();
        }

        private void HandlePigDepleted(PigController pig)
        {
            EnsureGameplayCollaborators();
            trayQueueCoordinator?.HandlePigDepleted(pig);
        }

        private void ResolveEnvironmentReferences()
        {
            environment?.ResolveMissingReferences();
            dispatchSpline = environment != null
                ? environment.DispatchSpline
                : null;
        }

        private SplineComputer ResolveDispatchSpline()
        {
            if (dispatchSpline != null)
            {
                return dispatchSpline;
            }

            ResolveEnvironmentReferences();
            return dispatchSpline;
        }

        private void BootstrapRuntimeIfNeeded()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var resolvedLevelSessionController = ResolveLevelSessionController();
            if (sceneContext == null || resolvedLevelSessionController == null)
            {
                return;
            }

            if (observedPlaySessionVersion < 0)
            {
                observedPlaySessionVersion = playSessionVersion;
            }
            else if (observedPlaySessionVersion != playSessionVersion)
            {
                ResetForPlaySession();
                observedPlaySessionVersion = playSessionVersion;
            }

            sceneContext.InitializeRuntimeSessionIfNeeded();
            if (!sceneContext.RuntimeSessionInitialized)
            {
                return;
            }

            var resolvedEnvironment = environment != null && environment.gameObject != null && environment.gameObject.activeInHierarchy
                ? environment
                : sceneContext.EnvironmentInstance != null && sceneContext.EnvironmentInstance.gameObject != null && sceneContext.EnvironmentInstance.gameObject.activeInHierarchy
                    ? sceneContext.EnvironmentInstance
                    : sceneContext.EnsureEnvironment();

            if (resolvedLevelSessionController == null
                || resolvedEnvironment == null)
            {
                return;
            }

            if (environment != resolvedEnvironment)
            {
                Construct(resolvedEnvironment);
            }

            if (!resolvedLevelSessionController.HasLoadedInitialLevel)
            {
                resolvedLevelSessionController.LoadInitialLevelIfNeeded();
                return;
            }

            if (resolvedLevelSessionController.CurrentRunState == LevelRunState.Won
                || resolvedLevelSessionController.CurrentRunState == LevelRunState.Lost)
            {
                return;
            }

            resolvedLevelSessionController.EnsureCurrentLevelLoaded();
        }

        private void UpdatePigRendererVisibility()
        {
            EnsureGameplayCollaborators();
            pigRendererVisibilityCoordinator?.UpdateVisibility(waitingLanes, holdingPigs, activeConveyorPigs);
        }

        private void ResetBurstMode()
        {
            EnsureGameplayCollaborators();
            burstCoordinator?.Reset();
        }

        private void TryDispatchBurstPigs()
        {
            EnsureGameplayCollaborators();
            burstCoordinator?.OnDispatchOpportunity();
        }

        private void ApplyBurstModifiersToPig(PigController pig)
        {
            EnsureGameplayCollaborators();
            burstCoordinator?.ApplyToPig(pig);
        }

        private void EnsureGameplayCollaborators(bool refreshSettings = false)
        {
            var createdCollaborator = false;

            if (targetingCoordinator == null
                || trayQueueCoordinator == null
                || pigRendererVisibilityCoordinator == null
                || burstCoordinator == null)
            {
                if (collaboratorFactory == null)
                {
                    return;
                }

                var collaborators = collaboratorFactory.Create(
                    queuedPigs,
                    holdingPigs,
                    trayStackVisuals,
                    pigsUsingTrayStack,
                    waitingLanes,
                    pigLaneLookup,
                    pigHoldingLookup,
                    activeConveyorPigs,
                    () => environment,
                    () => QueueCapacity,
                    () => ResolveLevelSessionController()?.ForceLevelFail(),
                    TryDispatchBurstPigs,
                    ResolveTrayEquipPosition,
                    ignoreTrayAvailability => DispatchNextPigToSpline(ignoreTrayAvailability),
                    gameObject,
                    () => sceneContext?.InputManager?.InputCamera);
                if (collaborators.TargetingCoordinator == null
                    || collaborators.TrayQueueCoordinator == null
                    || collaborators.VisibilityCoordinator == null
                    || collaborators.BurstCoordinator == null)
                {
                    return;
                }

                targetingCoordinator = collaborators.TargetingCoordinator;
                trayQueueCoordinator = collaborators.TrayQueueCoordinator;
                pigRendererVisibilityCoordinator = collaborators.VisibilityCoordinator;
                burstCoordinator = collaborators.BurstCoordinator;
                createdCollaborator = true;
            }

            if (!refreshSettings && !createdCollaborator)
            {
                return;
            }

            targetingCoordinator.Configure(
                environment,
                gameFactory,
                beltShotOriginForwardOffset,
                beltShotRadius,
                beltShotDistance,
                pigShotLayerMask);
            trayQueueCoordinator.Configure(
                traySendDuration,
                trayReturnDuration,
                trayTransferArcHeight);
            pigRendererVisibilityCoordinator.Configure(cullOffscreenPigRenderers, pigRendererViewportPadding);
            burstCoordinator.Configure(
                burstFollowSpeedMultiplier,
                burstRampDuration,
                burstFireIntervalMultiplier);
        }

        private void ResetForPlaySession()
        {
            CancelDispatchWarmup();
            ResetBurstMode();
            environment = null;
            dispatchSpline = null;

            ClearQueue();
            sceneContext?.ResetRuntimeSessionState();
            ResolveLevelSessionController()?.ResetForPlaySession();
        }

        private void OnDisable()
        {
            CancelDispatchWarmup();
        }

        private void OnDestroy()
        {
            CancelDispatchWarmup();
        }

        private LevelSessionController ResolveLevelSessionController()
        {
            if (levelSessionController != null)
            {
                return levelSessionController;
            }

            levelSessionController = GetComponent<LevelSessionController>();
            return levelSessionController;
        }

        private void ScheduleDispatchWarmup()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CancelDispatchWarmup();
            dispatchWarmupCts = new CancellationTokenSource();
            RunDispatchWarmupAsync(dispatchWarmupCts.Token).Forget();
        }

        private void CancelDispatchWarmup()
        {
            if (dispatchWarmupCts == null)
            {
                return;
            }

            dispatchWarmupCts.Cancel();
            dispatchWarmupCts.Dispose();
            dispatchWarmupCts = null;
        }

        private async UniTaskVoid RunDispatchWarmupAsync(CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.NextFrame(cancellationToken: cancellationToken);
                await UniTask.NextFrame(cancellationToken: cancellationToken);

                EnsureGameplayCollaborators();
                trayQueueCoordinator?.PrewarmDispatchRuntime();
                PrewarmPigDispatchRuntime();
                WarmupTweenDispatchPath();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void PrewarmPigDispatchRuntime()
        {
            for (var laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var lane = waitingLanes[laneIndex];
                if (lane == null)
                {
                    continue;
                }

                for (var pigIndex = 0; pigIndex < lane.Count; pigIndex++)
                {
                    lane[pigIndex]?.PrewarmDispatchRuntime();
                }
            }

            for (var slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                holdingPigs[slotIndex]?.PrewarmDispatchRuntime();
            }
        }

        private void WarmupTweenDispatchPath()
        {
            Tween.Custom(this, 0f, 1f, 0.0001f, static (_, _) => { }, Ease.Linear);
        }
    }
}
