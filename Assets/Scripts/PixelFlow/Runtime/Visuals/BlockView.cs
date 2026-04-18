using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class BlockView : MonoBehaviour
    {
        [SerializeField] private AtlasColorTarget atlasColorTarget;

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

        public void Render(BlockVisualModel model)
        {
            if (model == null)
            {
                return;
            }

            Render(model.Color);
        }

        public void Render(PigColor color)
        {
            EnsureReferences();
            atlasColorTarget?.SetColor(color);
        }

        public void Clear()
        {
        }

        private void EnsureReferences()
        {
            atlasColorTarget ??= GetComponent<AtlasColorTarget>();
            atlasColorTarget ??= GetComponentInChildren<AtlasColorTarget>(true);
        }
    }
}
