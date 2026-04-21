using System;
using PixelFlow.Runtime.Audio;
using PixelFlow.Runtime.Data;
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
        private readonly ISoundService soundService;

        public GameManagerCollaboratorFactory(IGameFactory gameFactory, ISoundService soundService)
        {
            this.gameFactory = gameFactory;
            this.soundService = soundService;
        }

        internal GameManagerCollaborators Create(GameManagerCollaboratorContext context)
        {
            if (context == null || context.QueueState == null)
            {
                return default;
            }

            var targetingCoordinator = new GameManagerTargetingCoordinator(context.QueueState, soundService);
            GameManagerBurstCoordinator burstCoordinator = null;

            var trayQueueCoordinator = new GameManagerTrayQueueCoordinator(
                context.QueueState,
                context.EnvironmentProvider,
                context.QueueCapacityProvider,
                targetingCoordinator.RegisterConveyorPig,
                targetingCoordinator.UnregisterConveyorPig,
                pig =>
                {
                    context.UnregisterTrackedPig?.Invoke(pig);
                    gameFactory?.ReleasePig(pig);
                },
                context.TriggerLevelFail,
                context.DispatchBurstPigs,
                context.OutcomeStateChanged,
                context.SpawnPendingPig,
                context.ResolveTrayEquipPosition,
                soundService);

            var visibilityCoordinator = new PigRendererVisibilityCoordinator(
                context.Owner,
                context.GameplayCameraProvider);
            burstCoordinator = new GameManagerBurstCoordinator(
                context.DispatchNextPig,
                context.QueueState);

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
