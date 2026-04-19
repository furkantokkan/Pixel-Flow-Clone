using UnityEngine;

namespace PixelFlow.Runtime.Factories
{
    public readonly struct VisualSpawnPlacement
    {
        public VisualSpawnPlacement(
            Transform parent = null,
            bool worldPositionStays = false,
            Vector3? position = null,
            Quaternion? rotation = null,
            Vector3? localScale = null,
            bool useWorldSpace = false)
        {
            Parent = parent;
            WorldPositionStays = worldPositionStays;
            Position = position;
            Rotation = rotation;
            LocalScale = localScale;
            UseWorldSpace = useWorldSpace;
        }

        public Transform Parent { get; }
        public bool WorldPositionStays { get; }
        public Vector3? Position { get; }
        public Quaternion? Rotation { get; }
        public Vector3? LocalScale { get; }
        public bool UseWorldSpace { get; }

        public void ApplyPoseTo(Transform target)
        {
            if (target == null)
            {
                return;
            }

            if (Position.HasValue)
            {
                if (UseWorldSpace)
                {
                    target.position = Position.Value;
                }
                else
                {
                    target.localPosition = Position.Value;
                }
            }

            if (Rotation.HasValue)
            {
                if (UseWorldSpace)
                {
                    target.rotation = Rotation.Value;
                }
                else
                {
                    target.localRotation = Rotation.Value;
                }
            }

            if (LocalScale.HasValue)
            {
                target.localScale = LocalScale.Value;
            }
        }
    }
}
