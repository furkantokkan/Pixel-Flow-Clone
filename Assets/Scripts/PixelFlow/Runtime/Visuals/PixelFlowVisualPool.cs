using Core.Pool;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Pigs;
using UnityEngine;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class PixelFlowVisualPool : MonoBehaviour
    {
        [SerializeField] private PixelFlowPigController pigPrefab;
        [SerializeField] private PixelFlowPoolableVisual blockPrefab;
        [SerializeField] private Transform pigPoolRoot;
        [SerializeField] private Transform blockPoolRoot;
        [SerializeField, Min(0)] private int pigPrewarmCount = 8;
        [SerializeField, Min(0)] private int blockPrewarmCount = 32;
        [SerializeField, Min(1)] private int pigMaxSize = 64;
        [SerializeField, Min(1)] private int blockMaxSize = 256;

        private ComponentPool<PixelFlowPigController> pigPool;
        private ComponentPool<PixelFlowPoolableVisual> blockPool;

        public int ActivePigCount => pigPool?.ActiveCount ?? 0;
        public int ActiveBlockCount => blockPool?.ActiveCount ?? 0;

        private void Awake()
        {
            EnsureInitialized();
            Prewarm();
        }

        private void OnDestroy()
        {
            pigPool?.Clear();
            blockPool?.Clear();
        }

        public void Configure(
            PixelFlowPigController pigPrefabOverride,
            PixelFlowPoolableVisual blockPrefabOverride,
            Transform pigRootOverride = null,
            Transform blockRootOverride = null)
        {
            var requiresRebuild = false;

            if (pigPrefabOverride != null)
            {
                requiresRebuild |= pigPrefab != pigPrefabOverride;
                pigPrefab = pigPrefabOverride;
            }

            if (blockPrefabOverride != null)
            {
                requiresRebuild |= blockPrefab != blockPrefabOverride;
                blockPrefab = blockPrefabOverride;
            }

            if (pigRootOverride != null)
            {
                requiresRebuild |= pigPool != null && pigPoolRoot != pigRootOverride;
                pigPoolRoot = pigRootOverride;
            }

            if (blockRootOverride != null)
            {
                requiresRebuild |= blockPool != null && blockPoolRoot != blockRootOverride;
                blockPoolRoot = blockRootOverride;
            }

            if (requiresRebuild)
            {
                RebuildPools();
            }
        }

        public PixelFlowPigController RentPig(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return pigPool?.Rent(parent, worldPositionStays);
        }

        public PixelFlowPigController RentPig(PigColor color, int ammo, Transform parent = null, bool worldPositionStays = false)
        {
            var pig = RentPig(parent, worldPositionStays);
            pig?.ConfigurePig(color, ammo);
            return pig;
        }

        public PixelFlowPoolableVisual RentBlock(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return blockPool?.Rent(parent, worldPositionStays);
        }

        public PixelFlowPoolableVisual RentBlock(PigColor color, Transform parent = null, bool worldPositionStays = false)
        {
            var block = RentBlock(parent, worldPositionStays);
            block?.ConfigureBlock(color);
            return block;
        }

        public void ReturnPig(PixelFlowPigController pig)
        {
            pigPool?.Return(pig);
        }

        public void ReturnBlock(PixelFlowPoolableVisual block)
        {
            blockPool?.Return(block);
        }

        public void ReturnAll()
        {
            pigPool?.ReturnAll();
            blockPool?.ReturnAll();
        }

        public void Prewarm()
        {
            EnsureInitialized();
            pigPool?.Prewarm(pigPrewarmCount);
            blockPool?.Prewarm(blockPrewarmCount);
        }

        private void EnsureInitialized()
        {
            pigPoolRoot ??= transform;
            blockPoolRoot ??= transform;

            if (pigPool == null && pigPrefab != null)
            {
                pigPool = new ComponentPool<PixelFlowPigController>(
                    pigPrefab,
                    pigPoolRoot,
                    Mathf.Max(1, pigPrewarmCount),
                    Mathf.Max(pigPrewarmCount, pigMaxSize));
            }

            if (blockPool == null && blockPrefab != null)
            {
                blockPool = new ComponentPool<PixelFlowPoolableVisual>(
                    blockPrefab,
                    blockPoolRoot,
                    Mathf.Max(1, blockPrewarmCount),
                    Mathf.Max(blockPrewarmCount, blockMaxSize));
            }
        }

        private void RebuildPools()
        {
            pigPool?.Clear();
            blockPool?.Clear();
            pigPool = null;
            blockPool = null;
            EnsureInitialized();
        }
    }
}
