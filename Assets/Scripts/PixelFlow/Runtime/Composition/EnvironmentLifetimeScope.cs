using PixelFlow.Runtime.LevelEditing;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentLifetimeScope : LifetimeScope
    {
        protected override LifetimeScope FindParent()
        {
            return GetComponentInParent<GameSceneContext>()
                ?? LifetimeScope.Find<ProjectLifetimeScope>();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            var environmentContext = GetComponent<EnvironmentContext>();
            if (environmentContext == null)
            {
                return;
            }

            environmentContext.ResolveMissingReferences();
            builder.RegisterComponent(environmentContext);
        }
    }
}
