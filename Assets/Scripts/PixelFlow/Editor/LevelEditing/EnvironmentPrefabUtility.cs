#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using PixelFlow.Runtime.LevelEditing;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.Editor.LevelEditing
{
    internal static class EnvironmentPrefabUtility
    {
        private const string GeneratedMeshFolderSuffix = "_GeneratedMeshes";

        [MenuItem("CONTEXT/EnvironmentContext/Bake Meshes And Save Prefab")]
        private static void BakeMeshesAndSavePrefab(MenuCommand command)
        {
            var environment = command.context as EnvironmentContext;
            if (!TryBakeMeshesAndSavePrefab(environment, out var message))
            {
                Debug.LogError(message, environment);
                return;
            }

            Debug.Log(message, environment);
        }

        internal static bool TryBakeMeshesAndSavePrefab(EnvironmentContext environment, out string message)
        {
            message = "Could not save EnvironmentContext prefab.";
            if (environment == null)
            {
                message = "EnvironmentContext reference is missing.";
                return false;
            }

            if (!environment.gameObject.scene.IsValid())
            {
                message = "Bake Meshes And Save Prefab requires a scene instance, not the prefab asset itself.";
                return false;
            }

            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(environment.gameObject);
            if (prefabRoot == null)
            {
                message = "EnvironmentContext is not connected to a prefab instance.";
                return false;
            }

            var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot);
            if (string.IsNullOrWhiteSpace(prefabAssetPath))
            {
                message = "Could not resolve the prefab asset path for the selected environment.";
                return false;
            }

            environment.ResolveMissingReferences();

            var generatedMeshFolderPath = EnsureGeneratedMeshFolder(prefabAssetPath);
            var bakedMeshes = new Dictionary<Mesh, Mesh>();
            var bakedMeshCount = 0;

            var meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                if (AssetDatabase.Contains(meshFilter.sharedMesh) || IsDynamicTextMesh(meshFilter))
                {
                    continue;
                }

                if (!bakedMeshes.TryGetValue(meshFilter.sharedMesh, out var bakedMesh))
                {
                    var assetName = BuildGeneratedMeshAssetName(prefabRoot.transform, meshFilter.transform);
                    var meshAssetPath = $"{generatedMeshFolderPath}/{assetName}.asset";
                    bakedMesh = SaveMeshAsset(meshFilter.sharedMesh, meshAssetPath, assetName);
                    bakedMeshes.Add(meshFilter.sharedMesh, bakedMesh);
                    bakedMeshCount++;
                }

                Undo.RecordObject(meshFilter, "Bake Environment Mesh");
                meshFilter.sharedMesh = bakedMesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
                EditorUtility.SetDirty(meshFilter);
            }

            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            message = bakedMeshCount > 0
                ? $"Baked {bakedMeshCount} generated mesh asset(s) and saved prefab '{prefabRoot.name}'."
                : $"Saved prefab '{prefabRoot.name}'. No transient non-text meshes required baking.";
            return true;
        }

        private static string EnsureGeneratedMeshFolder(string prefabAssetPath)
        {
            var prefabDirectory = Path.GetDirectoryName(prefabAssetPath)?.Replace("\\", "/");
            var prefabName = Path.GetFileNameWithoutExtension(prefabAssetPath);
            var folderPath = $"{prefabDirectory}/{prefabName}{GeneratedMeshFolderSuffix}";
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return folderPath;
            }

            AssetDatabase.CreateFolder(prefabDirectory, $"{prefabName}{GeneratedMeshFolderSuffix}");
            return folderPath;
        }

        private static Mesh SaveMeshAsset(Mesh sourceMesh, string meshAssetPath, string assetName)
        {
            var clone = Object.Instantiate(sourceMesh);
            clone.name = assetName;

            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existingMesh != null)
            {
                EditorUtility.CopySerialized(clone, existingMesh);
                EditorUtility.SetDirty(existingMesh);
                Object.DestroyImmediate(clone);
                return existingMesh;
            }

            AssetDatabase.CreateAsset(clone, meshAssetPath);
            return clone;
        }

        private static bool IsDynamicTextMesh(MeshFilter meshFilter)
        {
            return meshFilter.GetComponent<TMP_Text>() != null;
        }

        private static string BuildGeneratedMeshAssetName(Transform root, Transform target)
        {
            var relativePath = AnimationUtility.CalculateTransformPath(target, root);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = target.name;
            }

            relativePath = relativePath.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
            return $"{root.name}_{relativePath}_Mesh";
        }
    }

    internal static class SceneContextEnvironmentUtility
    {
        internal static bool TryResolveOpenSceneContext(out SceneContext sceneContext)
        {
            sceneContext = null;
            var sceneContexts = UnityEngine.Object.FindObjectsByType<SceneContext>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sceneContexts.Length; i++)
            {
                var candidate = sceneContexts[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                sceneContext = candidate;
                return true;
            }

            return false;
        }

        internal static EnvironmentContext ResolveEnvironment(SceneContext sceneContext)
        {
            if (sceneContext == null)
            {
                return null;
            }

            if (sceneContext.EnvironmentInstance != null && sceneContext.EnvironmentInstance.gameObject != null)
            {
                return sceneContext.EnvironmentInstance;
            }

            var candidates = sceneContext.GetComponentsInChildren<EnvironmentContext>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static bool IsEditorManagedEnvironment(SceneContext sceneContext, EnvironmentContext environment)
        {
            return sceneContext != null
                && environment != null
                && environment.gameObject != null
                && environment.gameObject.scene.IsValid()
                && environment.transform.parent == sceneContext.transform
                && (environment.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0;
        }

        internal static bool RemoveEditorManagedEnvironment(SceneContext sceneContext)
        {
            if (sceneContext == null)
            {
                return false;
            }

            var candidates = sceneContext.GetComponentsInChildren<EnvironmentContext>(true);
            var removedAny = false;
            for (int i = candidates.Length - 1; i >= 0; i--)
            {
                var candidate = candidates[i];
                if (!IsEditorManagedEnvironment(sceneContext, candidate))
                {
                    continue;
                }

                removedAny = true;
                Object.DestroyImmediate(candidate.gameObject);
            }

            return removedAny;
        }
    }
}
#endif
