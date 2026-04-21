using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Audio;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Levels;
using System;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PixelFlow.Runtime.Managers
{
    [DisallowMultipleComponent]
    public sealed class InputManager : MonoBehaviour
    {
        private const int MaxPigHitCount = 16;

        [SerializeField, HideInInspector] private Camera inputCamera;
        [SerializeField] private LayerMask pigLayerMask = 1 << 6;
        [SerializeField, Min(1f)] private float maxRayDistance = 500f;

        private readonly RaycastHit[] pigHitBuffer = new RaycastHit[MaxPigHitCount];
        private Selectable[] selectableBuffer = Array.Empty<Selectable>();

        private GameManager gameManager;
        private LevelSessionController levelSessionController;
        private ISoundService soundService;

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
            if (gameManager == null
                || levelSessionController == null
                || !levelSessionController.AcceptsInput)
            {
                return;
            }

            if (!TryGetPrimaryPointerReleased(out var screenPosition))
            {
                return;
            }

            if (IsPointerOverBlockingUi(screenPosition))
            {
                return;
            }

            var activeCamera = ResolveActiveCamera();
            if (activeCamera == null)
            {
                return;
            }

            if (!TryResolveClickedDispatchablePig(activeCamera, screenPosition, out var pig))
            {
                return;
            }

            if (gameManager.TryDispatchPig(pig))
            {
                soundService?.PlayPigSelect();
            }
        }

        [Inject]
        public void InjectProjectSettings(ProjectRuntimeSettings settings)
        {
            ConfigureFromProjectSettings(settings);
        }

        [Inject]
        public void InjectSceneDependencies(
            GameManager injectedGameManager,
            LevelSessionController injectedLevelSessionController,
            ISoundService injectedSoundService)
        {
            gameManager = injectedGameManager;
            levelSessionController = injectedLevelSessionController;
            soundService = injectedSoundService;
        }

        private void ConfigureFromProjectSettings(ProjectRuntimeSettings settings)
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

        public void SetInputCamera(Camera resolvedCamera)
        {
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
            if (HasValidSceneCamera(inputCamera))
            {
                return inputCamera;
            }

            EnsureInputCameraReference();
            return inputCamera;
        }

        private void EnsureInputCameraReference(bool preferSceneMain = false)
        {
            if (!preferSceneMain && HasValidSceneCamera(inputCamera))
            {
                return;
            }

            var sceneMainCamera = Camera.main;
            if (HasValidSceneCamera(sceneMainCamera))
            {
                inputCamera = sceneMainCamera;
                return;
            }

            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var candidate = cameras[i];
                if (HasValidSceneCamera(candidate))
                {
                    inputCamera = candidate;
                    return;
                }
            }

            inputCamera = sceneMainCamera;
        }

        private bool HasValidSceneCamera(Camera camera)
        {
            return camera != null
                && camera.gameObject.scene == gameObject.scene;
        }

        private void EnsurePigLayerMask()
        {
            var pigLayer = LayerMask.NameToLayer("Pig");
            if (pigLayerMask.value == 0)
            {
                pigLayerMask = pigLayer >= 0
                    ? 1 << pigLayer
                    : 1 << 6;
                return;
            }

            if (pigLayer >= 0)
            {
                pigLayerMask |= 1 << pigLayer;
            }
        }

        private bool TryResolveClickedDispatchablePig(Camera activeCamera, Vector2 screenPosition, out PigController pig)
        {
            pig = null;
            if (activeCamera == null || gameManager == null)
            {
                return false;
            }

            var ray = activeCamera.ScreenPointToRay(screenPosition);
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                pigHitBuffer,
                maxRayDistance,
                pigLayerMask,
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return false;
            }

            var closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                var hitPig = pigHitBuffer[i].collider != null
                    ? pigHitBuffer[i].collider.GetComponentInParent<PigController>()
                    : null;
                if (hitPig == null)
                {
                    continue;
                }

                if (!gameManager.TryResolveDispatchCandidate(hitPig, out var dispatchPig))
                {
                    continue;
                }

                if (pigHitBuffer[i].distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = pigHitBuffer[i].distance;
                pig = dispatchPig;
            }

            return pig != null;
        }

        private static bool TryGetPrimaryPointerReleased(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonUp(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            screenPosition = default;
            return false;
        }

        private bool IsPointerOverBlockingUi(Vector2 screenPosition)
        {
            var selectableCount = Selectable.allSelectableCount;
            if (selectableCount <= 0)
            {
                return false;
            }

            EnsureSelectableBufferCapacity(selectableCount);
            var copiedCount = Selectable.AllSelectablesNoAlloc(selectableBuffer);
            for (int i = 0; i < copiedCount; i++)
            {
                var selectable = selectableBuffer[i];
                if (!TryGetBlockingSelectableRect(selectable, out var rectTransform, out var eventCamera))
                {
                    continue;
                }

                if (!RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, eventCamera))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void EnsureSelectableBufferCapacity(int requiredCapacity)
        {
            if (selectableBuffer.Length >= requiredCapacity)
            {
                return;
            }

            selectableBuffer = new Selectable[Mathf.NextPowerOfTwo(requiredCapacity)];
        }

        private static bool TryGetBlockingSelectableRect(
            Selectable selectable,
            out RectTransform rectTransform,
            out Camera eventCamera)
        {
            rectTransform = null;
            eventCamera = null;
            if (selectable == null || !selectable.isActiveAndEnabled)
            {
                return false;
            }

            var targetGraphic = selectable.targetGraphic;
            if (targetGraphic != null)
            {
                if (!targetGraphic.isActiveAndEnabled || !targetGraphic.raycastTarget)
                {
                    return false;
                }

                rectTransform = targetGraphic.rectTransform;
                var canvas = targetGraphic.canvas;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    eventCamera = canvas.worldCamera;
                }
            }
            else
            {
                rectTransform = selectable.transform as RectTransform;
            }

            return rectTransform != null;
        }
    }
}
