using Core.Runtime.ColorAtlas;
using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowAtlasColorTarget : AtlasColorController
    {
        [SerializeField] private PigColor currentColor = PigColor.Pink;

        public PigColor CurrentColor => currentColor;

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
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            SetColor(MapColor(currentColor));
        }

        private static BaseColor MapColor(PigColor color)
        {
            return color switch
            {
                PigColor.Red => BaseColor.Red,
                PigColor.Pink => BaseColor.Pink,
                PigColor.Blue => BaseColor.Blue,
                PigColor.Green => BaseColor.Green,
                PigColor.Yellow => BaseColor.Yellow,
                PigColor.Orange => BaseColor.Orange,
                PigColor.Teal => BaseColor.Cyan,
                PigColor.Purple => BaseColor.Purple,
                PigColor.Black => BaseColor.Black,
                _ => BaseColor.Gray
            };
        }
    }
}
