using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    public static class PigColorPaletteUtility
    {
        private static readonly PigColor[] DefaultBrushColors =
        {
            PigColor.None,
            PigColor.Red,
            PigColor.Pink,
            PigColor.Blue,
            PigColor.DarkBlue,
            PigColor.Green,
            PigColor.Yellow,
            PigColor.Orange,
            PigColor.Teal,
            PigColor.Purple,
            PigColor.Gray,
            PigColor.White,
            PigColor.Black,
        };

        public static List<PigColorPaletteEntry> CreateDefaultPaletteEntries()
        {
            return new List<PigColorPaletteEntry>
            {
                new(PigColor.Red, GetDefaultDisplayColor(PigColor.Red)),
                new(PigColor.Pink, GetDefaultDisplayColor(PigColor.Pink)),
                new(PigColor.Blue, GetDefaultDisplayColor(PigColor.Blue)),
                new(PigColor.DarkBlue, GetDefaultDisplayColor(PigColor.DarkBlue)),
                new(PigColor.Green, GetDefaultDisplayColor(PigColor.Green)),
                new(PigColor.Yellow, GetDefaultDisplayColor(PigColor.Yellow)),
                new(PigColor.Orange, GetDefaultDisplayColor(PigColor.Orange)),
                new(PigColor.Teal, GetDefaultDisplayColor(PigColor.Teal)),
                new(PigColor.Purple, GetDefaultDisplayColor(PigColor.Purple)),
                new(PigColor.Gray, GetDefaultDisplayColor(PigColor.Gray)),
                new(PigColor.White, GetDefaultDisplayColor(PigColor.White)),
                new(PigColor.Black, GetDefaultDisplayColor(PigColor.Black)),
            };
        }

        public static IReadOnlyList<PigColor> GetDefaultBrushColors()
        {
            return DefaultBrushColors;
        }

        public static Color GetAtlasPreviewColor(PigColor color, int toneIndex = -1)
        {
            if (color == PigColor.None)
            {
                return Color.clear;
            }

            var resolvedToneIndex = toneIndex >= 0
                ? Core.Runtime.ColorAtlas.AtlasPaletteConstants.ClampToneIndex(toneIndex)
                : PigColorAtlasUtility.ResolveDefaultToneIndex(color);
            return GetAtlasPreviewColor(PigColorAtlasUtility.ResolveColorIndex(color), resolvedToneIndex);
        }

        public static Color GetAtlasPreviewColor(int colorIndex, int toneIndex)
        {
            if (colorIndex <= 0)
            {
                return new Color32(18, 18, 20, 255);
            }

            var previewBaseColor = Core.Runtime.ColorAtlas.AtlasPreviewPaletteUtility.ResolveBaseColor(colorIndex);
            var clampedToneIndex = Core.Runtime.ColorAtlas.AtlasPaletteConstants.ClampToneIndex(toneIndex);
            var toneLerp = clampedToneIndex / (float)Core.Runtime.ColorAtlas.AtlasPaletteConstants.MaxToneIndex;
            var brightness = Mathf.Lerp(0.28f, 1f, toneLerp);
            return Color.Lerp(Color.black, previewBaseColor, brightness);
        }

        public static void EnsurePaletteEntries(List<PigColorPaletteEntry> paletteEntries)
        {
            if (paletteEntries == null)
            {
                return;
            }

            var defaults = CreateDefaultPaletteEntries();
            for (int i = 0; i < defaults.Count; i++)
            {
                var defaultEntry = defaults[i];
                var exists = false;

                for (int j = 0; j < paletteEntries.Count; j++)
                {
                    if (paletteEntries[j].Color == defaultEntry.Color)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    paletteEntries.Add(defaultEntry);
                }
            }
        }

        public static Color GetDisplayColor(PigColor color, IReadOnlyList<PigColorPaletteEntry> paletteEntries = null)
        {
            if (paletteEntries != null)
            {
                for (int i = 0; i < paletteEntries.Count; i++)
                {
                    var entry = paletteEntries[i];
                    if (entry.Color == color)
                    {
                        return entry.DisplayColor;
                    }
                }
            }

            return GetDefaultDisplayColor(color);
        }

        public static string GetDisplayName(PigColor color)
        {
            return color switch
            {
                PigColor.None => "Empty",
                PigColor.DarkBlue => "Dark Blue",
                _ => color.ToString(),
            };
        }

        public static PigColor GetClosestColor(Color source, IReadOnlyList<PigColorPaletteEntry> paletteEntries)
        {
            if (paletteEntries == null || paletteEntries.Count == 0)
            {
                return PigColor.None;
            }

            Color.RGBToHSV(source, out var sourceHue, out var sourceSaturation, out var sourceValue);
            var sourceLuminance = ComputeLuminance(source);
            var sourceNeutral = IsNeutralLikeSource(sourceSaturation, sourceValue, sourceLuminance);
            var bestColor = PigColor.None;
            var bestDistance = float.MaxValue;

            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                if (!entry.Enabled || entry.Color == PigColor.None)
                {
                    continue;
                }

                var distance = ColorDistanceScore(
                    source,
                    sourceHue,
                    sourceSaturation,
                    sourceValue,
                    sourceLuminance,
                    sourceNeutral,
                    entry.Color,
                    entry.DisplayColor);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestColor = entry.Color;
                }
            }

            return bestColor;
        }

        public static float GetColorMatchDistance(Color source, PigColor targetColor, Color targetDisplayColor)
        {
            Color.RGBToHSV(source, out var sourceHue, out var sourceSaturation, out var sourceValue);
            var sourceLuminance = ComputeLuminance(source);
            var sourceNeutral = IsNeutralLikeSource(sourceSaturation, sourceValue, sourceLuminance);

            return ColorDistanceScore(
                source,
                sourceHue,
                sourceSaturation,
                sourceValue,
                sourceLuminance,
                sourceNeutral,
                targetColor,
                targetDisplayColor);
        }

        private static float ColorDistanceScore(
            Color source,
            float sourceHue,
            float sourceSaturation,
            float sourceValue,
            float sourceLuminance,
            bool sourceNeutral,
            PigColor targetColor,
            Color targetDisplayColor)
        {
            Color.RGBToHSV(targetDisplayColor, out var targetHue, out var targetSaturation, out var targetValue);
            var targetNeutral = IsNeutralPaletteColor(targetColor) || targetSaturation < 0.08f;
            var targetLuminance = ComputeLuminance(targetDisplayColor);

            if (sourceNeutral)
            {
                var luminanceDelta = sourceLuminance - targetLuminance;
                var valueDelta = sourceValue - targetValue;
                var saturationDelta = sourceSaturation - targetSaturation;
                var neutralPenalty = targetNeutral ? 0f : 0.55f + (targetSaturation * 0.35f);
                return (luminanceDelta * luminanceDelta * 2.1f)
                    + (valueDelta * valueDelta * 1.4f)
                    + (saturationDelta * saturationDelta * 0.8f)
                    + neutralPenalty;
            }

            var hueDelta = CircularHueDistance(sourceHue, targetHue);
            var saturationDifference = sourceSaturation - targetSaturation;
            var valueDifference = sourceValue - targetValue;
            var neutralBias = targetNeutral ? 0.85f + (sourceSaturation * 0.45f) : 0f;
            var rgbDistance = ComputeLinearRgbDistanceSquared(source, targetDisplayColor);

            return (hueDelta * hueDelta * 16f)
                + (saturationDifference * saturationDifference * 0.8f)
                + (valueDifference * valueDifference * 0.9f)
                + (rgbDistance * 0.2f)
                + neutralBias;
        }

        private static float ComputeLinearRgbDistanceSquared(Color a, Color b)
        {
            var linearA = a.linear;
            var linearB = b.linear;
            var dr = linearA.r - linearB.r;
            var dg = linearA.g - linearB.g;
            var db = linearA.b - linearB.b;
            return (dr * dr) + (dg * dg) + (db * db);
        }

        private static bool IsNeutralPaletteColor(PigColor color)
        {
            return color == PigColor.Black
                || color == PigColor.Gray
                || color == PigColor.White;
        }

        private static bool IsNeutralLikeSource(float saturation, float value, float luminance)
        {
            return saturation < 0.16f
                || (luminance >= 0.84f && value >= 0.9f && saturation <= 0.32f);
        }

        private static float CircularHueDistance(float a, float b)
        {
            var delta = Mathf.Abs(a - b);
            return Mathf.Min(delta, 1f - delta);
        }

        private static float ComputeLuminance(Color color)
        {
            return (0.299f * color.r) + (0.587f * color.g) + (0.114f * color.b);
        }

        private static Color GetDefaultDisplayColor(PigColor color)
        {
            return color switch
            {
                PigColor.Red => new Color32(242, 71, 71, 255),
                PigColor.Pink => new Color32(255, 115, 184, 255),
                PigColor.Blue => new Color32(72, 170, 255, 255),
                PigColor.DarkBlue => new Color32(0, 78, 158, 255),
                PigColor.Green => new Color32(87, 219, 115, 255),
                PigColor.Yellow => new Color32(255, 217, 64, 255),
                PigColor.Orange => new Color32(255, 148, 51, 255),
                PigColor.Teal => new Color32(31, 184, 184, 255),
                PigColor.Purple => new Color32(168, 115, 255, 255),
                PigColor.Gray => new Color32(158, 158, 158, 255),
                PigColor.White => new Color32(247, 247, 247, 255),
                PigColor.Black => Color.black,
                _ => Color.clear,
            };
        }

    }

    public static class PigColorAtlasUtility
    {
        public static int ResolveColorIndex(PigColor color)
        {
            return color switch
            {
                PigColor.Red => 13,
                PigColor.Pink => 2,
                PigColor.Blue => 5,
                PigColor.DarkBlue => 5,
                PigColor.Green => 10,
                PigColor.Yellow => 11,
                PigColor.Orange => 12,
                PigColor.Teal => 6,
                PigColor.Purple => 4,
                PigColor.Black => 0,
                PigColor.Gray => 15,
                PigColor.White => 15,
                _ => 15,
            };
        }

        public static int ResolveDefaultToneIndex(PigColor color)
        {
            return color switch
            {
                PigColor.Black => Core.Runtime.ColorAtlas.AtlasPaletteConstants.MinToneIndex,
                PigColor.DarkBlue => 9,
                PigColor.Gray => 9,
                PigColor.Blue => Core.Runtime.ColorAtlas.AtlasPaletteConstants.MaxToneIndex,
                PigColor.White => Core.Runtime.ColorAtlas.AtlasPaletteConstants.MaxToneIndex,
                _ => 13,
            };
        }
    }
}
