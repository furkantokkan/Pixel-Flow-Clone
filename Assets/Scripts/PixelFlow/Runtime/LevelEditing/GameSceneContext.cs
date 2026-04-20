using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.Levels;
using PixelFlow.Runtime.Managers;
using PixelFlow.Runtime.Pooling;
using PixelFlow.Runtime.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class GameSceneContext : LifetimeScope
    {
        private EnvironmentContext environmentInstance;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private InputManager inputManager;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private ThemeDatabase themeDatabase;
        [SerializeField] private Theme defaultTheme;
        [SerializeField] private BlockData defaultBlockData;
        [SerializeField] private ProjectRuntimeSettings runtimeSettings;
        [SerializeField] private LevelDatabase levelDatabase;

        private GameSceneHudPresenter gameSceneHudPresenter;
        private ThemeDatabase injectedThemeDatabase;
        private Theme injectedDefaultTheme;
        private IVisualPoolService visualPoolService;
        private IGameFactory gameFactory;
        private ProjectLifetimeScope projectLifetimeScope;
        private bool runtimeSessionInitialized;
        private bool sceneBindingsInjected;

        public Theme Theme => ResolveTheme(null);
        public ThemeDatabase ThemeDatabase => ResolveThemeDatabase();
        public LevelDatabase LevelDatabase => ResolveLevelDatabase();
        public Transform EnvironmentRoot => environmentInstance != null ? environmentInstance.transform : null;
        public EnvironmentContext EnvironmentInstance => environmentInstance;
        public bool RuntimeSessionInitialized => runtimeSessionInitialized;
        public IVisualPoolService VisualPoolService => visualPoolService;
        public GameManager GameManager => gameManager;
        public InputManager InputManager => inputManager;
        public IGameFactory GameFactory => gameFactory;

        private void Reset()
        {
            ResolveSceneComponents();
            TryAutoAssignSceneReferences();
            TryAutoAssignProjectReferences();
        }

        private void OnValidate()
        {
            ResolveSceneComponents();
            TryAutoAssignSceneReferences();
            TryAutoAssignProjectReferences();
        }

        private void OnTransformChildrenChanged()
        {
            TryAutoAssignSceneReferences();
        }

        protected override void Awake()
        {
            ResolveSceneComponents(addMissingLevelSessionController: true);
            base.Awake();
        }

        protected override LifetimeScope FindParent()
        {
            return null;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                runtimeSessionInitialized = false;
                sceneBindingsInjected = false;
            }
        }

        private void OnApplicationQuit()
        {
            runtimeSessionInitialized = false;
            sceneBindingsInjected = false;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            TryAutoAssignProjectReferences();

            RegisterInstance(builder, ResolveThemeDatabase());
            RegisterInstance(builder, ResolveTheme(null));
            RegisterInstance(builder, ResolveDefaultBlockData());
            RegisterInstance(builder, ResolveRuntimeSettings());
            RegisterInstance(builder, ResolveLevelDatabase());

            builder.Register<IVisualPoolService, VisualPoolService>(Lifetime.Scoped);
            builder.Register<IGameFactory, GameFactory>(Lifetime.Scoped);
            RegisterComponent(builder, gameManager != null ? gameManager : GetComponent<GameManager>());
            RegisterComponent(builder, inputManager != null ? inputManager : GetComponent<InputManager>());
            RegisterComponent(builder, levelSessionController != null ? levelSessionController : GetComponent<LevelSessionController>());
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

        public void InitializeRuntimeSessionIfNeeded()
        {
            ResolveSceneComponents(addMissingLevelSessionController: true);

            if (runtimeSessionInitialized && environmentInstance == null)
            {
                runtimeSessionInitialized = false;
            }

            if (!Application.isPlaying || runtimeSessionInitialized)
            {
                return;
            }

            if (gameManager == null || inputManager == null || levelSessionController == null)
            {
                return;
            }

            EnsureRuntimeServices();
            var resolvedEnvironment = EnsureEnvironment();
            if (resolvedEnvironment == null)
            {
                return;
            }

            gameManager?.Construct(resolvedEnvironment);
            inputManager?.RefreshInputCameraReference(preferSceneMain: true);
            inputManager?.Construct(gameManager, ResolveInputCamera());
            runtimeSessionInitialized = true;
        }

        public void ResetRuntimeSessionState()
        {
            runtimeSessionInitialized = false;
            sceneBindingsInjected = false;
            projectLifetimeScope = null;

            if (visualPoolService is System.IDisposable disposableVisualPoolService)
            {
                disposableVisualPoolService.Dispose();
            }

            visualPoolService = null;
            gameFactory = null;

            var managedEnvironments = GetComponentsInChildren<EnvironmentContext>(true);
            for (int i = 0; i < managedEnvironments.Length; i++)
            {
                DestroyEnvironmentInstance(managedEnvironments[i]);
            }

            environmentInstance = null;
            gameManager?.SetGameFactory(null);
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

            environmentInstance = CleanupDuplicateEnvironments(resolvedTheme, environmentInstance);

            if (environmentInstance != null)
            {
                resolvedTheme ??= environmentInstance.ResolveTheme();
                environmentInstance.Construct(
                    ResolveThemeDatabase(),
                    resolvedTheme,
                    ResolveDefaultBlockData());
                environmentInstance.ResolveMissingReferences();
                visualPoolService?.ConfigureRoots(
                    pigRoot: environmentInstance.DeckContainer != null ? environmentInstance.DeckContainer : environmentInstance.transform,
                    blockRoot: environmentInstance.BlockContainer != null ? environmentInstance.BlockContainer : environmentInstance.transform,
                    bulletRoot: environmentInstance.transform);
            }

            return environmentInstance;
        }

        [Inject]
        public void Construct(
            ThemeDatabase injectedThemeDatabase,
            Theme injectedDefaultTheme,
            IVisualPoolService visualPoolService,
            IGameFactory gameFactory)
        {
            this.injectedThemeDatabase ??= injectedThemeDatabase;
            this.injectedDefaultTheme ??= injectedDefaultTheme;
            this.visualPoolService ??= visualPoolService;
            this.gameFactory ??= gameFactory;
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

        private void TryAutoAssignProjectReferences()
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

            if (runtimeSettings == null)
            {
                runtimeSettings = FindFirstAsset<ProjectRuntimeSettings>();
            }

            if (levelDatabase == null)
            {
                levelDatabase = FindFirstAsset<LevelDatabase>();
            }
#endif
        }

        private void ResolveSceneComponents(bool addMissingLevelSessionController = false)
        {
            gameManager ??= GetComponent<GameManager>();
            inputManager ??= GetComponent<InputManager>();
            levelSessionController ??= GetComponent<LevelSessionController>();
            gameSceneHudPresenter ??= FindFirstObjectByType<GameSceneHudPresenter>(FindObjectsInactive.Include);

            if (levelSessionController == null && addMissingLevelSessionController)
            {
                levelSessionController = gameObject.AddComponent<LevelSessionController>();
            }
        }

        private void TryInjectRuntimeServicesIfNeeded()
        {
            if (Container == null)
            {
                return;
            }

            if (!sceneBindingsInjected)
            {
                Container.InjectGameObject(gameObject);
                if (gameSceneHudPresenter != null)
                {
                    Container.InjectGameObject(gameSceneHudPresenter.gameObject);
                }

                sceneBindingsInjected = true;
            }
        }

        private void EnsureRuntimeServices()
        {
            TryInjectRuntimeServicesIfNeeded();

            visualPoolService ??= new VisualPoolService(ResolveRuntimeSettings(), this);
            gameFactory ??= visualPoolService != null
                ? new GameFactory(visualPoolService)
                : null;

            gameManager?.ApplyProjectSettings(ResolveRuntimeSettings());
            gameManager?.SetGameFactory(gameFactory);
        }

        private void TryAutoAssignSceneReferences()
        {
            if (!gameObject.scene.IsValid())
            {
                return;
            }

            var resolvedTheme = ResolveTheme(null);

            if (!CanReuseEnvironment(environmentInstance, resolvedTheme))
            {
                environmentInstance = null;
            }

            if (environmentInstance == null)
            {
                environmentInstance = ResolveManagedEnvironment(resolvedTheme);
            }
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

        private EnvironmentContext CleanupDuplicateEnvironments(Theme resolvedTheme, EnvironmentContext preferredEnvironment)
        {
            var environmentHostRoot = ResolveEnvironmentHostRoot();
            if (environmentHostRoot == null)
            {
                return preferredEnvironment;
            }

            var resolvedEnvironment = CanReuseEnvironment(preferredEnvironment, resolvedTheme)
                ? preferredEnvironment
                : null;
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

                if (candidate != resolvedEnvironment)
                {
                    DestroyEnvironmentInstance(candidate);
                }
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
            LifetimeScope parentScope = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                spawnedGameObject = UnityEditor.PrefabUtility.InstantiatePrefab(environmentPrefab.gameObject, gameObject.scene) as GameObject;
            }
            else
            {
                parentScope = Container != null ? (LifetimeScope)this : ResolveProjectLifetimeScope();
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
            parentScope = Container != null ? (LifetimeScope)this : ResolveProjectLifetimeScope();
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
            if (spawnedGameObject.transform.parent != environmentHostRoot)
            {
                spawnedGameObject.transform.SetParent(environmentHostRoot, false);
            }

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
            if (!Application.isPlaying)
            {
                var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject);
                return source == environmentPrefab.gameObject || candidate.gameObject == environmentPrefab.gameObject;
            }
#endif

            var candidateName = candidate.gameObject.name.Replace("(Clone)", string.Empty).Trim();
            return candidateName == environmentPrefab.gameObject.name;
        }

        private bool CanReuseEnvironment(EnvironmentContext candidate, Theme resolvedTheme)
        {
            var environmentHostRoot = ResolveEnvironmentHostRoot();
            if (candidate == null
                || candidate.gameObject == null
                || candidate.gameObject.scene != gameObject.scene)
            {
                return false;
            }

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

        private Theme ResolveProjectDefaultTheme()
        {
            if (defaultTheme != null)
            {
                return defaultTheme;
            }

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
            if (themeDatabase != null)
            {
                return themeDatabase;
            }

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

        private BlockData ResolveDefaultBlockData()
        {
            if (defaultBlockData != null)
            {
                return defaultBlockData;
            }

            var projectScope = ResolveProjectLifetimeScope();
            if (projectScope != null && projectScope.DefaultBlockData != null)
            {
                return projectScope.DefaultBlockData;
            }

#if UNITY_EDITOR
            defaultBlockData = FindFirstAsset<BlockData>();
#endif
            return defaultBlockData;
        }

        private ProjectRuntimeSettings ResolveRuntimeSettings()
        {
            if (runtimeSettings != null)
            {
                return runtimeSettings;
            }

            var projectScope = ResolveProjectLifetimeScope();
            if (projectScope != null && projectScope.RuntimeSettings != null)
            {
                return projectScope.RuntimeSettings;
            }

#if UNITY_EDITOR
            runtimeSettings = FindFirstAsset<ProjectRuntimeSettings>();
#endif
            return runtimeSettings;
        }

        private LevelDatabase ResolveLevelDatabase()
        {
            if (levelDatabase != null)
            {
                return levelDatabase;
            }

#if UNITY_EDITOR
            levelDatabase = FindFirstAsset<LevelDatabase>();
#endif
            return levelDatabase;
        }

        private ProjectLifetimeScope ResolveProjectLifetimeScope()
        {
            if (projectLifetimeScope != null)
            {
                return projectLifetimeScope;
            }

            if (Application.isPlaying && VContainerSettings.Instance != null)
            {
                projectLifetimeScope = VContainerSettings.Instance.GetOrCreateRootLifetimeScopeInstance() as ProjectLifetimeScope;
                if (projectLifetimeScope != null)
                {
                    return projectLifetimeScope;
                }
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
                var candidate = loadedScopes[i];
                if (candidate == null
                    || UnityEditor.EditorUtility.IsPersistent(candidate)
                    || candidate.gameObject == null
                    || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                projectLifetimeScope = candidate;
                return projectLifetimeScope;
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

            if (candidate.transform.parent != null)
            {
                candidate.transform.SetParent(null, false);
            }

            UnityEngine.Object.Destroy(candidate.gameObject);
        }

        private static void RegisterComponent<TComponent>(IContainerBuilder builder, TComponent component)
            where TComponent : Component
        {
            if (component == null)
            {
                return;
            }

            builder.RegisterComponent(component);
        }

        private static void RegisterInstance<TInstance>(IContainerBuilder builder, TInstance instance)
            where TInstance : class
        {
            if (instance == null)
            {
                return;
            }

            builder.RegisterInstance(instance);
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
