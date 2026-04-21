using System;
using Core.Pool;
using PixelFlow.Runtime.Configuration;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Bullets
{
    [DisallowMultipleComponent]
    public sealed partial class BulletController : MonoBehaviour, IPoolable
    {
        [SerializeField] private BulletConfig config;
        [SerializeField] private BulletView view;
        [SerializeField] private bool resetParentOnDespawn = true;
        [SerializeField] private bool clearViewOnDespawn = true;

        private readonly BulletModel model = new();
        private Transform defaultParent;
        private Vector3 defaultLocalPosition;
        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalScale;
        private Transform target;
        private BlockVisual targetBlock;

        public event Action<BulletController, BlockVisual> HitBlock;
        public event Action<BulletController> Completed;

        public PigColor Color => model.Color;
        public bool IsActiveProjectile => model.IsActive;

        private void Awake()
        {
            EnsureReferences();
            CacheDefaults();
            Render();
        }

        private void Reset()
        {
            EnsureReferences();
            TryAutoAssignConfig();
            CacheDefaults();
        }

        private void OnValidate()
        {
            EnsureReferences();
            TryAutoAssignConfig();
            CacheDefaults();
            Render();
        }

        private void Update()
        {
            if (!model.IsActive)
            {
                return;
            }

            if (!model.Tick(Time.deltaTime))
            {
                Complete();
                return;
            }

            if (target == null)
            {
                Complete();
                return;
            }

            var targetPosition = target.position;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, model.Speed * Time.deltaTime);

            var lookDirection = targetPosition - transform.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }

            var hitDistance = ResolveHitDistance();
            if ((transform.position - targetPosition).sqrMagnitude <= hitDistance * hitDistance)
            {
                ResolveHit();
            }
        }

        public void Launch(
            PigColor color,
            Transform target,
            BlockVisual targetBlock = null,
            float speed = -1f,
            float maxLifetime = -1f)
        {
            this.target = target;
            this.targetBlock = targetBlock != null
                ? targetBlock
                : target != null ? target.GetComponentInParent<BlockVisual>() : null;

            var resolvedSpeed = speed > 0f ? speed : ResolveSpeed();
            var resolvedLifetime = maxLifetime > 0f ? maxLifetime : ResolveLifetime();
            model.Configure(color, resolvedSpeed, resolvedLifetime);

            if (this.target != null)
            {
                var lookDirection = this.target.position - transform.position;
                if (lookDirection.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                }
            }

            Render();
        }

        public void OnSpawned()
        {
            EnsureReferences();
            Render();
        }

        public void OnDespawned()
        {
            target = null;
            targetBlock = null;
            model.Reset();
            transform.localPosition = defaultLocalPosition;
            transform.localRotation = defaultLocalRotation;
            transform.localScale = defaultLocalScale;

            if (resetParentOnDespawn && defaultParent != null && transform.parent != defaultParent)
            {
                transform.SetParent(defaultParent, false);
            }

            if (clearViewOnDespawn)
            {
                view?.Clear();
            }
            else
            {
                Render();
            }
        }

        private void CacheDefaults()
        {
            defaultParent = transform.parent;
            defaultLocalPosition = transform.localPosition;
            defaultLocalRotation = transform.localRotation;
            defaultLocalScale = transform.localScale;
        }

        private void ResolveHit()
        {
            if (ShouldAutoApplyHitToBlock() && targetBlock != null)
            {
                targetBlock.TryBeginDestroy();
            }

            HitBlock?.Invoke(this, targetBlock);
            Complete();
        }

        private void Complete()
        {
            ReleaseTargetReservation();

            if (!model.IsActive)
            {
                Completed?.Invoke(this);
                return;
            }

            model.Expire();
            Render();
            Completed?.Invoke(this);
        }

        private void ReleaseTargetReservation()
        {
            if (targetBlock == null || targetBlock.IsDying)
            {
                return;
            }

            targetBlock.SetReserved(false);
        }

        private void Render()
        {
            EnsureReferences();
            view?.Render(model);
        }

        private void EnsureReferences()
        {
            view ??= GetComponent<BulletView>();
        }

        private float ResolveSpeed()
        {
            return config != null ? config.Speed : 25f;
        }

        private float ResolveLifetime()
        {
            return config != null ? config.Lifetime : 2.5f;
        }

        private float ResolveHitDistance()
        {
            return config != null ? config.HitDistance : 0.2f;
        }

        private bool ShouldAutoApplyHitToBlock()
        {
            return config == null || config.AutoApplyHitToBlock;
        }

        private void TryAutoAssignConfig()
        {
            EditorAutoAssignConfig();
        }

        partial void EditorAutoAssignConfig();
    }
}
