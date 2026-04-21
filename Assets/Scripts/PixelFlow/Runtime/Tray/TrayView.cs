using PixelFlow.Runtime.Configuration;
using UnityEngine;

namespace PixelFlow.Runtime.Tray
{
    [DisallowMultipleComponent]
    public sealed partial class TrayView : MonoBehaviour
    {
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [SerializeField] private TrayVisualConfig config;
        [SerializeField] private Renderer[] renderers;

        private MaterialPropertyBlock propertyBlock;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Reset()
        {
            EnsureReferences();
            TryAutoAssignConfig();
        }

        private void OnValidate()
        {
            EnsureReferences();
            TryAutoAssignConfig();
        }

        public void Render(TrayModel model)
        {
            if (model == null)
            {
                return;
            }

            EnsureReferences();
            if (model.Occupied)
            {
                ClearColorOverride();
                return;
            }

            ApplyColor(ResolveEmptyColor());
        }

        private void ApplyColor(Color color)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorProperty, color);
                propertyBlock.SetColor(ColorProperty, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void EnsureReferences()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private Color ResolveEmptyColor()
        {
            return config != null
                ? config.EmptyColor
                : new Color(0.45f, 0.45f, 0.5f, 1f);
        }

        private Color ResolveOccupiedColor()
        {
            return config != null
                ? config.OccupiedColor
                : new Color(0.61960775f, 0.70980394f, 0.9294118f, 1f);
        }

        private void ClearColorOverride()
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(propertyBlock);
                }
            }
        }

        private void TryAutoAssignConfig()
        {
            EditorAutoAssignConfig();
        }

        partial void EditorAutoAssignConfig();
    }
}
