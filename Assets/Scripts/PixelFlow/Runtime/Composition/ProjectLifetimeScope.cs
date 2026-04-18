using System;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [Serializable]
    public sealed class ProjectRuntimeSettings
    {
        [Header("Visual Pool")]
        [SerializeField] private PigController pigPrefab;
        [SerializeField] private BlockVisual blockPrefab;
        [SerializeField, Min(0)] private int pigPrewarmCount = 8;
        [SerializeField, Min(0)] private int blockPrewarmCount = 32;
        [SerializeField, Min(1)] private int pigMaxSize = 64;
        [SerializeField, Min(1)] private int blockMaxSize = 256;

        [Header("Game")]
        [SerializeField] private GameObject traySlotPrefab;
        [SerializeField, Min(0.01f)] private float dispatchFollowSpeed = 7f;

        [Header("Input")]
        [SerializeField] private LayerMask pigLayerMask = 1 << 7;
        [SerializeField, Min(1f)] private float maxRayDistance = 500f;

        public PigController PigPrefab => pigPrefab;
        public BlockVisual BlockPrefab => blockPrefab;
        public int PigPrewarmCount => pigPrewarmCount;
        public int BlockPrewarmCount => blockPrewarmCount;
        public int PigMaxSize => pigMaxSize;
        public int BlockMaxSize => blockMaxSize;
        public GameObject TraySlotPrefab => traySlotPrefab;
        public float DispatchFollowSpeed => dispatchFollowSpeed;
        public LayerMask PigLayerMask => pigLayerMask;
        public float MaxRayDistance => maxRayDistance;

        public void Normalize()
        {
            pigPrewarmCount = Mathf.Max(0, pigPrewarmCount);
            blockPrewarmCount = Mathf.Max(0, blockPrewarmCount);
            pigMaxSize = Mathf.Max(1, pigMaxSize);
            blockMaxSize = Mathf.Max(1, blockMaxSize);
            dispatchFollowSpeed = Mathf.Max(0.01f, dispatchFollowSpeed);
            maxRayDistance = Mathf.Max(1f, maxRayDistance);

            if (pigLayerMask.value == 0)
            {
                var pigLayer = LayerMask.NameToLayer("Pig");
                pigLayerMask = pigLayer >= 0
                    ? 1 << pigLayer
                    : 1 << 7;
            }
        }

#if UNITY_EDITOR
        public void TryAutoAssignAssets()
        {
            pigPrefab ??= FindPrefabComponent<PigController>("Pig");
            blockPrefab ??= FindPrefabComponent<BlockVisual>("Block");
            traySlotPrefab ??= FindPrefabGameObject("Tray");
            Normalize();
        }

        private static TComponent FindPrefabComponent<TComponent>(string prefabNameFilter)
            where TComponent : Component
        {
            var prefabs = UnityEditor.AssetDatabase.FindAssets($"t:Prefab {prefabNameFilter}");
            for (int i = 0; i < prefabs.Length; i++)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prefabs[i]);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                var component = prefab.GetComponentInChildren<TComponent>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject FindPrefabGameObject(string prefabNameFilter)
        {
            var prefabs = UnityEditor.AssetDatabase.FindAssets($"t:Prefab {prefabNameFilter}");
            if (prefabs == null || prefabs.Length == 0)
            {
                return null;
            }

            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prefabs[0]);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
#endif
    }

    [DisallowMultipleComponent]
    public sealed class ProjectLifetimeScope : LifetimeScope
    {
        [SerializeField] private ThemeDatabase themeDatabase;
        [SerializeField] private Theme defaultTheme;
        [SerializeField] private BlockData defaultBlockData;
        [SerializeField] private ProjectRuntimeSettings runtimeSettings = new();

        public ThemeDatabase ThemeDatabase => themeDatabase;
        public Theme DefaultTheme => defaultTheme;
        public BlockData DefaultBlockData => defaultBlockData;
        public ProjectRuntimeSettings RuntimeSettings => runtimeSettings;

        private void Reset()
        {
            TryAutoAssignAssets();
        }

        private void OnValidate()
        {
            TryAutoAssignAssets();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            PrepareRegistrations();

            if (themeDatabase != null)
            {
                builder.RegisterInstance(themeDatabase);
            }

            if (defaultTheme != null)
            {
                builder.RegisterInstance(defaultTheme);
            }

            if (defaultBlockData != null)
            {
                builder.RegisterInstance(defaultBlockData);
            }

            builder.RegisterInstance(runtimeSettings);
        }

        private void PrepareRegistrations()
        {
            runtimeSettings ??= new ProjectRuntimeSettings();
            runtimeSettings.Normalize();
            TryAutoAssignAssets();
        }

        private void TryAutoAssignAssets()
        {
#if UNITY_EDITOR
            if (themeDatabase == null)
            {
                themeDatabase = FindFirstAsset<ThemeDatabase>();
            }

            if (defaultTheme == null)
            {
                defaultTheme = themeDatabase != null
                    ? themeDatabase.GetDefaultTheme()
                    : FindFirstAsset<Theme>();
            }

            if (defaultBlockData == null)
            {
                defaultBlockData = FindFirstAsset<BlockData>();
            }

            runtimeSettings?.TryAutoAssignAssets();
#endif
        }

#if UNITY_EDITOR
        private static TAsset FindFirstAsset<TAsset>() where TAsset : UnityEngine.Object
        {
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TAsset>(path);
        }
#endif
    }
}
