using PixelFlow.Runtime.LevelEditing;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PixelFlow.Runtime.Composition
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class EnvironmentLifetimeScope : LifetimeScope
    {
        protected override LifetimeScope FindParent()
        {
            var current = transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent<LifetimeScope>(out var parentScope))
                {
                    return parentScope;
                }

                current = current.parent;
            }

            return null;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            var environmentContext = this as EnvironmentContext ?? GetComponent<EnvironmentContext>();
            if (environmentContext == null)
            {
                return;
            }

            environmentContext.ResolveMissingReferences();
            builder.RegisterBuildCallback(container => container.Inject(environmentContext));
        }
    }
}
