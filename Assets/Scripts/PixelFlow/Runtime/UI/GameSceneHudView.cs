using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PixelFlow.Runtime.UI
{
    [DisallowMultipleComponent]
    public sealed class GameSceneHudView : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private string levelTextFormat = "Level {0}";
        [SerializeField] private GameObject winPanel;
        [SerializeField] private GameObject losePanel;
        [SerializeField] private Button nextLevelButton;
        [SerializeField] private Button restartButton;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        public void SetLevelNumber(int levelNumber)
        {
            ResolveReferences();
            if (levelText == null)
            {
                return;
            }

            levelText.text = string.Format(levelTextFormat, Mathf.Max(1, levelNumber));
        }

        public void ShowPlayingState()
        {
            ResolveReferences();
            SetPanelVisible(winPanel, false);
            SetPanelVisible(losePanel, false);
        }

        public void ShowWinState()
        {
            ResolveReferences();
            SetPanelVisible(winPanel, true);
            SetPanelVisible(losePanel, false);
        }

        public void ShowLoseState()
        {
            ResolveReferences();
            SetPanelVisible(winPanel, false);
            SetPanelVisible(losePanel, true);
        }

        public void AddRestartListener(UnityAction listener)
        {
            ResolveReferences();
            restartButton?.onClick.AddListener(listener);
        }

        public void RemoveRestartListener(UnityAction listener)
        {
            restartButton?.onClick.RemoveListener(listener);
        }

        public void AddNextLevelListener(UnityAction listener)
        {
            ResolveReferences();
            nextLevelButton?.onClick.AddListener(listener);
        }

        public void RemoveNextLevelListener(UnityAction listener)
        {
            nextLevelButton?.onClick.RemoveListener(listener);
        }

        private void ResolveReferences()
        {
            var textComponents = GetComponentsInChildren<TMP_Text>(includeInactive: true);
            if (levelText == null)
            {
                foreach (var textComponent in textComponents)
                {
                    if (textComponent == null)
                    {
                        continue;
                    }

                    if (textComponent.name == "LevelText")
                    {
                        levelText = textComponent;
                        break;
                    }
                }

                if (levelText == null && textComponents.Length > 0)
                {
                    levelText = textComponents[0];
                }
            }

            winPanel ??= FindChildObject("Win Panel");
            losePanel ??= FindChildObject("Lose Panel");
            nextLevelButton ??= FindChildComponent<Button>("Next Level");
            restartButton ??= FindChildComponent<Button>("Restart");
        }

        private T FindChildComponent<T>(string childName) where T : Component
        {
            var child = FindChildObject(childName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private GameObject FindChildObject(string childName)
        {
            var childTransforms = GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < childTransforms.Length; i++)
            {
                var child = childTransforms[i];
                if (child != null && child.name == childName)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static void SetPanelVisible(GameObject panel, bool visible)
        {
            if (panel != null && panel.activeSelf != visible)
            {
                panel.SetActive(visible);
            }
        }
    }
}
