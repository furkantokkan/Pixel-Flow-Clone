using PixelFlow.Runtime.Data;

namespace PixelFlow.Runtime.Visuals
{
    public sealed class BlockVisualModel
    {
        public PigColor Color { get; private set; } = PigColor.Pink;
        public int ToneIndex { get; private set; } = PigColorAtlasUtility.ResolveDefaultToneIndex(PigColor.Pink);
        public bool IsReserved { get; private set; }
        public bool IsDying { get; private set; }

        public void Configure(PigColor color, int toneIndex)
        {
            Color = color;
            ToneIndex = toneIndex;
            IsReserved = false;
            IsDying = false;
        }

        public void SetReserved(bool reserved)
        {
            IsReserved = reserved;
        }

        public void SetDying(bool dying)
        {
            IsDying = dying;
        }

        public void Reset(PigColor defaultColor, int toneIndex)
        {
            Color = defaultColor;
            ToneIndex = toneIndex;
            IsReserved = false;
            IsDying = false;
        }
    }
}
