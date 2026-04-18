using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Core.Pool
{
    public sealed class ComponentPool<T> where T : Component
    {
        private readonly HashSet<T> activeItems = new();
        private readonly T prefab;
        private readonly Transform hierarchyParent;
        private readonly ObjectPool<T> pool;

        public ComponentPool(T prefab, Transform hierarchyParent = null, int defaultCapacity = 10, int maxSize = 100)
        {
            this.prefab = prefab;
            this.hierarchyParent = hierarchyParent;
            pool = new ObjectPool<T>(
                CreateInstance,
                OnTakeFromPool,
                OnReturnedToPool,
                OnDestroyPoolObject,
                collectionCheck: false,
                defaultCapacity: Mathf.Max(1, defaultCapacity),
                maxSize: Mathf.Max(1, maxSize));
        }

        public int ActiveCount => activeItems.Count;
        public int InactiveCount => pool.CountInactive;

        public T Rent(Transform parentOverride = null, bool worldPositionStays = false)
        {
            var instance = pool.Get();
            activeItems.Add(instance);

            var targetParent = parentOverride != null ? parentOverride : hierarchyParent;
            if (targetParent != null)
            {
                instance.transform.SetParent(targetParent, worldPositionStays);
            }

            return instance;
        }

        public void Return(T instance)
        {
            if (instance == null || !activeItems.Remove(instance))
            {
                return;
            }

            pool.Release(instance);
        }

        public void ReturnAll()
        {
            if (activeItems.Count == 0)
            {
                return;
            }

            var snapshot = new List<T>(activeItems);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Return(snapshot[i]);
            }
        }

        public void Prewarm(int count)
        {
            if (count <= 0)
            {
                return;
            }

            var buffer = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                buffer.Add(Rent());
            }

            for (int i = 0; i < buffer.Count; i++)
            {
                Return(buffer[i]);
            }
        }

        public void Clear()
        {
            ReturnAll();
            pool.Clear();
        }

        private T CreateInstance()
        {
            var instance = Object.Instantiate(prefab);
            if (hierarchyParent != null)
            {
                instance.transform.SetParent(hierarchyParent, false);
            }

            instance.gameObject.SetActive(false);
            return instance;
        }

        private static void OnTakeFromPool(T instance)
        {
            instance.gameObject.SetActive(true);
            NotifyPoolables(instance, isSpawned: true);
        }

        private void OnReturnedToPool(T instance)
        {
            NotifyPoolables(instance, isSpawned: false);

            if (hierarchyParent != null)
            {
                instance.transform.SetParent(hierarchyParent, false);
            }

            instance.gameObject.SetActive(false);
        }

        private static void OnDestroyPoolObject(T instance)
        {
            if (instance != null)
            {
                Object.Destroy(instance.gameObject);
            }
        }

        private static void NotifyPoolables(T instance, bool isSpawned)
        {
            if (instance == null)
            {
                return;
            }

            var components = ListPool<Component>.Get();
            instance.GetComponents(components);

            try
            {
                for (int i = 0; i < components.Count; i++)
                {
                    if (components[i] is not IPoolable poolable)
                    {
                        continue;
                    }

                    if (isSpawned)
                    {
                        poolable.OnSpawned();
                    }
                    else
                    {
                        poolable.OnDespawned();
                    }
                }
            }
            finally
            {
                ListPool<Component>.Release(components);
            }
        }
    }
}
