using System;
using TMPro;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Visuals;
using PrimeTween;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PixelFlow.Runtime.Pigs
{
    [DisallowMultipleComponent]
    public sealed class PigView : MonoBehaviour
    {
        private static readonly Quaternion QueueFacingOffset = Quaternion.Euler(0f, 180f, 0f);
        private static readonly Quaternion BeltFacingOffset = Quaternion.identity;
        private static readonly Color DarkAmmoTextColor = new(0.1f, 0.1f, 0.1f);

        [SerializeField] private AtlasColorTarget atlasColorTarget;
        [SerializeField] private TMP_Text ammoText;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform trayRoot;
        [SerializeField] private Transform projectileOrigin;
        [SerializeField, Min(0f)] private float trayProjectileHeightOffset = 0.12f;
        [SerializeField, Min(0f)] private float trayProjectileForwardOffset = 0.08f;
        [SerializeField, Min(0.01f)] private float facingTransitionDuration = 0.2f;
        [SerializeField] private Ease facingTransitionEase = Ease.InOutSine;

        private Transform cachedVisualRoot;
        private Quaternion visualRootBaseLocalRotation;
        private bool hasVisualRootBaseRotation;
        private bool useBeltFacingCorrection;
        private bool renderersVisible = true;
        private Renderer[] cachedRenderers = Array.Empty<Renderer>();
        private Collider[] cachedColliders = Array.Empty<Collider>();
        private bool targetingSourcesCached;
        private bool animateFacingCorrectionOnNextApply;
        private Tween visualFacingTween;
        private Quaternion visualFacingTweenStartLocalRotation;
        private Quaternion visualFacingTweenEndLocalRotation;

        public Transform ProjectileOrigin => projectileOrigin != null ? projectileOrigin : transform;
        public Vector3 ProjectileOriginPosition => ResolveProjectileOriginPosition();
        public Quaternion ProjectileOriginRotation => ResolveProjectileOriginRotation();
        public Vector3 TargetingOriginPosition => ResolveTargetingOriginPosition();
        public Vector3 FacingDirection => ResolveFacingDirection();

        private void Awake()
        {
            EnsureReferences();
            ApplyRendererVisibility();
        }

        private void Reset()
        {
            EnsureReferences();
            ApplyRendererVisibility();
        }

        private void OnValidate()
        {
            EnsureReferences();
            ApplyRendererVisibility();
        }

        private void OnDisable()
        {
            StopVisualFacingTween();
        }

        public void Render(PigModel model)
        {
            EnsureReferences();
            atlasColorTarget?.SetColor(model.Color);
            atlasColorTarget?.SetOutline(model.TrayVisible);

            if (ammoText != null)
            {
                ammoText.text = model.Ammo > 0
                    ? model.Ammo.ToString()
                    : string.Empty;
                ammoText.color = ResolveAmmoTextColor(model.Color);
            }

            if (trayRoot != null)
            {
                trayRoot.gameObject.SetActive(model.TrayVisible);
            }

            ApplyRendererVisibility();
        }

        public void Clear()
        {
            if (ammoText != null)
            {
                ammoText.text = string.Empty;
            }

            if (trayRoot != null)
            {
                trayRoot.gameObject.SetActive(false);
            }

            SetBeltFacingModeInternal(false, animate: false);
            SetRenderersVisible(true);
        }

        public void SetRenderersVisible(bool visible)
        {
            if (renderersVisible == visible)
            {
                return;
            }

            renderersVisible = visible;
            EnsureReferences();
            ApplyRendererVisibility();
        }

        public bool ShouldRenderForCamera(Camera activeCamera, float viewportPadding)
        {
            if (activeCamera == null)
            {
                return true;
            }

            EnsureReferences();
            if (!TryResolveVisibilityBounds(out var bounds))
            {
                return true;
            }

            return IsInsideExpandedViewport(activeCamera, bounds, Mathf.Clamp(viewportPadding, 0f, 0.5f));
        }

        public void SetBeltFacingMode(bool enabled)
        {
            SetBeltFacingModeInternal(enabled, animate: true);
        }

#if UNITY_EDITOR
        public void ApplyEditorPreviewFacingCorrection()
        {
            EnsureReferences(applyEditorPreviewFacingCorrection: true);
        }
#endif

        private void EnsureReferences(bool applyEditorPreviewFacingCorrection = false)
        {
            atlasColorTarget ??= GetComponent<AtlasColorTarget>();
            atlasColorTarget ??= GetComponentInChildren<AtlasColorTarget>(true);
            ammoText ??= GetComponentInChildren<TMP_Text>(true);
            visualRoot ??= ResolveVisualRoot();
            trayRoot ??= ResolveTrayRoot();
            projectileOrigin ??= ResolveProjectileOrigin();
            CacheTargetingSources();
            atlasColorTarget?.SetExcludedRoot(trayRoot);
            ApplyVisualFacingCorrection(applyEditorPreviewFacingCorrection);
        }

        private void SetBeltFacingModeInternal(bool enabled, bool animate)
        {
            if (useBeltFacingCorrection == enabled
                && !visualFacingTween.isAlive
                && !animateFacingCorrectionOnNextApply)
            {
                return;
            }

            useBeltFacingCorrection = enabled;
            animateFacingCorrectionOnNextApply = animate && Application.isPlaying;
            EnsureReferences();
        }

        private void CacheTargetingSources()
        {
            if (targetingSourcesCached)
            {
                return;
            }

            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedColliders = GetComponentsInChildren<Collider>(true);
            targetingSourcesCached = true;
        }

        private void ApplyRendererVisibility()
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                var rendererCandidate = cachedRenderers[i];
                if (rendererCandidate == null)
                {
                    continue;
                }

                rendererCandidate.enabled = renderersVisible;
            }
        }

        private void ApplyVisualFacingCorrection(bool applyEditorPreviewFacingCorrection = false)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying
                && (!applyEditorPreviewFacingCorrection
                    || !gameObject.scene.IsValid()
                    || EditorUtility.IsPersistent(this)))
            {
                return;
            }
#elif !UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif

            if (visualRoot == null)
            {
                StopVisualFacingTween();
                cachedVisualRoot = null;
                hasVisualRootBaseRotation = false;
                animateFacingCorrectionOnNextApply = false;
                return;
            }

            var visualRootChanged = cachedVisualRoot != visualRoot;
            if (cachedVisualRoot != visualRoot)
            {
                StopVisualFacingTween();
                cachedVisualRoot = visualRoot;
                visualRootBaseLocalRotation = visualRoot.localRotation;
                hasVisualRootBaseRotation = true;
            }

            if (!hasVisualRootBaseRotation)
            {
                visualRootBaseLocalRotation = visualRoot.localRotation;
                hasVisualRootBaseRotation = true;
            }

            var facingOffset = useBeltFacingCorrection
                ? BeltFacingOffset
                : QueueFacingOffset;

            var targetLocalRotation = visualRootBaseLocalRotation * facingOffset;
            if (!animateFacingCorrectionOnNextApply
                && visualFacingTween.isAlive
                && !visualRootChanged
                && Quaternion.Angle(visualFacingTweenEndLocalRotation, targetLocalRotation) <= 0.01f)
            {
                return;
            }

            var shouldAnimate = animateFacingCorrectionOnNextApply
                && !visualRootChanged
                && facingTransitionDuration > 0.01f
                && Quaternion.Angle(visualRoot.localRotation, targetLocalRotation) > 0.01f;
            animateFacingCorrectionOnNextApply = false;
            if (!shouldAnimate)
            {
                StopVisualFacingTween();
                visualRoot.localRotation = targetLocalRotation;
                return;
            }

            StartVisualFacingTween(targetLocalRotation);
        }

        private void StartVisualFacingTween(Quaternion targetLocalRotation)
        {
            if (visualRoot == null)
            {
                return;
            }

            StopVisualFacingTween();
            visualFacingTweenStartLocalRotation = visualRoot.localRotation;
            visualFacingTweenEndLocalRotation = targetLocalRotation;
            visualFacingTween = Tween.Custom(
                    this,
                    0f,
                    1f,
                    facingTransitionDuration,
                    static (target, t) => target.UpdateVisualFacingTween(t),
                    facingTransitionEase)
                .OnComplete(this, static target => target.CompleteVisualFacingTween());
        }

        private void UpdateVisualFacingTween(float t)
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.localRotation = Quaternion.SlerpUnclamped(
                visualFacingTweenStartLocalRotation,
                visualFacingTweenEndLocalRotation,
                t);
        }

        private void CompleteVisualFacingTween()
        {
            visualFacingTween = default;
            if (visualRoot != null)
            {
                visualRoot.localRotation = visualFacingTweenEndLocalRotation;
            }
        }

        private void StopVisualFacingTween()
        {
            if (visualFacingTween.isAlive)
            {
                visualFacingTween.Stop();
            }

            visualFacingTween = default;
        }

        private Transform ResolveVisualRoot()
        {
            var directChild = transform.Find("All");
            if (directChild != null)
            {
                return directChild;
            }

            var children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                var candidate = children[i];
                if (candidate == null || candidate == transform)
                {
                    continue;
                }

                if (candidate.GetComponentInChildren<Renderer>(true) != null
                    && candidate.GetComponentInChildren<TMP_Text>(true) != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private Transform ResolveTrayRoot()
        {
            Transform directChild = transform.Find("Tray");
            if (directChild != null)
            {
                return directChild;
            }

            var children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i] != transform && children[i].name == "Tray")
                {
                    return children[i];
                }
            }

            return null;
        }

        private Transform ResolveProjectileOrigin()
        {
            var directChild = transform.Find("ProjectileOrigin");
            if (directChild != null)
            {
                return directChild;
            }

            directChild = transform.Find("BulletOrigin");
            if (directChild != null)
            {
                return directChild;
            }

            return transform;
        }

        private Vector3 ResolveProjectileOriginPosition()
        {
            if (projectileOrigin != null && projectileOrigin != transform)
            {
                return projectileOrigin.position;
            }

            if (trayRoot != null)
            {
                return trayRoot.position
                    + (transform.up * trayProjectileHeightOffset)
                    + (transform.forward * trayProjectileForwardOffset);
            }

            return transform.position;
        }

        private Vector3 ResolveTargetingOriginPosition()
        {
            if (TryResolveTargetingBounds(out var bounds))
            {
                return bounds.center;
            }

            if (projectileOrigin != null)
            {
                return projectileOrigin.position;
            }

            return transform.position;
        }

        private Quaternion ResolveProjectileOriginRotation()
        {
            if (projectileOrigin != null && projectileOrigin != transform)
            {
                return projectileOrigin.rotation;
            }

            var forward = transform.forward;
            if (forward.sqrMagnitude <= Mathf.Epsilon)
            {
                forward = Vector3.forward;
            }

            return Quaternion.LookRotation(forward.normalized, transform.up);
        }

        private Vector3 ResolveFacingDirection()
        {
            var forward = projectileOrigin != null && projectileOrigin != transform
                ? projectileOrigin.forward
                : transform.forward;
            return forward.sqrMagnitude > Mathf.Epsilon
                ? forward.normalized
                : Vector3.forward;
        }

        private bool TryResolveVisibilityBounds(out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                var rendererCandidate = cachedRenderers[i];
                if (!IsValidVisibilityRenderer(rendererCandidate))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererCandidate.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(rendererCandidate.bounds);
            }

            if (hasBounds)
            {
                return true;
            }

            for (int i = 0; i < cachedColliders.Length; i++)
            {
                var colliderCandidate = cachedColliders[i];
                if (!IsValidVisibilityCollider(colliderCandidate))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = colliderCandidate.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(colliderCandidate.bounds);
            }

            return hasBounds;
        }

        private bool TryResolveTargetingBounds(out Bounds bounds)
        {
            if (TryResolveRendererBounds(out bounds))
            {
                return true;
            }

            return TryResolveColliderBounds(out bounds);
        }

        private bool TryResolveRendererBounds(out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                var rendererCandidate = cachedRenderers[i];
                if (!IsValidTargetingRenderer(rendererCandidate))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererCandidate.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(rendererCandidate.bounds);
            }

            return hasBounds;
        }

        private bool TryResolveColliderBounds(out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                var colliderCandidate = cachedColliders[i];
                if (!IsValidTargetingCollider(colliderCandidate))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = colliderCandidate.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(colliderCandidate.bounds);
            }

            return hasBounds;
        }

        private bool IsValidTargetingRenderer(Renderer rendererCandidate)
        {
            return rendererCandidate != null
                && rendererCandidate.enabled
                && rendererCandidate.gameObject.activeInHierarchy
                && rendererCandidate.GetComponent<TMP_Text>() == null
                && !IsTrayChild(rendererCandidate.transform);
        }

        private bool IsValidTargetingCollider(Collider colliderCandidate)
        {
            return colliderCandidate != null
                && colliderCandidate.enabled
                && colliderCandidate.gameObject.activeInHierarchy
                && !colliderCandidate.isTrigger
                && !IsTrayChild(colliderCandidate.transform);
        }

        private static bool IsInsideExpandedViewport(Camera activeCamera, Bounds bounds, float viewportPadding)
        {
            var min = bounds.min;
            var max = bounds.max;

            return IsInsideExpandedViewport(activeCamera, bounds.center, viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(min.x, min.y, min.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(min.x, min.y, max.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(min.x, max.y, min.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(min.x, max.y, max.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(max.x, min.y, min.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(max.x, min.y, max.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(max.x, max.y, min.z), viewportPadding)
                || IsInsideExpandedViewport(activeCamera, new Vector3(max.x, max.y, max.z), viewportPadding);
        }

        private static bool IsInsideExpandedViewport(Camera activeCamera, Vector3 worldPoint, float viewportPadding)
        {
            var viewportPoint = activeCamera.WorldToViewportPoint(worldPoint);
            if (viewportPoint.z <= 0f)
            {
                return false;
            }

            var min = -viewportPadding;
            var max = 1f + viewportPadding;
            return viewportPoint.x >= min
                && viewportPoint.x <= max
                && viewportPoint.y >= min
                && viewportPoint.y <= max;
        }

        private bool IsValidVisibilityRenderer(Renderer rendererCandidate)
        {
            return rendererCandidate != null
                && rendererCandidate.gameObject.activeInHierarchy;
        }

        private bool IsValidVisibilityCollider(Collider colliderCandidate)
        {
            return colliderCandidate != null
                && colliderCandidate.enabled
                && colliderCandidate.gameObject.activeInHierarchy
                && !colliderCandidate.isTrigger;
        }

        private bool IsTrayChild(Transform candidate)
        {
            return trayRoot != null
                && candidate != null
                && candidate != trayRoot
                && candidate.IsChildOf(trayRoot);
        }

        private static Color ResolveAmmoTextColor(PigColor pigColor)
        {
            var backgroundColor = PigColorPaletteUtility.GetAtlasPreviewColor(pigColor);
            var luminance = (0.299f * backgroundColor.r) + (0.587f * backgroundColor.g) + (0.114f * backgroundColor.b);
            return luminance > 0.58f
                ? DarkAmmoTextColor
                : Color.white;
        }
    }
}
