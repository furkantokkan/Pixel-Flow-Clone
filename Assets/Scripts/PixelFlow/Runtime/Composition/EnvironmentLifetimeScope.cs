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
            // GameSceneContext constructs EnvironmentContext explicitly after the theme is resolved.
            // Keeping this scope parentless avoids scene bootstrap order races inside VContainer.
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
            builder.RegisterComponent(environmentContext);
        }
    }
}
