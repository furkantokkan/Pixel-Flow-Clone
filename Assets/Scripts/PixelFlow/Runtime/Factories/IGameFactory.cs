using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;

namespace PixelFlow.Runtime.Factories
{
    public interface IGameFactory
    {
        PigController CreatePig(PigSpawnRequest request);
        BlockVisual CreateBlock(BlockSpawnRequest request);
        void ReleasePig(PigController pig);
        void ReleaseBlock(BlockVisual block);
    }
}
