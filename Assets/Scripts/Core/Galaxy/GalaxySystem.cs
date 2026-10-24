using System;
using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// Owns <see cref="StarSystem"/>, <see cref="SystemName"/> and <see cref="SystemChart"/>: which catalog stars have
    /// become simulated systems, which players know each one (the world is shared, knowledge is per player; THE SYSTEM's
    /// agencies know everything), and, as before,
    /// under which cluster id, who holds each, and what it is called. A star is materialised only through
    /// <see cref="SimulationWorld.MaterialiseStar"/>; this system registers it and keeps a cluster index (derived,
    /// rebuilt on load). Every <see cref="OwnerRefreshTicks"/> it recomputes each system's owner as the player
    /// holding the most of its planets (a tie keeps the current owner).
    /// <see cref="CommandType.RenameSystem"/> (Target: any site of the system, so the order travels with that site's signal latency; ParamA–C: the name packed by
    /// <see cref="SystemName.TryEncode"/>) names a system for its current owner and is published as a bulletin; an
    /// order from anyone else, or a name that doesn't decode to a legal one, changes nothing.
    /// </summary>
    public sealed class GalaxySystem : ISimSystem, ICommandHandler, IStarCharts
    {
        public const int OwnerRefreshTicks = 60;

        // Systems from before the catalog (schema 5 and older, or fixed-wiring setups) are laid out on this ring.
        private const int LegacyRingRadius = 40_000;

        private readonly World _world;
        private readonly ComponentStore<StarSystem> _systems;
        private readonly ComponentStore<SystemName> _names;
        private readonly ComponentStore<SystemChart> _charts;
        private readonly Dictionary<long, EntityId> _chartOf = new Dictionary<long, EntityId>(); // (player, cluster) -> chart
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly GalaxyCatalog _catalog;
        private readonly List<EntityId> _byCluster = new List<EntityId>();
        private readonly Dictionary<long, int> _clusterOfStar = new Dictionary<long, int>();
        private int[] _holdings = new int[16];
        private int[] _holders = new int[16];
        // Per-cluster site lists (dense site indexes, ascending), rebuilt when the site count changes.
        private int _indexedSiteCount = -1;
        private int _maxCluster = -1;
        private int[] _clusterFirstSite = Array.Empty<int>();
        private int[] _siteNext = Array.Empty<int>();
        private readonly List<StarId> _neighbours = new List<StarId>();
        private readonly ISystemRenameLog _renames;

        public GalaxySystem(World world, ComponentStore<StarSystem> systems, ComponentStore<SystemName> names,
            ComponentStore<SystemChart> charts, ComponentStore<SiteInfo> sites, GalaxyCatalog catalog, ISystemRenameLog renames)
        {
            _charts = charts ?? throw new ArgumentNullException(nameof(charts));
            _renames = renames ?? throw new ArgumentNullException(nameof(renames));
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
            EnsureSiteIndex();
            return Math.Max(_byCluster.Count, _maxCluster + 1);
        }

        /// <summary>The catalog star a cluster's system was materialised from; false for none or a pre-catalog one.</summary>
        public bool TryGetStar(int clusterId, out StarId star)
        {
            star = default;
            var system = SystemOf(clusterId);
            if (system.IsNull || !_systems.Has(system) || _systems.Get(system).StarId == 0)
                return false;
            star = StarId.FromPacked(_systems.Get(system).StarId);
            return true;
        }

        /// <summary>
        /// Whether a fleet of <paramref name="playerId"/> in <paramref name="fromCluster"/> may chart
        /// <paramref name="target"/>, a hyperlane neighbour of the cluster's star: an uncharted frontier-class star
        /// (home and hub stars are kept for spawning), or a system someone else charted that this player doesn't know.
        /// </summary>
        public bool CanSurvey(int playerId, int fromCluster, StarId target)
        {
            if (!TryGetStar(fromCluster, out var from) || !Surveyable(playerId, target))
                return false;
            _catalog.Neighbours(from, _neighbours);
            for (int i = 0; i < _neighbours.Count; i++)
                if (_neighbours[i] == target)
                    return true;
            return false;
        }

        /// <summary>The first star (by packed id) a fleet of the player in <paramref name="fromCluster"/> may chart, if any.</summary>
        public bool TryPickSurvey(int playerId, int fromCluster, out StarId target)
        {
            target = default;
            if (!TryGetStar(fromCluster, out var from))
                return false;
            _catalog.Neighbours(from, _neighbours);
            for (int i = 0; i < _neighbours.Count; i++)
            {
                if (!Surveyable(playerId, _neighbours[i]))
                    continue;
                target = _neighbours[i];
                return true;
            }
            return false;
        }

        private bool Surveyable(int playerId, StarId star)
        {
            if (TryGetCluster(star, out int cluster))
                return !Knows(playerId, cluster);
            return _catalog.TryGetStar(star, out var record) && record.Class == StarClass.Frontier;
        }

        /// <summary>
        /// Whether the player knows the system of <paramref name="clusterId"/>. THE SYSTEM (id 0 or less) knows all, and
        /// a cluster with no system (a hand-wired world) is open to everyone: charts cover registered systems only.
        /// </summary>
        public bool Knows(int playerId, int clusterId) =>
            playerId <= 0 || SystemOf(clusterId).IsNull || _chartOf.ContainsKey(ChartKey(playerId, clusterId));

        /// <summary>
        /// The one door into a player's charts: the player now knows the system (no-op if it already did or the cluster
        /// has no system). True when this added it.
        /// </summary>
        internal bool Learn(int playerId, int clusterId, ChartSource source, long tick)
        {
            if (playerId <= 0 || SystemOf(clusterId).IsNull || Knows(playerId, clusterId))
                return false;
            var chart = _world.Create();
            _charts.Add(chart, new SystemChart { PlayerId = playerId, ClusterId = clusterId, KnownTick = tick, Source = source });
            _chartOf[ChartKey(playerId, clusterId)] = chart;
            return true;
        }

        public bool KnowsSite(int playerId, EntityId site) =>
            playerId <= 0 || !_sites.Has(site) || Knows(playerId, _sites.Get(site).ClusterId);

        /// <summary>A fleet arrived at a site: its owner learns that system.</summary>
        public void FleetArrived(int playerId, EntityId site, long tick)
        {
            if (_sites.Has(site))
                Learn(playerId, _sites.Get(site).ClusterId, ChartSource.Arrival, tick);
        }

        /// <summary>The player whose survey materialised the cluster's system; 0 for setup systems or none.</summary>
        public int FirstSurveyor(int clusterId)
        {
            var system = SystemOf(clusterId);
            return !system.IsNull && _systems.Has(system) ? _systems.Get(system).FirstSurveyorPlayerId : 0;
        }

        /// <summary>Records the player whose survey materialised the cluster's system.</summary>
        internal void SetFirstSurveyor(int clusterId, int playerId)
        {
            var system = SystemOf(clusterId);
            if (!system.IsNull && _systems.Has(system))
                _systems.Get(system).FirstSurveyorPlayerId = playerId;
        }

        private static long ChartKey(int playerId, int clusterId) => (long)playerId << 32 | (uint)clusterId;

        /// <summary>
        /// Materialised hyperlane neighbours of a star, as cluster ids appended to <paramref name="clusters"/>
        /// (cleared first), in packed-id order.
        /// </summary>
        public void MaterialisedNeighbours(StarId star, List<int> clusters)
        {
            if (clusters == null) throw new ArgumentNullException(nameof(clusters));
            clusters.Clear();
            _catalog.Neighbours(star, _neighbours);
            for (int i = 0; i < _neighbours.Count; i++)
                if (TryGetCluster(_neighbours[i], out int cluster))
                    clusters.Add(cluster);
        }

        /// <summary>The cluster's site with this orbit index, or null.</summary>
        public EntityId SiteAtOrbit(int clusterId, int orbit)
        {
            EnsureSiteIndex();
            for (int i = FirstSite(clusterId); i >= 0; i = _siteNext[i])
                if (_sites.At(i).OrbitIndex == orbit)
                    return _sites.EntityAt(i);
            return default;
        }

        /// <summary>The cluster's outermost site (highest orbit index), or null.</summary>
        public EntityId OuterOrbit(int clusterId)
        {
            EnsureSiteIndex();
            EntityId best = default;
            int bestOrbit = -1;
            for (int i = FirstSite(clusterId); i >= 0; i = _siteNext[i])
            {
                if (_sites.At(i).OrbitIndex <= bestOrbit)
                    continue;
                bestOrbit = _sites.At(i).OrbitIndex;
                best = _sites.EntityAt(i);
            }
            return best;
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
            _chartOf.Clear();
            for (int i = 0; i < _charts.Count; i++)
                _chartOf[ChartKey(_charts.At(i).PlayerId, _charts.At(i).ClusterId)] = _charts.EntityAt(i);
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.RenameSystem)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (!_sites.Has(command.Target))
                return;
            var system = SystemOf(_sites.Get(command.Target).ClusterId);
            if (system.IsNull || !_systems.Has(system) || _systems.Get(system).OwnerPlayerId != command.PlayerId ||
                command.PlayerId <= 0)
                return;
            if (!SystemName.TryDecode(command.ParamA, command.ParamB, command.ParamC, command.PlayerId, tick, out var name))
                return;
            if (_names.Has(system))
                _names.Get(system) = name;
            else
                _names.Add(system, name);
            _renames.ReportRename(command.PlayerId, system, tick);
        }

        public void OnTick(long tick)
        {
            if (tick % OwnerRefreshTicks != 0)
                return;
            EnsureSiteIndex();
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
            for (int i = FirstSite(clusterId); i >= 0; i = _siteNext[i])
            {
                ref var site = ref _sites.At(i);
                if (site.OwnerPlayerId <= 0)
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
            EnsureSiteIndex();
            return FirstSite(clusterId) >= 0;
        }

        private int FirstSite(int clusterId) =>
            clusterId >= 0 && clusterId <= _maxCluster ? _clusterFirstSite[clusterId] : -1;

        // Sites are only ever added (never removed or moved between clusters), so the count says when to reindex.
        private void EnsureSiteIndex()
        {
            if (_indexedSiteCount == _sites.Count)
                return;
            _indexedSiteCount = _sites.Count;
            int maxCluster = -1;
            for (int i = 0; i < _sites.Count; i++)
                maxCluster = Math.Max(maxCluster, _sites.At(i).ClusterId);
            _maxCluster = maxCluster;
            if (_clusterFirstSite.Length < maxCluster + 1)
                Array.Resize(ref _clusterFirstSite, Math.Max(maxCluster + 1, _clusterFirstSite.Length * 2));
            if (_siteNext.Length < _sites.Count)
                Array.Resize(ref _siteNext, Math.Max(_sites.Count, _siteNext.Length * 2));
            for (int c = 0; c <= maxCluster; c++)
                _clusterFirstSite[c] = -1;
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                int c = _sites.At(i).ClusterId;
                if (c < 0)
                    continue;
                _siteNext[i] = _clusterFirstSite[c];
                _clusterFirstSite[c] = i;
            }
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
