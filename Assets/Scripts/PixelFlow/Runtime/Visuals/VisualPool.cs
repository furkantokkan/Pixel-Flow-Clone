using Core.Pool;
using PixelFlow.Runtime.Composition;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.Pigs;
using UnityEngine;
using VContainer;

namespace PixelFlow.Runtime.Visuals
{
    [DisallowMultipleComponent]
    public sealed class VisualPool : MonoBehaviour
    {
        [SerializeField] private bool useProjectDefaults = true;
        [SerializeField] private PigController pigPrefab;
        [SerializeField] private BlockVisual blockPrefab;
        [SerializeField] private Transform pigPoolRoot;
        [SerializeField] private Transform blockPoolRoot;
        [SerializeField, Min(0)] private int pigPrewarmCount = 8;
        [SerializeField, Min(0)] private int blockPrewarmCount = 32;
        [SerializeField, Min(1)] private int pigMaxSize = 64;
        [SerializeField, Min(1)] private int blockMaxSize = 256;

        private ComponentPool<PigController> pigPool;
        private ComponentPool<BlockVisual> blockPool;

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

        [Inject]
        public void InjectProjectSettings(ProjectRuntimeSettings settings)
        {
            ApplyProjectSettings(settings);
        }

        public void ApplyProjectSettings(ProjectRuntimeSettings settings)
        {
            if (!useProjectDefaults || settings == null)
            {
                return;
            }

            var requiresRebuild = false;
            requiresRebuild |= ApplyPrefabOverride(ref pigPrefab, settings.PigPrefab);
            requiresRebuild |= ApplyPrefabOverride(ref blockPrefab, settings.BlockPrefab);
            requiresRebuild |= ApplyValueOverride(ref pigPrewarmCount, Mathf.Max(0, settings.PigPrewarmCount));
            requiresRebuild |= ApplyValueOverride(ref blockPrewarmCount, Mathf.Max(0, settings.BlockPrewarmCount));
            requiresRebuild |= ApplyValueOverride(ref pigMaxSize, Mathf.Max(1, settings.PigMaxSize));
            requiresRebuild |= ApplyValueOverride(ref blockMaxSize, Mathf.Max(1, settings.BlockMaxSize));

            if (requiresRebuild && (pigPool != null || blockPool != null))
            {
                RebuildPools();
            }
        }

        public void Configure(
            PigController pigPrefabOverride,
            BlockVisual blockPrefabOverride,
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

        public PigController RentPig(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return pigPool?.Rent(parent, worldPositionStays);
        }

        public PigController RentPig(PigColor color, int ammo, Transform parent = null, bool worldPositionStays = false)
        {
            var pig = RentPig(parent, worldPositionStays);
            pig?.ConfigurePig(color, ammo);
            return pig;
        }

        public BlockVisual RentBlock(Transform parent = null, bool worldPositionStays = false)
        {
            EnsureInitialized();
            return blockPool?.Rent(parent, worldPositionStays);
        }

        public BlockVisual RentBlock(PigColor color, Transform parent = null, bool worldPositionStays = false)
        {
            var block = RentBlock(parent, worldPositionStays);
            block?.ConfigureBlock(color);
            return block;
        }

        public void ReturnPig(PigController pig)
        {
            pigPool?.Return(pig);
        }

        public void ReturnBlock(BlockVisual block)
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
                pigPool = new ComponentPool<PigController>(
                    pigPrefab,
                    pigPoolRoot,
                    Mathf.Max(1, pigPrewarmCount),
                    Mathf.Max(pigPrewarmCount, pigMaxSize));
            }

            if (blockPool == null && blockPrefab != null)
            {
                blockPool = new ComponentPool<BlockVisual>(
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

        private static bool ApplyPrefabOverride<TComponent>(ref TComponent currentValue, TComponent nextValue)
            where TComponent : Component
        {
            if (nextValue == null || currentValue == nextValue)
            {
                return false;
            }

            currentValue = nextValue;
            return true;
        }

        private static bool ApplyValueOverride(ref int currentValue, int nextValue)
        {
            if (currentValue == nextValue)
            {
                return false;
            }

            currentValue = nextValue;
            return true;
        }
    }
}
