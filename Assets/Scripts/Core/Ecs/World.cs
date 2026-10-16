using System;
using System.Collections.Generic;

namespace Unruled.Core.Ecs
{
    /// <summary>Allocates entity ids and owns the component stores, so destroying an entity clears all of them.</summary>
    public sealed class World
    {
        // Positive = generation of the live entity; negative = generation of the last entity destroyed at that index.
        private int[] _generations;
        private int[] _freeIndices;
        private int _freeCount;
        private int _nextIndex;
        private readonly List<IComponentStore> _stores = new List<IComponentStore>();

        public int AliveCount { get; private set; }

        public World(int initialCapacity = 256)
        {
            if (initialCapacity < 1) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _generations = new int[initialCapacity];
            _freeIndices = new int[initialCapacity];
        }

        public ComponentStore<T> CreateStore<T>(int initialCapacity = 64) where T : struct
        {
            var store = new ComponentStore<T>(initialCapacity);
            _stores.Add(store);
            return store;
        }

        /// <summary>Reuses the most recently freed index first (LIFO), which is deterministic for a given op sequence.</summary>
        public EntityId Create()
        {
            int index;
            if (_freeCount > 0)
            {
                index = _freeIndices[--_freeCount];
            }
            else
            {
                index = _nextIndex++;
                if (index == _generations.Length)
                {
                    Array.Resize(ref _generations, index * 2);
                    Array.Resize(ref _freeIndices, index * 2);
                }
            }

            int generation = -_generations[index] + 1;
            _generations[index] = generation;
            AliveCount++;
            return new EntityId(index, generation);
        }

        public bool IsAlive(EntityId entity) =>
            !entity.IsNull && entity.Index >= 0 && entity.Index < _nextIndex &&
            _generations[entity.Index] == entity.Generation;

        public void Destroy(EntityId entity)
        {
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive.");

            for (int i = 0; i < _stores.Count; i++)
                _stores[i].RemoveIfPresent(entity.Index);

            _generations[entity.Index] = -entity.Generation;
            _freeIndices[_freeCount++] = entity.Index;
            AliveCount--;
        }
    }
}
