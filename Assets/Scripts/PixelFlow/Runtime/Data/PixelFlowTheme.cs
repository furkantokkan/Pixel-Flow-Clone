using System;
using System.Collections.Generic;
using PixelFlow.Runtime.LevelEditing;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [Serializable]
    public sealed class RendererMaterialOverride
    {
        [SerializeField] private string rendererPath;
        [SerializeField] private List<Material> sharedMaterials = new();

        public string RendererPath => rendererPath;
        public IReadOnlyList<Material> SharedMaterials => sharedMaterials;
    }

    [CreateAssetMenu(fileName = "PixelFlowTheme", menuName = "Pixel Flow/Theme")]
    public sealed class PixelFlowTheme : ScriptableObject
    {
        [SerializeField] private PixelFlowEnvironmentContext environmentPrefab;
        [SerializeField] private GameObject blockPrefab;
        [SerializeField] private List<RendererMaterialOverride> rendererOverrides = new();

        public PixelFlowEnvironmentContext EnvironmentPrefab => environmentPrefab;
        public GameObject BlockPrefab => blockPrefab;
        public IReadOnlyList<RendererMaterialOverride> RendererOverrides => rendererOverrides;

        public void Apply(PixelFlowEnvironmentContext environment)
        {
            if (environment == null || rendererOverrides == null || rendererOverrides.Count == 0)
            {
                return;
            }

            for (int i = 0; i < rendererOverrides.Count; i++)
            {
                var entry = rendererOverrides[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.RendererPath))
                {
                    continue;
                }

                var target = environment.transform.Find(entry.RendererPath);
                if (target == null)
                {
                    continue;
                }

                var renderer = target.GetComponent<Renderer>();
                if (renderer == null || entry.SharedMaterials == null || entry.SharedMaterials.Count == 0)
                {
                    continue;
                }

                var materials = new Material[entry.SharedMaterials.Count];
                for (int materialIndex = 0; materialIndex < entry.SharedMaterials.Count; materialIndex++)
                {
                    materials[materialIndex] = entry.SharedMaterials[materialIndex];
                }

                renderer.sharedMaterials = materials;
            }
        }
    }
}
