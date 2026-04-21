using System;
using Core.Pool;
using Dreamteck.Splines;
using PixelFlow.Runtime.Data;
using PrimeTween;
using UnityEngine;

namespace PixelFlow.Runtime.Pigs
{
    public enum PigState
    {
        Idle = 0,
        Queued = 1,
        DispatchingToBelt = 2,
        FollowingSpline = 3,
        OrbitingTarget = 4,
        Firing = 5,
        ReturningToWaiting = 6,
        DespawningOnBelt = 7,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineFollower))]
    public sealed class PigController : MonoBehaviour, IPoolable
    {
        private const float BeltDepleteDurationScale = 1.12f;

        [SerializeField] private PigView view;
        [SerializeField] private SplineFollower splineFollower;
        [SerializeField] private bool resetParentOnDespawn = true;
        [SerializeField] private bool clearViewOnDespawn = true;
        [SerializeField, Min(0.01f)] private float queuedMoveSpeed = 12f;
        [SerializeField, Min(0.01f)] private float returnMoveSpeed = 10f;
        [SerializeField, Min(0.01f)] private float rotationSpeed = 18f;
        [SerializeField, Range(0.001f, 0.1f)] private float beltFacingLookAheadPercent = 0.02f;
        [SerializeField] private float beltFacingYawOffset = -90f;
        [SerializeField, Min(0.01f)] private float defaultFollowSpeed = 7f;
        [SerializeField, Min(0.01f)] private float dispatchDuration = 0.45f;
        [SerializeField, Min(0f)] private float dispatchJumpHeight = 1.75f;
        [SerializeField, Min(0.01f)] private float beltFireInterval = 0.1f;
        [SerializeField, Min(0f)] private float initialBeltShotDelay = 0.1f;
        [SerializeField, Min(0.01f)] private float orbitRadius = 1.15f;
        [SerializeField, Min(0.01f)] private float orbitAngularSpeed = 180f;
        [SerializeField, Min(0.01f)] private float orbitDuration = 0.85f;
        [SerializeField, Min(0.01f)] private float fireDuration = 0.35f;
        [SerializeField, Min(0.01f)] private float beltDepleteDuration = 0.65f;
        [SerializeField, Min(1f)] private float beltDepleteOvershootScale = 1.08f;
        [SerializeField, Min(0f)] private float beltDepleteSpinDegrees = 540f;

        private readonly PigModel model = new();
        private Transform defaultParent;
        private Vector3 defaultLocalPosition;
        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalScale;
        private Transform waitingAnchor;
        private Vector3 waitingOffset;
        private Transform orbitTarget;
        private double currentSplineStartPercent;
        private float activeBeltFacingYawOffset;
        private float stateTimer;
        private float beltShotCooldown;
        private float fireIntervalMultiplier = 1f;
        private float activeDispatchJumpHeight;
        private float activeFollowSpeed = -1f;
        private float followSpeedMultiplier = 1f;
        private bool firedDuringCurrentCycle;
        private Tween dispatchTween;
        private Sequence beltDepleteTween;
        private Vector3 dispatchStartPosition;
        private Vector3 dispatchEndPosition;
        private Quaternion dispatchStartRotation;
        private Quaternion dispatchEndRotation;
        private SplineComputer pendingDispatchSpline;
        private Transform pendingDispatchOrbitTarget;
        private double pendingDispatchStartPercent;
        private double pendingDispatchEndPercent = 1.0;
        private float pendingDispatchFollowSpeed = -1f;
        private SplineComputer activeLoopSpline;
        private Transform activeLoopOrbitTarget;
        private double activeLoopStartPercent;
        private double activeLoopEndPercent = 1.0;
        private float configuredFollowSpeed = -1f;
        private SplineFollower subscribedSplineFollower;

        public event Action<PigController> Fired;
        public event Action<PigController> ConveyorLoopCompleted;
        public event Action<PigController> ReturnedToWaiting;
        public event Action<PigController> BeltDepleteCompleted;

        public PigColor Color => model.Color;
        public int Ammo => model.Ammo;
        public bool HasAmmo => model.Ammo > 0;
        public PigDirection Direction => model.Direction;
        public bool IsQueued => model.Queued;
        public PigState State { get; private set; }
        public Transform ProjectileOrigin => view != null ? view.ProjectileOrigin : transform;
        public Vector3 ProjectileOriginPosition => view != null ? view.ProjectileOriginPosition : transform.position;
        public Quaternion ProjectileOriginRotation => view != null ? view.ProjectileOriginRotation : transform.rotation;
        public Vector3 TargetingOriginPosition => view != null ? view.TargetingOriginPosition : transform.position;
        public Vector3 FacingDirection => view != null ? view.FacingDirection : transform.forward;
        public bool CanAttemptBeltShot => State == PigState.FollowingSpline && HasAmmo && beltShotCooldown <= 0f;
        public double CurrentSplinePercent => splineFollower != null && splineFollower.spline != null
            ? splineFollower.GetPercent()
            : 0.0;

        public void SetRenderersVisible(bool visible)
        {
            EnsureReferences();
            view?.SetRenderersVisible(visible);
        }

        public void SetBeltSpeedModifiers(float followMultiplier, float fireIntervalScale)
        {
            followSpeedMultiplier = Mathf.Max(0.01f, followMultiplier);
            fireIntervalMultiplier = Mathf.Max(0.01f, fireIntervalScale);
            ApplyRuntimeFollowSpeed();
        }

        public void UpdateRendererVisibility(Camera activeCamera, float viewportPadding)
        {
            EnsureReferences();
            if (view == null)
            {
                return;
            }

            var shouldRender = activeCamera == null || view.ShouldRenderForCamera(activeCamera, viewportPadding);
            view.SetRenderersVisible(shouldRender);
        }

        public void PrewarmDispatchRuntime()
        {
            EnsureReferences();
            EnsureSplineFollower(runtimeOnly: true);
        }

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
                case PigState.Queued:
                    MoveTowardsWaitingAnchor(queuedMoveSpeed, PigState.Queued);
                    break;
                case PigState.DispatchingToBelt:
                    UpdateDispatching();
                    break;
                case PigState.FollowingSpline:
                    UpdateSplineFollowing();
                    break;
                case PigState.OrbitingTarget:
                    UpdateOrbiting();
                    break;
                case PigState.Firing:
                    UpdateFiring();
                    break;
                case PigState.ReturningToWaiting:
                    MoveTowardsWaitingAnchor(returnMoveSpeed, PigState.Queued);
                    break;
                case PigState.DespawningOnBelt:
                    break;
            }
        }

        private void LateUpdate()
        {
            if (State == PigState.FollowingSpline)
            {
                ApplyBeltFacingFromSpline();
            }
        }

        private void OnDisable()
        {
            StopBeltDepleteSequence();
            if (subscribedSplineFollower != null)
            {
                subscribedSplineFollower.onEndReached -= HandleSplineEndReached;
                subscribedSplineFollower = null;
            }
        }

        public void ConfigurePig(PigColor color, int ammo, PigDirection direction = PigDirection.None)
        {
            model.Configure(color, ammo, direction);
            StopSplineFollowing();
            activeBeltFacingYawOffset = beltFacingYawOffset;
            beltShotCooldown = 0f;
            activeFollowSpeed = -1f;
            followSpeedMultiplier = 1f;
            fireIntervalMultiplier = 1f;
            firedDuringCurrentCycle = false;
            State = PigState.Idle;
            view?.SetRenderersVisible(true);
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
            view?.SetBeltFacingMode(isOnBelt);
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
                State = model.Queued ? PigState.Queued : PigState.Idle;
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
                StopDispatchTween();
                StopSplineFollowing();
                State = PigState.Queued;
                if (snapImmediately)
                {
                    SnapToWaitingAnchor();
                }
            }
            else if (State == PigState.Queued)
            {
                State = PigState.Idle;
            }
        }

        public void BeginDispatchToSpline(
            SplineComputer spline,
            Vector3 dispatchWorldPosition,
            Quaternion dispatchWorldRotation,
            double startPercent = 0.0,
            double endPercent = 1.0,
            float followSpeed = -1f,
            Transform orbitAround = null,
            float dispatchDurationOverride = -1f)
        {
            if (spline == null)
            {
                ReturnToWaiting();
                return;
            }

            StopDispatchTween();
            StopSplineFollowing();

            model.SetQueued(false);
            orbitTarget = orbitAround;
            pendingDispatchSpline = spline;
            pendingDispatchOrbitTarget = orbitAround;
            pendingDispatchStartPercent = Math.Max(0.0, Math.Min(1.0, startPercent));
            pendingDispatchEndPercent = Math.Max(0.0, Math.Min(1.0, endPercent));
            pendingDispatchFollowSpeed = followSpeed;
            dispatchStartPosition = transform.position;
            dispatchEndPosition = dispatchWorldPosition;
            dispatchStartRotation = transform.rotation;
            dispatchEndRotation = dispatchWorldRotation;
            activeDispatchJumpHeight = Mathf.Max(0f, dispatchJumpHeight);
            beltShotCooldown = 0f;
            firedDuringCurrentCycle = false;
            State = PigState.DispatchingToBelt;
            SetOnBelt(false);
            view?.SetBeltFacingMode(true);
            Render();

            var resolvedDispatchDuration = dispatchDurationOverride > 0f
                ? dispatchDurationOverride
                : dispatchDuration;
            if (!Application.isPlaying || resolvedDispatchDuration <= 0.01f)
            {
                CompleteDispatchTween();
                return;
            }

            var peakHeight = Mathf.Max(dispatchStartPosition.y, dispatchEndPosition.y) + activeDispatchJumpHeight;
            var midpoint = Vector3.Lerp(dispatchStartPosition, dispatchEndPosition, 0.5f);
            midpoint.y = peakHeight;

            var firstHalfDuration = Mathf.Max(0.01f, resolvedDispatchDuration * 0.5f);
            var secondHalfDuration = Mathf.Max(0.01f, resolvedDispatchDuration - firstHalfDuration);
            dispatchTween = Tween.Custom(
                    this,
                    0f,
                    resolvedDispatchDuration,
                    resolvedDispatchDuration,
                    (target, elapsed) => target.UpdateDispatchTween(midpoint, firstHalfDuration, secondHalfDuration, elapsed),
                    Ease.Linear)
                .OnComplete(this, target => target.CompleteDispatchTween());
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
            StopDispatchTween();
            if (splineFollower == null || spline == null)
            {
                ReturnToWaiting();
                return;
            }

            model.SetQueued(false);
            orbitTarget = orbitAround;
            beltShotCooldown = Mathf.Max(0f, initialBeltShotDelay * fireIntervalMultiplier);
            var clipFrom = Math.Max(0.0, Math.Min(1.0, startPercent));
            var clipTo = Math.Max(0.0, Math.Min(1.0, endPercent));
            if (clipTo <= clipFrom)
            {
                clipTo = 1.0;
            }

            var resolvedFollowSpeed = followSpeed > 0f
                ? followSpeed
                : defaultFollowSpeed;
            activeFollowSpeed = Mathf.Abs(resolvedFollowSpeed);
            configuredFollowSpeed = resolvedFollowSpeed;
            activeLoopSpline = spline;
            activeLoopOrbitTarget = orbitAround;
            activeLoopStartPercent = clipFrom;
            activeLoopEndPercent = clipTo;

            splineFollower.follow = false;
            splineFollower.spline = spline;
            splineFollower.clipFrom = clipFrom;
            splineFollower.clipTo = clipTo;
            ApplyRuntimeFollowSpeed();
            splineFollower.followMode = SplineFollower.FollowMode.Uniform;
            splineFollower.wrapMode = SplineFollower.Wrap.Default;
            splineFollower.RebuildImmediate();

            var projectedSample = new SplineSample();
            splineFollower.Project(transform.position, ref projectedSample);
            currentSplineStartPercent = projectedSample.percent;
            activeBeltFacingYawOffset = ResolveBeltFacingYawOffset(orbitAround, currentSplineStartPercent);
            splineFollower.Restart(currentSplineStartPercent);
            splineFollower.follow = true;
            State = PigState.FollowingSpline;
            SetOnBelt(true);
            ApplyBeltFacingFromSpline();
            Render();
        }

        public void NotifyBeltShotFired()
        {
            beltShotCooldown = Mathf.Max(0.01f, beltFireInterval * fireIntervalMultiplier);
        }

        public void BeginOrbitAndFire(Transform orbitAround)
        {
            orbitTarget = orbitAround;
            stateTimer = orbitDuration;
            firedDuringCurrentCycle = false;
            State = PigState.OrbitingTarget;
        }

        public void ReturnToWaiting()
        {
            StopDispatchTween();
            StopBeltDepleteSequence();
            StopSplineFollowing();
            beltShotCooldown = 0f;
            stateTimer = 0f;
            State = waitingAnchor != null
                ? PigState.ReturningToWaiting
                : PigState.Idle;
            SetOnBelt(false);
        }

        public void BeginBeltDeplete()
        {
            StopDispatchTween();
            StopBeltDepleteSequence();
            StopSplineFollowing();
            beltShotCooldown = 0f;
            stateTimer = 0f;
            orbitTarget = null;
            State = PigState.DespawningOnBelt;
            SetOnBelt(true);
            Render();

            if (!Application.isPlaying || beltDepleteDuration <= 0.01f)
            {
                CompleteBeltDeplete();
                return;
            }

            var resolvedBeltDepleteDuration = Mathf.Max(0.01f, beltDepleteDuration * BeltDepleteDurationScale);
            var currentScale = transform.localScale;
            var overshootScale = currentScale * beltDepleteOvershootScale;
            var currentEuler = transform.localEulerAngles;
            var popDuration = Mathf.Max(0.01f, resolvedBeltDepleteDuration * 0.3f);
            var shrinkDuration = Mathf.Max(0.01f, resolvedBeltDepleteDuration - popDuration);
            StartBeltDepleteTween(
                currentScale,
                overshootScale,
                currentEuler,
                resolvedBeltDepleteDuration,
                popDuration,
                shrinkDuration);
        }

        public void OnSpawned()
        {
            EnsureReferences();
            view?.SetRenderersVisible(true);
            Render();
        }

        public void OnDespawned()
        {
            StopDispatchTween();
            StopBeltDepleteSequence();
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

        private void CompleteDispatchTween()
        {
            dispatchTween = default;
            transform.position = dispatchEndPosition;
            transform.rotation = dispatchEndRotation;

            var spline = pendingDispatchSpline;
            var orbitAround = pendingDispatchOrbitTarget;
            var startPercent = pendingDispatchStartPercent;
            var endPercent = pendingDispatchEndPercent;
            var followSpeed = pendingDispatchFollowSpeed;
            ResetPendingDispatchState();

            FollowSpline(spline, startPercent, endPercent, followSpeed, orbitAround);
        }

        private void UpdateDispatching()
        {
        }

        private void UpdateDispatchTween(
            Vector3 midpoint,
            float firstHalfDuration,
            float secondHalfDuration,
            float elapsed)
        {
            elapsed = Mathf.Max(0f, elapsed);
            Vector3 currentPosition;
            Vector3 lookTarget;
            if (elapsed <= firstHalfDuration)
            {
                var firstHalfT = firstHalfDuration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / firstHalfDuration);
                var easedFirstHalfT = 1f - ((1f - firstHalfT) * (1f - firstHalfT));
                currentPosition = Vector3.LerpUnclamped(dispatchStartPosition, midpoint, easedFirstHalfT);
                lookTarget = firstHalfT < 0.98f ? midpoint : dispatchEndPosition;
            }
            else
            {
                var secondHalfElapsed = elapsed - firstHalfDuration;
                var secondHalfT = secondHalfDuration <= 0.0001f ? 1f : Mathf.Clamp01(secondHalfElapsed / secondHalfDuration);
                var easedSecondHalfT = secondHalfT * secondHalfT;
                currentPosition = Vector3.LerpUnclamped(midpoint, dispatchEndPosition, easedSecondHalfT);
                lookTarget = dispatchEndPosition;
            }

            transform.position = currentPosition;
            var lookDirection = lookTarget - currentPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                RotateTowards(lookDirection);
            }
        }

        private void UpdateSplineFollowing()
        {
            if (splineFollower == null || splineFollower.spline == null)
            {
                CompleteSplineLoop();
                return;
            }

            if (beltShotCooldown > 0f)
            {
                beltShotCooldown = Mathf.Max(0f, beltShotCooldown - Time.deltaTime);
            }
        }

        private void CompleteSplineLoop()
        {
            StopSplineFollowing();
            beltShotCooldown = 0f;
            State = PigState.Idle;
            SetOnBelt(false);
            ConveyorLoopCompleted?.Invoke(this);
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

        private void ApplyBeltFacingFromSpline()
        {
            if (!TryGetSplineMoveDirection(splineFollower != null ? splineFollower.GetPercent() : 0.0, out var moveDirection))
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up)
                * Quaternion.Euler(0f, activeBeltFacingYawOffset, 0f);
        }

        private void UpdateFiring()
        {
            if (!firedDuringCurrentCycle)
            {
                if (!TryConsumeAmmo())
                {
                    stateTimer = 0f;
                    ReturnToWaiting();
                    return;
                }

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
            State = PigState.Firing;
        }

        private void MoveTowardsWaitingAnchor(float moveSpeed, PigState completedState)
        {
            if (waitingAnchor == null)
            {
                State = completedState == PigState.Queued
                    ? PigState.Idle
                    : completedState;
                return;
            }

            var targetPosition = waitingAnchor.position + waitingOffset;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            RotateTowards(ResolveWaitingFacingDirection(targetPosition));

            if ((transform.position - targetPosition).sqrMagnitude <= 0.0001f)
            {
                if (State != completedState)
                {
                    State = completedState;
                    if (completedState == PigState.Queued)
                    {
                        ReturnedToWaiting?.Invoke(this);
                    }
                }
            }
        }

        private void SnapToWaitingAnchor()
        {
            if (waitingAnchor == null)
            {
                return;
            }

            transform.position = waitingAnchor.position + waitingOffset;
            var facingDirection = ResolveWaitingFacingDirection(transform.position);
            if (facingDirection.sqrMagnitude > 0.001f)
            {
                RotateTowards(facingDirection, snap: true);
            }
        }

        private Vector3 ResolveWaitingFacingDirection(Vector3 currentPosition)
        {
            if (waitingAnchor == null)
            {
                return Vector3.zero;
            }

            // Holding slots point toward the board; queued pigs should face outward toward the player.
            if (waitingAnchor.forward.sqrMagnitude > 0.001f)
            {
                return -waitingAnchor.forward;
            }

            return waitingAnchor.position - currentPosition;
        }

        private void RotateTowards(Vector3 direction, bool snap = false, float yawOffset = 0f)
        {
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var lookRotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Quaternion.Euler(0f, yawOffset, 0f);
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
            splineFollower.spline = null;
            activeFollowSpeed = -1f;
            currentSplineStartPercent = 0.0;
            activeBeltFacingYawOffset = beltFacingYawOffset;
        }

        private void ApplyRuntimeFollowSpeed()
        {
            if (splineFollower == null || activeFollowSpeed <= 0f)
            {
                return;
            }

            splineFollower.followSpeed = activeFollowSpeed * followSpeedMultiplier;
        }

        private void StopDispatchTween()
        {
            if (dispatchTween.isAlive)
            {
                dispatchTween.Stop();
            }

            dispatchTween = default;
            ResetPendingDispatchState();
        }

        private void StartBeltDepleteTween(
            Vector3 startScale,
            Vector3 overshootScale,
            Vector3 startEuler,
            float totalDuration,
            float popDuration,
            float shrinkDuration)
        {
            var endEuler = startEuler + new Vector3(0f, beltDepleteSpinDegrees, 0f);
            var scaleTween = Tween.Scale(transform, overshootScale, popDuration, Ease.OutQuad)
                .Chain(Tween.Scale(transform, Vector3.zero, shrinkDuration, Ease.OutCubic));
            var spinTween = Tween.LocalEulerAngles(
                transform,
                startEuler,
                endEuler,
                totalDuration,
                Ease.OutCubic);

            beltDepleteTween = Sequence.Create()
                .Group(scaleTween)
                .Group(spinTween)
                .ChainCallback(this, target =>
                {
                    target.beltDepleteTween = default;
                    target.transform.localScale = Vector3.zero;
                    target.transform.localRotation = Quaternion.Euler(endEuler);
                    target.CompleteBeltDeplete();
                });
        }

        private void StopBeltDepleteSequence()
        {
            if (!beltDepleteTween.isAlive)
            {
                return;
            }

            beltDepleteTween.Stop();
            beltDepleteTween = default;
        }

        private void CompleteBeltDeplete()
        {
            BeltDepleteCompleted?.Invoke(this);
        }

        private void ResetPendingDispatchState()
        {
            pendingDispatchSpline = null;
            pendingDispatchOrbitTarget = null;
            pendingDispatchStartPercent = 0.0;
            pendingDispatchEndPercent = 1.0;
            pendingDispatchFollowSpeed = -1f;
            activeDispatchJumpHeight = 0f;
        }

        private void ClearRuntimeState()
        {
            StopDispatchTween();
            StopBeltDepleteSequence();
            stateTimer = 0f;
            beltShotCooldown = 0f;
            firedDuringCurrentCycle = false;
            orbitTarget = null;
            waitingAnchor = null;
            waitingOffset = Vector3.zero;
            activeBeltFacingYawOffset = beltFacingYawOffset;
            activeFollowSpeed = -1f;
            configuredFollowSpeed = -1f;
            followSpeedMultiplier = 1f;
            fireIntervalMultiplier = 1f;
            activeLoopSpline = null;
            activeLoopOrbitTarget = null;
            activeLoopStartPercent = 0.0;
            activeLoopEndPercent = 1.0;
            State = PigState.Idle;
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
            view ??= GetComponent<PigView>();
            EnsureSplineFollower(runtimeOnly: false);
        }

        private void EnsureSplineFollower(bool runtimeOnly)
        {
            splineFollower ??= GetComponent<SplineFollower>();

            if (!runtimeOnly || splineFollower != null || !Application.isPlaying)
            {
                ConfigureSplineFollower();
                RefreshSplineFollowerSubscriptions();
                return;
            }

            splineFollower = gameObject.AddComponent<SplineFollower>();
            ConfigureSplineFollower();
            RefreshSplineFollowerSubscriptions();
        }

        private void ConfigureSplineFollower()
        {
            if (splineFollower == null)
            {
                return;
            }

            splineFollower.autoStartPosition = false;
            splineFollower.updateMethod = SplineUser.UpdateMethod.Update;
            splineFollower.followMode = SplineFollower.FollowMode.Uniform;
            splineFollower.wrapMode = SplineFollower.Wrap.Default;
            splineFollower.physicsMode = SplineTracer.PhysicsMode.Transform;
            splineFollower.motion.applyPosition = true;
            splineFollower.motion.applyRotation = false;
            splineFollower.motion.applyScale = false;
        }

        private void RefreshSplineFollowerSubscriptions()
        {
            if (subscribedSplineFollower == splineFollower)
            {
                return;
            }

            if (subscribedSplineFollower != null)
            {
                subscribedSplineFollower.onEndReached -= HandleSplineEndReached;
            }

            subscribedSplineFollower = splineFollower;
            if (subscribedSplineFollower != null)
            {
                subscribedSplineFollower.onEndReached += HandleSplineEndReached;
            }
        }

        private void HandleSplineEndReached(double _)
        {
            if (State == PigState.FollowingSpline)
            {
                CompleteSplineLoop();
            }
        }

        private float ResolveBeltFacingYawOffset(Transform centerReference, double currentPercent)
        {
            if (centerReference == null || !TryGetSplineMoveDirection(currentPercent, out var moveDirection))
            {
                return beltFacingYawOffset;
            }

            var samplePosition = transform.position;
            if (splineFollower != null && splineFollower.spline != null)
            {
                samplePosition = splineFollower.spline.Evaluate(currentPercent).position;
            }

            var inwardDirection = centerReference.position - samplePosition;
            inwardDirection.y = 0f;
            if (inwardDirection.sqrMagnitude <= 0.0001f)
            {
                return beltFacingYawOffset;
            }

            var yawMagnitude = Mathf.Max(1f, Mathf.Abs(beltFacingYawOffset));
            var inwardNormalized = inwardDirection.normalized;
            var negativeYawForward = (Quaternion.LookRotation(moveDirection.normalized, Vector3.up)
                * Quaternion.Euler(0f, -yawMagnitude, 0f))
                * Vector3.forward;
            var positiveYawForward = (Quaternion.LookRotation(moveDirection.normalized, Vector3.up)
                * Quaternion.Euler(0f, yawMagnitude, 0f))
                * Vector3.forward;

            return Vector3.Dot(negativeYawForward.normalized, inwardNormalized)
                >= Vector3.Dot(positiveYawForward.normalized, inwardNormalized)
                ? -yawMagnitude
                : yawMagnitude;
        }

        private bool TryGetSplineMoveDirection(double currentPercent, out Vector3 moveDirection)
        {
            moveDirection = Vector3.zero;
            if (splineFollower == null || splineFollower.spline == null)
            {
                return false;
            }

            var clipFrom = Math.Max(0.0, Math.Min(1.0, splineFollower.clipFrom));
            var clipTo = Math.Max(clipFrom, Math.Min(1.0, splineFollower.clipTo));
            currentPercent = Math.Max(clipFrom, Math.Min(clipTo, currentPercent));
            var nextPercent = Math.Min(clipTo, currentPercent + beltFacingLookAheadPercent);
            var currentSample = splineFollower.spline.Evaluate(currentPercent);
            if (nextPercent > currentPercent + 0.000001d)
            {
                var nextSample = splineFollower.spline.Evaluate(nextPercent);
                moveDirection = nextSample.position - currentSample.position;
                moveDirection.y = 0f;

                if (moveDirection.sqrMagnitude > 0.0001f)
                {
                    return true;
                }
            }

            moveDirection = currentSample.forward;
            moveDirection.y = 0f;
            return moveDirection.sqrMagnitude > 0.0001f;
        }
    }
}
