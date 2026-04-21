#if UNITY_EDITOR
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.EditorOnly;

namespace PixelFlow.Runtime.Composition
{
    public sealed partial class ProjectLifetimeScope
    {
        partial void EditorAutoAssignAssets()
        {
            if (themeDatabase == null)
            {
                themeDatabase = EditorAssetAutoWireUtility.FindFirstAsset<ThemeDatabase>();
            }

            if (defaultTheme == null)
            {
                defaultTheme = themeDatabase != null
                    ? themeDatabase.GetDefaultTheme()
                    : EditorAssetAutoWireUtility.FindFirstAsset<Theme>();
            }

            if (defaultBlockData == null)
            {
                defaultBlockData = EditorAssetAutoWireUtility.FindFirstAsset<BlockData>();
            }

            if (runtimeSettings == null)
            {
                runtimeSettings = EditorAssetAutoWireUtility.FindFirstAsset<ProjectRuntimeSettings>();
            }

            if (levelDatabase == null)
            {
                levelDatabase = EditorAssetAutoWireUtility.FindFirstAsset<LevelDatabase>();
            }

            runtimeSettings?.TryAutoAssignAssets();
        }
    }
}
#endif
