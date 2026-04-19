using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Composition;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PixelFlow.Runtime.Managers
{
    [DisallowMultipleComponent]
    public sealed class InputManager : MonoBehaviour
    {
        [SerializeField, HideInInspector] private Camera inputCamera;
        [SerializeField] private LayerMask pigLayerMask = 1 << 7;
        [SerializeField, Min(1f)] private float maxRayDistance = 500f;

        private GameManager gameManager;

        public Camera InputCamera => ResolveActiveCamera();

        private void Awake()
        {
            EnsureInputCameraReference();
        }

        private void Reset()
        {
            EnsureInputCameraReference();
            EnsurePigLayerMask();
        }

        private void OnValidate()
        {
            EnsureInputCameraReference();
            EnsurePigLayerMask();
        }

        private void Update()
        {
            var activeCamera = ResolveActiveCamera();
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

            var pig = hit.collider.GetComponentInParent<PigController>();
            if (pig != null)
            {
                gameManager.TryQueuePig(pig);
            }
        }

        [Inject]
        public void InjectProjectSettings(ProjectRuntimeSettings settings)
        {
            ApplyProjectSettings(settings);
        }

        public void ApplyProjectSettings(ProjectRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.PigLayerMask.value != 0)
            {
                pigLayerMask = settings.PigLayerMask;
            }

            maxRayDistance = Mathf.Max(1f, settings.MaxRayDistance);
            EnsurePigLayerMask();
        }

        public void Construct(GameManager manager, Camera resolvedCamera)
        {
            gameManager = manager;
            if (resolvedCamera != null)
            {
                inputCamera = resolvedCamera;
            }

            EnsureInputCameraReference(preferSceneMain: true);
            EnsurePigLayerMask();
        }

        public void RefreshInputCameraReference(bool preferSceneMain = false)
        {
            EnsureInputCameraReference(preferSceneMain);
        }

        private Camera ResolveActiveCamera()
        {
            EnsureInputCameraReference();
            return inputCamera != null ? inputCamera : Camera.main;
        }

        private void EnsureInputCameraReference(bool preferSceneMain = false)
        {
            var sceneMainCamera = Camera.main;
            if (sceneMainCamera != null && sceneMainCamera.gameObject.scene == gameObject.scene)
            {
                inputCamera = sceneMainCamera;
                return;
            }

            if (!preferSceneMain && inputCamera != null && inputCamera.gameObject.scene == gameObject.scene)
            {
                return;
            }

            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var candidate = cameras[i];
                if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                {
                    inputCamera = candidate;
                    return;
                }
            }

            inputCamera = sceneMainCamera;
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
