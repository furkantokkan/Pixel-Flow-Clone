#if UNITY_EDITOR
using System;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.LevelEditing;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.Editor.LevelEditing
{
    internal static class TemporaryLevelSceneService
    {
        internal static bool TryResolveGenerationContext(
            out Theme previewTheme,
            out Transform tempParent,
            out GameSceneContext sceneContext,
            out string message)
        {
            previewTheme = null;
            tempParent = null;
            sceneContext = null;

            var sceneContexts = UnityEngine.Object.FindObjectsByType<GameSceneContext>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sceneContexts.Length; i++)
            {
                var candidate = sceneContexts[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                sceneContext = candidate;
                break;
            }

            if (sceneContext == null)
            {
                message = "Could not find SceneContext in the open scene.";
                return false;
            }

            previewTheme = ResolveGenerationTheme(sceneContext);
            if (previewTheme == null)
            {
                message = "Could not resolve a Theme for temporary test generation.";
                return false;
            }

            tempParent = sceneContext.transform;
            if (tempParent == null)
            {
                message = "Could not determine a parent transform for the temporary test level.";
                return false;
            }

            message = null;
            return true;
        }

        internal static GameObject CreateTemporaryRoot(Transform parent, string rootName)
        {
            var root = new GameObject(rootName);
            root.transform.SetParent(parent, false);
            ApplyTemporaryHideFlagsRecursive(root);
            return root;
        }

        internal static Transform CreateTemporaryChildRoot(Transform parent, string childName, Transform reference)
        {
            if (parent == null)
            {
                return null;
            }

            var child = new GameObject(childName);
            child.transform.SetParent(parent, false);
            ApplyTemporaryHideFlagsRecursive(child);
            AlignTemporaryRoot(child.transform, reference);
            return child.transform;
        }

        internal static bool TryInstantiateEnvironment(
            Transform tempRoot,
            Theme theme,
            out EnvironmentContext environment,
            out string message)
        {
            environment = null;
            if (tempRoot == null)
            {
                message = "Could not create a parent transform for the temporary environment.";
                return false;
            }

            if (theme == null)
            {
                message = "Could not resolve a Theme for temporary test generation.";
                return false;
            }

            var environmentPrefab = theme.EnvironmentPrefab;
            if (environmentPrefab == null)
            {
                message = $"Theme '{theme.name}' is missing a EnvironmentContext prefab.";
                return false;
            }

            var environmentGameObject = PrefabUtility.InstantiatePrefab(environmentPrefab.gameObject, tempRoot.gameObject.scene) as GameObject;
            if (environmentGameObject == null)
            {
                message = $"Could not instantiate the environment prefab for theme '{theme.name}'.";
                return false;
            }

            ApplyTemporaryHideFlagsRecursive(environmentGameObject);
            environmentGameObject.name = environmentPrefab.gameObject.name;
            environmentGameObject.transform.SetParent(tempRoot, false);
            environmentGameObject.transform.localPosition = Vector3.zero;
            environmentGameObject.transform.localRotation = Quaternion.identity;
            environmentGameObject.transform.localScale = Vector3.one;

            environment = environmentGameObject.GetComponent<EnvironmentContext>();
            if (environment == null)
            {
                UnityEngine.Object.DestroyImmediate(environmentGameObject);
                message = $"Theme '{theme.name}' environment prefab does not contain EnvironmentContext.";
                return false;
            }

            environment.ResolveMissingReferences();
            message = null;
            return true;
        }

        internal static void CleanupTemporaryArtifacts(string rootName, string boardRootName, string deckRootName)
        {
            var sceneContexts = Resources.FindObjectsOfTypeAll<GameSceneContext>();
            for (int i = 0; i < sceneContexts.Length; i++)
            {
                var sceneContext = sceneContexts[i];
                if (sceneContext == null
                    || EditorUtility.IsPersistent(sceneContext)
                    || !sceneContext.gameObject.scene.IsValid())
                {
                    continue;
                }

                LevelPreviewService.ClearPreview(sceneContext, sceneContext.Theme);
            }

            var transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                var candidate = transforms[i];
                if (!IsTemporaryRoot(candidate, rootName, boardRootName, deckRootName)
                    && !IsTemporaryChildRoot(candidate, rootName, boardRootName, deckRootName))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(candidate.gameObject);
            }
        }

        internal static void ApplyTemporaryHideFlagsRecursive(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            gameObject.hideFlags = HideFlags.DontSaveInEditor;
            var transforms = gameObject.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.hideFlags = HideFlags.DontSaveInEditor;
            }
        }

        private static Theme ResolveGenerationTheme(GameSceneContext sceneContext)
        {
            if (sceneContext?.Theme != null)
            {
                return sceneContext.Theme;
            }

            if (sceneContext?.ThemeDatabase != null)
            {
                var defaultTheme = sceneContext.ThemeDatabase.GetDefaultTheme();
                if (defaultTheme != null)
                {
                    return defaultTheme;
                }
            }

            var databaseGuids = AssetDatabase.FindAssets("t:ThemeDatabase");
            for (int i = 0; i < databaseGuids.Length; i++)
            {
                var databasePath = AssetDatabase.GUIDToAssetPath(databaseGuids[i]);
                var database = AssetDatabase.LoadAssetAtPath<ThemeDatabase>(databasePath);
                var defaultTheme = database != null ? database.GetDefaultTheme() : null;
                if (defaultTheme != null)
                {
                    return defaultTheme;
                }
            }

            var themeGuids = AssetDatabase.FindAssets("t:Theme");
            for (int i = 0; i < themeGuids.Length; i++)
            {
                var themePath = AssetDatabase.GUIDToAssetPath(themeGuids[i]);
                var theme = AssetDatabase.LoadAssetAtPath<Theme>(themePath);
                if (theme != null)
                {
                    return theme;
                }
            }

            return null;
        }

        private static void AlignTemporaryRoot(Transform target, Transform reference)
        {
            if (target == null)
            {
                return;
            }

            if (reference == null)
            {
                target.localPosition = Vector3.zero;
                target.localRotation = Quaternion.identity;
                target.localScale = Vector3.one;
                return;
            }

            target.position = reference.position;
            target.rotation = reference.rotation;
            target.localScale = reference.lossyScale;
        }

        private static bool IsTemporaryRoot(Transform candidate, string rootName, string boardRootName, string deckRootName)
        {
            if (candidate == null
                || EditorUtility.IsPersistent(candidate)
                || !string.Equals(candidate.name, rootName, StringComparison.Ordinal)
                || !candidate.gameObject.scene.IsValid())
            {
                return false;
            }

            return (candidate.hideFlags & HideFlags.DontSaveInEditor) != 0
                || candidate.Find(boardRootName) != null
                || candidate.Find(deckRootName) != null;
        }

        private static bool IsTemporaryChildRoot(Transform candidate, string rootName, string boardRootName, string deckRootName)
        {
            if (candidate == null
                || EditorUtility.IsPersistent(candidate)
                || !candidate.gameObject.scene.IsValid())
            {
                return false;
            }

            if (!string.Equals(candidate.name, boardRootName, StringComparison.Ordinal)
                && !string.Equals(candidate.name, deckRootName, StringComparison.Ordinal))
            {
                return false;
            }

            if ((candidate.hideFlags & HideFlags.DontSaveInEditor) != 0)
            {
                return true;
            }

            return candidate.parent != null
                && string.Equals(candidate.parent.name, rootName, StringComparison.Ordinal);
        }
    }
}
#endif
