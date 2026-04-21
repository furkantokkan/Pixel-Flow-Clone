using System;
using Core.Pool;
using PixelFlow.Runtime.Configuration;
using PixelFlow.Runtime.Data;
using PrimeTween;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class BlockVisual : MonoBehaviour, IPoolable
    {
        [SerializeField] private BlockVisualConfig config;
        [SerializeField] private BlockView view;
        [SerializeField] private bool resetParentOnDespawn = true;
        [SerializeField] private bool clearViewOnDespawn = true;

        private readonly BlockVisualModel model = new();
        private Transform defaultParent;
        private Vector3 defaultLocalPosition;
        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalScale;
        private int defaultToneIndex;
        private Collider[] colliders;
        private Tween destroyTween;
        private Vector3 destroyStartScale;
        private Vector3 destroyPopScale;
        private float destroyPopDuration;
        private float destroyShrinkDuration;
        private float destroyTotalDuration;
        private int runtimeGridX = int.MinValue;
        private int runtimeGridY = int.MinValue;

        public event Action<BlockVisual> Destroyed;
        public event Action<BlockVisual> StateChanged;

        public PigColor Color => model.Color;
        public bool IsReserved => model.IsReserved;
        public bool IsDying => model.IsDying;
        public bool HasRuntimeGridPosition => runtimeGridX != int.MinValue && runtimeGridY != int.MinValue;
        public int RuntimeGridX => runtimeGridX;
        public int RuntimeGridY => runtimeGridY;

        private void Awake()
        {
            EnsureReferences();
            CacheDefaults();
            model.Reset(ResolveDefaultColor(), ResolveDefaultToneIndex());
        }

        private void Reset()
        {
            EnsureReferences();
            TryAutoAssignConfig();
            CacheDefaults();
            model.Reset(ResolveDefaultColor(), ResolveDefaultToneIndex());
        }

        private void OnValidate()
        {
            EnsureReferences();
            TryAutoAssignConfig();
            CacheDefaults();
            model.Reset(ResolveDefaultColor(), ResolveDefaultToneIndex());
            RenderCurrentState();
        }

        private void OnDisable()
        {
            CancelDestroySequence();
        }

        public void ApplyColor(PigColor color)
        {
            ApplyColor(color, PigColorAtlasUtility.ResolveDefaultToneIndex(color));
        }

        public void ApplyColor(PigColor color, int toneIndex)
        {
            model.Configure(color, toneIndex);
            RenderCurrentState();
            NotifyStateChanged();
        }

        public void ConfigureBlock(PigColor color)
        {
            ConfigureBlock(color, PigColorAtlasUtility.ResolveDefaultToneIndex(color));
        }

        public void ConfigureBlock(PigColor color, int toneIndex)
        {
            ApplyColor(color, toneIndex);
        }

        public void SetReserved(bool reserved)
        {
            if (model.IsReserved == reserved)
            {
                return;
            }

            model.SetReserved(reserved);
            RenderCurrentState();
            NotifyStateChanged();
        }

        public void SetRuntimeGridPosition(int gridX, int gridY)
        {
            runtimeGridX = gridX;
            runtimeGridY = gridY;
        }

        public bool TryBeginDestroy()
        {
            if (model.IsDying)
            {
                return false;
            }

            model.SetReserved(false);
            model.SetDying(true);
            SetCollidersEnabled(false);
            RenderCurrentState();
            NotifyStateChanged();

            CancelDestroySequence();
            StartDestroySequence();
            return true;
        }

        public void ResetVisual()
        {
            CancelDestroySequence();

            transform.localScale = defaultLocalScale;
            runtimeGridX = int.MinValue;
            runtimeGridY = int.MinValue;
            model.Reset(ResolveDefaultColor(), defaultToneIndex);
            SetCollidersEnabled(true);
            if (clearViewOnDespawn)
            {
                view?.Clear();
            }
            else
            {
                RenderCurrentState();
            }

            NotifyStateChanged();
        }

        public void OnSpawned()
        {
            EnsureReferences();
            SetCollidersEnabled(true);
            view?.SetVisible(true);
            RenderCurrentState();
        }

        public void OnDespawned()
        {
            CancelDestroySequence();

            transform.localPosition = defaultLocalPosition;
            transform.localRotation = defaultLocalRotation;
            transform.localScale = defaultLocalScale;

            if (resetParentOnDespawn && defaultParent != null && transform.parent != defaultParent)
            {
                transform.SetParent(defaultParent, false);
            }

            ResetVisual();
        }

        private void CacheDefaults()
        {
            defaultParent = transform.parent;
            defaultLocalPosition = transform.localPosition;
            defaultLocalRotation = transform.localRotation;
            defaultLocalScale = transform.localScale;
            defaultToneIndex = ResolveDefaultToneIndex();
        }

        private void StartDestroySequence()
        {
            view?.SetVisible(true);

            destroyStartScale = defaultLocalScale;
            destroyPopScale = destroyStartScale * ResolveDestroyPopScaleMultiplier();
            destroyPopDuration = ResolveDestroyPopDuration();
            destroyShrinkDuration = ResolveDestroyShrinkDuration();
            destroyTotalDuration = Mathf.Max(0.01f, destroyPopDuration + destroyShrinkDuration);
            destroyTween = Tween.Custom(
                    this,
                    0f,
                    destroyTotalDuration,
                    destroyTotalDuration,
                    static (target, elapsed) => target.UpdateDestroyTween(elapsed),
                    Ease.Linear)
                .OnComplete(this, static target => target.CompleteDestroySequence());
        }

        private void CompleteDestroySequence()
        {
            destroyTween = default;
            transform.localScale = Vector3.zero;
            NotifyStateChanged();
            Destroyed?.Invoke(this);

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void CancelDestroySequence()
        {
            if (!destroyTween.isAlive)
            {
                return;
            }

            destroyTween.Stop();
            destroyTween = default;
        }

        private void UpdateDestroyTween(float elapsed)
        {
            elapsed = Mathf.Clamp(elapsed, 0f, destroyTotalDuration);

            if (elapsed <= destroyPopDuration)
            {
                var t = destroyPopDuration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / destroyPopDuration);
                transform.localScale = Vector3.LerpUnclamped(destroyStartScale, destroyPopScale, t);
                return;
            }

            var shrinkElapsed = elapsed - destroyPopDuration;
            var shrinkT = destroyShrinkDuration <= 0.0001f ? 1f : Mathf.Clamp01(shrinkElapsed / destroyShrinkDuration);
            transform.localScale = Vector3.LerpUnclamped(destroyPopScale, Vector3.zero, shrinkT);
        }

        private void RenderCurrentState()
        {
            EnsureReferences();
            view?.Render(model);
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke(this);
        }

        private void SetCollidersEnabled(bool enabled)
        {
            EnsureReferences();
            if (colliders == null)
            {
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = enabled;
                }
            }
        }

        private void EnsureReferences()
        {
            view ??= GetComponent<BlockView>();
            colliders ??= GetComponentsInChildren<Collider>(true);
        }

        private PigColor ResolveDefaultColor()
        {
            return config != null ? config.DefaultColor : PigColor.Pink;
        }

        private int ResolveDefaultToneIndex()
        {
            return PigColorAtlasUtility.ResolveDefaultToneIndex(ResolveDefaultColor());
        }

        private float ResolveDestroyPopDuration()
        {
            return config != null ? config.DestroyPopDuration : 0.08f;
        }

        private float ResolveDestroyShrinkDuration()
        {
            return config != null ? config.DestroyShrinkDuration : 0.18f;
        }

        private float ResolveDestroyPopScaleMultiplier()
        {
            return config != null ? config.DestroyPopScaleMultiplier : 1.12f;
        }

        private void TryAutoAssignConfig()
        {
#if UNITY_EDITOR
            if (config != null)
            {
                return;
            }

            var configGuids = UnityEditor.AssetDatabase.FindAssets("t:BlockVisualConfig");
            if (configGuids == null || configGuids.Length == 0)
            {
                return;
            }

            var configPath = UnityEditor.AssetDatabase.GUIDToAssetPath(configGuids[0]);
            config = UnityEditor.AssetDatabase.LoadAssetAtPath<BlockVisualConfig>(configPath);
#endif
        }
    }
}
