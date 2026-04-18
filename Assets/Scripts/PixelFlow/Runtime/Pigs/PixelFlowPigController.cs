using System;
using Core.Pool;
using Dreamteck.Splines;
using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Pigs
{
    public enum PixelFlowPigState
    {
        Idle = 0,
        Queued = 1,
        FollowingSpline = 2,
        OrbitingTarget = 3,
        Firing = 4,
        ReturningToWaiting = 5,
    }

    [DisallowMultipleComponent]
    public sealed class PixelFlowPigController : MonoBehaviour, IPoolable
    {
        [SerializeField] private PixelFlowPigView view;
        [SerializeField] private SplineFollower splineFollower;
        [SerializeField] private bool resetParentOnDespawn = true;
        [SerializeField] private bool clearViewOnDespawn = true;
        [SerializeField, Min(0.01f)] private float queuedMoveSpeed = 12f;
        [SerializeField, Min(0.01f)] private float returnMoveSpeed = 10f;
        [SerializeField, Min(0.01f)] private float rotationSpeed = 18f;
        [SerializeField, Min(0.01f)] private float defaultFollowSpeed = 7f;
        [SerializeField, Min(0.01f)] private float orbitRadius = 1.15f;
        [SerializeField, Min(0.01f)] private float orbitAngularSpeed = 180f;
        [SerializeField, Min(0.01f)] private float orbitDuration = 0.85f;
        [SerializeField, Min(0.01f)] private float fireDuration = 0.35f;

        private readonly PixelFlowPigModel model = new();
        private Transform defaultParent;
        private Vector3 defaultLocalPosition;
        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalScale;
        private Transform waitingAnchor;
        private Vector3 waitingOffset;
        private Transform orbitTarget;
        private double targetSplinePercent = 1.0;
        private float stateTimer;
        private bool firedDuringCurrentCycle;

        public event Action<PixelFlowPigController> Fired;

        public PigColor Color => model.Color;
        public int Ammo => model.Ammo;
        public PigDirection Direction => model.Direction;
        public bool IsQueued => model.Queued;
        public PixelFlowPigState State { get; private set; }

        private void Awake()
        {
            EnsureReferences();
            CacheDefaults();
            Render();
        }

        private void Reset()
        {
            EnsureReferences();
            CacheDefaults();
        }

        private void OnValidate()
        {
            EnsureReferences();
            CacheDefaults();
            Render();
        }

        private void Update()
        {
            switch (State)
            {
                case PixelFlowPigState.Queued:
                    MoveTowardsWaitingAnchor(queuedMoveSpeed, PixelFlowPigState.Queued);
                    break;
                case PixelFlowPigState.FollowingSpline:
                    UpdateSplineFollowing();
                    break;
                case PixelFlowPigState.OrbitingTarget:
                    UpdateOrbiting();
                    break;
                case PixelFlowPigState.Firing:
                    UpdateFiring();
                    break;
                case PixelFlowPigState.ReturningToWaiting:
                    MoveTowardsWaitingAnchor(returnMoveSpeed, PixelFlowPigState.Queued);
                    break;
            }
        }

        public void ConfigurePig(PigColor color, int ammo, PigDirection direction = PigDirection.None)
        {
            model.Configure(color, ammo, direction);
            StopSplineFollowing();
            firedDuringCurrentCycle = false;
            State = PixelFlowPigState.Idle;
            Render();
        }

        public void SetAmmo(int ammo)
        {
            model.SetAmmo(ammo);
            Render();
        }

        public void SetDirection(PigDirection direction)
        {
            model.SetDirection(direction);
        }

        public void SetTrayVisible(bool visible)
        {
            model.SetTrayVisible(visible);
            Render();
        }

        public void SetOnBelt(bool isOnBelt)
        {
            SetTrayVisible(isOnBelt);
        }

        public void ShowTray()
        {
            SetTrayVisible(true);
        }

        public void HideTray()
        {
            SetTrayVisible(false);
        }

        public void AssignWaitingAnchor(Transform anchor, bool snapImmediately = false, Vector3 worldOffset = default)
        {
            waitingAnchor = anchor;
            waitingOffset = worldOffset;

            if (snapImmediately)
            {
                SnapToWaitingAnchor();
                State = model.Queued ? PixelFlowPigState.Queued : PixelFlowPigState.Idle;
            }
        }

        public void ClearWaitingAnchor()
        {
            waitingAnchor = null;
            waitingOffset = Vector3.zero;
        }

        public void SetQueued(bool queued, bool snapImmediately = false)
        {
            model.SetQueued(queued);
            if (queued)
            {
                StopSplineFollowing();
                State = PixelFlowPigState.Queued;
                if (snapImmediately)
                {
                    SnapToWaitingAnchor();
                }
            }
            else if (State == PixelFlowPigState.Queued)
            {
                State = PixelFlowPigState.Idle;
            }
        }

        public bool TryConsumeAmmo(int amount = 1)
        {
            var consumed = model.TryConsumeAmmo(amount);
            if (consumed)
            {
                Render();
            }

            return consumed;
        }

        public void FollowSpline(
            SplineComputer spline,
            double startPercent = 0.0,
            double endPercent = 1.0,
            float followSpeed = -1f,
            Transform orbitAround = null)
        {
            EnsureSplineFollower(runtimeOnly: true);
            if (splineFollower == null || spline == null)
            {
                ReturnToWaiting();
                return;
            }

            model.SetQueued(false);
            orbitTarget = orbitAround;
            targetSplinePercent = Math.Max(0.0, Math.Min(1.0, endPercent));
            splineFollower.spline = spline;
            splineFollower.clipFrom = Math.Max(0.0, Math.Min(1.0, startPercent));
            splineFollower.clipTo = targetSplinePercent;
            splineFollower.followSpeed = followSpeed > 0f ? followSpeed : defaultFollowSpeed;
            splineFollower.followMode = SplineFollower.FollowMode.Uniform;
            splineFollower.wrapMode = SplineFollower.Wrap.Default;
            splineFollower.Restart(splineFollower.clipFrom);
            splineFollower.follow = true;
            State = PixelFlowPigState.FollowingSpline;
            SetOnBelt(true);
            Render();
        }

        public void BeginOrbitAndFire(Transform orbitAround)
        {
            orbitTarget = orbitAround;
            stateTimer = orbitDuration;
            firedDuringCurrentCycle = false;
            State = PixelFlowPigState.OrbitingTarget;
        }

        public void ReturnToWaiting()
        {
            StopSplineFollowing();
            stateTimer = 0f;
            State = waitingAnchor != null
                ? PixelFlowPigState.ReturningToWaiting
                : PixelFlowPigState.Idle;
            SetOnBelt(false);
        }

        public void OnSpawned()
        {
            EnsureReferences();
            Render();
        }

        public void OnDespawned()
        {
            StopSplineFollowing();
            transform.localPosition = defaultLocalPosition;
            transform.localRotation = defaultLocalRotation;
            transform.localScale = defaultLocalScale;

            if (resetParentOnDespawn && defaultParent != null && transform.parent != defaultParent)
            {
                transform.SetParent(defaultParent, false);
            }

            ClearRuntimeState();
        }

        private void CacheDefaults()
        {
            defaultParent = transform.parent;
            defaultLocalPosition = transform.localPosition;
            defaultLocalRotation = transform.localRotation;
            defaultLocalScale = transform.localScale;
        }

        private void UpdateSplineFollowing()
        {
            if (splineFollower == null || splineFollower.spline == null)
            {
                ReturnToWaiting();
                return;
            }

            if (splineFollower.GetPercent() + 0.0001d < targetSplinePercent)
            {
                return;
            }

            StopSplineFollowing();
            if (orbitTarget != null)
            {
                BeginOrbitAndFire(orbitTarget);
                return;
            }

            BeginFiring();
        }

        private void UpdateOrbiting()
        {
            if (orbitTarget == null)
            {
                BeginFiring();
                return;
            }

            stateTimer = Mathf.Max(0f, stateTimer - Time.deltaTime);
            var elapsed = orbitDuration - stateTimer;
            var angle = elapsed * orbitAngularSpeed;
            var orbitOffset = Quaternion.AngleAxis(angle, Vector3.up) * (Vector3.forward * orbitRadius);
            var desiredPosition = orbitTarget.position + orbitOffset;

            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * queuedMoveSpeed);
            RotateTowards(orbitTarget.position - transform.position);

            if (stateTimer <= 0f)
            {
                BeginFiring();
            }
        }

        private void UpdateFiring()
        {
            if (!firedDuringCurrentCycle)
            {
                TryConsumeAmmo();
                Fired?.Invoke(this);
                firedDuringCurrentCycle = true;
            }

            stateTimer = Mathf.Max(0f, stateTimer - Time.deltaTime);
            if (stateTimer <= 0f)
            {
                ReturnToWaiting();
            }
        }

        private void BeginFiring()
        {
            stateTimer = fireDuration;
            firedDuringCurrentCycle = false;
            State = PixelFlowPigState.Firing;
        }

        private void MoveTowardsWaitingAnchor(float moveSpeed, PixelFlowPigState completedState)
        {
            if (waitingAnchor == null)
            {
                State = completedState == PixelFlowPigState.Queued
                    ? PixelFlowPigState.Idle
                    : completedState;
                return;
            }

            var targetPosition = waitingAnchor.position + waitingOffset;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            RotateTowards(waitingAnchor.forward.sqrMagnitude > 0.001f
                ? waitingAnchor.forward
                : waitingAnchor.position - transform.position);

            if ((transform.position - targetPosition).sqrMagnitude <= 0.0001f)
            {
                State = completedState;
            }
        }

        private void SnapToWaitingAnchor()
        {
            if (waitingAnchor == null)
            {
                return;
            }

            transform.position = waitingAnchor.position + waitingOffset;
            if (waitingAnchor.forward.sqrMagnitude > 0.001f)
            {
                RotateTowards(waitingAnchor.forward, snap: true);
            }
        }

        private void RotateTowards(Vector3 direction, bool snap = false)
        {
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var lookRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = snap
                ? lookRotation
                : Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * rotationSpeed);
        }

        private void StopSplineFollowing()
        {
            if (splineFollower == null)
            {
                return;
            }

            splineFollower.follow = false;
        }

        private void ClearRuntimeState()
        {
            stateTimer = 0f;
            firedDuringCurrentCycle = false;
            targetSplinePercent = 1.0;
            orbitTarget = null;
            waitingAnchor = null;
            waitingOffset = Vector3.zero;
            State = PixelFlowPigState.Idle;
            model.Reset();

            if (clearViewOnDespawn)
            {
                view?.Clear();
            }
            else
            {
                Render();
            }
        }

        private void Render()
        {
            EnsureReferences();
            view?.Render(model);
        }

        private void EnsureReferences()
        {
            view ??= GetComponent<PixelFlowPigView>();
            EnsureSplineFollower(runtimeOnly: false);
        }

        private void EnsureSplineFollower(bool runtimeOnly)
        {
            splineFollower ??= GetComponent<SplineFollower>();

            if (!runtimeOnly || splineFollower != null || !Application.isPlaying)
            {
                return;
            }

            splineFollower = gameObject.AddComponent<SplineFollower>();
            splineFollower.follow = false;
            splineFollower.updateMethod = SplineUser.UpdateMethod.Update;
        }
    }
}
