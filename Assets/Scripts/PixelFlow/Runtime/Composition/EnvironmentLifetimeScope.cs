using PixelFlow.Runtime.LevelEditing;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentLifetimeScope : LifetimeScope
    {
        [SerializeField] private EnvironmentContext environmentContext;

        public EnvironmentContext EnvironmentContext => environmentContext;

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
            return GetComponentInParent<GameSceneLifetimeScope>()
                ?? LifetimeScope.Find<ProjectLifetimeScope>();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            ResolveReferences();
            if (environmentContext == null)
            {
                return;
            }

            environmentContext.ResolveMissingReferences();
            builder.RegisterComponent(environmentContext);
        }

        private void ResolveReferences()
        {
            environmentContext ??= GetComponent<EnvironmentContext>();
            environmentContext?.ResolveMissingReferences();
        }
    }
}
