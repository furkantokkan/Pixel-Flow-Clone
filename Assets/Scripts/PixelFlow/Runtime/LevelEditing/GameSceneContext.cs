using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.Levels;
using PixelFlow.Runtime.Managers;
using PixelFlow.Runtime.Pooling;
using PixelFlow.Runtime.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        [SerializeField] private bool optimizeGlobalVolumeForMobile = true;

        private GameSceneHudView gameSceneHudView;
        private ThemeDatabase injectedThemeDatabase;
        private Theme injectedDefaultTheme;
        private BlockData injectedDefaultBlockData;
        private LevelDatabase injectedLevelDatabase;
        private IVisualPoolService visualPoolService;
        private IGameFactory gameFactory;
        private ProjectLifetimeScope projectLifetimeScope;
        private bool runtimeSessionInitialized;
        private VolumeProfile runtimeMobileVolumeProfile;

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
        }

        private void OnValidate()
        {
            ResolveSceneComponents();
            TryAutoAssignSceneReferences();
        }

        private void OnTransformChildrenChanged()
        {
            TryAutoAssignSceneReferences();
        }

        protected override void Awake()
        {
            ResolveSceneComponents(addMissingLevelSessionController: true);
            base.Awake();
            ResolveRuntimeContainerDependencies();
        }

        protected override LifetimeScope FindParent()
        {
            return ResolveProjectLifetimeScope();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyRuntimeVolumePolicyIfNeeded();
            InitializeRuntimeSessionIfNeeded();
            levelSessionController?.LoadInitialLevelIfNeeded();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                runtimeSessionInitialized = false;
            }
        }

        private void OnApplicationQuit()
        {
            runtimeSessionInitialized = false;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            ResolveSceneComponents(addMissingLevelSessionController: true);

            builder.RegisterBuildCallback(container => container.Inject(this));
            builder.Register<IVisualPoolService, VisualPoolService>(Lifetime.Scoped);
            builder.Register<IGameFactory, GameFactory>(Lifetime.Scoped);
            builder.Register<GameManagerCollaboratorFactory>(Lifetime.Scoped);
            builder.RegisterEntryPoint<GameSceneHudPresenter>(Lifetime.Scoped);
            RegisterComponent(builder, gameManager != null ? gameManager : GetComponent<GameManager>());
            RegisterComponent(builder, inputManager != null ? inputManager : GetComponent<InputManager>());
            RegisterComponent(builder, levelSessionController != null ? levelSessionController : GetComponent<LevelSessionController>());
            RegisterComponent(builder, gameSceneHudView);
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
            inputManager?.SetInputCamera(ResolveInputCamera());
        }

        public void InitializeRuntimeSessionIfNeeded()
        {
            ResolveSceneComponents(addMissingLevelSessionController: true);
            ResolveRuntimeContainerDependencies();

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

            var resolvedEnvironment = EnsureEnvironment();
            if (resolvedEnvironment == null)
            {
                return;
            }

            gameManager?.Construct(resolvedEnvironment);
            inputManager?.RefreshInputCameraReference(preferSceneMain: true);
            inputManager?.SetInputCamera(ResolveInputCamera());
            runtimeSessionInitialized = true;
        }

        public void ResetRuntimeSessionState()
        {
            runtimeSessionInitialized = false;
            projectLifetimeScope = null;
            visualPoolService?.ReturnAll();

            var managedEnvironments = GetComponentsInChildren<EnvironmentContext>(true);
            for (int i = 0; i < managedEnvironments.Length; i++)
            {
                DestroyEnvironmentInstance(managedEnvironments[i]);
            }

            environmentInstance = null;
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
                environmentInstance = SpawnEnvironment(resolvedTheme.EnvironmentPrefab, resolvedTheme);
            }

            environmentInstance = CleanupDuplicateEnvironments(resolvedTheme, environmentInstance);

            if (environmentInstance != null)
            {
                ConfigureEnvironmentInstance(environmentInstance, resolvedTheme);
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
            BlockData injectedDefaultBlockData,
            LevelDatabase injectedLevelDatabase)
        {
            this.injectedThemeDatabase ??= injectedThemeDatabase;
            this.injectedDefaultTheme ??= injectedDefaultTheme;
            this.injectedDefaultBlockData ??= injectedDefaultBlockData;
            this.injectedLevelDatabase ??= injectedLevelDatabase;
        }

        [Inject]
        public void InjectScopedServices(
            IVisualPoolService injectedVisualPoolService,
            IGameFactory injectedGameFactory)
        {
            visualPoolService ??= injectedVisualPoolService;
            gameFactory ??= injectedGameFactory;
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

        private void ResolveSceneComponents(bool addMissingLevelSessionController = false)
        {
            gameManager ??= GetComponent<GameManager>();
            inputManager ??= GetComponent<InputManager>();
            levelSessionController ??= GetComponent<LevelSessionController>();
            gameSceneHudView ??= FindFirstObjectByType<GameSceneHudView>(FindObjectsInactive.Include);

            if (levelSessionController == null && addMissingLevelSessionController)
            {
                levelSessionController = gameObject.AddComponent<LevelSessionController>();
            }
        }

        private void ApplyRuntimeVolumePolicyIfNeeded()
        {
            if (!Application.isPlaying
                || !optimizeGlobalVolumeForMobile
                || !Application.isMobilePlatform)
            {
                return;
            }

            DisableMobileExpensiveVolumeEffects(runtimeMobileVolumeProfile);
        }

        private static void DisableMobileExpensiveVolumeEffects(VolumeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            DisableVolumeComponent<MotionBlur>(profile);
            DisableVolumeComponent<DepthOfField>(profile);
            DisableVolumeComponent<ChromaticAberration>(profile);
            DisableVolumeComponent<LensDistortion>(profile);
        }

        private static void DisableVolumeComponent<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var component) && component != null)
            {
                component.active = false;
            }
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

        private EnvironmentContext SpawnEnvironment(EnvironmentContext environmentPrefab, Theme resolvedTheme)
        {
            if (environmentPrefab == null)
            {
                return null;
            }

            if (Application.isPlaying)
            {
                var runtimeEnvironmentHostRoot = ResolveEnvironmentHostRoot();
                var childScope = Instantiate(environmentPrefab, runtimeEnvironmentHostRoot, false);
                if (childScope == null)
                {
                    return null;
                }

                childScope.name = environmentPrefab.gameObject.name;
                childScope.transform.localPosition = Vector3.zero;
                childScope.transform.localRotation = Quaternion.identity;
                childScope.ApplyEditorContext(
                    ResolveThemeDatabase(),
                    resolvedTheme,
                    ResolveDefaultBlockData());

                if (childScope.Container == null)
                {
                    childScope.Build();
                }

                return childScope;
            }

            var environmentHostRoot = ResolveEnvironmentHostRoot();
            GameObject spawnedGameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                spawnedGameObject = UnityEditor.PrefabUtility.InstantiatePrefab(environmentPrefab.gameObject, gameObject.scene) as GameObject;
            }
            else
            {
                spawnedGameObject = Instantiate(environmentPrefab.gameObject, environmentHostRoot, false);
            }
#else
            spawnedGameObject = Instantiate(environmentPrefab.gameObject, environmentHostRoot, false);
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

        private void ConfigureEnvironmentInstance(EnvironmentContext resolvedEnvironment, Theme resolvedTheme)
        {
            if (resolvedEnvironment == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                if (resolvedEnvironment.Container == null)
                {
                    resolvedEnvironment.Build();
                }

                return;
            }

            resolvedTheme ??= resolvedEnvironment.ResolveTheme();
            resolvedEnvironment.ApplyEditorContext(
                ResolveThemeDatabase(),
                resolvedTheme,
                ResolveDefaultBlockData());
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

            if (resolvedTheme != null)
            {
                var candidateTheme = candidate.ResolveTheme();
                if (candidateTheme != null && candidateTheme != resolvedTheme)
                {
                    return false;
                }

                if (Application.isPlaying && candidateTheme == null)
                {
                    return false;
                }
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

        private BlockData ResolveDefaultBlockData()
        {
            if (injectedDefaultBlockData != null)
            {
                return injectedDefaultBlockData;
            }

            var projectScope = ResolveProjectLifetimeScope();
            if (projectScope != null && projectScope.DefaultBlockData != null)
            {
                return projectScope.DefaultBlockData;
            }

#if UNITY_EDITOR
            return FindFirstAsset<BlockData>();
#else
            return null;
#endif
        }

        private LevelDatabase ResolveLevelDatabase()
        {
            if (injectedLevelDatabase != null)
            {
                return injectedLevelDatabase;
            }

            var projectScope = ResolveProjectLifetimeScope();
            if (projectScope != null && projectScope.LevelDatabase != null)
            {
                return projectScope.LevelDatabase;
            }

#if UNITY_EDITOR
            return FindFirstAsset<LevelDatabase>();
#else
            return null;
#endif
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

        private void ResolveRuntimeContainerDependencies()
        {
            if (!Application.isPlaying || Container == null)
            {
                return;
            }

            Container.Inject(this);

            if (gameManager != null)
            {
                Container.Inject(gameManager);
            }

            if (inputManager != null)
            {
                Container.Inject(inputManager);
            }

            if (levelSessionController != null)
            {
                Container.Inject(levelSessionController);
            }

            try
            {
                visualPoolService ??= Container.Resolve<IVisualPoolService>();
            }
            catch
            {
            }

            try
            {
                gameFactory ??= Container.Resolve<IGameFactory>();
            }
            catch
            {
            }
        }

        private static void DestroyEnvironmentInstance(EnvironmentContext candidate)
        {
            if (candidate == null || candidate.gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                candidate.Dispose();
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(candidate.gameObject);
                return;
            }
#endif
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
