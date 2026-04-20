using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Bullets
{
    public sealed class BulletModel
    {
        public PigColor Color { get; private set; } = PigColor.Pink;
        public float Speed { get; private set; }
        public float RemainingLifetime { get; private set; }
        public bool IsActive { get; private set; }

        public void Configure(PigColor color, float speed, float maxLifetime)
        {
            Color = color;
            Speed = Mathf.Max(0.01f, speed);
            RemainingLifetime = Mathf.Max(0.01f, maxLifetime);
            IsActive = true;
        }

        public bool Tick(float deltaTime)
        {
            if (!IsActive)
            {
                return false;
            }

            RemainingLifetime = Mathf.Max(0f, RemainingLifetime - Mathf.Max(0f, deltaTime));
            if (RemainingLifetime > 0f)
            {
                return true;
            }

            Expire();
            return false;
        }

        public void Expire()
        {
            RemainingLifetime = 0f;
            IsActive = false;
        }

        public void Reset()
        {
            Color = PigColor.Pink;
            Speed = 0f;
            RemainingLifetime = 0f;
            IsActive = false;
        }
    }
}
