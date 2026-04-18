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
                PigColor.Red => new Color(0.95f, 0.28f, 0.28f),
                PigColor.Pink => new Color(1f, 0.45f, 0.72f),
                PigColor.Blue => new Color(0.34f, 0.62f, 1f),
                PigColor.Green => new Color(0.34f, 0.86f, 0.45f),
                PigColor.Yellow => new Color(1f, 0.85f, 0.25f),
                PigColor.Orange => new Color(1f, 0.58f, 0.2f),
                PigColor.Teal => new Color(0.12f, 0.72f, 0.72f),
                PigColor.Purple => new Color(0.66f, 0.45f, 1f),
                PigColor.Gray => new Color(0.62f, 0.62f, 0.62f),
                PigColor.White => new Color(0.97f, 0.97f, 0.97f),
                PigColor.Black => Color.black,
                _ => Color.clear,
            };
        }
    }
}
