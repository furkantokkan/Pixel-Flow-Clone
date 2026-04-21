using System;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerCollaboratorContext
    {
        public GameManagerQueueRuntimeState QueueState { get; set; }
        public Func<EnvironmentContext> EnvironmentProvider { get; set; }
        public Func<int> QueueCapacityProvider { get; set; }
        public Action TriggerLevelFail { get; set; }
        public Action DispatchBurstPigs { get; set; }
        public Action OutcomeStateChanged { get; set; }
        public Action<PigController> UnregisterTrackedPig { get; set; }
        public Func<PigQueueEntry, int, PigController> SpawnPendingPig { get; set; }
        public Func<Vector3> ResolveTrayEquipPosition { get; set; }
        public Func<bool, PigController> DispatchNextPig { get; set; }
        public GameObject Owner { get; set; }
        public Func<Camera> GameplayCameraProvider { get; set; }
    }
}
