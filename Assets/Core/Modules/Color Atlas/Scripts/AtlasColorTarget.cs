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
        [SerializeField, HideInInspector] private Transform excludedRoot;

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

        public void SetExcludedRoot(Transform root)
        {
            if (excludedRoot == root)
            {
                return;
            }

            ClearExcludedRendererOverrides(excludedRoot);
            excludedRoot = root;
            ClearExcludedRendererOverrides(excludedRoot);
            RefreshControlledRenderers();
        }

        public bool IsRendererExcluded(Renderer rendererCandidate)
        {
            if (excludedRoot == null || rendererCandidate == null)
            {
                return false;
            }

            Transform candidateTransform = rendererCandidate.transform;
            return candidateTransform == excludedRoot
                || candidateTransform.IsChildOf(excludedRoot);
        }

        protected override bool ShouldControlRenderer(Renderer rendererCandidate)
        {
            if (!base.ShouldControlRenderer(rendererCandidate))
            {
                return false;
            }

            if (excludedRoot == null || rendererCandidate == null)
            {
                return true;
            }

            return !IsRendererExcluded(rendererCandidate);
        }

        private void ApplyCurrentColor()
        {
            SetColorAndTone(
                PigColorAtlasUtility.ResolveColorIndex(currentColor),
                ResolveToneIndex(currentColor, currentToneIndex));
        }

        private static void ClearExcludedRendererOverrides(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(null);
                }
            }
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
