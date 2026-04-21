using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Pigs;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class PigRendererVisibilityCoordinator
    {
        private readonly GameObject owner;
        private readonly Func<Camera> gameplayCameraProvider;

        private bool cullOffscreenPigRenderers;
        private float viewportPadding;

        public PigRendererVisibilityCoordinator(GameObject owner, Func<Camera> gameplayCameraProvider)
        {
            this.owner = owner;
            this.gameplayCameraProvider = gameplayCameraProvider;
        }

        public void Configure(bool cullOffscreenPigRenderers, float viewportPadding)
        {
            this.cullOffscreenPigRenderers = cullOffscreenPigRenderers;
            this.viewportPadding = Mathf.Clamp(viewportPadding, 0f, 0.5f);
        }

        public void UpdateVisibility(
            IReadOnlyList<List<PigController>> waitingLanes,
            IReadOnlyList<PigController> holdingPigs,
            IReadOnlyList<PigController> activeConveyorPigs)
        {
            if (!cullOffscreenPigRenderers)
            {
                SetWaitingLaneRendererVisibility(waitingLanes, true);
                SetPigRendererVisibility(holdingPigs, true);
                SetPigRendererVisibility(activeConveyorPigs, true);
                return;
            }

            var activeCamera = ResolveGameplayCamera();
            if (activeCamera == null)
            {
                SetWaitingLaneRendererVisibility(waitingLanes, true);
                SetPigRendererVisibility(holdingPigs, true);
                SetPigRendererVisibility(activeConveyorPigs, true);
                return;
            }

            ApplyWaitingLaneVisibility(waitingLanes, activeCamera);
            ApplyPigRendererVisibility(holdingPigs, activeCamera);
            ApplyPigRendererVisibility(activeConveyorPigs, activeCamera);
        }

        private void ApplyWaitingLaneVisibility(IReadOnlyList<List<PigController>> waitingLanes, Camera activeCamera)
        {
            if (waitingLanes == null)
            {
                return;
            }

            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                ApplyPigRendererVisibility(waitingLanes[laneIndex], activeCamera);
            }
        }

        private void ApplyPigRendererVisibility(IReadOnlyList<PigController> pigs, Camera activeCamera)
        {
            if (pigs == null)
            {
                return;
            }

            for (int i = 0; i < pigs.Count; i++)
            {
                var pig = pigs[i];
                if (pig == null)
                {
                    continue;
                }

                pig.UpdateRendererVisibility(activeCamera, viewportPadding);
            }
        }

        private static void SetWaitingLaneRendererVisibility(IReadOnlyList<List<PigController>> waitingLanes, bool visible)
        {
            if (waitingLanes == null)
            {
                return;
            }

            for (int laneIndex = 0; laneIndex < waitingLanes.Count; laneIndex++)
            {
                SetPigRendererVisibility(waitingLanes[laneIndex], visible);
            }
        }

        private static void SetPigRendererVisibility(IReadOnlyList<PigController> pigs, bool visible)
        {
            if (pigs == null)
            {
                return;
            }

            for (int i = 0; i < pigs.Count; i++)
            {
                var pig = pigs[i];
                if (pig == null)
                {
                    continue;
                }

                pig.SetRenderersVisible(visible);
            }
        }

        private Camera ResolveGameplayCamera()
        {
            var inputCamera = gameplayCameraProvider != null ? gameplayCameraProvider() : null;
            if (inputCamera != null && inputCamera.gameObject.scene == owner.scene)
            {
                return inputCamera;
            }

            var sceneMainCamera = Camera.main;
            if (sceneMainCamera != null && sceneMainCamera.gameObject.scene == owner.scene)
            {
                return sceneMainCamera;
            }

            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var candidate = cameras[i];
                if (candidate != null && candidate.gameObject.scene == owner.scene)
                {
                    return candidate;
                }
            }

            return sceneMainCamera;
        }
    }
}
