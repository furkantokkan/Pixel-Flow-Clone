using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Data;
using UnityEngine;
using Dreamteck.Splines;
using TMPro;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowEnvironmentContext : MonoBehaviour
    {
        private static readonly Vector3 TrayCounterLocalPosition = new(0f, -0.42f, 0f);
        private static readonly Quaternion TrayCounterLocalRotation = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Vector3 TrayCounterLocalScale = Vector3.one * 0.1f;

        [SerializeField] private Transform blockContainer;
        [SerializeField] private Transform holdingContainer;
        [SerializeField] private Transform deckContainer;
        [SerializeField] private Transform trayEquipPos;
        [SerializeField] private Transform trayDropPos;
        [SerializeField] private SplineComputer dispatchSpline;
        [SerializeField] private TMP_Text trayCounterText;

        private PixelFlowThemeDatabase themeDatabase;
        private PixelFlowTheme defaultTheme;
        private PixelFlowBlockData defaultBlockData;
        private bool dependenciesInjected;

        public Transform BlockContainer => blockContainer;
        public Transform HoldingContainer => holdingContainer;
        public Transform DeckContainer => deckContainer;
        public Transform TrayEquipPos => trayEquipPos;
        public Transform TrayDropPos => trayDropPos;
        public SplineComputer DispatchSpline => dispatchSpline;
        public TMP_Text TrayCounterText => trayCounterText;
        public PixelFlowThemeDatabase ThemeDatabase => themeDatabase;
        public PixelFlowTheme DefaultTheme => defaultTheme;
        public PixelFlowBlockData DefaultBlockData => defaultBlockData;

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

        private void Awake()
        {
            InjectDependencies();
            ResolveMissingReferences();
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

        public void InjectDependencies()
        {
            if (dependenciesInjected)
            {
                return;
            }

            var projectLifetimeScope = LifetimeScope.Find<PixelFlowProjectLifetimeScope>();
            if (projectLifetimeScope?.Container == null)
            {
                return;
            }

            projectLifetimeScope.Container.Inject(this);
        }

        [Inject]
        public void Construct(
            PixelFlowThemeDatabase injectedThemeDatabase,
            PixelFlowTheme injectedDefaultTheme,
            PixelFlowBlockData injectedDefaultBlockData)
        {
            themeDatabase ??= injectedThemeDatabase;
            defaultTheme ??= injectedDefaultTheme;
            defaultBlockData ??= injectedDefaultBlockData;
            dependenciesInjected = true;
        }

        public PixelFlowTheme ResolveTheme(PixelFlowTheme overrideTheme = null)
        {
            InjectDependencies();
            return overrideTheme ?? defaultTheme;
        }

        public TMP_Text EnsureTrayCounterText()
        {
            ResolveMissingReferences();

            var anchor = trayEquipPos != null
                ? trayEquipPos
                : holdingContainer;
            if (anchor == null)
            {
                return null;
            }

            if (trayCounterText == null)
            {
                trayCounterText = CreateTrayCounterText(anchor);
            }

            if (trayCounterText == null)
            {
                return null;
            }

            trayCounterText.transform.SetParent(anchor, false);
            trayCounterText.transform.localPosition = TrayCounterLocalPosition;
            trayCounterText.transform.localRotation = TrayCounterLocalRotation;
            trayCounterText.transform.localScale = TrayCounterLocalScale;
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
            for (int i = 0; i < capacity; i++)
            {
                holdingContainer.GetChild(i).gameObject.SetActive(i < appliedCount);
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

            if (!activeOnly)
            {
                return index < holdingContainer.childCount
                    ? holdingContainer.GetChild(index)
                    : null;
            }

            var activeIndex = 0;
            for (int i = 0; i < holdingContainer.childCount; i++)
            {
                var slot = holdingContainer.GetChild(i);
                if (!slot.gameObject.activeSelf)
                {
                    continue;
                }

                if (activeIndex == index)
                {
                    return slot;
                }

                activeIndex++;
            }

            return null;
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
            var directChild = transform.Find("TrayCounterText");
            if (directChild != null)
            {
                return directChild.GetComponent<TMP_Text>();
            }

            var texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "TrayCounterText")
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
