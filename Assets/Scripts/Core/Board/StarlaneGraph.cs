using System;
using Unruled.Core.Ecs;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Read-only adjacency index (CSR) over the <see cref="StarlaneEdge"/> store, keyed by site entity index.
    /// Neighbours are listed in ascending edge entity order. Call <see cref="Rebuild"/> after lanes change.
    /// </summary>
    public sealed class StarlaneGraph
    {
        private int[] _offsets = new int[1];
        private EntityId[] _neighbors = Array.Empty<EntityId>();
        private EntityId[] _edges = Array.Empty<EntityId>();
        private int _siteCapacity;

        public int SiteCapacity => _siteCapacity;

        /// <summary>Total adjacency slots (twice the lane count).</summary>
        public int SlotCount => _offsets[_siteCapacity];

        public void Rebuild(ComponentStore<StarlaneEdge> lanes)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));

            int capacity = 0;
            for (int i = 0; i < lanes.Count; i++)
            {
                ref var lane = ref lanes.At(i);
                capacity = Math.Max(capacity, Math.Max(lane.SiteA.Index, lane.SiteB.Index) + 1);
            }

            _siteCapacity = capacity;
            if (_offsets.Length < capacity + 1)
                _offsets = new int[capacity + 1];
            Array.Clear(_offsets, 0, _offsets.Length);
            if (_neighbors.Length < lanes.Count * 2)
            {
                _neighbors = new EntityId[lanes.Count * 2];
                _edges = new EntityId[lanes.Count * 2];
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                ref var lane = ref lanes.At(i);
                _offsets[lane.SiteA.Index + 1]++;
                _offsets[lane.SiteB.Index + 1]++;
            }
            for (int s = 0; s < capacity; s++)
                _offsets[s + 1] += _offsets[s];

            var cursor = new int[capacity];
            Array.Copy(_offsets, cursor, capacity);
            for (int i = 0; i < lanes.Count; i++)
            {
                ref var lane = ref lanes.At(i);
                var edge = lanes.EntityAt(i);
                int a = cursor[lane.SiteA.Index]++;
                _neighbors[a] = lane.SiteB;
                _edges[a] = edge;
                int b = cursor[lane.SiteB.Index]++;
                _neighbors[b] = lane.SiteA;
                _edges[b] = edge;
            }
        }

        /// <summary>First adjacency slot of the site; iterate slots up to <see cref="End"/>.</summary>
        public int Start(EntityId site) => Start(site.Index);

        public int End(EntityId site) => End(site.Index);

        public int Start(int siteIndex) => siteIndex < _siteCapacity ? _offsets[siteIndex] : 0;

        public int End(int siteIndex) => siteIndex < _siteCapacity ? _offsets[siteIndex + 1] : 0;

        public EntityId NeighborAt(int slot) => _neighbors[slot];

        public EntityId EdgeAt(int slot) => _edges[slot];
    }
}
