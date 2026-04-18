using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    public enum ImageFitMode
    {
        Contain = 0,
        Stretch = 1,
    }

    [Serializable]
    public sealed class PigColorPaletteEntry
    {
        [SerializeField] private PigColor color;
        [SerializeField] private Color displayColor;
        [SerializeField] private bool enabled = true;

        public PigColorPaletteEntry(PigColor color, Color displayColor, bool enabled = true)
        {
            this.color = color;
            this.displayColor = displayColor;
            this.enabled = enabled;
        }

        public PigColor Color
        {
            get => color;
            set => color = value;
        }

        public Color DisplayColor
        {
            get => displayColor;
            set => displayColor = value;
        }

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }
    }

    [Serializable]
    public sealed class ImageImportSettings
    {
        [Min(1)]
        [SerializeField] private int targetColumns = 12;

        [Min(1)]
        [SerializeField] private int targetRows = 12;

        [Range(0f, 1f)]
        [SerializeField] private float alphaThreshold = 0.1f;

        [SerializeField] private bool cropTransparentBorders = true;
        [SerializeField] private ImageFitMode fitMode = ImageFitMode.Contain;
        
        [Range(0.01f, 0.2f)]
        [SerializeField] private float imageScale = 0.05f;

        [Min(0.01f)]
        [SerializeField] private float cellSpacing = 0.6f;

        [SerializeField] private float verticalOffset = 0.5f;
        [SerializeField] private Vector3 blockLocalScale = Vector3.one;
        [SerializeField] private List<PigColorPaletteEntry> paletteEntries = PigColorPaletteUtility.CreateDefaultPaletteEntries();

        public int TargetColumns
        {
            get => targetColumns;
            set => targetColumns = Mathf.Max(1, value);
        }

        public int TargetRows
        {
            get => targetRows;
            set => targetRows = Mathf.Max(1, value);
        }

        public float AlphaThreshold
        {
            get => alphaThreshold;
            set => alphaThreshold = Mathf.Clamp01(value);
        }

        public bool CropTransparentBorders
        {
            get => cropTransparentBorders;
            set => cropTransparentBorders = value;
        }

        public ImageFitMode FitMode
        {
            get => fitMode;
            set => fitMode = value;
        }

        public float ImageScale
        {
            get => Mathf.Clamp(imageScale, 0.01f, 0.2f);
            set => imageScale = Mathf.Clamp(value, 0.01f, 0.2f);
        }

        public float CellSpacing
        {
            get => cellSpacing;
            set => cellSpacing = Mathf.Max(0.01f, value);
        }

        public float VerticalOffset
        {
            get => verticalOffset;
            set => verticalOffset = value;
        }

        public Vector3 BlockLocalScale
        {
            get => blockLocalScale;
            set => blockLocalScale = value;
        }

        public List<PigColorPaletteEntry> PaletteEntries => paletteEntries;

        public ImageImportSettings Clone()
        {
            var clone = new ImageImportSettings
            {
                targetColumns = targetColumns,
                targetRows = targetRows,
                alphaThreshold = alphaThreshold,
                cropTransparentBorders = cropTransparentBorders,
                fitMode = fitMode,
                imageScale = imageScale,
                cellSpacing = cellSpacing,
                verticalOffset = verticalOffset,
                blockLocalScale = blockLocalScale,
                paletteEntries = new List<PigColorPaletteEntry>(paletteEntries.Count),
            };

            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                clone.paletteEntries.Add(new PigColorPaletteEntry(entry.Color, entry.DisplayColor, entry.Enabled));
            }

            return clone;
        }
    }
}
