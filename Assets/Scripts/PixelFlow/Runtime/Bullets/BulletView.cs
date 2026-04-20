using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Bullets
{
    [DisallowMultipleComponent]
    public sealed class BulletView : MonoBehaviour
    {
        [SerializeField] private AtlasColorTarget atlasColorTarget;
        [SerializeField] private TrailRenderer trailRenderer;

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

        public void Render(BulletModel model)
        {
            if (model == null)
            {
                return;
            }

            EnsureReferences();
            atlasColorTarget?.SetColor(model.Color);

            if (trailRenderer != null)
            {
                trailRenderer.emitting = model.IsActive;
            }
        }

        public void Clear()
        {
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
                trailRenderer.Clear();
            }
        }

        private void EnsureReferences()
        {
            atlasColorTarget ??= GetComponent<AtlasColorTarget>();
            atlasColorTarget ??= GetComponentInChildren<AtlasColorTarget>(true);
            trailRenderer ??= GetComponentInChildren<TrailRenderer>(true);
        }
    }
}
