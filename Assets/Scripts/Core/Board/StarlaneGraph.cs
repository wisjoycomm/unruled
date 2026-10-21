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
        private int[] _cursor = Array.Empty<int>();
        private int[] _componentOf = Array.Empty<int>();
        private EntityId[] _neighbors = Array.Empty<EntityId>();
        private EntityId[] _edges = Array.Empty<EntityId>();
        private int _siteCapacity;
        private int _componentCount;
        private int _version;

        public int SiteCapacity => _siteCapacity;
        public int ComponentCount => _componentCount;
        public int Version => _version;

        /// <summary>Connected component id (1..ComponentCount), or 0 if the site has no incident lanes.</summary>
        public int ComponentOf(EntityId site) =>
            site.Index >= 0 && site.Index < _siteCapacity ? _componentOf[site.Index] : 0;

        /// <summary>True when both sites belong to the same connected component of the graph.</summary>
        public bool SameComponent(EntityId a, EntityId b) =>
            a.Index == b.Index ||
            (a.Index >= 0 && a.Index < _siteCapacity && b.Index >= 0 && b.Index < _siteCapacity &&
             _componentOf[a.Index] != 0 && _componentOf[a.Index] == _componentOf[b.Index]);

        /// <summary>Total adjacency slots (twice the lane count).</summary>
        public int SlotCount => _offsets[_siteCapacity];

        public void Rebuild(ComponentStore<StarlaneEdge> lanes)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            _version++;

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

            if (_cursor.Length < capacity)
                _cursor = new int[capacity];
            Array.Copy(_offsets, _cursor, capacity);
            for (int i = 0; i < lanes.Count; i++)
            {
                ref var lane = ref lanes.At(i);
                var edge = lanes.EntityAt(i);
                int a = _cursor[lane.SiteA.Index]++;
                _neighbors[a] = lane.SiteB;
                _edges[a] = edge;
                int b = _cursor[lane.SiteB.Index]++;
                _neighbors[b] = lane.SiteA;
                _edges[b] = edge;
            }

            if (_componentOf.Length < capacity)
                _componentOf = new int[capacity];
            Array.Clear(_componentOf, 0, capacity);
            int comp = 0;
            for (int s = 0; s < capacity; s++)
            {
                if (_componentOf[s] != 0 || _offsets[s] == _offsets[s + 1])
                    continue;
                comp++;
                _componentOf[s] = comp;
                int head = 0, tail = 0;
                _cursor[tail++] = s;
                while (head < tail)
                {
                    int cur = _cursor[head++];
                    for (int slot = _offsets[cur]; slot < _offsets[cur + 1]; slot++)
                    {
                        int nb = _neighbors[slot].Index;
                        if (nb < capacity && _componentOf[nb] == 0)
                        {
                            _componentOf[nb] = comp;
                            _cursor[tail++] = nb;
                        }
                    }
                }
            }
            _componentCount = comp;
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
