using PixelFlow.Runtime.Bullets;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Pooling;
using PixelFlow.Runtime.Visuals;

namespace PixelFlow.Runtime.Factories
{
    public sealed class GameFactory : IGameFactory
    {
        private readonly IVisualPoolService visualPool;

        public GameFactory(IVisualPoolService visualPool)
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
            block.Destroyed -= HandleBlockDestroyed;
            block.Destroyed += HandleBlockDestroyed;
            block.ConfigureBlock(request.Color, request.ToneIndex);
            return block;
        }

        public BulletController CreateBullet(BulletSpawnRequest request)
        {
            var bullet = visualPool?.RentBullet(request.Placement.Parent, request.Placement.WorldPositionStays);
            if (bullet == null)
            {
                return null;
            }

            request.Placement.ApplyPoseTo(bullet.transform);
            bullet.Completed -= HandleBulletCompleted;
            bullet.Completed += HandleBulletCompleted;
            bullet.Launch(request.Color, request.Target, request.TargetBlock, request.Speed, request.MaxLifetime);
            return bullet;
        }

        public void ReleasePig(PigController pig)
        {
            visualPool?.ReturnPig(pig);
        }

        public void ReleaseBlock(BlockVisual block)
        {
            if (block != null)
            {
                block.Destroyed -= HandleBlockDestroyed;
            }

            visualPool?.ReturnBlock(block);
        }

        public void ReleaseBullet(BulletController bullet)
        {
            if (bullet != null)
            {
                bullet.Completed -= HandleBulletCompleted;
            }

            visualPool?.ReturnBullet(bullet);
        }

        private void HandleBlockDestroyed(BlockVisual block)
        {
            ReleaseBlock(block);
        }

        private void HandleBulletCompleted(BulletController bullet)
        {
            ReleaseBullet(bullet);
        }
    }
}
