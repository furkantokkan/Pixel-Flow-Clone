using PixelFlow.Runtime.Bullets;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace PixelFlow.Runtime.Pooling
{
    public interface IVisualPoolService
    {
        int ActivePigCount { get; }
        int ActiveBlockCount { get; }
        int ActiveBulletCount { get; }

        void ConfigureRoots(
            Transform pigRoot = null,
            Transform blockRoot = null,
            Transform bulletRoot = null);

        PigController RentPig(Transform parent = null, bool worldPositionStays = false);
        BlockVisual RentBlock(Transform parent = null, bool worldPositionStays = false);
        BulletController RentBullet(Transform parent = null, bool worldPositionStays = false);
        void ReturnPig(PigController pig);
        void ReturnBlock(BlockVisual block);
        void ReturnBullet(BulletController bullet);
        void ReturnAll();
        UniTask PrewarmBulletsAsync(int desiredCount, int batchSize, CancellationToken cancellationToken = default);
    }
}
