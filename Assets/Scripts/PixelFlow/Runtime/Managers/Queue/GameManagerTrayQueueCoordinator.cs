using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Audio;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Tray;
using PrimeTween;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerTrayQueueCoordinator
    {
        private const float QueuedPigVerticalOffset = 0.6f;
        private const float TraySendRotationDegrees = -90f;
        private const float TrayReturnRotationDegrees = 90f;

        private readonly GameManagerQueueRuntimeState queueState;
        private readonly HashSet<ActiveTrayTransfer> activeTrayTransfers = new();
        private readonly Stack<Transform> trayTransferVisualPool = new();
        private readonly List<List<PigQueueEntry>> pendingLaneEntries = new();
        private readonly List<PigQueueEntry> pendingQueueEntries = new();
        private readonly Func<EnvironmentContext> environmentProvider;
        private readonly Func<int> queueCapacityProvider;
        private readonly Action<PigController> registerConveyorPig;
        private readonly Action<PigController> unregisterConveyorPig;
        private readonly Action<PigController> releasePig;
        private readonly Action triggerLevelFail;
        private readonly Action dispatchBurstPigs;
        private readonly Action outcomeStateChanged;
        private readonly Func<PigQueueEntry, int, PigController> spawnQueuedPig;
        private readonly Func<Vector3> resolveTrayEquipPosition;
        private readonly ISoundService soundService;

        private float traySendDuration = 0.45f;
        private float trayReturnDuration = 0.6f;
        private float trayTransferArcHeight = 1.5f;
        private GameObject trayTransferTemplate;

        public GameManagerTrayQueueCoordinator(
            GameManagerQueueRuntimeState queueState,
            Func<EnvironmentContext> environmentProvider,
            Func<int> queueCapacityProvider,
            Action<PigController> registerConveyorPig,
            Action<PigController> unregisterConveyorPig,
            Action<PigController> releasePig,
            Action triggerLevelFail,
            Action dispatchBurstPigs,
            Action outcomeStateChanged,
            Func<PigQueueEntry, int, PigController> spawnQueuedPig,
            Func<Vector3> resolveTrayEquipPosition,
            ISoundService soundService)
        {
            this.queueState = queueState;
            this.environmentProvider = environmentProvider;
            this.queueCapacityProvider = queueCapacityProvider;
            this.registerConveyorPig = registerConveyorPig;
            this.unregisterConveyorPig = unregisterConveyorPig;
            this.releasePig = releasePig;
            this.triggerLevelFail = triggerLevelFail;
            this.dispatchBurstPigs = dispatchBurstPigs;
            this.outcomeStateChanged = outcomeStateChanged;
            this.spawnQueuedPig = spawnQueuedPig;
            this.resolveTrayEquipPosition = resolveTrayEquipPosition;
            this.soundService = soundService;
        }

        public int AvailableTrayCount { get; private set; }
        public int LastKnownCapacity { get; private set; } = -1;
        public int HoldingPigCount => CountHoldingPigs();
        public IReadOnlyList<PigQueueEntry> PendingQueueEntries => pendingQueueEntries;

        private EnvironmentContext Environment => environmentProvider();
        private int QueueCapacity => queueCapacityProvider();
        private List<PigController> queuedPigs => queueState.QueuedPigs;
        private List<PigController> holdingPigs => queueState.HoldingPigs;
        private List<TrayController> trayStackVisuals => queueState.TrayStackVisuals;
        private HashSet<PigController> pigsUsingTrayStack => queueState.PigsUsingTrayStack;
        private List<List<PigController>> waitingLanes => queueState.WaitingLanes;
        private Dictionary<PigController, int> pigLaneLookup => queueState.PigLaneLookup;
        private Dictionary<PigController, int> pigHoldingLookup => queueState.PigHoldingLookup;
        private List<PigController> activeConveyorPigs => queueState.ActiveConveyorPigs;

        public void Configure(float traySendDuration, float trayReturnDuration, float trayTransferArcHeight)
        {
            this.traySendDuration = Mathf.Max(0.01f, traySendDuration);
            this.trayReturnDuration = Mathf.Max(0.01f, trayReturnDuration);
            this.trayTransferArcHeight = Mathf.Max(0.01f, trayTransferArcHeight);
        }

        public void InitializeForEnvironment()
        {
            ResetTrayRuntimeState();
            EnsureWaitingLaneCount();
            EnsureHoldingSlotCount();
            CacheTrayStackVisuals();
            RefreshTrayTransferTemplate();
            AvailableTrayCount = ResolveMaxVisibleTrayCount();
            Environment?.EnsureTrayCounterText();
            LastKnownCapacity = -1;
        }

        public void PrewarmDispatchRuntime()
        {
            CacheTrayStackVisuals();
            RefreshTrayTransferTemplate();
            PrewarmTrayTransferVisuals(1);
        }

        public void InitializeWaitingLanes(
            IReadOnlyList<List<PigController>> lanes,
            IReadOnlyList<List<PigQueueEntry>> pendingEntries)
        {
            ResetWaitingLaneState();
            EnsureWaitingLaneCount();
            EnsureHoldingSlotCount();
            EnsurePendingLaneCount();

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

            if (pendingEntries != null)
            {
                var laneCount = Mathf.Min(pendingEntries.Count, pendingLaneEntries.Count);
                for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
                {
                    var sourceLane = pendingEntries[laneIndex];
                    if (sourceLane == null || sourceLane.Count == 0)
                    {
                        continue;
                    }

                    pendingLaneEntries[laneIndex].AddRange(sourceLane);
                    pendingQueueEntries.AddRange(sourceLane);
                }
            }

            RefreshQueueVisuals(snapWaitingPigs: true);
        }

        public bool TryQueuePig(PigController pig)
        {
            if (pig == null || Environment == null || !pig.HasAmmo)
            {
                return false;
            }

            if (pigLaneLookup.ContainsKey(pig)
                || pigHoldingLookup.ContainsKey(pig)
                || ContainsActiveConveyorPig(pig))
            {
                return false;
            }

            if (!TryAssignPigToHoldingSlot(pig))
            {
                return false;
            }

            RefreshQueueVisuals(snapWaitingPigs: true);
            return true;
        }

        public bool CanDispatchPig(PigController pig, bool ignoreTrayAvailability = false)
        {
            if (pig == null
                || Environment == null
                || !pig.HasAmmo
                || (!ignoreTrayAvailability && AvailableTrayCount <= 0)
                || pig.State != PigState.Queued)
            {
                return false;
            }

            if (TryGetDeckLaneIndex(pig, out var laneIndex))
            {
                var lane = waitingLanes[laneIndex];
                return lane.Count > 0 && lane[0] == pig;
            }

            return TryGetHoldingSlotIndex(pig, out var holdingSlotIndex)
                && holdingPigs[holdingSlotIndex] == pig;
        }

        public bool TryResolveDispatchCandidate(PigController clickedPig, out PigController dispatchPig)
        {
            dispatchPig = CanDispatchPig(clickedPig) ? clickedPig : null;
            return dispatchPig != null;
        }

        public bool TryPrepareDispatch(PigController pig, Transform runtimeParent, bool ignoreTrayAvailability = false)
        {
            if (!CanDispatchPig(pig, ignoreTrayAvailability))
            {
                return false;
            }

            var shouldBorrowTray = !ignoreTrayAvailability || AvailableTrayCount > 0;
            if (shouldBorrowTray && !TryBorrowTray(pig))
            {
                return false;
            }

            if (TryGetDeckLaneIndex(pig, out var laneIndex))
            {
                waitingLanes[laneIndex].RemoveAt(0);
                pigLaneLookup.Remove(pig);
                TrySpawnPendingPigForLane(laneIndex);
            }
            else if (TryGetHoldingSlotIndex(pig, out var holdingSlotIndex))
            {
                holdingPigs[holdingSlotIndex] = null;
                pigHoldingLookup.Remove(pig);
            }
            else
            {
                pigsUsingTrayStack.Remove(pig);
                CompleteTrayReturn();
                return false;
            }

            pig.ClearWaitingAnchor();
            pig.SetQueued(false);
            if (runtimeParent != null)
            {
                pig.transform.SetParent(runtimeParent, true);
            }

            SubscribeConveyorPigLifecycle(pig);
            registerConveyorPig?.Invoke(pig);
            outcomeStateChanged?.Invoke();
            return true;
        }

        public PigController DispatchNextPig(Func<PigController, bool> dispatchAction)
        {
            if (dispatchAction == null)
            {
                return null;
            }

            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                var lane = waitingLanes[laneIndex];
                if (lane.Count == 0)
                {
                    continue;
                }

                var pig = lane[0];
                if (pig != null && dispatchAction(pig))
                {
                    return pig;
                }
            }

            for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                var pig = holdingPigs[slotIndex];
                if (pig != null && dispatchAction(pig))
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
                    UnsubscribePigLifecycle(pig);
                }

                lane.Clear();
            }

            for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                var pig = holdingPigs[slotIndex];
                if (pig == null)
                {
                    continue;
                }

                pig.ClearWaitingAnchor();
                pig.SetQueued(false);
                UnsubscribePigLifecycle(pig);
                holdingPigs[slotIndex] = null;
            }

            if (activeConveyorPigs != null)
            {
                var activePigBuffer = new List<PigController>(activeConveyorPigs.Count);
                for (int pigIndex = 0; pigIndex < activeConveyorPigs.Count; pigIndex++)
                {
                    activePigBuffer.Add(activeConveyorPigs[pigIndex]);
                }

                for (int pigIndex = 0; pigIndex < activePigBuffer.Count; pigIndex++)
                {
                    var pig = activePigBuffer[pigIndex];
                    if (pig == null)
                    {
                        continue;
                    }

                    pig.ClearWaitingAnchor();
                    pig.ReturnToWaiting();
                    UnsubscribePigLifecycle(pig);
                    unregisterConveyorPig?.Invoke(pig);
                }
            }

            pigLaneLookup.Clear();
            pigHoldingLookup.Clear();
            queuedPigs.Clear();
            ClearPendingLaneEntries();
            ResetTrayRuntimeState();
            trayStackVisuals.Clear();
            LastKnownCapacity = -1;

            if (Environment != null)
            {
                RefreshQueueVisuals(snapWaitingPigs: true);
            }
        }

        public void RefreshQueueVisuals(bool snapWaitingPigs = false)
        {
            var environment = Environment;
            if (environment == null)
            {
                return;
            }

            EnsureWaitingLaneCount();
            EnsureHoldingSlotCount();
            RemoveNullPigsFromLanes();
            CacheTrayStackVisuals();
            AvailableTrayCount = Mathf.Clamp(AvailableTrayCount, 0, ResolveMaxVisibleTrayCount());
            UpdateTrayStackVisuals();
            RefreshWaitingPigAnchors(snapWaitingPigs);
            RefreshHoldingPigAnchors(snapWaitingPigs);
            RebuildQueuedPigCache();

            var trayCounterText = environment.EnsureTrayCounterText();
            if (trayCounterText != null)
            {
                trayCounterText.text = $"{AvailableTrayCount}/{QueueCapacity}";
            }

            LastKnownCapacity = QueueCapacity;
            outcomeStateChanged?.Invoke();
        }

        public void HandlePigDepleted(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            unregisterConveyorPig?.Invoke(pig);
            pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            if (TryGetHoldingSlotIndex(pig, out var holdingSlotIndex))
            {
                holdingPigs[holdingSlotIndex] = null;
                pigHoldingLookup.Remove(pig);
            }

            pig.ClearWaitingAnchor();
            pig.BeltDepleteCompleted -= HandlePigBeltDepleteCompleted;
            pig.BeltDepleteCompleted += HandlePigBeltDepleteCompleted;
            pig.BeginBeltDeplete();
            RefreshQueueVisuals();
        }

        private bool ContainsActiveConveyorPig(PigController pig)
        {
            if (pig == null || activeConveyorPigs == null)
            {
                return false;
            }

            for (int i = 0; i < activeConveyorPigs.Count; i++)
            {
                if (activeConveyorPigs[i] == pig)
                {
                    return true;
                }
            }

            return false;
        }

        private void SubscribeConveyorPigLifecycle(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
            pig.ConveyorLoopCompleted += HandlePigConveyorLoopCompleted;
            pig.DispatchToBeltCompleted -= HandlePigDispatchToBeltCompleted;
            pig.DispatchToBeltCompleted += HandlePigDispatchToBeltCompleted;
            pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            pig.BeltDepleteCompleted -= HandlePigBeltDepleteCompleted;
        }

        private void UnsubscribePigLifecycle(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            pig.ConveyorLoopCompleted -= HandlePigConveyorLoopCompleted;
            pig.DispatchToBeltCompleted -= HandlePigDispatchToBeltCompleted;
            pig.ReturnedToWaiting -= HandlePigReturnedToWaiting;
            pig.BeltDepleteCompleted -= HandlePigBeltDepleteCompleted;
        }

        private void RefreshWaitingPigAnchors(bool snapWaitingPigs)
        {
            var environment = Environment;
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
                        pig.SetOnBelt(false);
                    }
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

        private void RefreshHoldingPigAnchors(bool snapHoldingPigs)
        {
            var environment = Environment;
            if (environment == null)
            {
                return;
            }

            for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                var pig = holdingPigs[slotIndex];
                if (pig == null)
                {
                    continue;
                }

                var slot = environment.GetHoldingSlot(slotIndex, activeOnly: true);
                if (slot == null)
                {
                    continue;
                }

                pigHoldingLookup[pig] = slotIndex;
                pig.AssignWaitingAnchor(slot, snapImmediately: false, ResolveHoldingPigWorldOffset());
                if (pig.State != PigState.ReturningToWaiting)
                {
                    pig.SetQueued(true, snapImmediately: snapHoldingPigs);
                    pig.SetOnBelt(false);
                }
            }
        }

        private Vector3 ResolveQueuedPigWorldOffset(Transform holdingSlot, int depthIndex, float depthSpacing)
        {
            var environment = Environment;
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

        private static Vector3 ResolveHoldingPigWorldOffset()
        {
            return Vector3.up * QueuedPigVerticalOffset;
        }

        private float ResolveDeckDepthSpacing()
        {
            var environment = Environment;
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
            var environment = Environment;
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

            nullPigs.Clear();
            foreach (var pair in pigHoldingLookup)
            {
                if (pair.Key == null)
                {
                    nullPigs.Add(pair.Key);
                }
            }

            for (int i = 0; i < nullPigs.Count; i++)
            {
                pigHoldingLookup.Remove(nullPigs[i]);
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

        private void EnsurePendingLaneCount()
        {
            var capacity = Mathf.Max(QueueCapacity, 0);
            while (pendingLaneEntries.Count < capacity)
            {
                pendingLaneEntries.Add(new List<PigQueueEntry>());
            }

            while (pendingLaneEntries.Count > capacity)
            {
                pendingLaneEntries.RemoveAt(pendingLaneEntries.Count - 1);
            }
        }

        private void EnsureHoldingSlotCount()
        {
            var capacity = Mathf.Max(QueueCapacity, 0);
            while (holdingPigs.Count < capacity)
            {
                holdingPigs.Add(null);
            }

            while (holdingPigs.Count > capacity)
            {
                var lastIndex = holdingPigs.Count - 1;
                var pig = holdingPigs[lastIndex];
                if (pig != null)
                {
                    pigHoldingLookup.Remove(pig);
                }

                holdingPigs.RemoveAt(lastIndex);
            }
        }

        private void CacheTrayStackVisuals()
        {
            trayStackVisuals.Clear();
            var environment = Environment;
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

            trayStackVisuals.Sort(CompareTrayStackVisuals);
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

                trayVisual.Configure(i < AvailableTrayCount, occupied: true);
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
                || AvailableTrayCount <= 0
                || !pigsUsingTrayStack.Add(pig))
            {
                return false;
            }

            var trayStartPosition = ResolveTraySendStartPosition();
            var trayStartRotation = ResolveTraySendStartRotation();
            AvailableTrayCount = Mathf.Max(0, AvailableTrayCount - 1);
            RefreshQueueVisuals();
            soundService?.PlayJump();
            StartTrayTransfer(
                trayStartPosition,
                ResolveTrayEquipPosition(),
                trayStartRotation,
                ResolveTraySendTargetRotation(trayStartRotation),
                traySendDuration,
                incrementTrayCountOnComplete: false);
            return true;
        }

        private void BeginReturnTray(PigController pig, Vector3 startPosition)
        {
            if (pig == null || !pigsUsingTrayStack.Remove(pig))
            {
                return;
            }

            soundService?.PlayJump();
            var startRotation = ResolveTrayReturnStartRotation(pig);
            StartTrayTransfer(
                startPosition,
                ResolveTrayReturnTargetPosition(),
                startRotation,
                ResolveTrayReturnTargetRotation(startRotation),
                trayReturnDuration,
                incrementTrayCountOnComplete: true);
        }

        private void StartTrayTransfer(
            Vector3 startPosition,
            Vector3 endPosition,
            Quaternion startRotation,
            Quaternion endRotation,
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

            var trayTransform = CreateAnimatedTrayVisual(startPosition, startRotation);
            if (trayTransform == null)
            {
                if (incrementTrayCountOnComplete)
                {
                    CompleteTrayReturn();
                }

                return;
            }

            var transfer = new ActiveTrayTransfer(
                this,
                trayTransform,
                startPosition,
                endPosition,
                startRotation,
                endRotation,
                incrementTrayCountOnComplete);
            activeTrayTransfers.Add(transfer);
            transfer.Tween = Tween.Custom(
                    transfer,
                    0f,
                    1f,
                    Mathf.Max(0.01f, duration),
                    static (target, t) => target.Update(t),
                    Ease.Linear)
                .OnComplete(transfer, static target => target.Complete());
        }

        private Transform CreateAnimatedTrayVisual(Vector3 startPosition, Quaternion startRotation)
        {
            var template = ResolveTrayAnimationTemplate();
            if (template == null)
            {
                return null;
            }

            RefreshTrayTransferTemplate();

            Transform trayTransform;
            if (trayTransferVisualPool.Count > 0)
            {
                trayTransform = trayTransferVisualPool.Pop();
                if (trayTransform == null)
                {
                    return CreateAnimatedTrayVisual(startPosition, startRotation);
                }
            }
            else
            {
                trayTransform = InstantiateTrayTransferVisual(template);
                if (trayTransform == null)
                {
                    return null;
                }
            }

            var trayObject = trayTransform.gameObject;
            var environment = Environment;
            if (environment != null && trayTransform.parent != environment.transform)
            {
                trayTransform.SetParent(environment.transform, true);
            }

            trayObject.SetActive(true);
            trayTransform.position = startPosition;
            trayTransform.rotation = startRotation;

            var trayController = trayObject.GetComponent<TrayController>();
            if (trayController != null)
            {
                trayController.Configure(true, occupied: true);
            }

            return trayTransform;
        }

        private void UpdateTrayTransfer(ActiveTrayTransfer transfer, float t)
        {
            if (transfer?.Transform == null)
            {
                return;
            }

            var position = Vector3.Lerp(transfer.StartPosition, transfer.EndPosition, t);
            position.y += Mathf.Sin(t * Mathf.PI) * trayTransferArcHeight;
            transfer.Transform.position = position;
            transfer.Transform.rotation = Quaternion.SlerpUnclamped(transfer.StartRotation, transfer.EndRotation, t);
        }

        private void CompleteTrayTransfer(ActiveTrayTransfer transfer)
        {
            if (transfer == null)
            {
                return;
            }

            transfer.Tween = default;
            if (transfer.Transform != null)
            {
                transfer.Transform.position = transfer.EndPosition;
                transfer.Transform.rotation = transfer.EndRotation;
            }

            if (transfer.IncrementTrayCountOnComplete)
            {
                CompleteTrayReturn();
            }

            activeTrayTransfers.Remove(transfer);
            ReleaseAnimatedTrayVisual(transfer.Transform);
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
            var environment = Environment;
            if (trayStackVisuals.Count > 0 && AvailableTrayCount > 0)
            {
                var trayIndex = Mathf.Clamp(AvailableTrayCount - 1, 0, trayStackVisuals.Count - 1);
                var trayVisual = trayStackVisuals[trayIndex];
                if (trayVisual != null)
                {
                    return trayVisual.transform.position;
                }
            }

            return environment?.TrayRoot != null
                ? environment.TrayRoot.position
                : ResolveRuntimeFallbackPosition();
        }

        private Quaternion ResolveTraySendStartRotation()
        {
            CacheTrayStackVisuals();
            var environment = Environment;
            if (trayStackVisuals.Count > 0 && AvailableTrayCount > 0)
            {
                var trayIndex = Mathf.Clamp(AvailableTrayCount - 1, 0, trayStackVisuals.Count - 1);
                var trayVisual = trayStackVisuals[trayIndex];
                if (trayVisual != null)
                {
                    return trayVisual.transform.rotation;
                }
            }

            if (environment?.TrayRoot != null)
            {
                return environment.TrayRoot.rotation;
            }

            return trayTransferTemplate != null
                ? trayTransferTemplate.transform.rotation
                : Quaternion.identity;
        }

        private Quaternion ResolveTraySendTargetRotation(Quaternion fallbackStartRotation)
        {
            var environment = Environment;
            if (environment?.TrayEquipPos != null)
            {
                return environment.TrayEquipPos.rotation;
            }

            if (environment?.TrayDropPos != null)
            {
                return environment.TrayDropPos.rotation;
            }

            return fallbackStartRotation * Quaternion.Euler(0f, TraySendRotationDegrees, 0f);
        }

        private Vector3 ResolveTrayEquipPosition()
        {
            var environment = Environment;
            if (environment?.TrayEquipPos != null)
            {
                return environment.TrayEquipPos.position;
            }

            return resolveTrayEquipPosition != null
                ? resolveTrayEquipPosition()
                : ResolveRuntimeFallbackPosition();
        }

        private Vector3 ResolveTrayReturnTargetPosition()
        {
            CacheTrayStackVisuals();
            var environment = Environment;
            if (trayStackVisuals.Count > 0)
            {
                var trayIndex = Mathf.Clamp(AvailableTrayCount, 0, trayStackVisuals.Count - 1);
                var trayVisual = trayStackVisuals[trayIndex];
                if (trayVisual != null)
                {
                    return trayVisual.transform.position;
                }
            }

            return environment?.TrayRoot != null
                ? environment.TrayRoot.position
                : ResolveRuntimeFallbackPosition();
        }

        private Quaternion ResolveTrayReturnStartRotation(PigController pig)
        {
            var environment = Environment;
            if (environment?.TrayDropPos != null)
            {
                return environment.TrayDropPos.rotation;
            }

            return pig != null
                ? pig.transform.rotation
                : ResolveTraySendStartRotation();
        }

        private Quaternion ResolveTrayReturnTargetRotation(Quaternion fallbackStartRotation)
        {
            var environment = Environment;
            if (environment?.TrayRoot != null)
            {
                return environment.TrayRoot.rotation;
            }

            return fallbackStartRotation * Quaternion.Euler(0f, TrayReturnRotationDegrees, 0f);
        }

        private void CompleteTrayReturn()
        {
            AvailableTrayCount = Mathf.Min(ResolveMaxVisibleTrayCount(), AvailableTrayCount + 1);
            RefreshQueueVisuals();
            dispatchBurstPigs?.Invoke();
        }

        private void ResetTrayRuntimeState()
        {
            CancelTrayTransfers();
            foreach (var pig in pigsUsingTrayStack)
            {
                if (pig == null)
                {
                    continue;
                }

                UnsubscribePigLifecycle(pig);
            }

            pigsUsingTrayStack.Clear();
            AvailableTrayCount = 0;
            ClearTrayTransferPool();
        }

        private void CancelTrayTransfers()
        {
            if (activeTrayTransfers.Count == 0)
            {
                return;
            }

            var transfers = new ActiveTrayTransfer[activeTrayTransfers.Count];
            activeTrayTransfers.CopyTo(transfers);
            activeTrayTransfers.Clear();

            for (int i = 0; i < transfers.Length; i++)
            {
                var transfer = transfers[i];
                if (transfer == null)
                {
                    continue;
                }

                transfer.Stop();
                ReleaseAnimatedTrayVisual(transfer.Transform);
            }
        }

        private void RefreshTrayTransferTemplate()
        {
            var resolvedTemplate = ResolveTrayAnimationTemplate();
            if (trayTransferTemplate == resolvedTemplate)
            {
                return;
            }

            ClearTrayTransferPool();
            trayTransferTemplate = resolvedTemplate;
        }

        private void PrewarmTrayTransferVisuals(int desiredCount)
        {
            if (desiredCount <= 0 || trayTransferTemplate == null)
            {
                return;
            }

            while (trayTransferVisualPool.Count < desiredCount)
            {
                var trayTransform = InstantiateTrayTransferVisual(trayTransferTemplate);
                if (trayTransform == null)
                {
                    return;
                }

                ReleaseAnimatedTrayVisual(trayTransform);
            }
        }

        private Transform InstantiateTrayTransferVisual(GameObject template)
        {
            if (template == null)
            {
                return null;
            }

            var environment = Environment;
            var trayObject = UnityEngine.Object.Instantiate(
                template,
                environment != null ? environment.transform : null);
            trayObject.name = $"{template.name}_Runtime";
            return trayObject.transform;
        }

        private void ReleaseAnimatedTrayVisual(Transform trayTransform)
        {
            if (trayTransform == null)
            {
                return;
            }

            var environment = Environment;
            if (environment != null && trayTransform.parent != environment.transform)
            {
                trayTransform.SetParent(environment.transform, false);
            }

            trayTransform.gameObject.SetActive(false);
            trayTransferVisualPool.Push(trayTransform);
        }

        private void ClearTrayTransferPool()
        {
            while (trayTransferVisualPool.Count > 0)
            {
                var trayTransform = trayTransferVisualPool.Pop();
                if (trayTransform != null)
                {
                    UnityEngine.Object.Destroy(trayTransform.gameObject);
                }
            }
        }

        private Vector3 ResolveRuntimeFallbackPosition()
        {
            var environment = Environment;
            if (environment?.TrayRoot != null)
            {
                return environment.TrayRoot.position;
            }

            return resolveTrayEquipPosition != null
                ? resolveTrayEquipPosition()
                : Vector3.zero;
        }

        private int CompareTrayStackVisuals(TrayController left, TrayController right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var trayRoot = Environment?.TrayRoot;
            if (trayRoot == null)
            {
                return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
            }

            var leftLocalX = trayRoot.InverseTransformPoint(left.transform.position).x;
            var rightLocalX = trayRoot.InverseTransformPoint(right.transform.position).x;
            var xComparison = leftLocalX.CompareTo(rightLocalX);
            if (xComparison != 0)
            {
                return xComparison;
            }

            return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
        }

        private void ResetWaitingLaneState()
        {
            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                waitingLanes[laneIndex].Clear();
            }

            for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                holdingPigs[slotIndex] = null;
            }

            pigLaneLookup.Clear();
            pigHoldingLookup.Clear();
            queuedPigs.Clear();
            ClearPendingLaneEntries();
        }

        private void ClearPendingLaneEntries()
        {
            for (int laneIndex = 0; laneIndex < pendingLaneEntries.Count; laneIndex++)
            {
                pendingLaneEntries[laneIndex].Clear();
            }

            pendingQueueEntries.Clear();
        }

        private int CountHoldingPigs()
        {
            var count = 0;
            for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                if (holdingPigs[slotIndex] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private int ResolveFirstAvailableHoldingSlotIndex()
        {
            EnsureHoldingSlotCount();
            for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
            {
                if (holdingPigs[slotIndex] == null)
                {
                    return slotIndex;
                }
            }

            return -1;
        }

        private bool TryGetDeckLaneIndex(PigController pig, out int laneIndex)
        {
            if (pig != null
                && pigLaneLookup.TryGetValue(pig, out laneIndex)
                && laneIndex >= 0
                && laneIndex < waitingLanes.Count
                && waitingLanes[laneIndex].Contains(pig))
            {
                return true;
            }

            laneIndex = -1;
            return false;
        }

        private bool TryGetHoldingSlotIndex(PigController pig, out int slotIndex)
        {
            if (pig != null
                && pigHoldingLookup.TryGetValue(pig, out slotIndex)
                && slotIndex >= 0
                && slotIndex < holdingPigs.Count
                && holdingPigs[slotIndex] == pig)
            {
                return true;
            }

            slotIndex = -1;
            return false;
        }

        private void HandlePigConveyorLoopCompleted(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            unregisterConveyorPig?.Invoke(pig);
            if (!pig.HasAmmo)
            {
                HandlePigDepleted(pig);
                return;
            }

            if (!TryAssignPigToHoldingSlot(pig))
            {
                BeginReturnTray(pig, ResolveTrayDropStartPosition(pig));
                pig.ClearWaitingAnchor();
                pig.SetQueued(false);
                pig.HideTray();
                releasePig?.Invoke(pig);
                triggerLevelFail?.Invoke();
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
            dispatchBurstPigs?.Invoke();
        }

        private void HandlePigDispatchToBeltCompleted(PigController pig)
        {
            if (pig != null)
            {
                pig.DispatchToBeltCompleted -= HandlePigDispatchToBeltCompleted;
            }

            RefreshQueueVisuals();
        }

        private bool TryAssignPigToHoldingSlot(PigController pig)
        {
            if (pig == null)
            {
                return false;
            }

            var slotIndex = ResolveFirstAvailableHoldingSlotIndex();
            if (slotIndex < 0 || slotIndex >= holdingPigs.Count)
            {
                return false;
            }

            holdingPigs[slotIndex] = pig;
            pigHoldingLookup[pig] = slotIndex;
            return true;
        }

        private void HandlePigBeltDepleteCompleted(PigController pig)
        {
            if (pig != null)
            {
                pig.BeltDepleteCompleted -= HandlePigBeltDepleteCompleted;
            }

            RestoreBorrowedTrayToStack(pig);
            releasePig?.Invoke(pig);
            RefreshQueueVisuals();
            dispatchBurstPigs?.Invoke();
        }

        private void TrySpawnPendingPigForLane(int laneIndex)
        {
            if (laneIndex < 0
                || laneIndex >= pendingLaneEntries.Count
                || laneIndex >= waitingLanes.Count
                || spawnQueuedPig == null)
            {
                return;
            }

            var laneEntries = pendingLaneEntries[laneIndex];
            if (laneEntries == null || laneEntries.Count == 0)
            {
                return;
            }

            var pig = spawnQueuedPig(laneEntries[0], laneIndex);
            if (pig == null)
            {
                return;
            }

            pendingQueueEntries.Remove(laneEntries[0]);
            laneEntries.RemoveAt(0);

            pigLaneLookup[pig] = laneIndex;
            waitingLanes[laneIndex].Add(pig);

            var environment = Environment;
            var holdingSlot = environment?.GetHoldingSlot(laneIndex, activeOnly: true)
                ?? environment?.GetHoldingSlot(laneIndex, activeOnly: false);
            if (holdingSlot == null)
            {
                pig.ClearWaitingAnchor();
                return;
            }

            var depthSpacing = ResolveDeckDepthSpacing();
            var depthIndex = waitingLanes[laneIndex].Count - 1;
            pig.AssignWaitingAnchor(
                holdingSlot,
                snapImmediately: true,
                ResolveQueuedPigWorldOffset(holdingSlot, depthIndex, depthSpacing));
            pig.SetQueued(true, snapImmediately: true);
            pig.SetOnBelt(false);
        }

        private void RestoreBorrowedTrayToStack(PigController pig)
        {
            if (pig == null || !pigsUsingTrayStack.Remove(pig))
            {
                return;
            }

            soundService?.PlayJump();
            CompleteTrayReturn();
        }

        private Vector3 ResolveTrayDropStartPosition(PigController pig)
        {
            var environment = Environment;
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

        private sealed class ActiveTrayTransfer
        {
            private readonly GameManagerTrayQueueCoordinator owner;

            public ActiveTrayTransfer(
                GameManagerTrayQueueCoordinator owner,
                Transform transform,
                Vector3 startPosition,
                Vector3 endPosition,
                Quaternion startRotation,
                Quaternion endRotation,
                bool incrementTrayCountOnComplete)
            {
                this.owner = owner;
                Transform = transform;
                StartPosition = startPosition;
                EndPosition = endPosition;
                StartRotation = startRotation;
                EndRotation = endRotation;
                IncrementTrayCountOnComplete = incrementTrayCountOnComplete;
            }

            public Transform Transform { get; }
            public Vector3 StartPosition { get; }
            public Vector3 EndPosition { get; }
            public Quaternion StartRotation { get; }
            public Quaternion EndRotation { get; }
            public bool IncrementTrayCountOnComplete { get; }
            public Tween Tween { get; set; }

            public void Update(float t)
            {
                owner.UpdateTrayTransfer(this, t);
            }

            public void Complete()
            {
                owner.CompleteTrayTransfer(this);
            }

            public void Stop()
            {
                if (Tween.isAlive)
                {
                    Tween.Stop();
                    Tween = default;
                }
            }
        }
    }
}
