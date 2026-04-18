namespace Core.Runtime.ColorAtlas
{
    // Values map directly to the atlas row used by the shader.
    public enum BaseColor
    {
        Black = 0,
        Red = 1,
        Pink = 2,
        Purple = 3,
        Blue = 5,
        Cyan = 6,
        Green = 9,
        Yellow = 11,
        Orange = 12,
        Gray = 14
    }

    public enum ColorTone
    {
        Pastel = 13
    }

    [System.Serializable]
    public struct AtlasColor
    {
        public BaseColor baseColor;
        public ColorTone tone;

        public AtlasColor(BaseColor color, ColorTone colorTone)
        {
            baseColor = color;
            tone = colorTone;
        }

        public int GetColorIndex() => (int)baseColor;
        public int GetToneIndex() => (int)tone;
        public bool SameBaseColor(AtlasColor other) => baseColor == other.baseColor;
        public override string ToString() => $"{baseColor}_{tone}";
    }
}
