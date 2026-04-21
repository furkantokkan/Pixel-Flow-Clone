using System.Collections.Generic;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Tray;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerQueueRuntimeState
    {
        public List<PigController> QueuedPigs { get; } = new();
        public List<PigController> HoldingPigs { get; } = new();
        public List<TrayController> TrayStackVisuals { get; } = new();
        public HashSet<PigController> PigsUsingTrayStack { get; } = new();
        public List<List<PigController>> WaitingLanes { get; } = new();
        public Dictionary<PigController, int> PigLaneLookup { get; } = new();
        public Dictionary<PigController, int> PigHoldingLookup { get; } = new();
        public List<PigController> ActiveConveyorPigs { get; } = new();
    }
}
