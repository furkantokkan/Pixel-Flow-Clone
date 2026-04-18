using PixelFlow.Runtime.Pigs;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PixelFlow.Runtime.Managers
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowInputManager : MonoBehaviour
    {
        [SerializeField] private Camera inputCamera;
        [SerializeField] private LayerMask pigLayerMask = 1 << 7;
        [SerializeField, Min(1f)] private float maxRayDistance = 500f;

        private PixelFlowGameManager gameManager;

        public Camera InputCamera => inputCamera;

        private void OnValidate()
        {
            EnsurePigLayerMask();
        }

        private void Update()
        {
            var activeCamera = inputCamera != null ? inputCamera : Camera.main;
            if (gameManager == null || activeCamera == null)
            {
                return;
            }

            if (!TryGetPrimaryPointerDown(out var screenPosition))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, maxRayDistance, pigLayerMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var pig = hit.collider.GetComponentInParent<PixelFlowPigController>();
            if (pig != null)
            {
                gameManager.TryQueuePig(pig);
            }
        }

        public void Construct(PixelFlowGameManager manager, Camera resolvedCamera)
        {
            gameManager = manager;
            if (resolvedCamera != null)
            {
                inputCamera = resolvedCamera;
            }

            EnsurePigLayerMask();
        }

        private void EnsurePigLayerMask()
        {
            if (pigLayerMask.value != 0)
            {
                return;
            }

            var pigLayer = LayerMask.NameToLayer("Pig");
            pigLayerMask = pigLayer >= 0
                ? 1 << pigLayer
                : 1 << 7;
        }

        private static bool TryGetPrimaryPointerDown(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            screenPosition = default;
            return false;
        }
    }
}
