using System;
using Core.Pool;
using PixelFlow.Runtime.Bullets;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Pooling
{
    public sealed class VisualPoolService : IVisualPoolService, IDisposable
    {
        private readonly GameSceneContext sceneContext;
        private readonly PigController pigPrefab;
        private readonly BlockVisual blockPrefab;
        private readonly BulletController bulletPrefab;
        private readonly int pigPrewarmCount;
        private readonly int blockPrewarmCount;
        private readonly int bulletPrewarmCount;
        private readonly int pigMaxSize;
        private readonly int blockMaxSize;
        private readonly int bulletMaxSize;

        private ComponentPool<PigController> pigPool;
        private ComponentPool<BlockVisual> blockPool;
        private ComponentPool<BulletController> bulletPool;
        private Transform pigPoolRoot;
        private Transform blockPoolRoot;
        private Transform bulletPoolRoot;
        private Transform generatedRoot;
        private bool prewarmed;

        public VisualPoolService(ProjectRuntimeSettings settings, GameSceneContext sceneContext)
        {
            this.sceneContext = sceneContext;

            pigPrefab = settings != null ? settings.PigPrefab : null;
            blockPrefab = settings != null ? settings.BlockPrefab : null;
            bulletPrefab = settings != null ? settings.BulletPrefab : null;
            pigPrewarmCount = settings != null ? Mathf.Max(0, settings.PigPrewarmCount) : 0;
            blockPrewarmCount = settings != null ? Mathf.Max(0, settings.BlockPrewarmCount) : 0;
            bulletPrewarmCount = settings != null ? Mathf.Max(0, settings.BulletPrewarmCount) : 0;
            pigMaxSize = settings != null ? Mathf.Max(1, settings.PigMaxSize) : 1;
            blockMaxSize = settings != null ? Mathf.Max(1, settings.BlockMaxSize) : 1;
            bulletMaxSize = settings != null ? Mathf.Max(1, settings.BulletMaxSize) : 1;
        }

        public int ActivePigCount => pigPool?.ActiveCount ?? 0;
        public int ActiveBlockCount => blockPool?.ActiveCount ?? 0;
        public int ActiveBulletCount => bulletPool?.ActiveCount ?? 0;

        public void ConfigureRoots(
            Transform pigRoot = null,
            Transform blockRoot = null,
            Transform bulletRoot = null)
        {
            var rootsChanged = false;
            rootsChanged |= ApplyRoot(ref pigPoolRoot, pigRoot);
            rootsChanged |= ApplyRoot(ref blockPoolRoot, blockRoot);
            rootsChanged |= ApplyRoot(ref bulletPoolRoot, bulletRoot);

            if (rootsChanged && (pigPool != null || blockPool != null || bulletPool != null))
            {
                RebuildPools();
            }
        }

        public PigController RentPig(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return pigPool?.Rent(parent, worldPositionStays);
        }

        public BlockVisual RentBlock(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return blockPool?.Rent(parent, worldPositionStays);
        }

        public BulletController RentBullet(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return bulletPool?.Rent(parent, worldPositionStays);
        }

        public void ReturnPig(PigController pig)
        {
            pigPool?.Return(pig);
        }

        public void ReturnBlock(BlockVisual block)
        {
            blockPool?.Return(block);
        }

        public void ReturnBullet(BulletController bullet)
        {
            bulletPool?.Return(bullet);
        }

        public void ReturnAll()
        {
            pigPool?.ReturnAll();
            blockPool?.ReturnAll();
            bulletPool?.ReturnAll();
        }

        public void Dispose()
        {
            ClearPools();
            DestroyGeneratedRoot();
        }

        private void EnsureInitialized()
        {
            if (pigPool == null && pigPrefab != null)
            {
                pigPool = new ComponentPool<PigController>(
                    pigPrefab,
                    ResolvePigRoot(),
                    Mathf.Max(1, pigPrewarmCount),
                    Mathf.Max(pigPrewarmCount, pigMaxSize));
            }

            if (blockPool == null && blockPrefab != null)
            {
                blockPool = new ComponentPool<BlockVisual>(
                    blockPrefab,
                    ResolveBlockRoot(),
                    Mathf.Max(1, blockPrewarmCount),
                    Mathf.Max(blockPrewarmCount, blockMaxSize));
            }

            if (bulletPool == null && bulletPrefab != null)
            {
                bulletPool = new ComponentPool<BulletController>(
                    bulletPrefab,
                    ResolveBulletRoot(),
                    Mathf.Max(1, bulletPrewarmCount),
                    Mathf.Max(bulletPrewarmCount, bulletMaxSize));
            }

            if (!prewarmed)
            {
                pigPool?.Prewarm(pigPrewarmCount);
                blockPool?.Prewarm(blockPrewarmCount);
                bulletPool?.Prewarm(bulletPrewarmCount);
                prewarmed = true;
            }
        }

        private void RebuildPools()
        {
            ClearPools();
            prewarmed = false;
            EnsureInitialized();
        }

        private void ClearPools()
        {
            pigPool?.Clear();
            blockPool?.Clear();
            bulletPool?.Clear();
            pigPool = null;
            blockPool = null;
            bulletPool = null;
        }

        private Transform ResolvePigRoot()
        {
            return pigPoolRoot != null ? pigPoolRoot : EnsureGeneratedChildRoot("Pigs");
        }

        private Transform ResolveBlockRoot()
        {
            return blockPoolRoot != null ? blockPoolRoot : EnsureGeneratedChildRoot("Blocks");
        }

        private Transform ResolveBulletRoot()
        {
            return bulletPoolRoot != null ? bulletPoolRoot : EnsureGeneratedChildRoot("Bullets");
        }

        private Transform EnsureGeneratedChildRoot(string name)
        {
            EnsureGeneratedRoot();

            var child = generatedRoot.Find(name);
            if (child != null)
            {
                return child;
            }

            var childRoot = new GameObject(name).transform;
            childRoot.SetParent(generatedRoot, false);
            return childRoot;
        }

        private void EnsureGeneratedRoot()
        {
            if (generatedRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("__VisualPoolRoot");
            if (sceneContext != null)
            {
                rootObject.transform.SetParent(sceneContext.transform, false);
            }

            generatedRoot = rootObject.transform;
        }

        private void DestroyGeneratedRoot()
        {
            if (generatedRoot == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(generatedRoot.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(generatedRoot.gameObject);
            }

            generatedRoot = null;
        }

        private static bool ApplyRoot(ref Transform currentRoot, Transform nextRoot)
        {
            if (currentRoot == nextRoot)
            {
                return false;
            }

            currentRoot = nextRoot;
            return true;
        }
    }
}
