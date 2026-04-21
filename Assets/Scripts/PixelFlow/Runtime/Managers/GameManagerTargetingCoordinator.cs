using System.Collections.Generic;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerTargetingCoordinator
    {
        private const float TargetSelectionEpsilon = 0.0001f;

        private readonly List<PigController> activeConveyorPigs;
        private readonly List<PigController> conveyorPigBuffer = new();

        private EnvironmentContext environment;
        private IGameFactory gameFactory;
        private float beltShotOriginForwardOffset;
        private float beltShotRadius;
        private float beltShotDistance;
        private LayerMask beltShotLayerMask;

        public GameManagerTargetingCoordinator(List<PigController> activeConveyorPigs)
        {
            this.activeConveyorPigs = activeConveyorPigs;
        }

        public IReadOnlyList<PigController> ActiveConveyorPigs => activeConveyorPigs;

        public void Configure(
            EnvironmentContext environment,
            IGameFactory gameFactory,
            float beltShotOriginForwardOffset,
            float beltShotRadius,
            float beltShotDistance,
            LayerMask beltShotLayerMask)
        {
            this.environment = environment;
            this.gameFactory = gameFactory;
            this.beltShotOriginForwardOffset = Mathf.Max(0f, beltShotOriginForwardOffset);
            this.beltShotRadius = Mathf.Max(0.01f, beltShotRadius);
            this.beltShotDistance = Mathf.Max(0.01f, beltShotDistance);
            this.beltShotLayerMask = beltShotLayerMask;
        }

        public bool Contains(PigController pig)
        {
            return pig != null && activeConveyorPigs.Contains(pig);
        }

        public void RegisterConveyorPig(PigController pig)
        {
            if (pig == null || activeConveyorPigs.Contains(pig))
            {
                return;
            }

            activeConveyorPigs.Add(pig);
        }

        public void UnregisterConveyorPig(PigController pig)
        {
            if (pig == null)
            {
                return;
            }

            activeConveyorPigs.Remove(pig);
        }

        public void Clear()
        {
            activeConveyorPigs.Clear();
            conveyorPigBuffer.Clear();
        }

        public void UpdateConveyorPigFiring(System.Action<PigController> onPigDepleted)
        {
            if (activeConveyorPigs.Count == 0)
            {
                return;
            }

            conveyorPigBuffer.Clear();
            for (int i = activeConveyorPigs.Count - 1; i >= 0; i--)
            {
                var pig = activeConveyorPigs[i];
                if (pig == null)
                {
                    activeConveyorPigs.RemoveAt(i);
                    continue;
                }

                if (pig.State == PigState.DispatchingToBelt)
                {
                    continue;
                }

                if (pig.State != PigState.FollowingSpline)
                {
                    activeConveyorPigs.RemoveAt(i);
                    continue;
                }

                conveyorPigBuffer.Add(pig);
            }

            conveyorPigBuffer.Sort(static (left, right) => right.CurrentSplinePercent.CompareTo(left.CurrentSplinePercent));
            for (int i = 0; i < conveyorPigBuffer.Count; i++)
            {
                var pig = conveyorPigBuffer[i];
                if (pig != null && pig.CanAttemptBeltShot)
                {
                    TryFirePig(pig, onPigDepleted);
                }
            }
        }

        private void TryFirePig(PigController pig, System.Action<PigController> onPigDepleted)
        {
            if (pig == null || environment == null || gameFactory == null)
            {
                return;
            }

            var targetBlock = FindBestTargetBlock(pig);
            if (targetBlock == null || !pig.TryConsumeAmmo())
            {
                return;
            }

            pig.NotifyBeltShotFired();
            targetBlock.SetReserved(true);

            gameFactory.CreateBullet(new BulletSpawnRequest(
                pig.Color,
                targetBlock.transform,
                targetBlock,
                new VisualSpawnPlacement(
                    parent: environment.transform,
                    position: pig.ProjectileOriginPosition,
                    rotation: pig.ProjectileOriginRotation,
                    useWorldSpace: true)));

            if (!pig.HasAmmo)
            {
                onPigDepleted?.Invoke(pig);
            }
        }

        private BlockVisual FindBestTargetBlock(PigController pig)
        {
            if (pig == null)
            {
                return null;
            }

            var direction = pig.FacingDirection;
            if (direction.sqrMagnitude <= TargetSelectionEpsilon)
            {
                return null;
            }

            var normalizedDirection = direction.normalized;
            var origin = pig.TargetingOriginPosition
                + (normalizedDirection * Mathf.Max(0f, beltShotOriginForwardOffset));
            if (!Physics.SphereCast(
                origin,
                beltShotRadius,
                normalizedDirection,
                out var hit,
                beltShotDistance,
                ResolveBeltShotLayerMask(),
                QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            var hitCollider = hit.collider;
            if (hitCollider == null)
            {
                return null;
            }

            var candidate = hitCollider.GetComponentInParent<BlockVisual>();
            if (candidate == null)
            {
                return null;
            }

            return IsValidTargetCandidate(candidate, pig.Color)
                ? candidate
                : null;
        }

        private static bool IsValidTargetCandidate(BlockVisual candidate, PixelFlow.Runtime.Data.PigColor color)
        {
            return candidate != null
                && candidate.gameObject.activeInHierarchy
                && !candidate.IsDying
                && !candidate.IsReserved
                && candidate.Color == color;
        }

        private int ResolveBeltShotLayerMask()
        {
            if (beltShotLayerMask.value != 0)
            {
                return beltShotLayerMask.value;
            }

            var blockLayer = LayerMask.NameToLayer("Block");
            if (blockLayer >= 0)
            {
                return 1 << blockLayer;
            }

            return Physics.DefaultRaycastLayers;
        }
    }
}
