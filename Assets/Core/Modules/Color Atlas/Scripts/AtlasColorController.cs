using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Core.Runtime.ColorAtlas
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class AtlasColorController : MonoBehaviour
    {
        [SerializeField] private BaseColor baseColor = BaseColor.Pink;
        [FormerlySerializedAs("targetRenderer")]
        [SerializeField, HideInInspector] private Renderer legacyTargetRenderer;
        [FormerlySerializedAs("additionalRenderers")]
        [SerializeField] private Renderer[] renderers;
        [SerializeField, HideInInspector] private string currentColorInfo;
        [SerializeField, HideInInspector] private int controlledRendererCount;
        [SerializeField, HideInInspector] private int colorIndexOverride = -1;
        [SerializeField, HideInInspector] private int toneIndexOverride = -1;
        [FormerlySerializedAs("useOutline")]
        [SerializeField] private bool enableOutline = true;

        private MaterialPropertyBlock propertyBlock;
        private Renderer[] controlledRenderers;
        private bool isDirty = true;
#if UNITY_EDITOR
        private readonly Dictionary<Renderer, Material[]> editorOriginalSharedMaterials = new();
        private readonly HashSet<Material> editorOwnedMaterials = new();
#endif

        private static readonly int ColorIndexProperty = Shader.PropertyToID("_ColorIndex");
        private static readonly int ToneIndexProperty = Shader.PropertyToID("_ToneIndex");
        private static readonly int EnableOutlineProperty = Shader.PropertyToID("_EnableOutline");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");

        public BaseColor BaseColor => baseColor;
        public bool EnableOutline => enableOutline;

        protected virtual void Awake()
        {
            UpgradeLegacyRendererData();
            CacheRenderers(true);
            ApplyColorSettings();
        }

        protected virtual void OnEnable()
        {
            isDirty = true;
            UpgradeLegacyRendererData();
            CacheRenderers(false);
            ApplyColorSettings();
        }

        protected virtual void Start()
        {
            UpgradeLegacyRendererData();
            CacheRenderers(true);
            ApplyColorSettings();
        }

        protected virtual void OnDisable()
        {
#if UNITY_EDITOR
            RestoreEditorMaterialSettings();
#endif
        }

        protected virtual void OnDestroy()
        {
#if UNITY_EDITOR
            RestoreEditorMaterialSettings();
#endif
        }

        private void LateUpdate()
        {
            if (isDirty)
            {
                ApplyColorSettings();
            }
        }

        public void SetColor(BaseColor color)
        {
            if (baseColor == color
                && colorIndexOverride < 0
                && toneIndexOverride < 0)
            {
                return;
            }

            baseColor = color;
            colorIndexOverride = -1;
            toneIndexOverride = -1;
            isDirty = true;
            ApplyColorSettings();
        }

        public void SetColor(AtlasColor color)
        {
            SetColorAndTone(color.GetColorIndex(), color.GetToneIndex());
        }

        public void SetColorIndex(int colorIndex)
        {
            var clampedColorIndex = AtlasPaletteConstants.ClampColorIndex(colorIndex);
            if (colorIndexOverride == clampedColorIndex)
            {
                return;
            }

            colorIndexOverride = clampedColorIndex;
            isDirty = true;
            ApplyColorSettings();
        }

        public void SetToneIndex(int toneIndex)
        {
            var clampedToneIndex = AtlasPaletteConstants.ClampToneIndex(toneIndex);
            if (toneIndexOverride == clampedToneIndex)
            {
                return;
            }

            toneIndexOverride = clampedToneIndex;
            isDirty = true;
            ApplyColorSettings();
        }

        public void SetColorAndTone(int colorIndex, int toneIndex)
        {
            var clampedColorIndex = AtlasPaletteConstants.ClampColorIndex(colorIndex);
            var clampedToneIndex = AtlasPaletteConstants.ClampToneIndex(toneIndex);
            if (colorIndexOverride == clampedColorIndex
                && toneIndexOverride == clampedToneIndex)
            {
                return;
            }

            colorIndexOverride = clampedColorIndex;
            toneIndexOverride = clampedToneIndex;
            isDirty = true;
            ApplyColorSettings();
        }

        public void SetOutline(bool enabled)
        {
            if (enableOutline == enabled)
            {
                return;
            }

            enableOutline = enabled;
            isDirty = true;
            ApplyColorSettings();
        }

        public AtlasColor GetColor()
        {
            return new AtlasColor((BaseColor)ResolveColorIndex(), (ColorTone)ResolveToneIndex());
        }

        [ContextMenu("Fill Renderers")]
        private void FillRenderers()
        {
            UpgradeLegacyRendererData();
            renderers = CollectDiscoveredRenderers();
            isDirty = true;
            CacheRenderers(true);
            ApplyColorSettings();

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        private void UpgradeLegacyRendererData()
        {
            if (legacyTargetRenderer == null)
            {
                return;
            }

            if (renderers == null || renderers.Length == 0)
            {
                renderers = new[] { legacyTargetRenderer };
            }
            else
            {
                var alreadyExists = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == legacyTargetRenderer)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    var expanded = new Renderer[renderers.Length + 1];
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        expanded[i] = renderers[i];
                    }

                    expanded[renderers.Length] = legacyTargetRenderer;
                    renderers = expanded;
                }
            }

            legacyTargetRenderer = null;
        }

        private void CacheRenderers(bool forceRefresh)
        {
            if (!forceRefresh
                && controlledRenderers != null
                && controlledRenderers.Length > 0)
            {
                return;
            }

            var rendererCount = 0;
            Renderer[] discoveredRenderers = GetComponentsInChildren<Renderer>(true);
            controlledRenderers = new Renderer[discoveredRenderers.Length + (renderers?.Length ?? 0)];

            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    TryAddRenderer(renderers[i], ref rendererCount);
                }
            }

            for (int i = 0; i < discoveredRenderers.Length; i++)
            {
                TryAddRenderer(discoveredRenderers[i], ref rendererCount);
            }

            if (rendererCount != controlledRenderers.Length)
            {
                var trimmedRenderers = new Renderer[rendererCount];
                for (int i = 0; i < rendererCount; i++)
                {
                    trimmedRenderers[i] = controlledRenderers[i];
                }

                controlledRenderers = trimmedRenderers;
            }

            controlledRendererCount = rendererCount;
        }

        private void TryAddRenderer(Renderer rendererCandidate, ref int rendererCount)
        {
            if (!IsValidTargetRenderer(rendererCandidate))
            {
                return;
            }

            for (int i = 0; i < rendererCount; i++)
            {
                if (controlledRenderers[i] == rendererCandidate)
                {
                    return;
                }
            }

            controlledRenderers[rendererCount] = rendererCandidate;
            rendererCount++;
        }

        private static bool IsValidTargetRenderer(Renderer rendererCandidate)
        {
            return rendererCandidate != null
                && rendererCandidate.GetComponent<TMP_Text>() == null
                && SupportsAtlasProperties(rendererCandidate);
        }

        private static bool SupportsAtlasProperties(Renderer rendererCandidate)
        {
            if (rendererCandidate == null)
            {
                return false;
            }

            var sharedMaterials = rendererCandidate.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                var material = sharedMaterials[i];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty(ColorIndexProperty) && material.HasProperty(ToneIndexProperty))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyColorSettings()
        {
            CacheRenderers(false);
            if (!isDirty || controlledRenderers == null || controlledRenderers.Length == 0)
            {
                return;
            }

            #if UNITY_EDITOR
            if (ShouldUseEditorMaterialMode())
            {
                ApplyEditorMaterialSettings();
                isDirty = false;
                return;
            }

            RestoreEditorMaterialSettings();
            #endif

            ApplyRuntimePropertyBlockSettings();
            isDirty = false;
        }

        private void ApplyRuntimePropertyBlockSettings()
        {
            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            var resolvedColorIndex = ResolveColorIndex();
            var resolvedToneIndex = ResolveToneIndex();
            propertyBlock.SetFloat(ColorIndexProperty, resolvedColorIndex);
            propertyBlock.SetFloat(ToneIndexProperty, resolvedToneIndex);
            propertyBlock.SetFloat(EnableOutlineProperty, enableOutline ? 1f : 0f);

            var appliedRendererCount = 0;
            for (int i = 0; i < controlledRenderers.Length; i++)
            {
                var rendererCandidate = controlledRenderers[i];
                if (!IsValidTargetRenderer(rendererCandidate))
                {
                    continue;
                }

                rendererCandidate.SetPropertyBlock(propertyBlock);
                appliedRendererCount++;
            }

            controlledRendererCount = appliedRendererCount;
            currentColorInfo = $"Color:{resolvedColorIndex} Tone:{resolvedToneIndex} Outline:{enableOutline} Renderers:{appliedRendererCount}";
        }

        private int ResolveColorIndex()
        {
            return colorIndexOverride >= 0
                ? AtlasPaletteConstants.ClampColorIndex(colorIndexOverride)
                : AtlasPaletteConstants.ClampColorIndex((int)baseColor);
        }

        private int ResolveToneIndex()
        {
            return toneIndexOverride >= 0
                ? AtlasPaletteConstants.ClampToneIndex(toneIndexOverride)
                : AtlasPaletteConstants.DefaultToneIndex;
        }

#if UNITY_EDITOR
        private void ApplyEditorMaterialSettings()
        {
            var appliedRendererCount = 0;
            var resolvedColorIndex = ResolveColorIndex();
            var resolvedToneIndex = ResolveToneIndex();
            var fallbackColor = ResolveEditorPreviewColor(resolvedColorIndex, resolvedToneIndex);

            for (int i = 0; i < controlledRenderers.Length; i++)
            {
                var rendererCandidate = controlledRenderers[i];
                if (!IsValidTargetRenderer(rendererCandidate))
                {
                    continue;
                }

                var sourceMaterials = rendererCandidate.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    continue;
                }

                if (!editorOriginalSharedMaterials.ContainsKey(rendererCandidate))
                {
                    editorOriginalSharedMaterials[rendererCandidate] = (Material[])sourceMaterials.Clone();
                }

                var editorMaterials = new Material[sourceMaterials.Length];
                var requiresAssignment = false;

                for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
                {
                    var sourceMaterial = sourceMaterials[materialIndex];
                    if (sourceMaterial == null)
                    {
                        continue;
                    }

                    Material materialInstance;
                    if (editorOwnedMaterials.Contains(sourceMaterial))
                    {
                        materialInstance = sourceMaterial;
                    }
                    else
                    {
                        materialInstance = new Material(sourceMaterial)
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        editorOwnedMaterials.Add(materialInstance);
                        requiresAssignment = true;
                    }

                    ApplyEditorMaterialColorSettings(materialInstance, fallbackColor);
                    editorMaterials[materialIndex] = materialInstance;
                }

                if (requiresAssignment)
                {
                    rendererCandidate.sharedMaterials = editorMaterials;
                }

                rendererCandidate.SetPropertyBlock(null);
                appliedRendererCount++;
            }

            controlledRendererCount = appliedRendererCount;
            currentColorInfo = $"Color:{resolvedColorIndex} Tone:{resolvedToneIndex} EditorMaterial Renderers:{appliedRendererCount}";
        }

        private void ApplyEditorMaterialColorSettings(Material material, Color fallbackColor)
        {
            if (material == null)
            {
                return;
            }

            var resolvedColorIndex = ResolveColorIndex();
            var resolvedToneIndex = ResolveToneIndex();

            if (material.HasProperty(ColorIndexProperty))
            {
                material.SetFloat(ColorIndexProperty, resolvedColorIndex);
            }

            if (material.HasProperty(ToneIndexProperty))
            {
                material.SetFloat(ToneIndexProperty, resolvedToneIndex);
            }

            if (material.HasProperty(EnableOutlineProperty))
            {
                material.SetFloat(EnableOutlineProperty, enableOutline ? 1f : 0f);
            }

            if (material.HasProperty(BaseColorProperty))
            {
                material.SetColor(BaseColorProperty, fallbackColor);
            }

            if (material.HasProperty(ColorProperty))
            {
                material.SetColor(ColorProperty, fallbackColor);
            }

            if (material.HasProperty(EmissionColorProperty))
            {
                material.SetColor(EmissionColorProperty, Color.black);
            }
        }

        private void RestoreEditorMaterialSettings()
        {
            if (editorOriginalSharedMaterials.Count == 0 && editorOwnedMaterials.Count == 0)
            {
                return;
            }

            foreach (var pair in editorOriginalSharedMaterials)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                pair.Key.sharedMaterials = pair.Value;
                pair.Key.SetPropertyBlock(null);
            }

            foreach (var material in editorOwnedMaterials)
            {
                if (material != null)
                {
                    DestroyImmediate(material);
                }
            }

            editorOriginalSharedMaterials.Clear();
            editorOwnedMaterials.Clear();
        }

        private bool ShouldUseEditorMaterialMode()
        {
            return !Application.isPlaying
                && gameObject.scene.IsValid()
                && !EditorUtility.IsPersistent(this);
        }

        private static Color ResolveEditorPreviewColor(int colorIndex, int toneIndex)
        {
            if (colorIndex <= 0)
            {
                return new Color32(18, 18, 20, 255);
            }

            var previewBaseColor = ResolveEditorPreviewBaseColor(colorIndex);
            var toneLerp = AtlasPaletteConstants.ClampToneIndex(toneIndex) / (float)AtlasPaletteConstants.MaxToneIndex;
            var brightness = Mathf.Lerp(0.28f, 1f, toneLerp);
            return Color.Lerp(Color.black, previewBaseColor, brightness);
        }

        private static Color ResolveEditorPreviewBaseColor(int colorIndex)
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
#endif

        private Renderer[] CollectDiscoveredRenderers()
        {
            Renderer[] discoveredRenderers = GetComponentsInChildren<Renderer>(true);
            Renderer[] collectedRenderers = new Renderer[discoveredRenderers.Length];
            int rendererCount = 0;

            for (int i = 0; i < discoveredRenderers.Length; i++)
            {
                Renderer rendererCandidate = discoveredRenderers[i];
                if (!IsValidTargetRenderer(rendererCandidate))
                {
                    continue;
                }

                bool alreadyAdded = false;
                for (int j = 0; j < rendererCount; j++)
                {
                    if (collectedRenderers[j] == rendererCandidate)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (alreadyAdded)
                {
                    continue;
                }

                collectedRenderers[rendererCount] = rendererCandidate;
                rendererCount++;
            }

            if (rendererCount == 0)
            {
                return System.Array.Empty<Renderer>();
            }

            if (rendererCount == collectedRenderers.Length)
            {
                return collectedRenderers;
            }

            Renderer[] trimmedRenderers = new Renderer[rendererCount];
            for (int i = 0; i < rendererCount; i++)
            {
                trimmedRenderers[i] = collectedRenderers[i];
            }

            return trimmedRenderers;
        }

        protected virtual void OnValidate()
        {
            isDirty = true;
            UpgradeLegacyRendererData();
            CacheRenderers(true);
            ApplyColorSettings();
        }
    }

    public sealed class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public sealed class ReadOnlyDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            UnityEditor.EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
#endif
}
