using Core.Pool;
using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowPoolableVisual : MonoBehaviour, IPoolable
    {
        [SerializeField] private PixelFlowVisualView view;
        [SerializeField] private bool resetParentOnDespawn = true;
        [SerializeField] private bool clearViewOnDespawn = true;

        private Transform defaultParent;
        private Vector3 defaultLocalPosition;
        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalScale;
        private PixelFlowVisualModel currentModel = PixelFlowVisualModel.Empty;

        private void Awake()
        {
            EnsureReferences();
            CacheDefaults();
        }

        private void Reset()
        {
            EnsureReferences();
            CacheDefaults();
        }

        private void OnValidate()
        {
            EnsureReferences();
            CacheDefaults();
            RenderCurrentModel();
        }

        public void ApplyColor(PigColor color)
        {
            currentModel = currentModel.WithColor(color);
            RenderCurrentModel();
        }

        public void SetAmmo(int ammo)
        {
            currentModel = currentModel.WithAmmo(ammo);
            RenderCurrentModel();
        }

        public void ConfigurePig(PigColor color, int ammo)
        {
            currentModel = PixelFlowVisualModel.CreatePig(color, ammo);
            RenderCurrentModel();
        }

        public void ConfigureBlock(PigColor color)
        {
            currentModel = PixelFlowVisualModel.CreateBlock(color);
            RenderCurrentModel();
        }

        public void SetTrayVisible(bool visible)
        {
            currentModel = currentModel.WithTray(visible);
            RenderCurrentModel();
        }

        public void SetOnBelt(bool isOnBelt)
        {
            SetTrayVisible(isOnBelt);
        }

        public void ShowTray()
        {
            SetTrayVisible(true);
        }

        public void HideTray()
        {
            SetTrayVisible(false);
        }

        public void ResetVisual()
        {
            currentModel = PixelFlowVisualModel.Empty;
            if (clearViewOnDespawn)
            {
                view?.Clear();
            }
            else
            {
                RenderCurrentModel();
            }
        }

        public void OnSpawned()
        {
            EnsureReferences();
            RenderCurrentModel();
        }

        public void OnDespawned()
        {
            transform.localPosition = defaultLocalPosition;
            transform.localRotation = defaultLocalRotation;
            transform.localScale = defaultLocalScale;

            if (resetParentOnDespawn && defaultParent != null && transform.parent != defaultParent)
            {
                transform.SetParent(defaultParent, false);
            }

            ResetVisual();
        }

        private void CacheDefaults()
        {
            defaultParent = transform.parent;
            defaultLocalPosition = transform.localPosition;
            defaultLocalRotation = transform.localRotation;
            defaultLocalScale = transform.localScale;
        }

        private void RenderCurrentModel()
        {
            EnsureReferences();
            view?.Render(currentModel);
        }

        private void EnsureReferences()
        {
            view ??= GetComponent<PixelFlowVisualView>();
        }
    }
}
