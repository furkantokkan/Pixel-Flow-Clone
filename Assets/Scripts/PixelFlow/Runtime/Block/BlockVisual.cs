using System;
using System.Collections;
using Core.Pool;
using PixelFlow.Runtime.Configuration;
using PixelFlow.Runtime.Data;
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
        private Coroutine destroyRoutine;

        public event Action<BlockVisual> Destroyed;

        public PigColor Color => model.Color;
        public bool IsReserved => model.IsReserved;
        public bool IsDying => model.IsDying;

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

            if (destroyRoutine != null)
            {
                StopCoroutine(destroyRoutine);
            }

            destroyRoutine = StartCoroutine(PlayDestroySequence());
            return true;
        }

        public void ResetVisual()
        {
            if (destroyRoutine != null)
            {
                StopCoroutine(destroyRoutine);
                destroyRoutine = null;
            }

            transform.localScale = defaultLocalScale;
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
            if (destroyRoutine != null)
            {
                StopCoroutine(destroyRoutine);
                destroyRoutine = null;
            }

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

        private IEnumerator PlayDestroySequence()
        {
            view?.SetVisible(true);

            var startScale = defaultLocalScale;
            var popScale = startScale * ResolveDestroyPopScaleMultiplier();
            var elapsed = 0f;
            var popDuration = ResolveDestroyPopDuration();
            while (elapsed < popDuration)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(startScale, popScale, Mathf.Clamp01(elapsed / popDuration));
                yield return null;
            }

            elapsed = 0f;
            var shrinkDuration = ResolveDestroyShrinkDuration();
            while (elapsed < shrinkDuration)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(popScale, Vector3.zero, Mathf.Clamp01(elapsed / shrinkDuration));
                yield return null;
            }

            destroyRoutine = null;
            Destroyed?.Invoke(this);

            if (!gameObject.activeSelf)
            {
                yield break;
            }

            gameObject.SetActive(false);
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
