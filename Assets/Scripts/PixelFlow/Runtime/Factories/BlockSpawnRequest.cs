using Core.Runtime.ColorAtlas;
using PixelFlow.Runtime.Data;

namespace PixelFlow.Runtime.Factories
{
    public readonly struct BlockSpawnRequest
    {
        public BlockSpawnRequest(PigColor color, int toneIndex = -1, VisualSpawnPlacement placement = default)
        {
            Color = color;
            ToneIndex = toneIndex >= 0
                ? AtlasPaletteConstants.ClampToneIndex(toneIndex)
                : PigColorAtlasUtility.ResolveDefaultToneIndex(color);
            Placement = placement;
        }

        public PigColor Color { get; }
        public int ToneIndex { get; }
        public VisualSpawnPlacement Placement { get; }
    }
}
