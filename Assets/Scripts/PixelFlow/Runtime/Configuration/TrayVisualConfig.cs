using UnityEngine;

namespace PixelFlow.Runtime.Configuration
{
    [CreateAssetMenu(fileName = "TrayVisualConfig", menuName = "Pixel Flow/Configuration/Tray Visual Config")]
    public sealed class TrayVisualConfig : ScriptableObject
    {
        [SerializeField] private Color emptyColor = new(0.45f, 0.45f, 0.5f, 1f);
        [SerializeField] private Color occupiedColor = new(0.61960775f, 0.70980394f, 0.9294118f, 1f);

        public Color EmptyColor => emptyColor;
        public Color OccupiedColor => occupiedColor;
    }
}
