using System.Collections.Generic;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Levels
{
    internal sealed class LevelRuntimeSpawnResult
    {
        public LevelRuntimeSpawnResult(
            Transform boardRoot,
            Transform deckRoot,
            List<BlockVisual> spawnedBlocks,
            List<BlockVisual> targetBlocks,
            List<PigController> spawnedPigs,
            List<PigController>[] waitingLanes)
        {
            BoardRoot = boardRoot;
            DeckRoot = deckRoot;
            SpawnedBlocks = spawnedBlocks;
            TargetBlocks = targetBlocks;
            SpawnedPigs = spawnedPigs;
            WaitingLanes = waitingLanes;
        }

        public Transform BoardRoot { get; }
        public Transform DeckRoot { get; }
        public List<BlockVisual> SpawnedBlocks { get; }
        public List<BlockVisual> TargetBlocks { get; }
        public List<PigController> SpawnedPigs { get; }
        public List<PigController>[] WaitingLanes { get; }
    }
}
