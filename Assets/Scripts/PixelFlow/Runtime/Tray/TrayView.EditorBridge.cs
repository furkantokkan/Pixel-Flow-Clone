#if UNITY_EDITOR
using PixelFlow.Runtime.Configuration;
using PixelFlow.Runtime.EditorOnly;

namespace PixelFlow.Runtime.Tray
{
    public sealed partial class TrayView
    {
        partial void EditorAutoAssignConfig()
        {
            config ??= EditorAssetAutoWireUtility.FindFirstAsset<TrayVisualConfig>();
        }
    }
}
#endif
