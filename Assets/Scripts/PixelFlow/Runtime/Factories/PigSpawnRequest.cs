using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Pigs;

namespace PixelFlow.Runtime.Factories
{
    public readonly struct PigSpawnRequest
    {
        public PigSpawnRequest(
            PigColor color,
            int ammo,
            PigDirection direction = PigDirection.None,
            VisualSpawnPlacement placement = default)
        {
            Color = color;
            Ammo = ammo;
            Direction = direction;
            Placement = placement;
        }

        public PigColor Color { get; }
        public int Ammo { get; }
        public PigDirection Direction { get; }
        public VisualSpawnPlacement Placement { get; }
    }
}
