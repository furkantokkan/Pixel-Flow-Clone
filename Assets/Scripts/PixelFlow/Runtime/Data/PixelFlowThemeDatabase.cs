using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [CreateAssetMenu(fileName = "PixelFlowThemeDatabase", menuName = "Pixel Flow/Theme Database")]
    public sealed class PixelFlowThemeDatabase : ScriptableObject
    {
        [SerializeField] private List<PixelFlowTheme> themes = new();
        [SerializeField] private int defaultThemeIndex;

        public IReadOnlyList<PixelFlowTheme> Themes => themes;
        public int DefaultThemeIndex => Mathf.Clamp(defaultThemeIndex, 0, Mathf.Max(0, themes.Count - 1));

        public PixelFlowTheme GetTheme(int index)
        {
            if (themes == null || themes.Count == 0)
            {
                return null;
            }

            var safeIndex = Mathf.Clamp(index, 0, themes.Count - 1);
            return themes[safeIndex];
        }

        public PixelFlowTheme GetDefaultTheme()
        {
            return GetTheme(DefaultThemeIndex);
        }
    }
}
