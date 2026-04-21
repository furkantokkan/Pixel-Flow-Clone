#if UNITY_EDITOR
using PixelFlow.Runtime.Configuration;
using PixelFlow.Runtime.EditorOnly;

namespace PixelFlow.Runtime.Visuals
{
    public sealed partial class BlockVisual
    {
        partial void EditorAutoAssignConfig()
        {
            config ??= EditorAssetAutoWireUtility.FindFirstAsset<BlockVisualConfig>();
        }
    }
}
#endif
