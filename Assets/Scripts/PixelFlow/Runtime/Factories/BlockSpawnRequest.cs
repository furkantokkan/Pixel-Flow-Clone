using PixelFlow.Runtime.Data;

namespace PixelFlow.Runtime.Factories
{
    public readonly struct BlockSpawnRequest
    {
        public BlockSpawnRequest(PigColor color, VisualSpawnPlacement placement = default)
        {
            Color = color;
            Placement = placement;
        }

        public PigColor Color { get; }
        public VisualSpawnPlacement Placement { get; }
    }
}
