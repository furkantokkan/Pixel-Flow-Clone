using PixelFlow.Runtime.Data;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowProjectLifetimeScope : LifetimeScope
    {
        [SerializeField] private PixelFlowThemeDatabase themeDatabase;
        [SerializeField] private PixelFlowTheme defaultTheme;
        [SerializeField] private PixelFlowBlockData defaultBlockData;

        private void Reset()
        {
            TryAutoAssignAssets();
        }

        private void OnValidate()
        {
            TryAutoAssignAssets();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (themeDatabase != null)
            {
                builder.RegisterInstance(themeDatabase);
            }

            if (defaultTheme != null)
            {
                builder.RegisterInstance(defaultTheme);
            }

            if (defaultBlockData != null)
            {
                builder.RegisterInstance(defaultBlockData);
            }
        }

        private void TryAutoAssignAssets()
        {
#if UNITY_EDITOR
            if (themeDatabase == null)
            {
                themeDatabase = FindFirstAsset<PixelFlowThemeDatabase>();
            }

            if (defaultTheme == null)
            {
                defaultTheme = themeDatabase != null
                    ? themeDatabase.GetDefaultTheme()
                    : FindFirstAsset<PixelFlowTheme>();
            }

            if (defaultBlockData == null)
            {
                defaultBlockData = FindFirstAsset<PixelFlowBlockData>();
            }
#endif
        }

#if UNITY_EDITOR
        private static TAsset FindFirstAsset<TAsset>() where TAsset : Object
        {
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TAsset>(path);
        }
#endif
    }
}
