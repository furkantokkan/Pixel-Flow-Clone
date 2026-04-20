using PixelFlow.Runtime.Bullets;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Factories
{
    public readonly struct BulletSpawnRequest
    {
        public BulletSpawnRequest(
            PigColor color,
            Transform target,
            BlockVisual targetBlock = null,
            VisualSpawnPlacement placement = default,
            float speed = -1f,
            float maxLifetime = -1f)
        {
            Color = color;
            Target = target;
            TargetBlock = targetBlock;
            Placement = placement;
            Speed = speed;
            MaxLifetime = maxLifetime;
        }

        public PigColor Color { get; }
        public Transform Target { get; }
        public BlockVisual TargetBlock { get; }
        public VisualSpawnPlacement Placement { get; }
        public float Speed { get; }
        public float MaxLifetime { get; }
    }
}
