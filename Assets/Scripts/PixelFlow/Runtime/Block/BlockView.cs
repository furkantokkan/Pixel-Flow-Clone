using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class BlockView : MonoBehaviour
    {
        private const float FallbackReservedOutlineWidth = 0.12f;

        [SerializeField] private AtlasColorTarget atlasColorTarget;
        [SerializeField] private Renderer[] renderers;
        [SerializeField, Min(0f)] private float reservedOutlineWidth = FallbackReservedOutlineWidth;

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

            bool outlined = model.IsReserved && !model.IsDying;
            Render(model.Color, model.ToneIndex, outlined, outlined ? ResolveReservedOutlineWidth() : -1f);
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
            Render(color, toneIndex, outlined, -1f);
        }

        public void Render(PigColor color, int toneIndex, bool outlined, float outlineWidth)
        {
            EnsureReferences();
            atlasColorTarget?.SetColor(color, toneIndex);
            atlasColorTarget?.SetOutline(outlined);
            atlasColorTarget?.SetOutlineWidth(outlined ? outlineWidth : -1f);
            SetVisible(true);
        }

        public void Clear()
        {
            atlasColorTarget?.SetOutline(false);
            atlasColorTarget?.SetOutlineWidth(-1f);
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

        private float ResolveReservedOutlineWidth()
        {
            return reservedOutlineWidth > 0f ? reservedOutlineWidth : FallbackReservedOutlineWidth;
        }

    }
}
