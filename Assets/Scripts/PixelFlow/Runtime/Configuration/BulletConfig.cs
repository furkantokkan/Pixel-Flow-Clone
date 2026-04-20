using UnityEngine;

namespace PixelFlow.Runtime.Configuration
{
    [CreateAssetMenu(fileName = "BulletConfig", menuName = "Pixel Flow/Configuration/Bullet Config")]
    public sealed class BulletConfig : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float speed = 25f;
        [SerializeField, Min(0.01f)] private float lifetime = 2.5f;
        [SerializeField, Min(0.01f)] private float hitDistance = 0.2f;
        [SerializeField] private bool autoApplyHitToBlock = true;

        public float Speed => speed;
        public float Lifetime => lifetime;
        public float HitDistance => hitDistance;
        public bool AutoApplyHitToBlock => autoApplyHitToBlock;

        private void OnValidate()
        {
            speed = Mathf.Max(0.01f, speed);
            lifetime = Mathf.Max(0.01f, lifetime);
            hitDistance = Mathf.Max(0.01f, hitDistance);
        }
    }
}
