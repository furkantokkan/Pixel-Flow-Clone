using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
using System.Collections.Generic;
using UnityEngine;
using Dreamteck.Splines;
using TMPro;
using VContainer;

namespace PixelFlow.Runtime.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentContext : EnvironmentLifetimeScope
    {
        private static readonly Vector3 TrayCounterLocalPosition = new(0f, -0.42f, 0f);
        private static readonly Quaternion TrayCounterLocalRotation = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Vector3 TrayCounterLocalScale = Vector3.one * 0.1f;

        [SerializeField] private Transform blockContainer;
        [SerializeField] private Transform holdingContainer;
        [SerializeField] private Transform deckContainer;
        
        [SerializeField] private Transform trayRootPos;
        [SerializeField] private Transform trayEquipPos;
        [SerializeField] private Transform trayDropPos;
        [SerializeField] private SplineComputer dispatchSpline;
        [SerializeField] private TMP_Text trayCounterText;

        private ThemeDatabase themeDatabase;
        private Theme defaultTheme;
        private BlockData defaultBlockData;

        public Transform BlockContainer => blockContainer;
        public Transform HoldingContainer => holdingContainer;
        public Transform DeckContainer => deckContainer;
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
        }

        [Inject]
        public void Construct(
            ThemeDatabase injectedThemeDatabase,
            Theme injectedDefaultTheme,
            BlockData injectedDefaultBlockData)
        {
            themeDatabase ??= injectedThemeDatabase;
            defaultTheme ??= injectedDefaultTheme;
            defaultBlockData ??= injectedDefaultBlockData;
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
    }
}
