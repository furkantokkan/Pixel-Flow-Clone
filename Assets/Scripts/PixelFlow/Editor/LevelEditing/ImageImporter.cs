#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Core.Runtime.ColorAtlas;
using PixelFlow.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.Editor.LevelEditing
{
    internal static class ImageImporter
    {
        internal readonly struct ImportedCellData
        {
            public ImportedCellData(PigColor color, int toneIndex)
            {
                Color = color;
                ToneIndex = AtlasPaletteConstants.ClampToneIndex(toneIndex);
            }

            public PigColor Color { get; }
            public int ToneIndex { get; }
        }

        public static bool TryImport(Texture2D sourceTexture, ImageImportSettings settings, out ImportedCellData[,] grid, out string error)
        {
            grid = null;
            error = null;

            if (sourceTexture == null)
            {
                error = "Select a source image first.";
                return false;
            }

            if (settings == null)
            {
                error = "Import settings are missing.";
                return false;
            }

            PigColorPaletteUtility.EnsurePaletteEntries(settings.PaletteEntries);

            var changedReadability = false;
            TextureImporter importer = null;
            var sourcePath = AssetDatabase.GetAssetPath(sourceTexture);

            if (!EnsureReadable(sourcePath, out importer, out changedReadability, out error))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(sourcePath))
            {
                var refreshedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                if (refreshedTexture != null)
                {
                    sourceTexture = refreshedTexture;
                }
            }

            Texture2D workingTexture = sourceTexture;
            Texture2D croppedTexture = null;

            try
            {
                if (settings.CropTransparentBorders)
                {
                    croppedTexture = CropTransparentBorders(sourceTexture, settings.AlphaThreshold);
                    if (croppedTexture != null)
                    {
                        workingTexture = croppedTexture;
                    }
                }

                var sampledPixels = SampleIntoBoard(workingTexture, settings);
                if (sampledPixels == null)
                {
                    error = "Could not sample the selected image.";
                    return false;
                }

                var paletteSourcePixels = CollectPaletteSourcePixels(workingTexture, settings.AlphaThreshold);
                var mappedColors = MapSampledPixelsToPigColors(sampledPixels, paletteSourcePixels, settings);
                grid = new ImportedCellData[settings.TargetColumns, settings.TargetRows];

                for (int x = 0; x < settings.TargetColumns; x++)
                {
                    for (int yView = 0; yView < settings.TargetRows; yView++)
                    {
                        var yData = (settings.TargetRows - 1) - yView;
                        var index = yData * settings.TargetColumns + x;
                        var color = sampledPixels[index];

                        if (color.a < settings.AlphaThreshold)
                        {
                            grid[x, yView] = new ImportedCellData(PigColor.None, AtlasPaletteConstants.DefaultToneIndex);
                            continue;
                        }

                        var mappedColor = mappedColors[index];
                        grid[x, yView] = new ImportedCellData(
                            mappedColor,
                            ResolveToneIndex(color, mappedColor));
                    }
                }

                return true;
            }
            finally
            {
                if (croppedTexture != null)
                {
                    Object.DestroyImmediate(croppedTexture);
                }

                RestoreReadable(importer, changedReadability);
            }
        }

        private static bool EnsureReadable(string assetPath, out TextureImporter importer, out bool changedReadability, out string error)
        {
            importer = null;
            changedReadability = false;
            error = null;

            if (string.IsNullOrEmpty(assetPath))
            {
                return true;
            }

            importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null || importer.isReadable)
            {
                return true;
            }

            try
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                changedReadability = true;
                return true;
            }
            catch
            {
                error = $"Enable Read/Write for {Path.GetFileName(assetPath)} in the texture import settings.";
                return false;
            }
        }

        private static void RestoreReadable(TextureImporter importer, bool changedReadability)
        {
            if (importer == null || !changedReadability)
            {
                return;
            }

            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        private static Color[] SampleIntoBoard(Texture2D sourceTexture, ImageImportSettings settings)
        {
            var targetWidth = settings.TargetColumns;
            var targetHeight = settings.TargetRows;

            if (settings.FitMode == ImageFitMode.Stretch)
            {
                return ResizeAndReadPixels(sourceTexture, targetWidth, targetHeight);
            }

            var containedPixels = new Color[targetWidth * targetHeight];
            for (int i = 0; i < containedPixels.Length; i++)
            {
                containedPixels[i] = Color.clear;
            }

            var sourceAspect = sourceTexture.width / (float)sourceTexture.height;
            var targetAspect = targetWidth / (float)targetHeight;

            var scaledWidth = targetWidth;
            var scaledHeight = targetHeight;

            if (sourceAspect > targetAspect)
            {
                scaledHeight = Mathf.Max(1, Mathf.RoundToInt(targetWidth / sourceAspect));
            }
            else
            {
                scaledWidth = Mathf.Max(1, Mathf.RoundToInt(targetHeight * sourceAspect));
            }

            var resizedPixels = ResizeAndReadPixels(sourceTexture, scaledWidth, scaledHeight);
            if (resizedPixels == null)
            {
                return null;
            }

            var offsetX = (targetWidth - scaledWidth) / 2;
            var offsetY = (targetHeight - scaledHeight) / 2;

            for (int y = 0; y < scaledHeight; y++)
            {
                for (int x = 0; x < scaledWidth; x++)
                {
                    var sourceIndex = y * scaledWidth + x;
                    var targetIndex = (y + offsetY) * targetWidth + (x + offsetX);
                    containedPixels[targetIndex] = resizedPixels[sourceIndex];
                }
            }

            return containedPixels;
        }

        private static PigColor[] MapSampledPixelsToPigColors(
            Color[] sampledPixels,
            Color[] paletteSourcePixels,
            ImageImportSettings settings)
        {
            var mappedColors = new PigColor[sampledPixels.Length];
            var matchingPaletteEntries = CreateMatchingPaletteEntries(settings.PaletteEntries);
            var enabledPaletteEntries = GetEnabledPaletteEntries(matchingPaletteEntries);

            if (enabledPaletteEntries.Count == 0)
            {
                return mappedColors;
            }

            Color[] clusterCentroids = null;
            PigColor[] clusterPigColors = null;

            if (paletteSourcePixels != null && paletteSourcePixels.Length > 0)
            {
                var clusterCount = DetermineClusterCount(paletteSourcePixels, enabledPaletteEntries.Count);
                if (clusterCount > 1)
                {
                    KMeansQuantize(paletteSourcePixels, clusterCount, out clusterCentroids, out var clusterAssignments);
                    MergeSmallClusters(paletteSourcePixels, clusterCentroids, clusterAssignments);
                    clusterPigColors = ResolveClusterColors(clusterCentroids, clusterAssignments, enabledPaletteEntries);
                    ApplyResolvedClusterDisplayColors(clusterCentroids, clusterAssignments, clusterPigColors, settings.PaletteEntries);
                }
            }

            for (int i = 0; i < sampledPixels.Length; i++)
            {
                var color = sampledPixels[i];
                if (color.a < settings.AlphaThreshold)
                {
                    mappedColors[i] = PigColor.None;
                    continue;
                }

                if (clusterCentroids == null || clusterPigColors == null || clusterCentroids.Length == 0)
                {
                    mappedColors[i] = PigColorPaletteUtility.GetClosestColor(color, enabledPaletteEntries);
                    continue;
                }

                var bestClusterIndex = 0;
                var bestDistance = float.MaxValue;
                for (int clusterIndex = 0; clusterIndex < clusterCentroids.Length; clusterIndex++)
                {
                    var distance = ComputeRgbDistanceSquared(color, clusterCentroids[clusterIndex]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestClusterIndex = clusterIndex;
                    }
                }

                mappedColors[i] = clusterPigColors[bestClusterIndex];
            }

            return mappedColors;
        }

        private static int ResolveToneIndex(Color sourceColor, PigColor mappedColor)
        {
            if (mappedColor == PigColor.None)
            {
                return AtlasPaletteConstants.DefaultToneIndex;
            }

            if (mappedColor == PigColor.Black)
            {
                return AtlasPaletteConstants.MinToneIndex;
            }

            Color.RGBToHSV(sourceColor, out _, out var saturation, out var value);
            var luminance = ComputeLuminance(sourceColor);
            var brightness = Mathf.Lerp(luminance, value, saturation >= 0.2f ? 0.65f : 0.35f);
            var normalizedBrightness = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(brightness));
            var toneIndex = Mathf.RoundToInt(normalizedBrightness * AtlasPaletteConstants.MaxToneIndex);

            if (mappedColor == PigColor.White)
            {
                toneIndex = Mathf.Max(AtlasPaletteConstants.MaxToneIndex - 1, toneIndex);
            }
            else if (mappedColor == PigColor.Gray)
            {
                toneIndex = Mathf.Max(2, toneIndex);
            }

            return AtlasPaletteConstants.ClampToneIndex(toneIndex);
        }

        private static List<PigColorPaletteEntry> CreateMatchingPaletteEntries(IReadOnlyList<PigColorPaletteEntry> paletteEntries)
        {
            var matchingEntries = new List<PigColorPaletteEntry>();
            if (paletteEntries == null)
            {
                return matchingEntries;
            }

            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                if (entry == null || entry.Color == PigColor.None)
                {
                    continue;
                }

                matchingEntries.Add(new PigColorPaletteEntry(
                    entry.Color,
                    PigColorPaletteUtility.GetDisplayColor(entry.Color),
                    entry.Enabled));
            }

            return matchingEntries;
        }

        private static Color[] CollectPaletteSourcePixels(Texture2D sourceTexture, float alphaThreshold)
        {
            if (sourceTexture == null)
            {
                return null;
            }

            var pixels = sourceTexture.GetPixels();
            const int maxPaletteSamples = 4096;
            var opaquePixelCount = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a >= alphaThreshold)
                {
                    opaquePixelCount++;
                }
            }

            if (opaquePixelCount == 0)
            {
                return new Color[0];
            }

            var sampleStride = opaquePixelCount <= maxPaletteSamples
                ? 1
                : Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(opaquePixelCount / (float)maxPaletteSamples)));

            var sampledPixels = new List<Color>(Mathf.Min(opaquePixelCount, maxPaletteSamples));
            for (int y = 0; y < sourceTexture.height; y += sampleStride)
            {
                for (int x = 0; x < sourceTexture.width; x += sampleStride)
                {
                    var color = pixels[(y * sourceTexture.width) + x];
                    if (color.a < alphaThreshold)
                    {
                        continue;
                    }

                    sampledPixels.Add(color);
                }
            }

            if (sampledPixels.Count == 0)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    var color = pixels[i];
                    if (color.a >= alphaThreshold)
                    {
                        sampledPixels.Add(color);
                    }
                }
            }

            return sampledPixels.ToArray();
        }

        private static List<PigColorPaletteEntry> GetEnabledPaletteEntries(IReadOnlyList<PigColorPaletteEntry> paletteEntries)
        {
            var enabledEntries = new List<PigColorPaletteEntry>();
            if (paletteEntries == null)
            {
                return enabledEntries;
            }

            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                if (!entry.Enabled || entry.Color == PigColor.None)
                {
                    continue;
                }

                enabledEntries.Add(entry);
            }

            return enabledEntries;
        }

        private static int DetermineClusterCount(IReadOnlyList<Color> opaquePixels, int enabledPaletteCount)
        {
            var maxClusters = Mathf.Min(8, enabledPaletteCount, opaquePixels.Count);
            if (maxClusters <= 1)
            {
                return maxClusters;
            }

            var coarseBins = new HashSet<int>();
            for (int i = 0; i < opaquePixels.Count; i++)
            {
                var color = opaquePixels[i];
                var r = Mathf.Clamp(Mathf.FloorToInt(color.r * 8f), 0, 7);
                var g = Mathf.Clamp(Mathf.FloorToInt(color.g * 8f), 0, 7);
                var b = Mathf.Clamp(Mathf.FloorToInt(color.b * 8f), 0, 7);
                coarseBins.Add((r << 6) | (g << 3) | b);
            }

            var estimatedClusters = Mathf.Clamp(Mathf.RoundToInt(coarseBins.Count * 0.4f), 2, maxClusters);
            return Mathf.Max(1, estimatedClusters);
        }

        private static void KMeansQuantize(IReadOnlyList<Color> pixels, int clusterCount, out Color[] centroids, out int[] assignments)
        {
            assignments = new int[pixels.Count];
            centroids = new Color[clusterCount];

            InitializeCentroidsFromDominantColors(pixels, centroids);

            for (int iteration = 0; iteration < 16; iteration++)
            {
                var changed = false;

                for (int pixelIndex = 0; pixelIndex < pixels.Count; pixelIndex++)
                {
                    var bestDistance = float.MaxValue;
                    var bestCluster = 0;

                    for (int clusterIndex = 0; clusterIndex < centroids.Length; clusterIndex++)
                    {
                        var distance = ComputeRgbDistanceSquared(pixels[pixelIndex], centroids[clusterIndex]);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestCluster = clusterIndex;
                        }
                    }

                    if (assignments[pixelIndex] != bestCluster)
                    {
                        assignments[pixelIndex] = bestCluster;
                        changed = true;
                    }
                }

                if (!changed && iteration > 0)
                {
                    break;
                }

                RecalculateCentroids(pixels, assignments, centroids);
            }
        }

        private static void InitializeCentroidsFromDominantColors(IReadOnlyList<Color> pixels, Color[] centroids)
        {
            var buckets = new Dictionary<int, ColorBucket>();
            for (int i = 0; i < pixels.Count; i++)
            {
                var color = pixels[i];
                var bucketKey = QuantizeColorKey(color);
                if (!buckets.TryGetValue(bucketKey, out var bucket))
                {
                    bucket = new ColorBucket();
                    buckets.Add(bucketKey, bucket);
                }

                bucket.Add(color);
            }

            var orderedBuckets = new List<ColorBucket>(buckets.Values);
            orderedBuckets.Sort((a, b) => b.Count.CompareTo(a.Count));

            var initializedCount = 0;
            const float minimumSeedDistance = 0.01f;
            for (int i = 0; i < orderedBuckets.Count && initializedCount < centroids.Length; i++)
            {
                var candidateColor = orderedBuckets[i].AverageColor;
                var isDistinct = true;
                for (int seedIndex = 0; seedIndex < initializedCount; seedIndex++)
                {
                    if (ComputeRgbDistanceSquared(candidateColor, centroids[seedIndex]) < minimumSeedDistance)
                    {
                        isDistinct = false;
                        break;
                    }
                }

                if (!isDistinct)
                {
                    continue;
                }

                centroids[initializedCount] = candidateColor;
                initializedCount++;
            }

            for (int i = initializedCount; i < centroids.Length; i++)
            {
                var sampleIndex = Mathf.Clamp(Mathf.RoundToInt(i * pixels.Count / (float)centroids.Length), 0, pixels.Count - 1);
                centroids[i] = pixels[sampleIndex];
            }
        }

        private static void MergeSmallClusters(IReadOnlyList<Color> pixels, Color[] centroids, int[] assignments)
        {
            if (centroids.Length <= 2)
            {
                return;
            }

            var counts = CountAssignments(centroids.Length, assignments);
            var minimumClusterSize = Mathf.Max(6, Mathf.RoundToInt(assignments.Length * 0.015f));

            for (int clusterIndex = 0; clusterIndex < centroids.Length; clusterIndex++)
            {
                if (counts[clusterIndex] == 0 || counts[clusterIndex] >= minimumClusterSize)
                {
                    continue;
                }

                var targetCluster = -1;
                var bestDistance = float.MaxValue;

                for (int candidateIndex = 0; candidateIndex < centroids.Length; candidateIndex++)
                {
                    if (candidateIndex == clusterIndex || counts[candidateIndex] == 0)
                    {
                        continue;
                    }

                    var distance = ComputeRgbDistanceSquared(centroids[clusterIndex], centroids[candidateIndex]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        targetCluster = candidateIndex;
                    }
                }

                if (targetCluster < 0)
                {
                    continue;
                }

                for (int assignmentIndex = 0; assignmentIndex < assignments.Length; assignmentIndex++)
                {
                    if (assignments[assignmentIndex] == clusterIndex)
                    {
                        assignments[assignmentIndex] = targetCluster;
                    }
                }

                counts[targetCluster] += counts[clusterIndex];
                counts[clusterIndex] = 0;
            }

            RecalculateCentroids(pixels, assignments, centroids);
        }

        private static PigColor[] ResolveClusterColors(Color[] centroids, int[] assignments, IReadOnlyList<PigColorPaletteEntry> paletteEntries)
        {
            var counts = CountAssignments(centroids.Length, assignments);
            var primaryColors = new PigColor[centroids.Length];
            var resolvedColors = new PigColor[centroids.Length];
            var groupedIndices = new Dictionary<PigColor, List<int>>();

            for (int clusterIndex = 0; clusterIndex < centroids.Length; clusterIndex++)
            {
                if (counts[clusterIndex] == 0)
                {
                    continue;
                }

                var primaryColor = PigColorPaletteUtility.GetClosestColor(centroids[clusterIndex], paletteEntries);
                if (TryGetDarkNeutralFallbackColor(
                    centroids[clusterIndex],
                    counts[clusterIndex],
                    assignments.Length,
                    primaryColor,
                    paletteEntries,
                    out var darkNeutralColor))
                {
                    primaryColor = darkNeutralColor;
                }

                primaryColors[clusterIndex] = primaryColor;
                resolvedColors[clusterIndex] = primaryColor;

                if (!groupedIndices.TryGetValue(primaryColor, out var indices))
                {
                    indices = new List<int>();
                    groupedIndices.Add(primaryColor, indices);
                }

                indices.Add(clusterIndex);
            }

            foreach (var pair in groupedIndices)
            {
                var indices = pair.Value;
                if (indices.Count <= 1)
                {
                    continue;
                }

                indices.Sort((a, b) => ComputeLuminance(centroids[a]).CompareTo(ComputeLuminance(centroids[b])));
                var baseIndex = indices[indices.Count / 2];
                var baseLuminance = ComputeLuminance(centroids[baseIndex]);
                var luminanceRange = ComputeLuminance(centroids[indices[^1]]) - ComputeLuminance(centroids[indices[0]]);
                var usedColors = new HashSet<PigColor> { pair.Key };

                resolvedColors[baseIndex] = pair.Key;

                for (int i = 0; i < indices.Count; i++)
                {
                    var clusterIndex = indices[i];
                    if (clusterIndex == baseIndex)
                    {
                        continue;
                    }

                    if (TryGetSecondaryChromaticColor(centroids[clusterIndex], pair.Key, usedColors, paletteEntries, out var secondaryColor))
                    {
                        resolvedColors[clusterIndex] = secondaryColor;
                        usedColors.Add(secondaryColor);
                        continue;
                    }

                    if (luminanceRange < 0.08f)
                    {
                        resolvedColors[clusterIndex] = pair.Key;
                        continue;
                    }

                    var clusterLuminance = ComputeLuminance(centroids[clusterIndex]);
                    if (!IsNeutralColor(pair.Key)
                        && indices.Count >= 3
                        && clusterLuminance > baseLuminance + 0.04f
                        && TryGetDuplicateLightFallbackColor(usedColors, paletteEntries, out var duplicateLightColor))
                    {
                        resolvedColors[clusterIndex] = duplicateLightColor;
                        usedColors.Add(duplicateLightColor);
                        continue;
                    }

                    var isHighlightCluster = clusterLuminance > baseLuminance + 0.04f;
                    if (!IsNeutralColor(pair.Key)
                        && isHighlightCluster
                        && TryGetHighlightNeutralColor(centroids[clusterIndex], usedColors, paletteEntries, out var neutralColor))
                    {
                        resolvedColors[clusterIndex] = neutralColor;
                        usedColors.Add(neutralColor);
                    }
                    else
                    {
                        resolvedColors[clusterIndex] = pair.Key;
                    }
                }
            }

            return resolvedColors;
        }

        private static void ApplyResolvedClusterDisplayColors(
            Color[] centroids,
            int[] assignments,
            PigColor[] resolvedColors,
            IList<PigColorPaletteEntry> paletteEntries)
        {
            if (centroids == null
                || assignments == null
                || resolvedColors == null
                || paletteEntries == null)
            {
                return;
            }

            var counts = CountAssignments(centroids.Length, assignments);
            var bestClusterByColor = new Dictionary<PigColor, int>();

            for (int clusterIndex = 0; clusterIndex < resolvedColors.Length; clusterIndex++)
            {
                var color = resolvedColors[clusterIndex];
                if (color == PigColor.None || counts[clusterIndex] == 0)
                {
                    continue;
                }

                if (!bestClusterByColor.TryGetValue(color, out var bestCluster)
                    || counts[clusterIndex] > counts[bestCluster])
                {
                    bestClusterByColor[color] = clusterIndex;
                }
            }

            for (int entryIndex = 0; entryIndex < paletteEntries.Count; entryIndex++)
            {
                var entry = paletteEntries[entryIndex];
                if (entry == null
                    || entry.Color == PigColor.None
                    || IsNeutralColor(entry.Color)
                    || !bestClusterByColor.TryGetValue(entry.Color, out var clusterIndex))
                {
                    continue;
                }

                entry.DisplayColor = centroids[clusterIndex];
            }
        }

        private static bool TryGetSecondaryChromaticColor(
            Color sourceColor,
            PigColor primaryColor,
            HashSet<PigColor> usedColors,
            IReadOnlyList<PigColorPaletteEntry> paletteEntries,
            out PigColor secondaryColor)
        {
            secondaryColor = PigColor.None;
            var primaryDistance = float.MaxValue;
            var bestDistance = float.MaxValue;

            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                if (!entry.Enabled || entry.Color == PigColor.None)
                {
                    continue;
                }

                var distance = PigColorPaletteUtility.GetColorMatchDistance(sourceColor, entry.Color, entry.DisplayColor);
                if (entry.Color == primaryColor)
                {
                    primaryDistance = distance;
                    continue;
                }

                if (usedColors.Contains(entry.Color) || IsNeutralColor(entry.Color))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    secondaryColor = entry.Color;
                }
            }

            if (secondaryColor == PigColor.None)
            {
                return false;
            }

            return bestDistance <= primaryDistance + 0.18f
                && bestDistance <= primaryDistance * 1.85f;
        }

        private static bool TryGetHighlightNeutralColor(
            Color sourceColor,
            HashSet<PigColor> usedColors,
            IReadOnlyList<PigColorPaletteEntry> paletteEntries,
            out PigColor neutralColor)
        {
            neutralColor = PigColor.None;
            if (!IsNeutralLikeHighlight(sourceColor))
            {
                return false;
            }

            var preferredColors = ComputeLuminance(sourceColor) >= 0.72f
                ? new[] { PigColor.White, PigColor.Gray }
                : new[] { PigColor.Gray, PigColor.White };

            for (int preferredIndex = 0; preferredIndex < preferredColors.Length; preferredIndex++)
            {
                var preferredColor = preferredColors[preferredIndex];
                if (usedColors.Contains(preferredColor)
                    || !TryGetEnabledPaletteEntry(preferredColor, paletteEntries, out _))
                {
                    continue;
                }

                neutralColor = preferredColor;
                return true;
            }

            return false;
        }

        private static bool TryGetDuplicateLightFallbackColor(
            HashSet<PigColor> usedColors,
            IReadOnlyList<PigColorPaletteEntry> paletteEntries,
            out PigColor fallbackColor)
        {
            fallbackColor = PigColor.None;
            var preferredColors = new[] { PigColor.Gray, PigColor.White };

            for (int i = 0; i < preferredColors.Length; i++)
            {
                var preferredColor = preferredColors[i];
                if (usedColors.Contains(preferredColor)
                    || !TryGetEnabledPaletteEntry(preferredColor, paletteEntries, out _))
                {
                    continue;
                }

                fallbackColor = preferredColor;
                return true;
            }

            return false;
        }

        private static bool TryGetDarkNeutralFallbackColor(
            Color sourceColor,
            int clusterSize,
            int totalAssignments,
            PigColor primaryColor,
            IReadOnlyList<PigColorPaletteEntry> paletteEntries,
            out PigColor neutralColor)
        {
            neutralColor = PigColor.None;
            if (IsNeutralColor(primaryColor)
                || clusterSize <= 0
                || totalAssignments <= 0
                || !TryGetEnabledPaletteEntry(PigColor.Black, paletteEntries, out _))
            {
                return false;
            }

            Color.RGBToHSV(sourceColor, out _, out _, out var value);
            var luminance = ComputeLuminance(sourceColor);
            var clusterShare = clusterSize / (float)totalAssignments;

            if (clusterShare > 0.18f)
            {
                return false;
            }

            if (value > 0.58f || luminance > 0.4f)
            {
                return false;
            }

            neutralColor = PigColor.Black;
            return true;
        }

        private static bool IsNeutralLikeHighlight(Color sourceColor)
        {
            Color.RGBToHSV(sourceColor, out _, out var saturation, out var value);
            var luminance = ComputeLuminance(sourceColor);
            return saturation < 0.16f
                || (luminance >= 0.84f && value >= 0.9f && saturation <= 0.32f);
        }

        private static bool TryGetEnabledPaletteEntry(
            PigColor color,
            IReadOnlyList<PigColorPaletteEntry> paletteEntries,
            out PigColorPaletteEntry paletteEntry)
        {
            paletteEntry = null;

            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                if (entry.Enabled && entry.Color == color)
                {
                    paletteEntry = entry;
                    return true;
                }
            }

            return false;
        }

        private static bool IsNeutralColor(PigColor color)
        {
            return color == PigColor.Black
                || color == PigColor.Gray
                || color == PigColor.White;
        }

        private static int QuantizeColorKey(Color color)
        {
            var r = Mathf.Clamp(Mathf.FloorToInt(color.r * 15f), 0, 15);
            var g = Mathf.Clamp(Mathf.FloorToInt(color.g * 15f), 0, 15);
            var b = Mathf.Clamp(Mathf.FloorToInt(color.b * 15f), 0, 15);
            return (r << 8) | (g << 4) | b;
        }

        private static int[] CountAssignments(int clusterCount, int[] assignments)
        {
            var counts = new int[clusterCount];
            for (int i = 0; i < assignments.Length; i++)
            {
                counts[assignments[i]]++;
            }

            return counts;
        }

        private static void RecalculateCentroids(IReadOnlyList<Color> pixels, int[] assignments, Color[] centroids)
        {
            var sumR = new float[centroids.Length];
            var sumG = new float[centroids.Length];
            var sumB = new float[centroids.Length];
            var sumA = new float[centroids.Length];
            var counts = new int[centroids.Length];

            for (int i = 0; i < assignments.Length; i++)
            {
                var clusterIndex = assignments[i];
                var color = pixels[i];
                sumR[clusterIndex] += color.r;
                sumG[clusterIndex] += color.g;
                sumB[clusterIndex] += color.b;
                sumA[clusterIndex] += color.a;
                counts[clusterIndex]++;
            }

            for (int clusterIndex = 0; clusterIndex < centroids.Length; clusterIndex++)
            {
                if (counts[clusterIndex] == 0)
                {
                    continue;
                }

                centroids[clusterIndex] = new Color(
                    sumR[clusterIndex] / counts[clusterIndex],
                    sumG[clusterIndex] / counts[clusterIndex],
                    sumB[clusterIndex] / counts[clusterIndex],
                    sumA[clusterIndex] / counts[clusterIndex]);
            }
        }

        private static float ComputeRgbDistanceSquared(Color a, Color b)
        {
            var dr = a.r - b.r;
            var dg = a.g - b.g;
            var db = a.b - b.b;
            return (dr * dr) + (dg * dg) + (db * db);
        }

        private static float ComputeLuminance(Color color)
        {
            return (0.299f * color.r) + (0.587f * color.g) + (0.114f * color.b);
        }

        private sealed class ColorBucket
        {
            private float sumR;
            private float sumG;
            private float sumB;
            private float sumA;

            public int Count { get; private set; }

            public Color AverageColor => Count <= 0
                ? Color.clear
                : new Color(sumR / Count, sumG / Count, sumB / Count, sumA / Count);

            public void Add(Color color)
            {
                sumR += color.r;
                sumG += color.g;
                sumB += color.b;
                sumA += color.a;
                Count++;
            }
        }

        private static Texture2D CropTransparentBorders(Texture2D texture, float alphaThreshold)
        {
            var pixels = texture.GetPixels();
            var minX = texture.width;
            var minY = texture.height;
            var maxX = -1;
            var maxY = -1;

            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    var color = pixels[(y * texture.width) + x];
                    if (color.a < alphaThreshold)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return null;
            }

            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cropped.SetPixels(texture.GetPixels(minX, minY, width, height));
            cropped.Apply();
            return cropped;
        }

        private static Color[] ResizeAndReadPixels(Texture2D source, int width, int height)
        {
            var resizedPixels = new Color[width * height];

            // Bilinear sampling keeps small source details alive before the adaptive cluster
            // pass maps them into the fixed pig palette.
            for (int y = 0; y < height; y++)
            {
                var sampleY = Mathf.Clamp01((y + 0.5f) / height);

                for (int x = 0; x < width; x++)
                {
                    var sampleX = Mathf.Clamp01((x + 0.5f) / width);
                    resizedPixels[(y * width) + x] = source.GetPixelBilinear(sampleX, sampleY);
                }
            }

            return resizedPixels;
        }
    }
}
#endif
