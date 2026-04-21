using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Visuals;
using System.Collections.Generic;
using UnityEngine;
using Dreamteck.Splines;
using TMPro;
using VContainer;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PixelFlow.Runtime.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentContext : EnvironmentLifetimeScope
    {
        private const string AtlasSharedMaterialGuid = "10582670d432d8d43a86556ee38b7e14";
        private static readonly Vector3 TrayCounterLocalPosition = new(0f, -0.42f, 0f);
        private static readonly Quaternion TrayCounterLocalRotation = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Vector3 TrayCounterLocalScale = Vector3.one * 0.1f;

        [SerializeField] private Transform blockContainer;
        [SerializeField] private Transform holdingContainer;
        [SerializeField] private Transform deckContainer;
        [SerializeField] private Material atlasSharedMaterial;
        
        [SerializeField] private Transform trayRootPos;
        [SerializeField] private Transform trayEquipPos;
        [SerializeField] private Transform trayDropPos;
        [SerializeField] private SplineComputer dispatchSpline;
        [SerializeField] private TMP_Text trayCounterText;

        private ThemeDatabase themeDatabase;
        private Theme defaultTheme;
        private BlockData defaultBlockData;
        private ProjectRuntimeSettings runtimeSettings;

        public Transform BlockContainer => blockContainer;
        public Transform HoldingContainer => holdingContainer;
        public Transform DeckContainer => deckContainer;
        public Material AtlasSharedMaterial => atlasSharedMaterial;
        public Transform TrayRoot => trayRootPos;
        public Transform TrayEquipPos => trayEquipPos;
        public Transform TrayDropPos => trayDropPos;
        public SplineComputer DispatchSpline => dispatchSpline;
        public TMP_Text TrayCounterText => trayCounterText;
        public ThemeDatabase ThemeDatabase => themeDatabase;
        public Theme DefaultTheme => defaultTheme;
        public BlockData DefaultBlockData => defaultBlockData;

        public int HoldingContainerCapacity
        {
            get
            {
                ResolveMissingReferences();
                return holdingContainer != null ? holdingContainer.childCount : 0;
            }
        }

        public int ActiveHoldingContainerCount
        {
            get
            {
                ResolveMissingReferences();
                if (holdingContainer == null)
                {
                    return 0;
                }

                var activeCount = 0;
                for (int i = 0; i < holdingContainer.childCount; i++)
                {
                    if (holdingContainer.GetChild(i).gameObject.activeSelf)
                    {
                        activeCount++;
                    }
                }

                return activeCount;
            }
        }

        protected override void Awake()
        {
            ResolveMissingReferences();
            base.Awake();
        }

        private void Reset()
        {
            ResolveMissingReferences();
        }

        private void OnValidate()
        {
            ResolveMissingReferences();
        }

        [ContextMenu("Resolve Missing References")]
        public void ResolveMissingReferences()
        {
            if (blockContainer == null)
            {
                blockContainer = ResolveTransform("Block_Container");
            }

            if (holdingContainer == null)
            {
                holdingContainer = ResolveTransform("Holding_Container");
            }

            if (deckContainer == null)
            {
                deckContainer = ResolveTransform("Deck_Container");
            }

            if (trayRootPos == null)
            {
                trayRootPos = ResolveTransform("TrayRoot");
            }

            if (trayEquipPos == null)
            {
                trayEquipPos = ResolveTransform("TrayEquipPos");
            }

            if (trayDropPos == null)
            {
                trayDropPos = ResolveTransform("TrayDropPos");
            }

            if (dispatchSpline == null)
            {
                dispatchSpline = GetComponentInChildren<SplineComputer>(true);
            }

            if (trayCounterText == null)
            {
                trayCounterText = ResolveTrayCounterText();
            }

            runtimeSettings ??= ResolveRuntimeSettings();
            atlasSharedMaterial ??= ResolveAtlasSharedMaterial();
            SynchronizeAtlasMaterials();
        }

        [Inject]
        public void InjectProjectDependencies(
            ThemeDatabase injectedThemeDatabase,
            Theme injectedDefaultTheme,
            BlockData injectedDefaultBlockData,
            ProjectRuntimeSettings injectedRuntimeSettings)
        {
            themeDatabase ??= injectedThemeDatabase;
            defaultTheme ??= injectedDefaultTheme;
            defaultBlockData ??= injectedDefaultBlockData;
            runtimeSettings ??= injectedRuntimeSettings;
            atlasSharedMaterial ??= ResolveAtlasSharedMaterial();
            SynchronizeAtlasMaterials();
        }

        public void ApplyEditorContext(
            ThemeDatabase resolvedThemeDatabase,
            Theme resolvedTheme,
            BlockData resolvedDefaultBlockData)
        {
            themeDatabase ??= resolvedThemeDatabase;
            if (resolvedTheme != null)
            {
                defaultTheme = resolvedTheme;
            }

            defaultBlockData ??= resolvedDefaultBlockData;
            atlasSharedMaterial ??= ResolveAtlasSharedMaterial();
            SynchronizeAtlasMaterials();
        }

        public Theme ResolveTheme(Theme overrideTheme = null)
        {
            return overrideTheme ?? defaultTheme;
        }

        public TMP_Text EnsureTrayCounterText()
        {
            ResolveMissingReferences();

            if (trayCounterText == null)
            {
                var anchor = trayRootPos != null
                    ? trayRootPos
                    : trayEquipPos != null
                        ? trayEquipPos
                        : holdingContainer;
                if (anchor == null)
                {
                    return null;
                }

                trayCounterText = CreateTrayCounterText(anchor);
                if (trayCounterText == null)
                {
                    return null;
                }

                trayCounterText.transform.localPosition = TrayCounterLocalPosition;
                trayCounterText.transform.localRotation = TrayCounterLocalRotation;
                trayCounterText.transform.localScale = TrayCounterLocalScale;
            }

            return trayCounterText;
        }

        [ContextMenu("Synchronize Atlas Materials")]
        public void SynchronizeAtlasMaterials()
        {
            if (atlasSharedMaterial == null)
            {
                return;
            }

            runtimeSettings ??= ResolveRuntimeSettings();

            ApplyAtlasMaterialToRoot(runtimeSettings != null && runtimeSettings.BlockPrefab != null
                ? runtimeSettings.BlockPrefab.gameObject
                : null);
            ApplyAtlasMaterialToRoot(runtimeSettings != null && runtimeSettings.PigPrefab != null
                ? runtimeSettings.PigPrefab.gameObject
                : null);
            ApplyAtlasMaterialToRoot(runtimeSettings != null && runtimeSettings.BulletPrefab != null
                ? runtimeSettings.BulletPrefab.gameObject
                : null);
            ApplyAtlasMaterialToRoot(defaultBlockData != null ? defaultBlockData.BlockPrefab : null);
            ApplyAtlasMaterialToRoot(gameObject);
        }

        public int ApplyHoldingContainerCount(int desiredCount, int minCount = 0, int maxCount = int.MaxValue)
        {
            ResolveMissingReferences();
            if (holdingContainer == null)
            {
                return 0;
            }

            var capacity = holdingContainer.childCount;
            if (capacity == 0)
            {
                return 0;
            }

            var appliedCount = Mathf.Clamp(desiredCount, Mathf.Max(0, minCount), Mathf.Min(maxCount, capacity));
            var orderedSlots = GetOrderedHoldingSlots(activeOnly: false);
            for (int i = 0; i < orderedSlots.Count; i++)
            {
                orderedSlots[i].gameObject.SetActive(i < appliedCount);
            }

            return appliedCount;
        }

        public Transform GetHoldingSlot(int index, bool activeOnly = false)
        {
            ResolveMissingReferences();
            if (holdingContainer == null || index < 0)
            {
                return null;
            }

            var orderedSlots = GetOrderedHoldingSlots(activeOnly);
            return index < orderedSlots.Count
                ? orderedSlots[index]
                : null;
        }

        private List<Transform> GetOrderedHoldingSlots(bool activeOnly)
        {
            var orderedSlots = new List<Transform>();
            if (holdingContainer == null)
            {
                return orderedSlots;
            }

            for (int i = 0; i < holdingContainer.childCount; i++)
            {
                var slot = holdingContainer.GetChild(i);
                if (slot == null)
                {
                    continue;
                }

                if (activeOnly && !slot.gameObject.activeSelf)
                {
                    continue;
                }

                orderedSlots.Add(slot);
            }

            orderedSlots.Sort(CompareHoldingSlotsByVisualOrder);
            return orderedSlots;
        }

        private static int CompareHoldingSlotsByVisualOrder(Transform left, Transform right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var xCompare = left.position.x.CompareTo(right.position.x);
            if (xCompare != 0)
            {
                return xCompare;
            }

            return left.GetSiblingIndex().CompareTo(right.GetSiblingIndex());
        }

        private Transform ResolveTransform(string targetName)
        {
            var child = transform.Find(targetName);
            if (child != null)
            {
                return child;
            }

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == targetName)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private TMP_Text ResolveTrayCounterText()
        {
            var directChild = transform.Find("TrayCounterText") ?? transform.Find("Tray Text");
            if (directChild != null)
            {
                return directChild.GetComponent<TMP_Text>();
            }

            var texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null
                    && (texts[i].name == "TrayCounterText" || texts[i].name == "Tray Text"))
                {
                    return texts[i];
                }
            }

            return null;
        }

        private static TMP_Text CreateTrayCounterText(Transform parent)
        {
            var counterObject = new GameObject("TrayCounterText");
            counterObject.transform.SetParent(parent, false);

            var text = counterObject.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 8f;
            text.color = Color.white;
            text.enableAutoSizing = false;
            text.raycastTarget = false;

            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            return text;
        }

        private ProjectRuntimeSettings ResolveRuntimeSettings()
        {
            if (runtimeSettings != null)
            {
                return runtimeSettings;
            }

#if UNITY_EDITOR
            var runtimeSettingGuids = AssetDatabase.FindAssets($"t:{nameof(ProjectRuntimeSettings)}");
            if (runtimeSettingGuids == null || runtimeSettingGuids.Length == 0)
            {
                return null;
            }

            var runtimeSettingsPath = AssetDatabase.GUIDToAssetPath(runtimeSettingGuids[0]);
            runtimeSettings = AssetDatabase.LoadAssetAtPath<ProjectRuntimeSettings>(runtimeSettingsPath);
#endif
            return runtimeSettings;
        }

        private Material ResolveAtlasSharedMaterial()
        {
            if (atlasSharedMaterial != null)
            {
                return atlasSharedMaterial;
            }

            runtimeSettings ??= ResolveRuntimeSettings();

            var resolvedMaterial = ResolveAtlasMaterialFromRoot(runtimeSettings != null && runtimeSettings.BlockPrefab != null
                ? runtimeSettings.BlockPrefab.gameObject
                : null);
            if (resolvedMaterial != null)
            {
                return resolvedMaterial;
            }

            resolvedMaterial = ResolveAtlasMaterialFromRoot(runtimeSettings != null && runtimeSettings.PigPrefab != null
                ? runtimeSettings.PigPrefab.gameObject
                : null);
            if (resolvedMaterial != null)
            {
                return resolvedMaterial;
            }

            resolvedMaterial = ResolveAtlasMaterialFromRoot(runtimeSettings != null && runtimeSettings.BulletPrefab != null
                ? runtimeSettings.BulletPrefab.gameObject
                : null);
            if (resolvedMaterial != null)
            {
                return resolvedMaterial;
            }

            resolvedMaterial = ResolveAtlasMaterialFromRoot(defaultBlockData != null ? defaultBlockData.BlockPrefab : null);
            if (resolvedMaterial != null)
            {
                return resolvedMaterial;
            }

#if UNITY_EDITOR
            var atlasMaterialPath = AssetDatabase.GUIDToAssetPath(AtlasSharedMaterialGuid);
            return string.IsNullOrWhiteSpace(atlasMaterialPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Material>(atlasMaterialPath);
#else
            return null;
#endif
        }

        private void ApplyAtlasMaterialToRoot(GameObject root)
        {
            if (root == null || atlasSharedMaterial == null)
            {
                return;
            }

            var atlasTargets = root.GetComponentsInChildren<AtlasColorTarget>(true);
            for (int i = 0; i < atlasTargets.Length; i++)
            {
                ApplyAtlasMaterialToTarget(atlasTargets[i]);
            }
        }

        private void ApplyAtlasMaterialToTarget(AtlasColorTarget atlasTarget)
        {
            if (atlasTarget == null)
            {
                return;
            }

            var renderers = atlasTarget.GetComponentsInChildren<Renderer>(true);
            var appliedChanges = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var rendererCandidate = renderers[i];
                if (!ShouldUseAtlasSharedMaterial(rendererCandidate))
                {
                    continue;
                }

                var sharedMaterials = rendererCandidate.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                var rendererChanged = false;
                for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    if (sharedMaterials[materialIndex] == atlasSharedMaterial)
                    {
                        continue;
                    }

                    sharedMaterials[materialIndex] = atlasSharedMaterial;
                    rendererChanged = true;
                }

                if (!rendererChanged)
                {
                    continue;
                }

                rendererCandidate.sharedMaterials = sharedMaterials;
                appliedChanges = true;

#if UNITY_EDITOR
                EditorUtility.SetDirty(rendererCandidate);
#endif
            }

            if (!appliedChanges)
            {
                return;
            }

            atlasTarget.SetColor(atlasTarget.CurrentColor, atlasTarget.CurrentToneIndex);

#if UNITY_EDITOR
            EditorUtility.SetDirty(atlasTarget);
#endif
        }

        private static Material ResolveAtlasMaterialFromRoot(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var rendererCandidate = renderers[i];
                if (!ShouldUseAtlasSharedMaterial(rendererCandidate))
                {
                    continue;
                }

                var sharedMaterials = rendererCandidate.sharedMaterials;
                for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    var material = sharedMaterials[materialIndex];
                    if (MaterialSupportsAtlas(material))
                    {
                        return material;
                    }
                }
            }

            return null;
        }

        private static bool ShouldUseAtlasSharedMaterial(Renderer rendererCandidate)
        {
            return rendererCandidate != null
                && rendererCandidate.GetComponent<TMP_Text>() == null
                && rendererCandidate is not TrailRenderer
                && rendererCandidate is not ParticleSystemRenderer;
        }

        private static bool MaterialSupportsAtlas(Material material)
        {
            return material != null
                && material.HasProperty("_ColorIndex")
                && material.HasProperty("_ToneIndex");
        }
    }
}
