using System.Collections.Generic;
using Dreamteck.Splines;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowGameManager : MonoBehaviour
    {
        private static readonly Vector3 QueuedPigWorldOffset = new(0f, 0.6f, 0f);
        private static readonly Vector3 TrayIndicatorLocalPosition = Vector3.zero;
        private static readonly Quaternion TrayIndicatorLocalRotation = Quaternion.identity;
        private static readonly Vector3 TrayIndicatorLocalScale = Vector3.one;

        [SerializeField] private GameObject traySlotPrefab;
        [SerializeField, Min(0.01f)] private float dispatchFollowSpeed = 7f;

        private readonly List<PixelFlowPigController> queuedPigs = new();
        private readonly List<GameObject> trayIndicators = new();
        private PixelFlowEnvironmentContext environment;
        private SplineComputer dispatchSpline;
        private Transform orbitTarget;
        private int lastKnownCapacity = -1;

        public IReadOnlyList<PixelFlowPigController> QueuedPigs => queuedPigs;
        public int QueueCount => queuedPigs.Count;
        public int QueueCapacity => environment != null ? environment.ActiveHoldingContainerCount : 0;

        private void Reset()
        {
            TryAutoAssignTrayPrefab();
        }

        private void OnValidate()
        {
            TryAutoAssignTrayPrefab();
        }

        private void LateUpdate()
        {
            if (environment == null)
            {
                return;
            }

            var capacity = QueueCapacity;
            if (capacity != lastKnownCapacity)
            {
                RefreshQueueVisuals();
            }
        }

        public void Construct(PixelFlowEnvironmentContext resolvedEnvironment)
        {
            environment = resolvedEnvironment;
            if (environment != null)
            {
                environment.ResolveMissingReferences();
            }

            ResolveEnvironmentReferences();

            EnsureTrayIndicators();
            environment?.EnsureTrayCounterText();
            RefreshQueueVisuals();
        }

        public bool TryQueuePig(PixelFlowPigController pig)
        {
            if (pig == null || environment == null)
            {
                return false;
            }

            if (queuedPigs.Contains(pig))
            {
                return false;
            }

            if (queuedPigs.Count >= QueueCapacity)
            {
                return false;
            }

            queuedPigs.Add(pig);
            pig.SetQueued(true);
            RealignQueuedPigs();
            RefreshQueueVisuals();
            return true;
        }

        public PixelFlowPigController DispatchNextPigToSpline()
        {
            if (queuedPigs.Count == 0)
            {
                return null;
            }

            var pig = queuedPigs[0];
            queuedPigs.RemoveAt(0);
            pig.SetQueued(false);
            pig.SetOnBelt(true);

            ResolveEnvironmentReferences();
            if (dispatchSpline != null)
            {
                pig.FollowSpline(dispatchSpline, 0.0, 1.0, dispatchFollowSpeed, orbitTarget);
            }

            RealignQueuedPigs();
            RefreshQueueVisuals();
            return pig;
        }

        public void ClearQueue()
        {
            for (int i = 0; i < queuedPigs.Count; i++)
            {
                queuedPigs[i].SetQueued(false);
                queuedPigs[i].ClearWaitingAnchor();
            }

            queuedPigs.Clear();
            RefreshQueueVisuals();
        }

        private void RefreshQueueVisuals()
        {
            if (environment == null)
            {
                return;
            }

            TrimQueueToCapacity();
            EnsureTrayIndicators();

            for (int i = 0; i < trayIndicators.Count; i++)
            {
                var slot = environment.GetHoldingSlot(i);
                trayIndicators[i].SetActive(slot != null && slot.gameObject.activeSelf);
            }

            var trayCounterText = environment.EnsureTrayCounterText();
            if (trayCounterText != null)
            {
                trayCounterText.text = $"{queuedPigs.Count}/{QueueCapacity}";
            }

            lastKnownCapacity = QueueCapacity;
            RealignQueuedPigs();
        }

        private void RealignQueuedPigs()
        {
            if (environment == null)
            {
                return;
            }

            for (int i = 0; i < queuedPigs.Count; i++)
            {
                var slot = environment.GetHoldingSlot(i, activeOnly: true);
                var pig = queuedPigs[i];
                pig.AssignWaitingAnchor(slot, snapImmediately: true, QueuedPigWorldOffset);
                pig.SetQueued(true, snapImmediately: true);
            }
        }

        private void TrimQueueToCapacity()
        {
            var capacity = QueueCapacity;
            if (capacity < 0)
            {
                capacity = 0;
            }

            while (queuedPigs.Count > capacity)
            {
                var lastIndex = queuedPigs.Count - 1;
                var pig = queuedPigs[lastIndex];
                queuedPigs.RemoveAt(lastIndex);
                pig.SetQueued(false);
                pig.ClearWaitingAnchor();
            }
        }

        private void EnsureTrayIndicators()
        {
            if (environment == null || traySlotPrefab == null || environment.HoldingContainer == null)
            {
                ClearTrayIndicators();
                return;
            }

            if (!NeedsTrayIndicatorRebuild())
            {
                return;
            }

            ClearTrayIndicators();

            for (int i = 0; i < environment.HoldingContainer.childCount; i++)
            {
                var slot = environment.HoldingContainer.GetChild(i);
                var trayInstance = Instantiate(traySlotPrefab, slot, false);
                trayInstance.name = $"QueueTray_{i + 1}";
                trayInstance.transform.localPosition = TrayIndicatorLocalPosition;
                trayInstance.transform.localRotation = TrayIndicatorLocalRotation;
                trayInstance.transform.localScale = TrayIndicatorLocalScale;
                trayIndicators.Add(trayInstance);
            }
        }

        private void ClearTrayIndicators()
        {
            if (trayIndicators.Count == 0)
            {
                return;
            }

            for (int i = 0; i < trayIndicators.Count; i++)
            {
                if (trayIndicators[i] != null)
                {
                    Destroy(trayIndicators[i]);
                }
            }

            trayIndicators.Clear();
        }

        private bool NeedsTrayIndicatorRebuild()
        {
            if (environment == null || environment.HoldingContainer == null)
            {
                return trayIndicators.Count > 0;
            }

            if (trayIndicators.Count != environment.HoldingContainer.childCount)
            {
                return true;
            }

            for (int i = 0; i < trayIndicators.Count; i++)
            {
                var slot = environment.HoldingContainer.GetChild(i);
                var tray = trayIndicators[i];
                if (tray == null || tray.transform.parent != slot)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveEnvironmentReferences()
        {
            dispatchSpline = environment != null
                ? environment.DispatchSpline
                : null;
            orbitTarget = environment != null
                ? (environment.TrayDropPos != null ? environment.TrayDropPos : environment.TrayEquipPos)
                : null;
        }

        private void TryAutoAssignTrayPrefab()
        {
#if UNITY_EDITOR
            if (traySlotPrefab != null)
            {
                return;
            }

            var trayGuids = UnityEditor.AssetDatabase.FindAssets("t:Prefab Tray");
            if (trayGuids == null || trayGuids.Length == 0)
            {
                return;
            }

            var trayPath = UnityEditor.AssetDatabase.GUIDToAssetPath(trayGuids[0]);
            traySlotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(trayPath);
#endif
        }
    }
}
