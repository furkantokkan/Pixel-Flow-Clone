using PixelFlow.Runtime.Audio;
using PixelFlow.Runtime.Data;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [DisallowMultipleComponent]
    public sealed partial class ProjectLifetimeScope : LifetimeScope
    {
        private static ProjectLifetimeScope instance;

        [SerializeField] private ThemeDatabase themeDatabase;
        [SerializeField] private Theme defaultTheme;
        [SerializeField] private BlockData defaultBlockData;
        [SerializeField] private ProjectRuntimeSettings runtimeSettings;
        [SerializeField] private LevelDatabase levelDatabase;

        public ThemeDatabase ThemeDatabase => themeDatabase;
        public Theme DefaultTheme => defaultTheme;
        public BlockData DefaultBlockData => defaultBlockData;
        public ProjectRuntimeSettings RuntimeSettings => runtimeSettings;
        public LevelDatabase LevelDatabase => levelDatabase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
        }

        private void Reset()
        {
            EditorAutoAssignAssets();
        }

        private void OnValidate()
        {
            EditorAutoAssignAssets();
            runtimeSettings?.Normalize();
        }

        protected override void Awake()
        {
            if (Application.isPlaying)
            {
                if (instance != null && instance != this)
                {
                    Destroy(gameObject);
                    return;
                }

                instance = this;
            }

            base.Awake();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DestroyDuplicateRuntimeInstances();
        }

        protected override void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnDestroy();
        }

        private void DestroyDuplicateRuntimeInstances()
        {
            var scopes = Resources.FindObjectsOfTypeAll<ProjectLifetimeScope>();
            for (int i = 0; i < scopes.Length; i++)
            {
                var candidate = scopes[i];
                if (candidate == null
                    || candidate == this
                    || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Destroy(candidate.gameObject);
            }
        }

        protected override void Configure(IContainerBuilder builder)
        {
            PrepareRegistrations();
            builder.RegisterEntryPoint<SoundService>(Lifetime.Singleton);

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

            if (runtimeSettings != null)
            {
                builder.RegisterInstance(runtimeSettings);
            }

            if (levelDatabase != null)
            {
                builder.RegisterInstance(levelDatabase);
            }
        }

        private void PrepareRegistrations()
        {
            EditorAutoAssignAssets();

            if (runtimeSettings == null)
            {
                runtimeSettings = ScriptableObject.CreateInstance<ProjectRuntimeSettings>();
            }

            runtimeSettings.Normalize();
        }

        partial void EditorAutoAssignAssets();
    }
}
