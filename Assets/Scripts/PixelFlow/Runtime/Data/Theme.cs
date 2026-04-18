using PixelFlow.Runtime.LevelEditing;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [CreateAssetMenu(fileName = "Theme", menuName = "Pixel Flow/Theme")]
    public sealed class Theme : ScriptableObject
    {
        [SerializeField] private EnvironmentContext environmentPrefab;

        public EnvironmentContext EnvironmentPrefab => environmentPrefab;
    }
}
