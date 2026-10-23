using System;
using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// Owns <see cref="StarSystem"/> and <see cref="SystemName"/>: which catalog stars have become simulated systems,
    /// under which cluster id, who holds each, and what it is called. A star is materialised only through
    /// <see cref="SimulationWorld.MaterialiseStar"/>; this system registers it and keeps a cluster index (derived,
    /// rebuilt on load). Every <see cref="OwnerRefreshTicks"/> it recomputes each system's owner as the player
    /// holding the most of its planets (a tie keeps the current owner).
    /// </summary>
    public sealed class GalaxySystem : ISimSystem
    {
        public const int OwnerRefreshTicks = 60;

        // Systems from before the catalog (schema 5 and older, or fixed-wiring setups) are laid out on this ring.
        private const int LegacyRingRadius = 40_000;

        private readonly World _world;
        private readonly ComponentStore<StarSystem> _systems;
        private readonly ComponentStore<SystemName> _names;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly GalaxyCatalog _catalog;
        private readonly List<EntityId> _byCluster = new List<EntityId>();
        private readonly Dictionary<long, int> _clusterOfStar = new Dictionary<long, int>();
        private int[] _holdings = new int[16];
        private int[] _holders = new int[16];

        public GalaxySystem(World world, ComponentStore<StarSystem> systems, ComponentStore<SystemName> names,
            ComponentStore<SiteInfo> sites, GalaxyCatalog catalog)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
            _names = names ?? throw new ArgumentNullException(nameof(names));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public GalaxyCatalog Catalog => _catalog;

        /// <summary>The cluster id the next materialised star will get.</summary>
        public int NextClusterId()
        {
            int next = _byCluster.Count;
            for (int i = 0; i < _sites.Count; i++)
                next = Math.Max(next, _sites.At(i).ClusterId + 1);
            return next;
        }

        public bool IsMaterialised(StarId star) => _clusterOfStar.ContainsKey(star.Packed);

        public bool TryGetCluster(StarId star, out int clusterId) => _clusterOfStar.TryGetValue(star.Packed, out clusterId);

        /// <summary>The system entity of a cluster, or null when the cluster has none.</summary>
        public EntityId SystemOf(int clusterId) =>
            clusterId >= 0 && clusterId < _byCluster.Count ? _byCluster[clusterId] : default;

        /// <summary>World setup / materialisation: records a catalog star as the system of <paramref name="clusterId"/>.</summary>
        internal EntityId Register(in StarRecord star, int clusterId, long tick)
        {
            if (_clusterOfStar.ContainsKey(star.Id.Packed))
                throw new InvalidOperationException("That star is already a system.");
            if (!SystemOf(clusterId).IsNull)
                throw new InvalidOperationException($"Cluster {clusterId} already has a system.");
            var entity = _world.Create();
            _systems.Add(entity, new StarSystem
            {
                StarId = star.Id.Packed,
                ClusterId = clusterId,
                MaterialisedTick = tick,
                X = star.X,
                Y = star.Y,
                Class = star.Class
            });
            Index(entity, clusterId, star.Id.Packed);
            return entity;
        }

        /// <summary>
        /// Gives every cluster that has sites but no system an uncatalogued one (star id 0) on a ring around the
        /// galaxy's home band: saves from before the catalog, and setups that wire clusters by hand.
        /// </summary>
        public void EnsureSystemsForAllClusters(long tick)
        {
            RebuildIndex();
            int clusters = NextClusterId();
            int ringY = _catalog.Balance.RadiusUnits / 2;
            for (int c = 0; c < clusters; c++)
            {
                if (!SystemOf(c).IsNull || !HasSites(c))
                    continue;
                int angle = (int)((long)c * FixedMath.AngleFull / Math.Max(1, clusters));
                var entity = _world.Create();
                _systems.Add(entity, new StarSystem
                {
                    ClusterId = c,
                    MaterialisedTick = tick,
                    X = (int)(((long)LegacyRingRadius * FixedMath.Cos(angle)) >> 16),
                    Y = ringY + (int)(((long)LegacyRingRadius * FixedMath.Sin(angle)) >> 16),
                    Class = StarClass.Frontier
                });
                Index(entity, c, 0);
            }
        }

        /// <summary>Rebuilds the cluster and star indexes from the stores (after a load).</summary>
        public void RebuildIndex()
        {
            _byCluster.Clear();
            _clusterOfStar.Clear();
            for (int i = 0; i < _systems.Count; i++)
            {
                ref var s = ref _systems.At(i);
                Index(_systems.EntityAt(i), s.ClusterId, s.StarId);
            }
        }

        public void OnTick(long tick)
        {
            if (tick % OwnerRefreshTicks != 0)
                return;
            for (int i = 0; i < _systems.Count; i++)
            {
                ref var system = ref _systems.At(i);
                system.OwnerPlayerId = LeadingHolder(system.ClusterId, system.OwnerPlayerId);
            }
        }

        // The player owning the most planets in the cluster; ties keep `current`, nobody = 0.
        private int LeadingHolder(int clusterId, int current)
        {
            int distinct = 0;
            for (int i = 0; i < _sites.Count; i++)
            {
                ref var site = ref _sites.At(i);
                if (site.ClusterId != clusterId || site.OwnerPlayerId <= 0)
                    continue;
                int slot = 0;
                while (slot < distinct && _holders[slot] != site.OwnerPlayerId)
                    slot++;
                if (slot == distinct)
                {
                    if (distinct == _holders.Length)
                    {
                        Array.Resize(ref _holders, distinct * 2);
                        Array.Resize(ref _holdings, distinct * 2);
                    }
                    _holders[distinct] = site.OwnerPlayerId;
                    _holdings[distinct] = 0;
                    distinct++;
                }
                _holdings[slot]++;
            }
            int best = 0, bestCount = 0, currentCount = 0;
            for (int s = 0; s < distinct; s++)
            {
                if (_holders[s] == current)
                    currentCount = _holdings[s];
                if (_holdings[s] > bestCount || (_holdings[s] == bestCount && _holders[s] < best))
                {
                    best = _holders[s];
                    bestCount = _holdings[s];
                }
            }
            if (bestCount == 0)
                return 0;
            return currentCount == bestCount ? current : best;
        }

        private bool HasSites(int clusterId)
        {
            for (int i = 0; i < _sites.Count; i++)
                if (_sites.At(i).ClusterId == clusterId)
                    return true;
            return false;
        }

        private void Index(EntityId entity, int clusterId, long starId)
        {
            while (_byCluster.Count <= clusterId)
                _byCluster.Add(default);
            _byCluster[clusterId] = entity;
            if (starId != 0)
                _clusterOfStar[starId] = clusterId;
        }
    }
}
