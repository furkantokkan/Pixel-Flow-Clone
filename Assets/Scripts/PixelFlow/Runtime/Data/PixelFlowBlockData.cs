using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [CreateAssetMenu(fileName = "PixelFlowBlockData", menuName = "Pixel Flow/Block Data")]
    public sealed class PixelFlowBlockData : ScriptableObject
    {
        [SerializeField] private GameObject blockPrefab;

        [Min(0.01f)]
        [SerializeField] private float cellSpacing = 0.6f;

        [SerializeField] private float verticalOffset = 0.5f;

        public GameObject BlockPrefab => TryGetBlockPrefab(out var prefab) ? prefab : null;
        public float CellSpacing => Mathf.Max(0.01f, cellSpacing);
        public float VerticalOffset => verticalOffset;
        public Vector3 ResolvedBlockScale => TryGetResolvedBlockScale(out var scale) ? scale : Vector3.one;

        public bool TryGetBlockPrefab(out GameObject prefab)
        {
            if (!IsAccessible(blockPrefab))
            {
                prefab = null;
                return false;
            }

            prefab = blockPrefab;
            return prefab != null;
        }

        public bool TryGetResolvedBlockScale(out Vector3 scale)
        {
            scale = Vector3.one;
            if (!TryGetBlockPrefab(out var prefab))
            {
                return false;
            }

            try
            {
                var prefabTransform = prefab.transform;
                if (prefabTransform == null)
                {
                    return false;
                }

                scale = prefabTransform.localScale;
                return true;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
            catch (System.NullReferenceException)
            {
                return false;
            }
        }

        private void OnValidate()
        {
            cellSpacing = Mathf.Max(0.01f, cellSpacing);
        }

        private static bool IsAccessible(Object target)
        {
            if (ReferenceEquals(target, null))
            {
                return false;
            }

            try
            {
                return target != null;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }
    }
}
