using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Levels
{
    internal sealed class LevelSessionOutcomeTracker
    {
        private const float OutcomeSafetyPollIntervalSeconds = 0.25f;

        private readonly HashSet<PigController> observedOutcomePigs = new();
        private readonly LevelOutcomeEvaluator outcomeEvaluator = new();

        private bool outcomeEvaluationDirty = true;
        private float nextOutcomeSafetyPollTime = -1f;

        public void Invalidate()
        {
            outcomeEvaluationDirty = true;
            nextOutcomeSafetyPollTime = -1f;
        }

        public bool ShouldEvaluate()
        {
            if (outcomeEvaluationDirty)
            {
                outcomeEvaluationDirty = false;
                nextOutcomeSafetyPollTime = Application.isPlaying
                    ? Time.unscaledTime + OutcomeSafetyPollIntervalSeconds
                    : -1f;
                return true;
            }

            if (!Application.isPlaying)
            {
                return false;
            }

            if (nextOutcomeSafetyPollTime >= 0f
                && Time.unscaledTime < nextOutcomeSafetyPollTime)
            {
                return false;
            }

            nextOutcomeSafetyPollTime = Time.unscaledTime + OutcomeSafetyPollIntervalSeconds;
            return true;
        }

        public LevelOutcomeDecision Evaluate(
            int remainingTargetBlocks,
            bool isBurstActive,
            bool isHoldingContainerFilled,
            bool allowBurstEntry,
            IReadOnlyList<BlockVisual> spawnedBlocks,
            IReadOnlyList<PigController> spawnedPigs,
            IReadOnlyList<PigQueueEntry> pendingQueueEntries)
        {
            return outcomeEvaluator.Evaluate(
                remainingTargetBlocks,
                isBurstActive,
                isHoldingContainerFilled,
                allowBurstEntry,
                spawnedBlocks,
                spawnedPigs,
                pendingQueueEntries);
        }

        public bool HasPendingTargetResolution(IReadOnlyList<BlockVisual> spawnedBlocks)
        {
            return LevelOutcomeEvaluator.HasPendingTargetResolution(spawnedBlocks);
        }

        public bool HasPendingPigAction(IReadOnlyList<PigController> spawnedPigs)
        {
            return LevelOutcomeEvaluator.HasPendingPigAction(spawnedPigs);
        }

        public void SubscribeToCurrentOutcomePigs(
            IReadOnlyList<PigController> pigs,
            Action<PigController> handler)
        {
            if (pigs == null)
            {
                return;
            }

            for (int i = 0; i < pigs.Count; i++)
            {
                SubscribeToOutcomePig(pigs[i], handler);
            }
        }

        public void SubscribeToOutcomePig(PigController pig, Action<PigController> handler)
        {
            if (pig == null || handler == null || !observedOutcomePigs.Add(pig))
            {
                return;
            }

            pig.OutcomeChanged -= handler;
            pig.OutcomeChanged += handler;
        }

        public void UnsubscribeFromOutcomePig(PigController pig, Action<PigController> handler)
        {
            if (pig == null || handler == null || !observedOutcomePigs.Remove(pig))
            {
                return;
            }

            pig.OutcomeChanged -= handler;
        }

        public void UnsubscribeFromOutcomePigs(Action<PigController> handler)
        {
            if (handler == null || observedOutcomePigs.Count == 0)
            {
                return;
            }

            foreach (var pig in observedOutcomePigs)
            {
                if (pig != null)
                {
                    pig.OutcomeChanged -= handler;
                }
            }

            observedOutcomePigs.Clear();
        }
    }
}
