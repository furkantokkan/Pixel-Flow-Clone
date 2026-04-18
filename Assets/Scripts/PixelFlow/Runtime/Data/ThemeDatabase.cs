using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [CreateAssetMenu(fileName = "ThemeDatabase", menuName = "Pixel Flow/Theme Database")]
    public sealed class ThemeDatabase : ScriptableObject
    {
        [SerializeField] private List<Theme> themes = new();
        [SerializeField] private int defaultThemeIndex;

        public IReadOnlyList<Theme> Themes => themes;
        public int DefaultThemeIndex => Mathf.Clamp(defaultThemeIndex, 0, Mathf.Max(0, themes.Count - 1));

        public Theme GetTheme(int index)
        {
            if (themes == null || themes.Count == 0)
            {
                return null;
            }

            var safeIndex = Mathf.Clamp(index, 0, themes.Count - 1);
            return themes[safeIndex];
        }

        public Theme GetDefaultTheme()
        {
            return GetTheme(DefaultThemeIndex);
        }
    }
}
