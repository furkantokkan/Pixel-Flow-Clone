#if UNITY_EDITOR
using PixelFlow.Runtime.Bullets;
using PixelFlow.Runtime.EditorOnly;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;

namespace PixelFlow.Runtime.Composition
{
    public sealed partial class ProjectRuntimeSettings
    {
        partial void EditorAutoAssignAssets()
        {
            pigPrefab ??= EditorAssetAutoWireUtility.FindPrefabComponent<PigController>("Pig");
            blockPrefab ??= EditorAssetAutoWireUtility.FindPrefabComponent<BlockVisual>("Block");
            bulletPrefab ??= EditorAssetAutoWireUtility.FindPrefabComponent<BulletController>("Bullet");
        }
    }
}
#endif
