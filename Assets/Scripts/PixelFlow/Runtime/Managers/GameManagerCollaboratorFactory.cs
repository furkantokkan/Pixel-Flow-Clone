using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Factories;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Tray;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerCollaboratorFactory
    {
        private readonly IGameFactory gameFactory;

        public GameManagerCollaboratorFactory(IGameFactory gameFactory)
        {
            this.gameFactory = gameFactory;
        }

        internal GameManagerCollaborators Create(
            List<PigController> queuedPigs,
            List<PigController> holdingPigs,
            List<TrayController> trayStackVisuals,
            HashSet<PigController> pigsUsingTrayStack,
            List<List<PigController>> waitingLanes,
            Dictionary<PigController, int> pigLaneLookup,
            Dictionary<PigController, int> pigHoldingLookup,
            List<PigController> activeConveyorPigs,
            Func<EnvironmentContext> environmentProvider,
            Func<int> queueCapacityProvider,
            Action triggerLevelFail,
            Action tryDispatchBurstPigs,
            Func<Vector3> resolveTrayEquipPosition,
            Func<bool, PigController> dispatchNextPig,
            GameObject owner,
            Func<Camera> gameplayCameraProvider)
        {
            var targetingCoordinator = new GameManagerTargetingCoordinator(activeConveyorPigs);
            GameManagerBurstCoordinator burstCoordinator = null;

            var trayQueueCoordinator = new GameManagerTrayQueueCoordinator(
                queuedPigs,
                holdingPigs,
                trayStackVisuals,
                pigsUsingTrayStack,
                waitingLanes,
                pigLaneLookup,
                pigHoldingLookup,
                environmentProvider,
                queueCapacityProvider,
                () => targetingCoordinator.ActiveConveyorPigs,
                targetingCoordinator.RegisterConveyorPig,
                targetingCoordinator.UnregisterConveyorPig,
                pig => gameFactory?.ReleasePig(pig),
                triggerLevelFail,
                tryDispatchBurstPigs,
                resolveTrayEquipPosition);

            var visibilityCoordinator = new PigRendererVisibilityCoordinator(owner, gameplayCameraProvider);
            burstCoordinator = new GameManagerBurstCoordinator(
                dispatchNextPig,
                () => waitingLanes,
                () => holdingPigs,
                () => targetingCoordinator.ActiveConveyorPigs);

            return new GameManagerCollaborators(
                targetingCoordinator,
                trayQueueCoordinator,
                visibilityCoordinator,
                burstCoordinator);
        }
    }

    internal readonly struct GameManagerCollaborators
    {
        public GameManagerCollaborators(
            GameManagerTargetingCoordinator targetingCoordinator,
            GameManagerTrayQueueCoordinator trayQueueCoordinator,
            PigRendererVisibilityCoordinator visibilityCoordinator,
            GameManagerBurstCoordinator burstCoordinator)
        {
            TargetingCoordinator = targetingCoordinator;
            TrayQueueCoordinator = trayQueueCoordinator;
            VisibilityCoordinator = visibilityCoordinator;
            BurstCoordinator = burstCoordinator;
        }

        public GameManagerTargetingCoordinator TargetingCoordinator { get; }
        public GameManagerTrayQueueCoordinator TrayQueueCoordinator { get; }
        public PigRendererVisibilityCoordinator VisibilityCoordinator { get; }
        public GameManagerBurstCoordinator BurstCoordinator { get; }
    }
}
