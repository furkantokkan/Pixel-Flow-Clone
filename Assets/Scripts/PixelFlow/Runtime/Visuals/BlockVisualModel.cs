using PixelFlow.Runtime.Data;

namespace PixelFlow.Runtime.Visuals
{
    public sealed class BlockVisualModel
    {
        public PigColor Color { get; private set; } = PigColor.Pink;

        public void Configure(PigColor color)
        {
            Color = color;
        }

        public void Reset(PigColor defaultColor)
        {
            Color = defaultColor;
        }
    }
}
