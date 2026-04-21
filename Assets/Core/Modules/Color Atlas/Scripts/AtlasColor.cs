using UnityEngine;

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
        Pastel = 13,
        Highlight = 14
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

    public static class AtlasPaletteConstants
    {
        public const int MinColorIndex = 0;
        public const int MaxColorIndex = 15;
        public const int MinToneIndex = 0;
        public const int MaxToneIndex = 14;
        public const int DefaultToneIndex = 14;

        public static int ClampColorIndex(int colorIndex)
        {
            if (colorIndex < MinColorIndex)
            {
                return MinColorIndex;
            }

            return colorIndex > MaxColorIndex
                ? MaxColorIndex
                : colorIndex;
        }

        public static int ClampToneIndex(int toneIndex)
        {
            if (toneIndex < MinToneIndex)
            {
                return MinToneIndex;
            }

            return toneIndex > MaxToneIndex
                ? MaxToneIndex
                : toneIndex;
        }
    }

    public static class AtlasPreviewPaletteUtility
    {
        public static Color ResolveBaseColor(int colorIndex)
        {
            return AtlasPaletteConstants.ClampColorIndex(colorIndex) switch
            {
                1 => new Color32(188, 48, 48, 255),
                2 => new Color32(210, 150, 195, 255),
                3 => new Color32(110, 68, 156, 255),
                4 => new Color32(173, 57, 173, 255),
                5 => new Color32(0, 95, 191, 255),
                6 => new Color32(48, 161, 172, 255),
                9 => new Color32(74, 186, 82, 255),
                10 => new Color32(54, 189, 54, 255),
                11 => new Color32(191, 191, 63, 255),
                12 => new Color32(188, 125, 40, 255),
                13 => new Color32(177, 42, 42, 255),
                14 => new Color32(128, 128, 132, 255),
                15 => new Color32(200, 200, 200, 255),
                _ => new Color32(184, 184, 188, 255),
            };
        }
    }
}
