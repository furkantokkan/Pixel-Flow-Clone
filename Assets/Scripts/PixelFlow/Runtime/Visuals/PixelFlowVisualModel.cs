using PixelFlow.Runtime.Data;

namespace PixelFlow.Runtime.Visuals
{
    public readonly struct PixelFlowVisualModel
    {
        public PixelFlowVisualModel(PigColor color, int ammo, bool showAmmo, bool showTray)
        {
            Color = color;
            Ammo = ammo < 0 ? 0 : ammo;
            ShowAmmo = showAmmo;
            ShowTray = showTray;
        }

        public PigColor Color { get; }
        public int Ammo { get; }
        public bool ShowAmmo { get; }
        public bool ShowTray { get; }

        public static PixelFlowVisualModel Empty => new(PigColor.Pink, 0, false, false);

        public static PixelFlowVisualModel CreatePig(PigColor color, int ammo)
        {
            return new PixelFlowVisualModel(color, ammo, true, false);
        }

        public static PixelFlowVisualModel CreateBlock(PigColor color)
        {
            return new PixelFlowVisualModel(color, 0, false, false);
        }

        public PixelFlowVisualModel WithColor(PigColor color)
        {
            return new PixelFlowVisualModel(color, Ammo, ShowAmmo, ShowTray);
        }

        public PixelFlowVisualModel WithAmmo(int ammo)
        {
            return new PixelFlowVisualModel(Color, ammo, ShowAmmo, ShowTray);
        }

        public PixelFlowVisualModel WithTray(bool showTray)
        {
            return new PixelFlowVisualModel(Color, Ammo, ShowAmmo, showTray);
        }
    }
}
