using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;

namespace PixelFlow.Runtime.Factories
{
    public sealed class GameFactory : IGameFactory
    {
        private readonly VisualPool visualPool;

        public GameFactory(VisualPool visualPool)
        {
            this.visualPool = visualPool;
        }

        public PigController CreatePig(PigSpawnRequest request)
        {
            var pig = visualPool?.RentPig(request.Placement.Parent, request.Placement.WorldPositionStays);
            if (pig == null)
            {
                return null;
            }

            request.Placement.ApplyPoseTo(pig.transform);
            pig.ConfigurePig(request.Color, request.Ammo, request.Direction);
            return pig;
        }

        public BlockVisual CreateBlock(BlockSpawnRequest request)
        {
            var block = visualPool?.RentBlock(request.Placement.Parent, request.Placement.WorldPositionStays);
            if (block == null)
            {
                return null;
            }

            request.Placement.ApplyPoseTo(block.transform);
            block.ConfigureBlock(request.Color);
            return block;
        }

        public void ReleasePig(PigController pig)
        {
            visualPool?.ReturnPig(pig);
        }

        public void ReleaseBlock(BlockVisual block)
        {
            visualPool?.ReturnBlock(block);
        }
    }
}
