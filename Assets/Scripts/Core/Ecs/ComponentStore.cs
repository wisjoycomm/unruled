using System;

namespace Unruled.Core.Ecs
{
    internal interface IComponentStore
    {
        void RemoveIfPresent(int entityIndex);
        void Clear();
    }

    /// <summary>
    /// Sparse set of struct components kept sorted by entity index, so iteration order is ascending and
    /// deterministic. Add/Remove shift the dense arrays (O(n)); Get and iteration are O(1) per element.
    /// </summary>
    public sealed class ComponentStore<T> : IComponentStore where T : struct
    {
        private int[] _sparse; // entity index -> dense slot + 1 (0 = absent)
        private EntityId[] _entities;
        private T[] _data;

        public int Count { get; private set; }

        public ComponentStore(int initialCapacity = 64)
        {
            if (initialCapacity < 1) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _sparse = new int[initialCapacity];
            _entities = new EntityId[initialCapacity];
            _data = new T[initialCapacity];
        }

        public bool Has(EntityId entity) =>
            entity.Index >= 0 && entity.Index < _sparse.Length && _sparse[entity.Index] != 0 &&
            _entities[_sparse[entity.Index] - 1] == entity;

        public void Add(EntityId entity, in T value)
        {
            if (entity.IsNull) throw new ArgumentException("Cannot add a component to the null entity.", nameof(entity));
            EnsureSparse(entity.Index);
            if (_sparse[entity.Index] != 0)
                throw new InvalidOperationException($"Entity index {entity.Index} already has {typeof(T).Name}.");

            if (Count == _data.Length)
            {
                Array.Resize(ref _entities, Count * 2);
                Array.Resize(ref _data, Count * 2);
            }

            int slot = Count;
            while (slot > 0 && _entities[slot - 1].Index > entity.Index)
                slot--;
            if (slot < Count)
            {
                Array.Copy(_entities, slot, _entities, slot + 1, Count - slot);
                Array.Copy(_data, slot, _data, slot + 1, Count - slot);
            }

            _entities[slot] = entity;
            _data[slot] = value;
            Count++;
            ReindexFrom(slot);
        }

        public void Remove(EntityId entity)
        {
            if (!Has(entity))
                throw new InvalidOperationException($"Entity {entity} has no {typeof(T).Name}.");
            RemoveAtSlot(_sparse[entity.Index] - 1);
        }

        /// <summary>
        /// Adds or replaces the component, dropping one held by an older generation at the same index: a client's
        /// mirror of a server world applies what the server sends this way (see <c>Net.PlayerView</c>).
        /// </summary>
        internal void Set(EntityId entity, in T value)
        {
            if (Has(entity))
            {
                _data[_sparse[entity.Index] - 1] = value;
                return;
            }
            ((IComponentStore)this).RemoveIfPresent(entity.Index);
            Add(entity, value);
        }

        /// <summary>Reference to the component of a live entity. Throws on a missing component or stale id.</summary>
        public ref T Get(EntityId entity)
        {
            if (!Has(entity))
                throw new InvalidOperationException($"Entity {entity} has no {typeof(T).Name}.");
            return ref _data[_sparse[entity.Index] - 1];
        }

        /// <summary>Entity at dense position <paramref name="i"/> (0..Count-1), ascending by index.</summary>
        public EntityId EntityAt(int i)
        {
            if ((uint)i >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(i));
            return _entities[i];
        }

        /// <summary>Component at dense position <paramref name="i"/> (0..Count-1), ascending by entity index.</summary>
        public ref T At(int i)
        {
            if ((uint)i >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(i));
            return ref _data[i];
        }

        void IComponentStore.RemoveIfPresent(int entityIndex)
        {
            if (entityIndex < _sparse.Length && _sparse[entityIndex] != 0)
                RemoveAtSlot(_sparse[entityIndex] - 1);
        }

        void IComponentStore.Clear()
        {
            Array.Clear(_sparse, 0, _sparse.Length);
            Array.Clear(_entities, 0, Count);
            Array.Clear(_data, 0, Count);
            Count = 0;
        }

        private void RemoveAtSlot(int slot)
        {
            _sparse[_entities[slot].Index] = 0;
            Count--;
            if (slot < Count)
            {
                Array.Copy(_entities, slot + 1, _entities, slot, Count - slot);
                Array.Copy(_data, slot + 1, _data, slot, Count - slot);
            }
            _entities[Count] = default;
            _data[Count] = default;
            ReindexFrom(slot);
        }

        private void ReindexFrom(int slot)
        {
            for (int i = slot; i < Count; i++)
                _sparse[_entities[i].Index] = i + 1;
        }

        private void EnsureSparse(int index)
        {
            if (index < _sparse.Length) return;
            int size = _sparse.Length;
            while (size <= index) size *= 2;
            Array.Resize(ref _sparse, size);
        }
    }
}
