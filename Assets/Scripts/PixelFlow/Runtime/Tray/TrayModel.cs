namespace PixelFlow.Runtime.Tray
{
    public sealed class TrayModel
    {
        public bool Visible { get; private set; }
        public bool Occupied { get; private set; }

        public void Configure(bool visible, bool occupied)
        {
            Visible = visible;
            Occupied = occupied;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }

        public void SetOccupied(bool occupied)
        {
            Occupied = occupied;
        }

        public void Reset()
        {
            Visible = false;
            Occupied = false;
        }
    }
}
