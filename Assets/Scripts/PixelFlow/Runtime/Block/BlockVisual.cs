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
        private int runtimeGridX = int.MinValue;
        private int runtimeGridY = int.MinValue;

        public event Action<BlockVisual> Destroyed;

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

        public void ApplyColor(PigColor color)
        {
            ApplyColor(color, PigColorAtlasUtility.ResolveDefaultToneIndex(color));
        }

        public void ApplyColor(PigColor color, int toneIndex)
        {
            model.Configure(color, toneIndex);
            RenderCurrentState();
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
            model.SetReserved(reserved);
            RenderCurrentState();
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

            var startScale = defaultLocalScale;
            var popScale = startScale * ResolveDestroyPopScaleMultiplier();
            var popDuration = ResolveDestroyPopDuration();
            var shrinkDuration = ResolveDestroyShrinkDuration();
            var totalDuration = Mathf.Max(0.01f, popDuration + shrinkDuration);
            destroyTween = Tween.Custom(
                    this,
                    0f,
                    totalDuration,
                    totalDuration,
                    (target, elapsed) => target.UpdateDestroyTween(startScale, popScale, popDuration, shrinkDuration, elapsed),
                    Ease.Linear)
                .OnComplete(this, target => target.CompleteDestroySequence());
        }

        private void CompleteDestroySequence()
        {
            destroyTween = default;
            transform.localScale = Vector3.zero;
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

        private void UpdateDestroyTween(
            Vector3 startScale,
            Vector3 popScale,
            float popDuration,
            float shrinkDuration,
            float elapsed)
        {
            var totalDuration = Mathf.Max(0.01f, popDuration + shrinkDuration);
            elapsed = Mathf.Clamp(elapsed, 0f, totalDuration);

            if (elapsed <= popDuration)
            {
                var t = popDuration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / popDuration);
                transform.localScale = Vector3.LerpUnclamped(startScale, popScale, t);
                return;
            }

            var shrinkElapsed = elapsed - popDuration;
            var shrinkT = shrinkDuration <= 0.0001f ? 1f : Mathf.Clamp01(shrinkElapsed / shrinkDuration);
            transform.localScale = Vector3.LerpUnclamped(popScale, Vector3.zero, shrinkT);
        }

        private void RenderCurrentState()
        {
            EnsureReferences();
            view?.Render(model);
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
