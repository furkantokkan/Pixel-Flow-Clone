using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Managers;
using PixelFlow.Runtime.Visuals;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [DisallowMultipleComponent]
    public sealed class GameSceneLifetimeScope : LifetimeScope
    {
        [SerializeField] private SceneContext sceneContext;
        [SerializeField] private VisualPool visualPool;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private InputManager inputManager;

        public SceneContext SceneContext => sceneContext;

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        protected override LifetimeScope FindParent()
        {
            return LifetimeScope.Find<ProjectLifetimeScope>();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            ResolveReferences();

            RegisterComponent(builder, sceneContext);
            RegisterComponent(builder, visualPool);
            RegisterComponent(builder, gameManager);
            RegisterComponent(builder, inputManager);
        }

        private void ResolveReferences()
        {
            sceneContext ??= GetComponent<SceneContext>();
            visualPool ??= GetComponent<VisualPool>();
            gameManager ??= GetComponent<GameManager>();
            inputManager ??= GetComponent<InputManager>();
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
    }
}
