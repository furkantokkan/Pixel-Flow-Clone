#if UNITY_EDITOR
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.EditorOnly;
using UnityEngine;

namespace PixelFlow.Runtime.LevelEditing
{
    public sealed partial class EnvironmentContext
    {
        partial void EditorAutoAssignProjectAssets()
        {
            runtimeSettings ??= EditorAssetAutoWireUtility.FindFirstAsset<ProjectRuntimeSettings>();
            atlasSharedMaterial ??= ResolveAtlasSharedMaterialFromKnownRoots();
            atlasSharedMaterial ??= EditorAssetAutoWireUtility.LoadAssetByGuid<Material>(AtlasSharedMaterialGuid);

            if (!Application.isPlaying)
            {
                SynchronizeAtlasMaterials();
            }
        }
    }
}
#endif
