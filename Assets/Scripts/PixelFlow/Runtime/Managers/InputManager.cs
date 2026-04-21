using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Levels;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private static readonly List<RaycastResult> UiRaycastResults = new();

        [SerializeField, HideInInspector] private Camera inputCamera;
        [SerializeField] private LayerMask pigLayerMask = 1 << 7;
        [SerializeField, Min(1f)] private float maxRayDistance = 500f;

        private GameManager gameManager;
        private LevelSessionController levelSessionController;

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
            if (gameManager == null
                || activeCamera == null
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

            if (!TryResolveClickedDispatchablePig(activeCamera, screenPosition, out var pig))
            {
                return;
            }

            gameManager.TryDispatchPig(pig);
        }

        [Inject]
        public void InjectProjectSettings(ProjectRuntimeSettings settings)
        {
            ConfigureFromProjectSettings(settings);
        }

        [Inject]
        public void InjectSceneDependencies(
            GameManager injectedGameManager,
            LevelSessionController injectedLevelSessionController)
        {
            gameManager = injectedGameManager;
            levelSessionController = injectedLevelSessionController;
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

        private bool TryResolveClickedDispatchablePig(Camera activeCamera, Vector2 screenPosition, out PigController pig)
        {
            pig = null;
            if (activeCamera == null || gameManager == null)
            {
                return false;
            }

            var ray = activeCamera.ScreenPointToRay(screenPosition);
            var hits = Physics.RaycastAll(ray, maxRayDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var hitPig = hits[i].collider != null
                    ? hits[i].collider.GetComponentInParent<PigController>()
                    : null;
                if (hitPig == null)
                {
                    continue;
                }

                if (!gameManager.TryResolveDispatchCandidate(hitPig, out var dispatchPig))
                {
                    continue;
                }

                pig = dispatchPig;
                return true;
            }

            return false;
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

        private static bool IsPointerOverBlockingUi(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            UiRaycastResults.Clear();
            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            EventSystem.current.RaycastAll(pointerData, UiRaycastResults);
            for (int i = 0; i < UiRaycastResults.Count; i++)
            {
                var target = UiRaycastResults[i].gameObject;
                if (target == null)
                {
                    continue;
                }

                if (target.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
