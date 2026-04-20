using TMPro;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PixelFlow.Runtime.Pigs
{
    [DisallowMultipleComponent]
    public sealed class PigView : MonoBehaviour
    {
        private static readonly Quaternion VisualFacingOffset = Quaternion.Euler(0f, 180f, 0f);

        [SerializeField] private AtlasColorTarget atlasColorTarget;
        [SerializeField] private TMP_Text ammoText;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform trayRoot;
        [SerializeField] private Transform projectileOrigin;

        private Transform cachedVisualRoot;
        private Quaternion visualRootBaseLocalRotation;
        private bool hasVisualRootBaseRotation;

        public Transform ProjectileOrigin => projectileOrigin != null ? projectileOrigin : transform;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Reset()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            EnsureReferences();
        }

        public void Render(PigModel model)
        {
            EnsureReferences();
            atlasColorTarget?.SetColor(model.Color);

            if (ammoText != null)
            {
                ammoText.text = model.Ammo > 0
                    ? model.Ammo.ToString()
                    : string.Empty;
            }

            if (trayRoot != null)
            {
                trayRoot.gameObject.SetActive(model.TrayVisible);
            }
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
            ApplyVisualFacingCorrection(applyEditorPreviewFacingCorrection);
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
                cachedVisualRoot = null;
                hasVisualRootBaseRotation = false;
                return;
            }

            if (cachedVisualRoot != visualRoot)
            {
                cachedVisualRoot = visualRoot;
                visualRootBaseLocalRotation = visualRoot.localRotation;
                hasVisualRootBaseRotation = true;
            }

            if (!hasVisualRootBaseRotation)
            {
                visualRootBaseLocalRotation = visualRoot.localRotation;
                hasVisualRootBaseRotation = true;
            }

            visualRoot.localRotation = visualRootBaseLocalRotation * VisualFacingOffset;
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
    }
}
