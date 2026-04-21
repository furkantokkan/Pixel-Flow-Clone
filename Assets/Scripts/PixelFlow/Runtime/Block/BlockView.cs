using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class BlockView : MonoBehaviour
    {
        [SerializeField] private AtlasColorTarget atlasColorTarget;
        [SerializeField] private Renderer[] renderers;

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

            Render(model.Color, model.ToneIndex, model.IsReserved && !model.IsDying);
        }

        public void Render(PigColor color)
        {
            Render(color, PigColorAtlasUtility.ResolveDefaultToneIndex(color), outlined: false);
        }

        public void Render(PigColor color, bool outlined)
        {
            Render(color, PigColorAtlasUtility.ResolveDefaultToneIndex(color), outlined);
        }

        public void Render(PigColor color, int toneIndex, bool outlined)
        {
            EnsureReferences();
            atlasColorTarget?.SetColor(color, toneIndex);
            atlasColorTarget?.SetOutline(outlined);
            SetVisible(true);
        }

        public void Clear()
        {
            atlasColorTarget?.SetOutline(false);
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            EnsureReferences();
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
        }

        private void EnsureReferences()
        {
            atlasColorTarget ??= GetComponent<AtlasColorTarget>();
            atlasColorTarget ??= GetComponentInChildren<AtlasColorTarget>(true);
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

    }
}
