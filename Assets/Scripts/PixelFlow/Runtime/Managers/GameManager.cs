using System;
using System.Collections;
using System.Collections.Generic;
using Dreamteck.Splines;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Levels;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Tray;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
using VContainer;

namespace PixelFlow.Runtime.Managers
{
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        private static int playSessionVersion;
        private const float QueuedPigVerticalOffset = 0.6f;
        private const float TargetSelectionEpsilon = 0.0001f;

        [SerializeField, Min(0.01f)] private float dispatchFollowSpeed = 7f;
        [SerializeField, Min(0.01f)] private float traySendDuration = 0.45f;
        [SerializeField, Min(0.01f)] private float trayReturnDuration = 0.6f;
        [SerializeField, Min(0.01f)] private float trayTransferArcHeight = 1.5f;

        private readonly List<PigController> queuedPigs = new();
        private readonly List<TrayController> trayStackVisuals = new();
        private readonly HashSet<PigController> pigsUsingTrayStack = new();
        private readonly List<List<PigController>> waitingLanes = new();
        private readonly Dictionary<PigController, int> pigLaneLookup = new();
        private readonly List<PigController> activeConveyorPigs = new();
        private readonly List<PigController> conveyorPigBuffer = new();

        private EnvironmentContext environment;
        private SplineComputer dispatchSpline;
        private IGameFactory gameFactory;
        private GameSceneContext sceneContext;
        private LevelSessionController levelSessionController;
        private int availableTrayCount;
        private int lastKnownCapacity = -1;
        private int observedPlaySessionVersion = -1;

        public IReadOnlyList<PigController> QueuedPigs => queuedPigs;
        public int QueueCount => queuedPigs.Count;
        public int QueueCapacity => environment != null ? environment.ActiveHoldingContainerCount : 0;

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
            BootstrapRuntimeIfNeeded();

            if (environment == null)
            {
                return;
            }

            var capacity = QueueCapacity;
            if (capacity != lastKnownCapacity)
            {
                RefreshQueueVisuals(snapWaitingPigs: true);
            }

            UpdateConveyorPigFiring();
        }

        [Inject]
        public void InjectProjectSettings(ProjectRuntimeSettings settings)
        {
            ApplyProjectSettings(settings);
        }

        [Inject]
        public void InjectGameFactory(IGameFactory injectedGameFactory)
        {
            gameFactory = injectedGameFactory;
        }

        public void SetGameFactory(IGameFactory injectedGameFactory)
        {
            gameFactory = injectedGameFactory;
        }

        public void ApplyProjectSettings(ProjectRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            dispatchFollowSpeed = Mathf.Max(0.01f, settings.DispatchFollowSpeed);
        }

        public void Construct(EnvironmentContext resolvedEnvironment)
        {
            environment = resolvedEnvironment;
            if (environment != null)
            {
                environment.ResolveMissingReferences();
            }

            ResolveEnvironmentReferences();
            ResetTrayRuntimeState();
            EnsureWaitingLaneCount();
            CacheTrayStackVisuals();
            availableTrayCount = ResolveMaxVisibleTrayCount();
            environment?.EnsureTrayCounterText();
            RefreshQueueVisuals(snapWaitingPigs: true);
        }

        public void InitializeWaitingLanes(IReadOnlyList<List<PigController>> lanes)
        {
            ResetWaitingLaneState();
            EnsureWaitingLaneCount();

            if (lanes != null)
            {
                var laneCount = Mathf.Min(lanes.Count, waitingLanes.Count);
                for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
                {
                    var sourceLane = lanes[laneIndex];
                    var targetLane = waitingLanes[laneIndex];
                    if (sourceLane == null)
                    {
                        continue;
                    }

                    for (int pigIndex = 0; pigIndex < sourceLane.Count; pigIndex++)
                    {
                        var pig = sourceLane[pigIndex];
                        if (pig == null)
                        {
                            continue;
                        }

                        pigLaneLookup[pig] = laneIndex;
                        targetLane.Add(pig);
                    }
                }
            }

            RefreshQueueVisuals(snapWaitingPigs: true);
        }

        public bool TryQueuePig(PigController pig)
        {
            if (pig == null || environment == null || !pig.HasAmmo)
            {
                return false;
            }

            if (pigLaneLookup.ContainsKey(pig) || activeConveyorPigs.Contains(pig))
            {
                return false;
            }

            EnsureWaitingLaneCount();
            var laneIndex = ResolveFirstAvailableLaneIndex();
            if (laneIndex < 0)
            {
                return false;
            }

            waitingLanes[laneIndex].Add(pig);
            pigLaneLookup[pig] = laneIndex;
            RefreshQueueVisuals(snapWaitingPigs: true);
            return true;
        }

        public bool CanDispatchPig(PigController pig)
        {
            if (pig == null
                || environment == null
                || !pig.HasAmmo
                || availableTrayCount <= 0
                || !pigLaneLookup.TryGetValue(pig, out var laneIndex))
            {
                return false;
            }

            if (laneIndex < 0 || laneIndex >= waitingLanes.Count)
            {
                return false;
            }

            var lane = waitingLanes[laneIndex];
            return lane.Count > 0
                && lane[0] == pig
                && pig.State == PigState.Queued;
        }

        public bool TryDispatchPig(PigController pig)
        {
            if (!CanDispatchPig(pig))
            {
                return false;
            }

            var resolvedDispatchSpline = ResolveDispatchSpline();
            if (resolvedDispatchSpline == null)
            {
                Debug.LogWarning("[GameManager] Dispatch spline could not be resolved. Pig dispatch was cancelled.", this);
                return false;
            }

            if (!TryBorrowTray(pig))
            {
                return false;
            }

            var laneIndex = pigLaneLookup[pig];
            waitingLanes[laneIndex].RemoveAt(0);
            pigLaneLookup.Remove(pig);

            pig.ClearWaitingAnchor();
            pig.SetQueued(false);
            pig.SetOnBelt(true);
            pig.transform.SetParent(environment.transform, true);

            RegisterConveyorPig(pig);
            pig.FollowSpline(resolvedDispatchSpline, 0.0, 1.0, dispatchFollowSpeed);

            RefreshQueueVisuals();
            return true;
        }

        public PigController DispatchNextPigToSpline()
        {
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var lane = waitingLanes[laneIndex];
                if (lane.Count == 0)
                {
                    continue;
                }

                var pig = lane[0];
                if (pig != null && TryDispatchPig(pig))
                {
                    return pig;
                }
            }

            return null;
        }

        public void ClearQueue()
        {
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var lane = waitingLanes[laneIndex];
                for (int pigIndex = 0; pigIndex < lane.Count; pigIndex++)
                {
                    var pig = lane[pigIndex];
                    if (pig == null)
                    {
                        continue;
                    }

                    pig.ClearWaitingAnchor();
                    pig.SetQueued(false);
                    pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
                    pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
                }

                lane.Clear();
            }

            for (int pigIndex = 0; pigIndex < activeConveyorPigs.Count; pigIndex++)
            {
                var pig = activeConveyorPigs[pigIndex];
                if (pig == null)
                {
                    continue;
                }

                pig.ClearWaitingAnchor();
                pig.ReturnToWaiting();
                pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
                pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            }

            pigLaneLookup.Clear();
            activeConveyorPigs.Clear();
            queuedPigs.Clear();
            ResetTrayRuntimeState();
            trayStackVisuals.Clear();
            lastKnownCapacity = -1;

            if (environment != null)
            {
                RefreshQueueVisuals(snapWaitingPigs: true);
            }
        }

        private void RefreshQueueVisuals(bool snapWaitingPigs = false)
        {
            if (environment == null)
            {
                return;
            }

            EnsureWaitingLaneCount();
            RemoveNullPigsFromLanes();
            CacheTrayStackVisuals();
            availableTrayCount = Mathf.Clamp(availableTrayCount, 0, ResolveMaxVisibleTrayCount());
            UpdateTrayStackVisuals();
            RefreshWaitingPigAnchors(snapWaitingPigs);
            RebuildQueuedPigCache();

            var trayCounterText = environment.EnsureTrayCounterText();
            if (trayCounterText != null)
            {
                trayCounterText.text = $"{availableTrayCount}/{QueueCapacity}";
            }

            lastKnownCapacity = QueueCapacity;
        }

        private void RefreshWaitingPigAnchors(bool snapWaitingPigs)
        {
            if (environment == null)
            {
                return;
            }

            var depthSpacing = ResolveDeckDepthSpacing();
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var slot = environment.GetHoldingSlot(laneIndex, activeOnly: true);
                if (slot == null)
                {
                    continue;
                }

                var lane = waitingLanes[laneIndex];
                for (int depthIndex = 0; depthIndex < lane.Count; depthIndex++)
                {
                    var pig = lane[depthIndex];
                    if (pig == null)
                    {
                        continue;
                    }

                    pigLaneLookup[pig] = laneIndex;
                    var worldOffset = ResolveQueuedPigWorldOffset(slot, depthIndex, depthSpacing);
                    pig.AssignWaitingAnchor(slot, snapImmediately: false, worldOffset);

                    if (pig.State != PigState.ReturningToWaiting)
                    {
                        pig.SetQueued(true, snapImmediately: snapWaitingPigs);
                    }

                    pig.SetOnBelt(false);
                }
            }
        }

        private void RebuildQueuedPigCache()
        {
            queuedPigs.Clear();
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var lane = waitingLanes[laneIndex];
                if (lane.Count == 0 || lane[0] == null)
                {
                    continue;
                }

                queuedPigs.Add(lane[0]);
            }
        }

        private Vector3 ResolveQueuedPigWorldOffset(Transform holdingSlot, int depthIndex, float depthSpacing)
        {
            var fallbackOffset = Vector3.up * QueuedPigVerticalOffset;
            if (environment == null
                || holdingSlot == null
                || environment.DeckContainer == null)
            {
                return fallbackOffset;
            }

            var deckContainer = environment.DeckContainer;
            var slotPositionInDeckSpace = deckContainer.InverseTransformPoint(holdingSlot.position);
            var queuedWorldPosition = deckContainer.TransformPoint(new Vector3(
                slotPositionInDeckSpace.x,
                0f,
                depthIndex * depthSpacing * ResolveDeckDepthDirection()));
            queuedWorldPosition.y += QueuedPigVerticalOffset;
            return queuedWorldPosition - holdingSlot.position;
        }

        private float ResolveDeckDepthSpacing()
        {
            if (environment == null)
            {
                return 1.1f;
            }

            var totalSpacing = 0f;
            var spacingSamples = 0;
            var previousPosition = 0f;
            var hasPrevious = false;

            for (int laneIndex = 0; laneIndex < QueueCapacity; laneIndex++)
            {
                var slot = environment.GetHoldingSlot(laneIndex, activeOnly: true);
                if (slot == null)
                {
                    continue;
                }

                var localPosition = environment.DeckContainer != null
                    ? environment.DeckContainer.InverseTransformPoint(slot.position)
                    : slot.localPosition;
                if (hasPrevious)
                {
                    totalSpacing += Mathf.Abs(localPosition.x - previousPosition);
                    spacingSamples++;
                }

                previousPosition = localPosition.x;
                hasPrevious = true;
            }

            return spacingSamples > 0
                ? Mathf.Max(0.9f, totalSpacing / spacingSamples)
                : 1.1f;
        }

        private float ResolveDeckDepthDirection()
        {
            if (environment?.DeckContainer == null || environment.TrayEquipPos == null)
            {
                return -1f;
            }

            var trayLocalPosition = environment.DeckContainer.InverseTransformPoint(environment.TrayEquipPos.position);
            return trayLocalPosition.z >= 0f ? -1f : 1f;
        }

        private void RemoveNullPigsFromLanes()
        {
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var lane = waitingLanes[laneIndex];
                for (int pigIndex = lane.Count - 1; pigIndex >= 0; pigIndex--)
                {
                    if (lane[pigIndex] != null)
                    {
                        continue;
                    }

                    lane.RemoveAt(pigIndex);
                }
            }

            var nullPigs = new List<PigController>();
            foreach (var pair in pigLaneLookup)
            {
                if (pair.Key == null)
                {
                    nullPigs.Add(pair.Key);
                }
            }

            for (int i = 0; i < nullPigs.Count; i++)
            {
                pigLaneLookup.Remove(nullPigs[i]);
            }
        }

        private void EnsureWaitingLaneCount()
        {
            var capacity = Mathf.Max(QueueCapacity, 0);
            while (waitingLanes.Count < capacity)
            {
                waitingLanes.Add(new List<PigController>());
            }

            while (waitingLanes.Count > capacity)
            {
                waitingLanes.RemoveAt(waitingLanes.Count - 1);
            }
        }

        private void CacheTrayStackVisuals()
        {
            trayStackVisuals.Clear();
            if (environment?.TrayRoot == null)
            {
                return;
            }

            for (int i = 0; i < environment.TrayRoot.childCount; i++)
            {
                var trayVisual = environment.TrayRoot.GetChild(i).GetComponent<TrayController>();
                if (trayVisual != null)
                {
                    trayStackVisuals.Add(trayVisual);
                }
            }
        }

        private void UpdateTrayStackVisuals()
        {
            for (int i = 0; i < trayStackVisuals.Count; i++)
            {
                var trayVisual = trayStackVisuals[i];
                if (trayVisual == null)
                {
                    continue;
                }

                trayVisual.Configure(i < availableTrayCount, occupied: true);
            }
        }

        private int ResolveMaxVisibleTrayCount()
        {
            if (trayStackVisuals.Count == 0)
            {
                return Mathf.Max(QueueCapacity, 0);
            }

            return Mathf.Min(Mathf.Max(QueueCapacity, 0), trayStackVisuals.Count);
        }

        private bool TryBorrowTray(PigController pig)
        {
            if (pig == null
                || availableTrayCount <= 0
                || !pigsUsingTrayStack.Add(pig))
            {
                return false;
            }

            var trayStartPosition = ResolveTraySendStartPosition();
            availableTrayCount = Mathf.Max(0, availableTrayCount - 1);
            RefreshQueueVisuals();
            StartTrayTransfer(trayStartPosition, ResolveTrayEquipPosition(), traySendDuration, incrementTrayCountOnComplete: false);
            return true;
        }

        private void BeginReturnTray(PigController pig, Vector3 startPosition)
        {
            if (pig == null || !pigsUsingTrayStack.Remove(pig))
            {
                return;
            }

            StartTrayTransfer(startPosition, ResolveTrayReturnTargetPosition(), trayReturnDuration, incrementTrayCountOnComplete: true);
        }

        private void StartTrayTransfer(
            Vector3 startPosition,
            Vector3 endPosition,
            float duration,
            bool incrementTrayCountOnComplete)
        {
            if (!Application.isPlaying)
            {
                if (incrementTrayCountOnComplete)
                {
                    CompleteTrayReturn();
                }

                return;
            }

            StartCoroutine(PlayTrayTransfer(startPosition, endPosition, Mathf.Max(0.01f, duration), incrementTrayCountOnComplete));
        }

        private IEnumerator PlayTrayTransfer(
            Vector3 startPosition,
            Vector3 endPosition,
            float duration,
            bool incrementTrayCountOnComplete)
        {
            var trayTransform = CreateAnimatedTrayVisual(startPosition);
            var elapsed = 0f;

            while (trayTransform != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var position = Vector3.Lerp(startPosition, endPosition, t);
                position.y += Mathf.Sin(t * Mathf.PI) * trayTransferArcHeight;
                trayTransform.position = position;
                yield return null;
            }

            if (trayTransform != null)
            {
                Destroy(trayTransform.gameObject);
            }

            if (incrementTrayCountOnComplete)
            {
                CompleteTrayReturn();
            }
        }

        private Transform CreateAnimatedTrayVisual(Vector3 startPosition)
        {
            var template = ResolveTrayAnimationTemplate();
            if (template == null)
            {
                return null;
            }

            var trayObject = Instantiate(
                template,
                startPosition,
                template.transform.rotation,
                environment != null ? environment.transform : null);
            trayObject.name = $"{template.name}_Runtime";

            var trayController = trayObject.GetComponent<TrayController>();
            if (trayController != null)
            {
                trayController.Configure(true, occupied: true);
            }

            return trayObject.transform;
        }

        private GameObject ResolveTrayAnimationTemplate()
        {
            for (int i = 0; i < trayStackVisuals.Count; i++)
            {
                if (trayStackVisuals[i] != null)
                {
                    return trayStackVisuals[i].gameObject;
                }
            }

            return null;
        }

        private Vector3 ResolveTraySendStartPosition()
        {
            CacheTrayStackVisuals();
            if (trayStackVisuals.Count > 0 && availableTrayCount > 0)
            {
                var trayIndex = Mathf.Clamp(availableTrayCount - 1, 0, trayStackVisuals.Count - 1);
                var trayVisual = trayStackVisuals[trayIndex];
                if (trayVisual != null)
                {
                    return trayVisual.transform.position;
                }
            }

            return environment?.TrayRoot != null
                ? environment.TrayRoot.position
                : transform.position;
        }

        private Vector3 ResolveTrayEquipPosition()
        {
            if (environment?.TrayEquipPos != null)
            {
                return environment.TrayEquipPos.position;
            }

            return environment?.TrayRoot != null
                ? environment.TrayRoot.position
                : transform.position;
        }

        private Vector3 ResolveTrayReturnTargetPosition()
        {
            CacheTrayStackVisuals();
            if (trayStackVisuals.Count > 0)
            {
                var trayIndex = Mathf.Clamp(availableTrayCount, 0, trayStackVisuals.Count - 1);
                var trayVisual = trayStackVisuals[trayIndex];
                if (trayVisual != null)
                {
                    return trayVisual.transform.position;
                }
            }

            return environment?.TrayRoot != null
                ? environment.TrayRoot.position
                : transform.position;
        }

        private void CompleteTrayReturn()
        {
            availableTrayCount = Mathf.Min(ResolveMaxVisibleTrayCount(), availableTrayCount + 1);
            RefreshQueueVisuals();
        }

        private void ResetTrayRuntimeState()
        {
            foreach (var pig in pigsUsingTrayStack)
            {
                if (pig == null)
                {
                    continue;
                }

                pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
                pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
            }

            pigsUsingTrayStack.Clear();
            availableTrayCount = 0;
        }

        private void ResetWaitingLaneState()
        {
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                waitingLanes[laneIndex].Clear();
            }

            pigLaneLookup.Clear();
            queuedPigs.Clear();
        }

        private int ResolveFirstAvailableLaneIndex()
        {
            EnsureWaitingLaneCount();
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                if (waitingLanes[laneIndex].Count == 0)
                {
                    return laneIndex;
                }
            }

            return -1;
        }

        private void RegisterConveyorPig(PigController pig)
        {
            if (pig == null || activeConveyorPigs.Contains(pig))
            {
                return;
            }

            activeConveyorPigs.Add(pig);
            pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
            pig.ConveyorLoopCompleted += HandlePigConveyorLoopCompleted;
            pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
        }

        private void UnregisterConveyorPig(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            activeConveyorPigs.Remove(pig);
            pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
        }

        private void UpdateConveyorPigFiring()
        {
            if (activeConveyorPigs.Count == 0)
            {
                return;
            }

            conveyorPigBuffer.Clear();
            for (int i = activeConveyorPigs.Count - 1; i >= 0; i--)
            {
                var pig = activeConveyorPigs[i];
                if (pig == null || pig.State != PigState.FollowingSpline)
                {
                    activeConveyorPigs.RemoveAt(i);
                    continue;
                }

                conveyorPigBuffer.Add(pig);
            }

            conveyorPigBuffer.Sort(static (left, right) => right.CurrentSplinePercent.CompareTo(left.CurrentSplinePercent));
            for (int i = 0; i < conveyorPigBuffer.Count; i++)
            {
                var pig = conveyorPigBuffer[i];
                if (pig != null && pig.CanAttemptBeltShot)
                {
                    TryFirePig(pig);
                }
            }
        }

        private bool TryFirePig(PigController pig)
        {
            if (pig == null || environment == null || gameFactory == null)
            {
                return false;
            }

            var targetBlock = FindBestTargetBlock(pig);
            if (targetBlock == null || !pig.TryConsumeAmmo())
            {
                return false;
            }

            pig.NotifyBeltShotFired();
            targetBlock.SetReserved(true);

            gameFactory.CreateBullet(new BulletSpawnRequest(
                pig.Color,
                targetBlock.transform,
                targetBlock,
                new VisualSpawnPlacement(
                    parent: environment.transform,
                    position: pig.ProjectileOrigin.position,
                    rotation: pig.ProjectileOrigin.rotation,
                    useWorldSpace: true)));

            if (!pig.HasAmmo)
            {
                HandlePigDepleted(pig);
            }

            return true;
        }

        private BlockVisual FindBestTargetBlock(PigController pig)
        {
            if (pig == null || environment?.BlockContainer == null)
            {
                return null;
            }

            var blocks = environment.BlockContainer.GetComponentsInChildren<BlockVisual>(true);
            if (blocks == null || blocks.Length == 0)
            {
                return null;
            }

            var origin = pig.ProjectileOrigin.position;
            var boardCenter = ResolveBoardCenter(blocks);
            var inwardDirection = boardCenter - origin;
            inwardDirection.y = 0f;
            if (inwardDirection.sqrMagnitude <= TargetSelectionEpsilon)
            {
                inwardDirection = environment.BlockContainer.position - origin;
                inwardDirection.y = 0f;
            }

            if (inwardDirection.sqrMagnitude <= TargetSelectionEpsilon)
            {
                inwardDirection = Vector3.forward;
            }

            inwardDirection.Normalize();

            BlockVisual bestBlock = null;
            var bestLateral = float.PositiveInfinity;
            var bestDepth = float.PositiveInfinity;
            var bestDistance = float.PositiveInfinity;

            for (int i = 0; i < blocks.Length; i++)
            {
                var candidate = blocks[i];
                if (!IsValidTargetCandidate(candidate, pig.Color))
                {
                    continue;
                }

                var toCandidate = candidate.transform.position - origin;
                var depth = Vector3.Dot(inwardDirection, toCandidate);
                if (depth <= TargetSelectionEpsilon)
                {
                    continue;
                }

                var lateral = (toCandidate - (inwardDirection * depth)).sqrMagnitude;
                var distance = toCandidate.sqrMagnitude;
                if (!IsBetterTarget(lateral, depth, distance, bestLateral, bestDepth, bestDistance))
                {
                    continue;
                }

                bestLateral = lateral;
                bestDepth = depth;
                bestDistance = distance;
                bestBlock = candidate;
            }

            if (bestBlock != null)
            {
                return bestBlock;
            }

            for (int i = 0; i < blocks.Length; i++)
            {
                var candidate = blocks[i];
                if (!IsValidTargetCandidate(candidate, pig.Color))
                {
                    continue;
                }

                var distance = (candidate.transform.position - origin).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestBlock = candidate;
            }

            return bestBlock;
        }

        private static bool IsBetterTarget(
            float lateral,
            float depth,
            float distance,
            float bestLateral,
            float bestDepth,
            float bestDistance)
        {
            if (lateral < bestLateral - TargetSelectionEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(lateral - bestLateral) > TargetSelectionEpsilon)
            {
                return false;
            }

            if (depth < bestDepth - TargetSelectionEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(depth - bestDepth) > TargetSelectionEpsilon)
            {
                return false;
            }

            return distance < bestDistance;
        }

        private static bool IsValidTargetCandidate(BlockVisual candidate, PixelFlow.Runtime.Data.PigColor color)
        {
            return candidate != null
                && candidate.gameObject.activeInHierarchy
                && !candidate.IsDying
                && !candidate.IsReserved
                && candidate.Color == color;
        }

        private static Vector3 ResolveBoardCenter(IReadOnlyList<BlockVisual> blocks)
        {
            var accumulated = Vector3.zero;
            var count = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || !block.gameObject.activeInHierarchy || block.IsDying)
                {
                    continue;
                }

                accumulated += block.transform.position;
                count++;
            }

            return count > 0
                ? accumulated / count
                : Vector3.zero;
        }

        private void HandlePigConveyorLoopCompleted(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            UnregisterConveyorPig(pig);
            if (!pig.HasAmmo)
            {
                HandlePigDepleted(pig);
                return;
            }

            if (!TryAssignPigToReturnLane(pig))
            {
                BeginReturnTray(pig, ResolveTrayDropStartPosition(pig));
                pig.ClearWaitingAnchor();
                pig.ReturnToWaiting();
                gameFactory?.ReleasePig(pig);
                levelSessionController?.TriggerLevelFail();
                RefreshQueueVisuals();
                return;
            }

            pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            pig.ReturnedToWaiting += HandlePigReturnedToWaiting;
            pig.ReturnToWaiting();
            BeginReturnTray(pig, ResolveTrayDropStartPosition(pig));
            RefreshQueueVisuals();
        }

        private void HandlePigReturnedToWaiting(PigController pig)
        {
            if (pig != null)
            {
                pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            }

            RefreshQueueVisuals();
        }

        private bool TryAssignPigToReturnLane(PigController pig)
        {
            if (pig == null)
            {
                return false;
            }

            var laneIndex = ResolveFirstAvailableLaneIndex();
            if (laneIndex < 0 || laneIndex >= waitingLanes.Count)
            {
                return false;
            }

            waitingLanes[laneIndex].Add(pig);
            pigLaneLookup[pig] = laneIndex;
            return true;
        }

        private void HandlePigDepleted(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            UnregisterConveyorPig(pig);
            pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            pig.ClearWaitingAnchor();
            pig.ReturnToWaiting();
            BeginReturnTray(pig, pig.transform.position);
            gameFactory?.ReleasePig(pig);
            RefreshQueueVisuals();
        }

        private Vector3 ResolveTrayDropStartPosition(PigController pig)
        {
            if (environment?.TrayDropPos != null)
            {
                return environment.TrayDropPos.position;
            }

            if (pig != null)
            {
                return pig.transform.position;
            }

            return ResolveTrayEquipPosition();
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

            sceneContext ??= GetComponent<GameSceneContext>();
            levelSessionController ??= GetComponent<LevelSessionController>();
            if (sceneContext == null)
            {
                return;
            }

            if (observedPlaySessionVersion != playSessionVersion)
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

            if (levelSessionController == null
                || resolvedEnvironment == null)
            {
                return;
            }

            if (environment != resolvedEnvironment)
            {
                Construct(resolvedEnvironment);
            }

            if (!levelSessionController.HasLoadedInitialLevel)
            {
                levelSessionController.LoadSavedOrFirstLevel();
                return;
            }

            levelSessionController.EnsureCurrentLevelLoaded();
        }

        private void ResetForPlaySession()
        {
            environment = null;
            dispatchSpline = null;
            lastKnownCapacity = -1;

            ClearQueue();
            sceneContext?.ResetRuntimeSessionState();
            levelSessionController?.ResetForPlaySession();
        }
    }
}
