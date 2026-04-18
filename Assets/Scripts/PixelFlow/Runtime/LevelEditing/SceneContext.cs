using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Managers;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.LevelEditing
{
    [RequireComponent(typeof(GameSceneLifetimeScope))]
    [DisallowMultipleComponent]
    public sealed class SceneContext : MonoBehaviour
    {
        [SerializeField, HideInInspector] private Transform environmentRoot;
        [SerializeField, HideInInspector] private EnvironmentContext environmentInstance;
        [FormerlySerializedAs("debugEnvironment")]
        [SerializeField, HideInInspector] private EnvironmentContext legacyDebugEnvironment;
        [SerializeField] private VisualPool visualPool;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private InputManager inputManager;

        private ThemeDatabase injectedThemeDatabase;
        private Theme injectedDefaultTheme;
        private GameSceneLifetimeScope sceneLifetimeScope;
        private ProjectLifetimeScope projectLifetimeScope;

        public Theme Theme => ResolveTheme(null);
        public ThemeDatabase ThemeDatabase => ResolveThemeDatabase();
        public Transform EnvironmentRoot => environmentRoot;
        public EnvironmentContext EnvironmentInstance => environmentInstance;
        public VisualPool VisualPool => visualPool;
        public GameManager GameManager => gameManager;
        public InputManager InputManager => inputManager;

        private void Reset()
        {
            SyncEnvironmentRoot();
            visualPool ??= GetComponent<VisualPool>();
            gameManager ??= GetComponent<GameManager>();
            inputManager ??= GetComponent<InputManager>();
            TryAutoAssignSceneReferences();
            TryAutoAssignThemeReferences();
        }

        private void OnValidate()
        {
            SyncEnvironmentRoot();
            visualPool ??= GetComponent<VisualPool>();
            gameManager ??= GetComponent<GameManager>();
            inputManager ??= GetComponent<InputManager>();
            TryAutoAssignSceneReferences();
            TryAutoAssignThemeReferences();
        }

        private void Awake()
        {
            SyncEnvironmentRoot();
        }

        private void Start()
        {
            InstallBindings();
        }

        [ContextMenu("Refresh Environment")]
        private void RefreshEnvironmentFromContextMenu()
        {
            EnsureEnvironment();
            if (Application.isPlaying)
            {
                InstallBindings();
            }
        }

        [ContextMenu("Install Bindings")]
        private void InstallBindingsFromContextMenu()
        {
            InstallBindings();
        }

        public void InstallBindings(Theme overrideTheme = null)
        {
            var resolvedEnvironment = EnsureEnvironment(overrideTheme);
            gameManager?.Construct(resolvedEnvironment);
            inputManager?.RefreshInputCameraReference(preferSceneMain: true);
            inputManager?.Construct(gameManager, ResolveInputCamera());
        }

        public bool TryResolveEnvironment(Theme overrideTheme, out EnvironmentContext resolvedEnvironment, out string error)
        {
            resolvedEnvironment = EnsureEnvironment(overrideTheme);
            if (resolvedEnvironment != null)
            {
                error = null;
                return true;
            }

            var resolvedTheme = ResolveTheme(overrideTheme);
            error = resolvedTheme == null
                ? "Configure a default theme or theme database on ProjectLifetimeScope."
                : "SceneContext could not find or spawn a EnvironmentContext.";
            return false;
        }

        public EnvironmentContext EnsureEnvironment(Theme overrideTheme = null)
        {
            SyncEnvironmentRoot();

            var resolvedTheme = ResolveTheme(overrideTheme);
            if (!CanReuseEnvironment(environmentInstance, resolvedTheme))
            {
                DestroyEnvironmentInstance(environmentInstance);
                environmentInstance = null;
            }

            environmentInstance = ResolveManagedEnvironment(resolvedTheme);

            if (environmentInstance == null && resolvedTheme != null && resolvedTheme.EnvironmentPrefab != null)
            {
                environmentInstance = SpawnEnvironment(resolvedTheme.EnvironmentPrefab);
            }

            if (environmentInstance != null)
            {
                resolvedTheme ??= environmentInstance.ResolveTheme();
                environmentInstance.ResolveMissingReferences();
                visualPool?.Configure(
                    pigPrefabOverride: null,
                    blockPrefabOverride: null,
                    pigRootOverride: environmentInstance.DeckContainer != null ? environmentInstance.DeckContainer : environmentInstance.transform,
                    blockRootOverride: environmentInstance.BlockContainer != null ? environmentInstance.BlockContainer : environmentInstance.transform);
            }

            SyncEnvironmentRoot();

            return environmentInstance;
        }

        [Inject]
        public void Construct(ThemeDatabase injectedThemeDatabase, Theme injectedDefaultTheme)
        {
            this.injectedThemeDatabase ??= injectedThemeDatabase;
            this.injectedDefaultTheme ??= injectedDefaultTheme;
        }

        private Camera ResolveInputCamera()
        {
            if (Camera.main != null && Camera.main.gameObject.scene == gameObject.scene)
            {
                return Camera.main;
            }

            if (inputManager != null && inputManager.InputCamera != null)
            {
                return inputManager.InputCamera;
            }

            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var candidate = cameras[i];
                if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                {
                    return candidate;
                }
            }

            return Camera.main;
        }

        private Theme ResolveTheme(Theme overrideTheme)
        {
            if (overrideTheme != null)
            {
                return overrideTheme;
            }

            var projectDefaultTheme = ResolveProjectDefaultTheme();
            if (projectDefaultTheme != null)
            {
                return projectDefaultTheme;
            }

            return ResolveThemeDatabase()?.GetDefaultTheme();
        }

        private void TryAutoAssignThemeReferences()
        {
#if UNITY_EDITOR
            if (injectedDefaultTheme == null)
            {
                injectedDefaultTheme = ResolveProjectDefaultTheme();
            }
#endif
        }

        private void TryAutoAssignSceneReferences()
        {
            if (!gameObject.scene.IsValid())
            {
                return;
            }

            var resolvedTheme = ResolveTheme(null);

            if (environmentInstance == null)
            {
                environmentInstance = ResolveManagedEnvironment(resolvedTheme);
            }

            SyncEnvironmentRoot();
        }

        private EnvironmentContext ResolveManagedEnvironment(Theme resolvedTheme)
        {
            var environmentHostRoot = ResolveEnvironmentHostRoot();
            if (environmentHostRoot == null)
            {
                return null;
            }

            EnvironmentContext resolvedEnvironment = null;
            var candidates = environmentHostRoot.GetComponentsInChildren<EnvironmentContext>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!CanReuseEnvironment(candidate, resolvedTheme))
                {
                    DestroyEnvironmentInstance(candidate);
                    continue;
                }

                if (resolvedEnvironment == null)
                {
                    resolvedEnvironment = candidate;
                    continue;
                }

                DestroyEnvironmentInstance(candidate);
            }

            return resolvedEnvironment;
        }

        private EnvironmentContext SpawnEnvironment(EnvironmentContext environmentPrefab)
        {
            if (environmentPrefab == null)
            {
                return null;
            }

            var environmentHostRoot = ResolveEnvironmentHostRoot();
            GameObject spawnedGameObject;
            var parentScope = (LifetimeScope)ResolveSceneLifetimeScope() ?? ResolveProjectLifetimeScope();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                spawnedGameObject = UnityEditor.PrefabUtility.InstantiatePrefab(environmentPrefab.gameObject, gameObject.scene) as GameObject;
            }
            else
            {
                if (parentScope != null)
                {
                    using (LifetimeScope.EnqueueParent(parentScope))
                    {
                        spawnedGameObject = Instantiate(environmentPrefab.gameObject, environmentHostRoot, false);
                    }
                }
                else
                {
                    spawnedGameObject = Instantiate(environmentPrefab.gameObject, environmentHostRoot, false);
                }
            }
#else
            if (parentScope != null)
            {
                using (LifetimeScope.EnqueueParent(parentScope))
                {
                    spawnedGameObject = Instantiate(environmentPrefab.gameObject, environmentHostRoot, false);
                }
            }
            else
            {
                spawnedGameObject = Instantiate(environmentPrefab.gameObject, environmentHostRoot, false);
            }
#endif

            if (spawnedGameObject == null)
            {
                return null;
            }

            spawnedGameObject.name = environmentPrefab.gameObject.name;
            spawnedGameObject.transform.SetParent(environmentHostRoot, false);
            spawnedGameObject.transform.localPosition = Vector3.zero;
            spawnedGameObject.transform.localRotation = Quaternion.identity;

            return spawnedGameObject.GetComponent<EnvironmentContext>();
        }

        private static bool MatchesThemePrefab(EnvironmentContext candidate, EnvironmentContext environmentPrefab)
        {
            if (candidate == null || environmentPrefab == null)
            {
                return false;
            }

#if UNITY_EDITOR
            var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject);
            return source == environmentPrefab.gameObject || candidate.gameObject == environmentPrefab.gameObject;
#else
            var candidateName = candidate.gameObject.name.Replace("(Clone)", string.Empty).Trim();
            return candidateName == environmentPrefab.gameObject.name;
#endif
        }

        private bool CanReuseEnvironment(EnvironmentContext candidate, Theme resolvedTheme)
        {
            if (candidate == null
                || candidate.gameObject == null
                || candidate.gameObject.scene != gameObject.scene)
            {
                return false;
            }

            var environmentHostRoot = ResolveEnvironmentHostRoot();
            if (environmentHostRoot != null && !candidate.transform.IsChildOf(environmentHostRoot))
            {
                return false;
            }

            if (resolvedTheme == null || resolvedTheme.EnvironmentPrefab == null)
            {
                return true;
            }

            return MatchesThemePrefab(candidate, resolvedTheme.EnvironmentPrefab);
        }

        private Transform ResolveEnvironmentHostRoot()
        {
            return transform;
        }

        private void SyncEnvironmentRoot()
        {
            environmentRoot = environmentInstance != null ? environmentInstance.transform : null;
        }

        private Theme ResolveProjectDefaultTheme()
        {
            if (injectedDefaultTheme != null)
            {
                return injectedDefaultTheme;
            }

            var projectScope = ResolveProjectLifetimeScope();
            if (projectScope != null && projectScope.DefaultTheme != null)
            {
                return projectScope.DefaultTheme;
            }

            return null;
        }

        private ThemeDatabase ResolveThemeDatabase()
        {
            if (injectedThemeDatabase != null)
            {
                return injectedThemeDatabase;
            }

            var projectScope = ResolveProjectLifetimeScope();
            if (projectScope != null && projectScope.ThemeDatabase != null)
            {
                return projectScope.ThemeDatabase;
            }

#if UNITY_EDITOR
            var databaseGuids = UnityEditor.AssetDatabase.FindAssets("t:ThemeDatabase");
            if (databaseGuids != null && databaseGuids.Length > 0)
            {
                var databasePath = UnityEditor.AssetDatabase.GUIDToAssetPath(databaseGuids[0]);
                return UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeDatabase>(databasePath);
            }
#endif

            return null;
        }

        private GameSceneLifetimeScope ResolveSceneLifetimeScope()
        {
            sceneLifetimeScope ??= GetComponent<GameSceneLifetimeScope>();
            return sceneLifetimeScope;
        }

        private ProjectLifetimeScope ResolveProjectLifetimeScope()
        {
            if (projectLifetimeScope != null)
            {
                return projectLifetimeScope;
            }

            projectLifetimeScope = LifetimeScope.Find<ProjectLifetimeScope>() as ProjectLifetimeScope;
            if (projectLifetimeScope != null)
            {
                return projectLifetimeScope;
            }

#if UNITY_EDITOR
            var loadedScopes = Resources.FindObjectsOfTypeAll<ProjectLifetimeScope>();
            for (int i = 0; i < loadedScopes.Length; i++)
            {
                if (loadedScopes[i] != null)
                {
                    projectLifetimeScope = loadedScopes[i];
                    return projectLifetimeScope;
                }
            }
#endif

            return null;
        }

        private static void DestroyEnvironmentInstance(EnvironmentContext candidate)
        {
            if (candidate == null || candidate.gameObject == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(candidate.gameObject);
                return;
            }
#endif

            UnityEngine.Object.Destroy(candidate.gameObject);
        }
    }
}
