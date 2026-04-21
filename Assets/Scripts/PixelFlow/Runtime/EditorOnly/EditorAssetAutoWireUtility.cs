#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PixelFlow.Runtime.EditorOnly
{
    internal static class EditorAssetAutoWireUtility
    {
        public static TAsset FindFirstAsset<TAsset>() where TAsset : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<TAsset>(path);
        }

        public static TAsset LoadAssetByGuid<TAsset>(string guid) where TAsset : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
        }

        public static TComponent FindPrefabComponent<TComponent>(string prefabNameFilter)
            where TComponent : Component
        {
            var prefabs = AssetDatabase.FindAssets($"t:Prefab {prefabNameFilter}");
            for (int i = 0; i < prefabs.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(prefabs[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
    }
}
#endif
