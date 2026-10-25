using System;
using System.Collections.Generic;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Net
{
    /// <summary>
    /// Which players might see a component: the keys a channel's <see cref="ViewKeys{T}"/> gives each one. A view
    /// collects the slots under the keys it holds (public, its own player id, radar reveals, its charted systems, the
    /// sites it has eyes on) instead of scanning the whole store. Keys only narrow the search: the channel's form
    /// function still decides what is shown, so a key set must cover every player who could see the component.
    /// </summary>
    internal enum ViewKey : byte
    {
        Public = 0,
        Cluster = 1, // a charted system's cluster id
        Player = 2, // an owner's player id
        Site = 3, // a site's entity index: fleets docked there are seen by whoever has eyes on it
        Revealed = 4 // a fleet a radar reveal shows to everyone
    }

    internal delegate void ViewKeys<T>(EntityId entity, in T value, ViewIndex keys);

    /// <summary>One channel's keys for the current pass, sorted, for range lookups.</summary>
    internal sealed class ViewIndex
    {
        private readonly SimulationWorld _sim;
        private long[] _keys = new long[256];
        private int[] _slots = new int[256];
        private int _count;
        private int _slot;
        private int _kinds; // bit per ViewKey present this pass

        public ViewIndex(SimulationWorld sim) => _sim = sim;

        public int Epoch { get; private set; }

        public void Begin(int epoch)
        {
            Epoch = epoch;
            _count = 0;
            _kinds = 0;
        }

        public void At(int slot) => _slot = slot;

        public void Seal() => Array.Sort(_keys, _slots, 0, _count);

        public void AddPublic() => Add(ViewKey.Public, 0);

        public void AddPlayer(int playerId)
        {
            if (playerId > 0)
                Add(ViewKey.Player, playerId);
        }

        public void AddRevealed() => Add(ViewKey.Revealed, 0);

        public void AddSite(EntityId site)
        {
            if (!site.IsNull)
                Add(ViewKey.Site, site.Index);
        }

        /// <summary>A system's cluster; a cluster with no system is known to everyone (see <c>GalaxySystem.Knows</c>).</summary>
        public void AddCluster(int clusterId)
        {
            if (_sim.Galaxy.SystemOf(clusterId).IsNull)
                AddPublic();
            else
                Add(ViewKey.Cluster, clusterId);
        }

        public void AddSiteCluster(EntityId site)
        {
            if (_sim.Sites.Has(site))
                AddCluster(_sim.Sites.Get(site).ClusterId);
        }

        public void AddSiteOwner(EntityId site)
        {
            if (_sim.Sites.Has(site))
                AddPlayer(_sim.Sites.Get(site).OwnerPlayerId);
        }

        public void AddHqOwner(EntityId hq)
        {
            if (_sim.Players.Has(hq))
                AddPlayer(_sim.Players.Get(hq).PlayerId);
        }

        public void AddFleetOwner(EntityId fleet)
        {
            if (_sim.Fleets.Has(fleet))
                AddPlayer(_sim.Fleets.Get(fleet).OwnerPlayerId);
        }

        /// <summary>Appends the slots under a key to <paramref name="into"/> (unsorted, may repeat across keys).</summary>
        public void Collect(ViewKey kind, int key, List<int> into)
        {
            if ((_kinds & (1 << (int)kind)) == 0)
                return;
            long packed = Pack(kind, key);
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_keys[mid] < packed) lo = mid + 1;
                else hi = mid;
            }
            for (int i = lo; i < _count && _keys[i] == packed; i++)
                into.Add(_slots[i]);
        }

        private void Add(ViewKey kind, int key)
        {
            if (_count == _keys.Length)
            {
                Array.Resize(ref _keys, _count * 2);
                Array.Resize(ref _slots, _count * 2);
            }
            _keys[_count] = Pack(kind, key);
            _slots[_count] = _slot;
            _count++;
            _kinds |= 1 << (int)kind;
        }

        private static long Pack(ViewKey kind, int key) => ((long)kind << 32) | (uint)key;
    }

    /// <summary>What one view looks for this pass: its own id, its charted clusters and the sites it has eyes on.</summary>
    internal sealed class ViewInterest
    {
        public int PlayerId;
        public readonly List<int> Clusters = new List<int>();
        public readonly List<int> Sites = new List<int>(); // entity indices

        /// <summary>Every slot of <paramref name="index"/> this view might see, ascending, without repeats.</summary>
        public void Collect(ViewIndex index, List<int> into)
        {
            into.Clear();
            index.Collect(ViewKey.Public, 0, into);
            index.Collect(ViewKey.Player, PlayerId, into);
            index.Collect(ViewKey.Revealed, 0, into);
            for (int i = 0; i < Clusters.Count; i++)
                index.Collect(ViewKey.Cluster, Clusters[i], into);
            for (int i = 0; i < Sites.Count; i++)
                index.Collect(ViewKey.Site, Sites[i], into);
            into.Sort();
            int kept = 0;
            for (int i = 0; i < into.Count; i++)
                if (kept == 0 || into[kept - 1] != into[i])
                    into[kept++] = into[i];
            into.RemoveRange(kept, into.Count - kept);
        }
    }
}
