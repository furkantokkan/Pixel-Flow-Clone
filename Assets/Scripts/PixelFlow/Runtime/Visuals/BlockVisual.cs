using Core.Pool;
using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class BlockVisual : MonoBehaviour, IPoolable
    {
        [SerializeField] private BlockView view;
        [SerializeField] private bool resetParentOnDespawn = true;
        [SerializeField] private bool clearViewOnDespawn = true;
        [SerializeField] private PigColor defaultColor = PigColor.Pink;

        private readonly BlockVisualModel model = new();
        private Transform defaultParent;
        private Vector3 defaultLocalPosition;
        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalScale;

        private void Awake()
        {
            EnsureReferences();
            CacheDefaults();
            model.Reset(defaultColor);
        }

        private void Reset()
        {
            EnsureReferences();
            CacheDefaults();
            model.Reset(defaultColor);
        }

        private void OnValidate()
        {
            EnsureReferences();
            CacheDefaults();
            model.Reset(defaultColor);
            RenderCurrentColor();
        }

        public void ApplyColor(PigColor color)
        {
            model.Configure(color);
            RenderCurrentColor();
        }

        public void ConfigureBlock(PigColor color)
        {
            ApplyColor(color);
        }

        public void ResetVisual()
        {
            model.Reset(defaultColor);
            if (clearViewOnDespawn)
            {
                view?.Clear();
            }
            else
            {
                RenderCurrentColor();
            }
        }

        public void OnSpawned()
        {
            EnsureReferences();
            RenderCurrentColor();
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

        private void RenderCurrentColor()
        {
            EnsureReferences();
            view?.Render(model);
        }

        private void EnsureReferences()
        {
            view ??= GetComponent<BlockView>();
        }
    }
}
