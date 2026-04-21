using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Pigs;
using PrimeTween;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerBurstCoordinator
    {
        private readonly Func<bool, PigController> dispatchNextPig;
        private readonly Func<IReadOnlyList<List<PigController>>> waitingLanesProvider;
        private readonly Func<IReadOnlyList<PigController>> holdingPigsProvider;
        private readonly Func<IReadOnlyList<PigController>> activeConveyorPigsProvider;

        private Tween burstRampTween;
        private float burstFollowSpeedMultiplier = 1f;
        private float burstRampDuration = 0.01f;
        private float burstFireIntervalMultiplier = 1f;
        private float burstRampProgress;

        public GameManagerBurstCoordinator(
            Func<bool, PigController> dispatchNextPig,
            Func<IReadOnlyList<List<PigController>>> waitingLanesProvider,
            Func<IReadOnlyList<PigController>> holdingPigsProvider,
            Func<IReadOnlyList<PigController>> activeConveyorPigsProvider)
        {
            this.dispatchNextPig = dispatchNextPig;
            this.waitingLanesProvider = waitingLanesProvider;
            this.holdingPigsProvider = holdingPigsProvider;
            this.activeConveyorPigsProvider = activeConveyorPigsProvider;
        }

        public bool IsActive { get; private set; }

        public void Configure(float followSpeedMultiplier, float rampDuration, float fireIntervalMultiplier)
        {
            burstFollowSpeedMultiplier = Mathf.Max(1f, followSpeedMultiplier);
            burstRampDuration = Mathf.Max(0.01f, rampDuration);
            burstFireIntervalMultiplier = Mathf.Max(0.01f, fireIntervalMultiplier);
            ApplyBurstModifiersToAllKnownPigs();
        }

        public void SetActive(bool active)
        {
            if (active)
            {
                if (IsActive)
                {
                    OnDispatchOpportunity();
                    return;
                }

                IsActive = true;
                StartBurstRamp();
                OnDispatchOpportunity();
                return;
            }

            if (!IsActive && !burstRampTween.isAlive && burstRampProgress <= 0f)
            {
                return;
            }

            Reset();
        }

        public void OnDispatchOpportunity()
        {
            if (!IsActive)
            {
                return;
            }

            TryDispatchNextPig();
        }

        public void Update()
        {
            if (!IsActive)
            {
                return;
            }

            TryDispatchNextPig();
        }

        public void ApplyToPig(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            pig.SetBeltSpeedModifiers(
                Mathf.Lerp(1f, burstFollowSpeedMultiplier, burstRampProgress),
                Mathf.Lerp(1f, burstFireIntervalMultiplier, burstRampProgress));
        }

        public void Reset()
        {
            IsActive = false;
            burstRampProgress = 0f;
            StopBurstRamp();
            ApplyBurstModifiersToAllKnownPigs();
        }

        private void StartBurstRamp()
        {
            StopBurstRamp();
            burstRampProgress = 0f;
            ApplyBurstModifiersToAllKnownPigs();

            if (!Application.isPlaying || burstRampDuration <= 0.01f)
            {
                burstRampProgress = 1f;
                ApplyBurstModifiersToAllKnownPigs();
                return;
            }

            burstRampTween = Tween.Custom(
                    this,
                    0f,
                    1f,
                    burstRampDuration,
                    (target, linearProgress) =>
                    {
                        target.burstRampProgress = 1f - Mathf.Pow(1f - linearProgress, 2f);
                        target.ApplyBurstModifiersToAllKnownPigs();
                    },
                    Ease.Linear)
                .OnComplete(this, target =>
                {
                    target.burstRampTween = default;
                    target.burstRampProgress = 1f;
                    target.ApplyBurstModifiersToAllKnownPigs();
                });
        }

        private void StopBurstRamp()
        {
            if (!burstRampTween.isAlive)
            {
                return;
            }

            burstRampTween.Stop();
            burstRampTween = default;
        }

        private void ApplyBurstModifiersToAllKnownPigs()
        {
            var waitingLanes = waitingLanesProvider();
            if (waitingLanes != null)
            {
                for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
                {
                    var lane = waitingLanes[laneIndex];
                    if (lane == null)
                    {
                        continue;
                    }

                    for (int pigIndex = 0; pigIndex < lane.Count; pigIndex++)
                    {
                        ApplyToPig(lane[pigIndex]);
                    }
                }
            }

            var holdingPigs = holdingPigsProvider();
            if (holdingPigs != null)
            {
                for (int slotIndex = 0; slotIndex < holdingPigs.Count; slotIndex++)
                {
                    ApplyToPig(holdingPigs[slotIndex]);
                }
            }

            var activeConveyorPigs = activeConveyorPigsProvider();
            if (activeConveyorPigs == null)
            {
                return;
            }

            for (int pigIndex = 0; pigIndex < activeConveyorPigs.Count; pigIndex++)
            {
                ApplyToPig(activeConveyorPigs[pigIndex]);
            }
        }

        private void TryDispatchNextPig()
        {
            if (HasPigDispatchingToBelt())
            {
                return;
            }

            dispatchNextPig(true);
        }

        private bool HasPigDispatchingToBelt()
        {
            var activeConveyorPigs = activeConveyorPigsProvider();
            if (activeConveyorPigs == null)
            {
                return false;
            }

            for (int i = 0; i < activeConveyorPigs.Count; i++)
            {
                var pig = activeConveyorPigs[i];
                if (pig != null && pig.State == PigState.DispatchingToBelt)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
