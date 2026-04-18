using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Managers;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowSceneContext : MonoBehaviour
    {
        [SerializeField] private PixelFlowTheme theme;
        [SerializeField] private PixelFlowThemeDatabase themeDatabase;
        [SerializeField] private int themeDatabaseIndex;
        [SerializeField] private Transform environmentRoot;
        [SerializeField] private PixelFlowEnvironmentContext environmentInstance;
        [SerializeField] private PixelFlowEnvironmentContext debugEnvironment;
        [SerializeField] private PixelFlowVisualPool visualPool;
        [SerializeField] private PixelFlowGameManager gameManager;
        [SerializeField] private PixelFlowInputManager inputManager;

        private PixelFlowThemeDatabase injectedThemeDatabase;
        private PixelFlowTheme injectedDefaultTheme;
        private bool dependenciesInjected;

        public PixelFlowTheme Theme => theme;
        public PixelFlowThemeDatabase ThemeDatabase => themeDatabase;
        public int ThemeDatabaseIndex => themeDatabaseIndex;
        public PixelFlowEnvironmentContext EnvironmentInstance => environmentInstance;
        public PixelFlowEnvironmentContext DebugEnvironment => debugEnvironment;
        public PixelFlowVisualPool VisualPool => visualPool;
        public PixelFlowGameManager GameManager => gameManager;
        public PixelFlowInputManager InputManager => inputManager;

        private void Reset()
        {
            environmentRoot = transform;
            visualPool ??= GetComponent<PixelFlowVisualPool>();
            gameManager ??= GetComponent<PixelFlowGameManager>();
            inputManager ??= GetComponent<PixelFlowInputManager>();
            TryAutoAssignSceneReferences();
            TryAutoAssignThemeReferences();
        }

        private void OnValidate()
        {
            EnsureEnvironmentRoot();
            visualPool ??= GetComponent<PixelFlowVisualPool>();
            gameManager ??= GetComponent<PixelFlowGameManager>();
            inputManager ??= GetComponent<PixelFlowInputManager>();
            TryAutoAssignSceneReferences();
            TryAutoAssignThemeReferences();
        }

        private void Awake()
        {
            InjectDependencies();
            EnsureEnvironment();
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

        public void InstallBindings(PixelFlowTheme overrideTheme = null)
        {
            InjectDependencies();
            var resolvedEnvironment = EnsureEnvironment(overrideTheme);
            gameManager?.Construct(resolvedEnvironment);
            inputManager?.Construct(gameManager, ResolveInputCamera());
        }

        public bool TryResolveEnvironment(PixelFlowTheme overrideTheme, out PixelFlowEnvironmentContext resolvedEnvironment, out string error)
        {
            resolvedEnvironment = EnsureEnvironment(overrideTheme);
            if (resolvedEnvironment != null)
            {
                error = null;
                return true;
            }

            var resolvedTheme = ResolveTheme(overrideTheme);
            error = resolvedTheme == null
                ? "Assign a theme or place a PixelFlowEnvironmentContext in the open scene."
                : "PixelFlowSceneContext could not find or spawn a PixelFlowEnvironmentContext.";
            return false;
        }

        public PixelFlowEnvironmentContext EnsureEnvironment(PixelFlowTheme overrideTheme = null)
        {
            InjectDependencies();
            EnsureEnvironmentRoot();

            var resolvedTheme = ResolveTheme(overrideTheme);
            environmentInstance = ResolveManagedEnvironment(resolvedTheme);

            if (environmentInstance == null)
            {
                environmentInstance = ResolveSceneEnvironment(resolvedTheme);
            }

            if (environmentInstance == null && resolvedTheme != null && resolvedTheme.EnvironmentPrefab != null)
            {
                environmentInstance = SpawnEnvironment(resolvedTheme.EnvironmentPrefab);
            }

            if (environmentInstance == null && debugEnvironment != null)
            {
                debugEnvironment.InjectDependencies();
                debugEnvironment.ResolveMissingReferences();
                environmentInstance = debugEnvironment;
            }

            if (environmentInstance != null)
            {
                environmentInstance.InjectDependencies();
                resolvedTheme ??= environmentInstance.ResolveTheme();
                environmentInstance.ResolveMissingReferences();
                resolvedTheme?.Apply(environmentInstance);
                visualPool?.Configure(
                    pigPrefabOverride: null,
                    blockPrefabOverride: null,
                    pigRootOverride: environmentInstance.DeckContainer != null ? environmentInstance.DeckContainer : environmentInstance.transform,
                    blockRootOverride: environmentInstance.BlockContainer != null ? environmentInstance.BlockContainer : environmentInstance.transform);
            }

            return environmentInstance;
        }

        public void InjectDependencies()
        {
            if (dependenciesInjected)
            {
                return;
            }

            var projectLifetimeScope = LifetimeScope.Find<PixelFlowProjectLifetimeScope>();
            if (projectLifetimeScope?.Container == null)
            {
                return;
            }

            projectLifetimeScope.Container.Inject(this);
        }

        [Inject]
        public void Construct(PixelFlowThemeDatabase injectedThemeDatabase, PixelFlowTheme injectedDefaultTheme)
        {
            this.injectedThemeDatabase ??= injectedThemeDatabase;
            this.injectedDefaultTheme ??= injectedDefaultTheme;
            dependenciesInjected = true;
        }

        private Camera ResolveInputCamera()
        {
            if (inputManager != null && inputManager.InputCamera != null)
            {
                return inputManager.InputCamera;
            }

            if (Camera.main != null && Camera.main.gameObject.scene == gameObject.scene)
            {
                return Camera.main;
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

        private PixelFlowTheme ResolveTheme(PixelFlowTheme overrideTheme)
        {
            if (overrideTheme != null)
            {
                return overrideTheme;
            }

            if (theme != null)
            {
                return theme;
            }

            if (themeDatabase != null)
            {
                return themeDatabase.GetTheme(themeDatabaseIndex);
            }

            if (injectedThemeDatabase != null)
            {
                return injectedThemeDatabase.GetTheme(themeDatabaseIndex);
            }

            return injectedDefaultTheme;
        }

        private void TryAutoAssignThemeReferences()
        {
#if UNITY_EDITOR
            if (themeDatabase == null)
            {
                var databaseGuids = UnityEditor.AssetDatabase.FindAssets("t:PixelFlowThemeDatabase");
                if (databaseGuids != null && databaseGuids.Length > 0)
                {
                    var databasePath = UnityEditor.AssetDatabase.GUIDToAssetPath(databaseGuids[0]);
                    themeDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<PixelFlowThemeDatabase>(databasePath);
                }
            }

            if (theme == null)
            {
                if (themeDatabase != null)
                {
                    theme = themeDatabase.GetDefaultTheme();
                }

                if (theme == null)
                {
                    var themeGuids = UnityEditor.AssetDatabase.FindAssets("t:PixelFlowTheme");
                    if (themeGuids != null && themeGuids.Length > 0)
                    {
                        var themePath = UnityEditor.AssetDatabase.GUIDToAssetPath(themeGuids[0]);
                        theme = UnityEditor.AssetDatabase.LoadAssetAtPath<PixelFlowTheme>(themePath);
                    }
                }
            }
#endif
        }

        private void EnsureEnvironmentRoot()
        {
            if (environmentRoot == null)
            {
                environmentRoot = transform;
            }
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

            if (environmentInstance == null)
            {
                environmentInstance = ResolveSceneEnvironment(resolvedTheme);
            }

            if (debugEnvironment == null)
            {
                debugEnvironment = environmentInstance;
            }
        }

        private PixelFlowEnvironmentContext ResolveManagedEnvironment(PixelFlowTheme resolvedTheme)
        {
            if (environmentInstance != null && environmentInstance.gameObject != null)
            {
                return environmentInstance;
            }

            if (environmentRoot == null)
            {
                return null;
            }

            var candidate = environmentRoot.GetComponentInChildren<PixelFlowEnvironmentContext>(true);
            if (candidate == null)
            {
                return null;
            }

            if (resolvedTheme == null || resolvedTheme.EnvironmentPrefab == null || MatchesThemePrefab(candidate, resolvedTheme.EnvironmentPrefab))
            {
                return candidate;
            }

            return null;
        }

        private PixelFlowEnvironmentContext ResolveSceneEnvironment(PixelFlowTheme resolvedTheme)
        {
            var environments = FindObjectsByType<PixelFlowEnvironmentContext>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < environments.Length; i++)
            {
                var candidate = environments[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                if (resolvedTheme == null || resolvedTheme.EnvironmentPrefab == null || MatchesThemePrefab(candidate, resolvedTheme.EnvironmentPrefab))
                {
                    return candidate;
                }
            }

            return null;
        }

        private PixelFlowEnvironmentContext SpawnEnvironment(PixelFlowEnvironmentContext environmentPrefab)
        {
            if (environmentPrefab == null)
            {
                return null;
            }

            GameObject spawnedGameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                spawnedGameObject = UnityEditor.PrefabUtility.InstantiatePrefab(environmentPrefab.gameObject, gameObject.scene) as GameObject;
            }
            else
            {
                spawnedGameObject = Instantiate(environmentPrefab.gameObject);
            }
#else
            spawnedGameObject = Instantiate(environmentPrefab.gameObject);
#endif

            if (spawnedGameObject == null)
            {
                return null;
            }

            spawnedGameObject.name = environmentPrefab.gameObject.name;
            spawnedGameObject.transform.SetParent(environmentRoot, false);
            spawnedGameObject.transform.localPosition = Vector3.zero;
            spawnedGameObject.transform.localRotation = Quaternion.identity;

            return spawnedGameObject.GetComponent<PixelFlowEnvironmentContext>();
        }

        private static bool MatchesThemePrefab(PixelFlowEnvironmentContext candidate, PixelFlowEnvironmentContext environmentPrefab)
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
    }
}
