using PixelFlow.Runtime.Bullets;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Composition
{
    [CreateAssetMenu(fileName = "ProjectRuntimeSettings", menuName = "Pixel Flow/Project Runtime Settings")]
    public sealed class ProjectRuntimeSettings : ScriptableObject
    {
        [Header("Visual Pool")]
        [SerializeField] private PigController pigPrefab;
        [SerializeField] private BlockVisual blockPrefab;
        [SerializeField] private BulletController bulletPrefab;
        [SerializeField, Min(0)] private int pigPrewarmCount = 8;
        [SerializeField, Min(0)] private int blockPrewarmCount = 32;
        [SerializeField, Min(0)] private int bulletPrewarmCount = 16;
        [SerializeField, Min(1)] private int pigMaxSize = 64;
        [SerializeField, Min(1)] private int blockMaxSize = 256;
        [SerializeField, Min(1)] private int bulletMaxSize = 128;

        [Header("Game")]
        [SerializeField, Min(0.01f)] private float dispatchFollowSpeed = 7f;

        [Header("Input")]
        [SerializeField] private LayerMask pigLayerMask = 1 << 7;
        [SerializeField, Min(1f)] private float maxRayDistance = 500f;

        public PigController PigPrefab => pigPrefab;
        public BlockVisual BlockPrefab => blockPrefab;
        public BulletController BulletPrefab => bulletPrefab;
        public int PigPrewarmCount => pigPrewarmCount;
        public int BlockPrewarmCount => blockPrewarmCount;
        public int BulletPrewarmCount => bulletPrewarmCount;
        public int PigMaxSize => pigMaxSize;
        public int BlockMaxSize => blockMaxSize;
        public int BulletMaxSize => bulletMaxSize;
        public float DispatchFollowSpeed => dispatchFollowSpeed;
        public LayerMask PigLayerMask => pigLayerMask;
        public float MaxRayDistance => maxRayDistance;

        public void Normalize()
        {
            pigPrewarmCount = Mathf.Max(0, pigPrewarmCount);
            blockPrewarmCount = Mathf.Max(0, blockPrewarmCount);
            bulletPrewarmCount = Mathf.Max(0, bulletPrewarmCount);
            pigMaxSize = Mathf.Max(1, pigMaxSize);
            blockMaxSize = Mathf.Max(1, blockMaxSize);
            bulletMaxSize = Mathf.Max(1, bulletMaxSize);
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
        private void OnValidate()
        {
            TryAutoAssignAssets();
        }

        public void TryAutoAssignAssets()
        {
            pigPrefab ??= FindPrefabComponent<PigController>("Pig");
            blockPrefab ??= FindPrefabComponent<BlockVisual>("Block");
            bulletPrefab ??= FindPrefabComponent<BulletController>("Bullet");
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
#endif
    }
}
