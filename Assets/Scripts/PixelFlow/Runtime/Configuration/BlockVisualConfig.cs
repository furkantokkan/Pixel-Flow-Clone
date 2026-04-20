using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Configuration
{
    [CreateAssetMenu(fileName = "BlockVisualConfig", menuName = "Pixel Flow/Configuration/Block Visual Config")]
    public sealed class BlockVisualConfig : ScriptableObject
    {
        [SerializeField] private PigColor defaultColor = PigColor.Pink;
        [SerializeField, Min(0.01f)] private float destroyPopDuration = 0.08f;
        [SerializeField, Min(0.01f)] private float destroyShrinkDuration = 0.18f;
        [SerializeField, Min(1f)] private float destroyPopScaleMultiplier = 1.12f;

        public PigColor DefaultColor => defaultColor;
        public float DestroyPopDuration => destroyPopDuration;
        public float DestroyShrinkDuration => destroyShrinkDuration;
        public float DestroyPopScaleMultiplier => destroyPopScaleMultiplier;

        private void OnValidate()
        {
            destroyPopDuration = Mathf.Max(0.01f, destroyPopDuration);
            destroyShrinkDuration = Mathf.Max(0.01f, destroyShrinkDuration);
            destroyPopScaleMultiplier = Mathf.Max(1f, destroyPopScaleMultiplier);
        }
    }
}
