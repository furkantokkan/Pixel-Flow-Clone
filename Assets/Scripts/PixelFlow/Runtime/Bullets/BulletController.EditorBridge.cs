#if UNITY_EDITOR
using PixelFlow.Runtime.Configuration;
using PixelFlow.Runtime.EditorOnly;

namespace PixelFlow.Runtime.Bullets
{
    public sealed partial class BulletController
    {
        partial void EditorAutoAssignConfig()
        {
            config ??= EditorAssetAutoWireUtility.FindFirstAsset<BulletConfig>();
        }
    }
}
#endif
