using Core.Runtime.ColorAtlas;
using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class AtlasColorTarget : AtlasColorController
    {
        [SerializeField] private PigColor currentColor = PigColor.Pink;
        [SerializeField, Range(AtlasPaletteConstants.MinToneIndex, AtlasPaletteConstants.MaxToneIndex)]
        private int currentToneIndex = AtlasPaletteConstants.DefaultToneIndex;

        public PigColor CurrentColor => currentColor;
        public int CurrentToneIndex => AtlasPaletteConstants.ClampToneIndex(currentToneIndex);

        protected override void Awake()
        {
            base.Awake();
            ApplyCurrentColor();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyCurrentColor();
        }

        protected override void Start()
        {
            base.Start();
            ApplyCurrentColor();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ApplyCurrentColor();
        }

        public void SetColor(PigColor color)
        {
            currentColor = color;
            currentToneIndex = PigColorAtlasUtility.ResolveDefaultToneIndex(color);
            ApplyCurrentColor();
        }

        public void SetColor(PigColor color, int toneIndex)
        {
            currentColor = color;
            currentToneIndex = AtlasPaletteConstants.ClampToneIndex(toneIndex);
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            SetColorAndTone(
                PigColorAtlasUtility.ResolveColorIndex(currentColor),
                ResolveToneIndex(currentColor, currentToneIndex));
        }

        private static int ResolveToneIndex(PigColor color, int toneIndex)
        {
            return color == PigColor.None
                ? AtlasPaletteConstants.DefaultToneIndex
                : color == PigColor.Black
                    ? AtlasPaletteConstants.MinToneIndex
                    : AtlasPaletteConstants.ClampToneIndex(toneIndex);
        }
    }
}
