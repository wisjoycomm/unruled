using System;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Owns <see cref="StormFront"/>, <see cref="SiteWeather"/> and <see cref="LaneWeather"/> (NOAA R/S/G 1–5).
    /// Each tick: expire fronts, roll new fronts per star system on roll ticks, then recompute weather.
    /// A site feels the strongest front of each scale covering it; an irradiated site is held at the irradiation S level.
    /// A lane feels the stronger of its two ends, capped by its tier's storm exposure.
    /// Rolls use a fresh RNG keyed by (world seed, tick, cluster), so the engine keeps no RNG state and any future roll
    /// can be read ahead with <see cref="TryRollFront"/> (the NOAA forecast).
    /// </summary>
    public sealed class NoaaWeatherEngine : ISimSystem
    {
        private const ulong TickMix = 0x9E37_79B9_7F4A_7C15UL;
        private const ulong ClusterMix = 0xC2B2_AE3D_27D4_EB4FUL;

        private readonly World _world;
        private readonly ComponentStore<StormFront> _fronts;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<SiteWeather> _siteWeather;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<LaneWeather> _laneWeather;
        private readonly NoaaBalance _balance;
        private readonly StarlaneBalance _laneBalance;
        private readonly byte _irradiationSolarLevel;
        private readonly ulong _worldSeed;

        private EntityId[] _expired = new EntityId[16];
        private int _indexedSiteCount = -1;
        private int _maxCluster = -1;
        private int[] _clusterFirstSite = Array.Empty<int>();
        private int[] _clusterSiteCount = Array.Empty<int>();
        private int[] _siteNextInCluster = Array.Empty<int>();
        private SiteWeather[] _prevWeather = Array.Empty<SiteWeather>();
        private int _lastLaneCount = -1;
        private int _lastLaneWeatherCount = -1;

        public NoaaWeatherEngine(
            World world, ComponentStore<StormFront> fronts, ComponentStore<SiteInfo> sites,
            ComponentStore<SiteWeather> siteWeather, ComponentStore<StarlaneEdge> lanes,
            ComponentStore<LaneWeather> laneWeather, NoaaBalance balance, StarlaneBalance laneBalance,
            LifecycleBalance lifecycle, ulong worldSeed)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _fronts = fronts ?? throw new ArgumentNullException(nameof(fronts));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _siteWeather = siteWeather ?? throw new ArgumentNullException(nameof(siteWeather));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _laneWeather = laneWeather ?? throw new ArgumentNullException(nameof(laneWeather));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _laneBalance = laneBalance ?? throw new ArgumentNullException(nameof(laneBalance));
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            _irradiationSolarLevel = lifecycle.IrradiationSolarLevel;
            _worldSeed = worldSeed;
        }

        public void OnTick(long tick)
        {
            EnsureClusterIndex();
            ExpireFronts(tick);
            if (tick % _balance.RollIntervalTicks == 0)
                RollFronts(tick);
            bool changed = UpdateSiteWeather(tick);
            if (changed || _lanes.Count != _lastLaneCount || _laneWeather.Count != _lastLaneWeatherCount)
            {
                UpdateLaneWeather();
                _lastLaneCount = _lanes.Count;
                _lastLaneWeatherCount = _laneWeather.Count;
            }
        }

        private void EnsureClusterIndex()
        {
            if (_indexedSiteCount == _sites.Count)
                return;
            _indexedSiteCount = _sites.Count;
            int maxCluster = -1;
            for (int i = 0; i < _sites.Count; i++)
            {
                int c = _sites.At(i).ClusterId;
                if (c > maxCluster)
                    maxCluster = c;
            }
            _maxCluster = maxCluster;
            if (maxCluster < 0)
                return;
            int clusterCap = maxCluster + 1;
            if (_clusterFirstSite.Length < clusterCap)
            {
                int cap = Math.Max(clusterCap, _clusterFirstSite.Length == 0 ? 32 : _clusterFirstSite.Length * 2);
                Array.Resize(ref _clusterFirstSite, cap);
                Array.Resize(ref _clusterSiteCount, cap);
            }
            for (int c = 0; c <= maxCluster; c++)
            {
                _clusterFirstSite[c] = -1;
                _clusterSiteCount[c] = 0;
            }
            if (_siteNextInCluster.Length < _sites.Count)
                Array.Resize(ref _siteNextInCluster, Math.Max(_sites.Count, _siteNextInCluster.Length == 0 ? 64 : _siteNextInCluster.Length * 2));
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                int c = _sites.At(i).ClusterId;
                if (c < 0)
                {
                    _siteNextInCluster[i] = -1;
                    continue;
                }
                _siteNextInCluster[i] = _clusterFirstSite[c];
                _clusterFirstSite[c] = i;
                _clusterSiteCount[c]++;
            }
        }

        private void ExpireFronts(long tick)
        {
            int count = 0;
            for (int i = 0; i < _fronts.Count; i++)
            {
                if (_fronts.At(i).EndTick > tick)
                    continue;
                if (count == _expired.Length)
                    Array.Resize(ref _expired, count * 2);
                _expired[count++] = _fronts.EntityAt(i);
            }
            for (int i = 0; i < count; i++)
                _world.Destroy(_expired[i]);
        }

        private void RollFronts(long tick)
        {
            int maxCluster = MaxClusterId();
            for (int cluster = 0; cluster <= maxCluster; cluster++)
                if (TryRollFront(tick, cluster, out var front))
                    _fronts.Add(_world.Create(), front);
        }

        public int RollIntervalTicks => _balance.RollIntervalTicks;

        public int MaxClusterId()
        {
            EnsureClusterIndex();
            return _maxCluster;
        }

        /// <summary>The front a star system spawns on a roll tick, if any. Pure: the same map, seed and tick give the same front.</summary>
        public bool TryRollFront(long tick, int cluster, out StormFront front)
        {
            front = default;
            if (tick % _balance.RollIntervalTicks != 0)
                return false;
            EnsureClusterIndex();
            int siteCount = cluster >= 0 && cluster <= _maxCluster ? _clusterSiteCount[cluster] : 0;
            if (siteCount == 0)
                return false;

            var rng = new SplitMix64(_worldSeed ^ ((ulong)tick * TickMix) ^ ((ulong)cluster * ClusterMix));
            if (rng.NextInt(0, 99) >= _balance.SpawnChancePercent)
                return false;

            var scale = (StormScale)rng.NextInt((int)StormScale.Radio, (int)StormScale.Geomagnetic);
            byte level = RollLevel(ref rng);
            int centerOrdinal = rng.NextInt(0, siteCount - 1);
            int radius = rng.NextInt(_balance.MinRadius, _balance.MaxRadius);
            int duration = rng.NextInt(_balance.MinDurationTicks, _balance.MaxDurationTicks);

            ref var center = ref SiteOfCluster(cluster, centerOrdinal);
            front = new StormFront
            {
                ClusterId = cluster, Scale = scale, Level = level, CenterX = center.PosX, CenterY = center.PosY,
                Radius = radius, EndTick = tick + duration
            };
            return true;
        }

        private byte RollLevel(ref SplitMix64 rng)
        {
            int roll = rng.NextInt(0, _balance.LevelWeightTotal - 1);
            for (int level = 1; level <= NoaaBalance.LevelCount; level++)
            {
                roll -= _balance.LevelWeight(level);
                if (roll < 0)
                    return (byte)level;
            }
            throw new InvalidOperationException("Level weights do not cover the roll.");
        }

        private ref SiteInfo SiteOfCluster(int cluster, int ordinal)
        {
            for (int i = _clusterFirstSite[cluster]; i >= 0; i = _siteNextInCluster[i])
            {
                if (ordinal-- == 0)
                    return ref _sites.At(i);
            }
            throw new InvalidOperationException($"Cluster {cluster} has fewer sites than expected.");
        }

        private bool UpdateSiteWeather(long tick)
        {
            int count = _siteWeather.Count;
            if (_prevWeather.Length < count)
                Array.Resize(ref _prevWeather, Math.Max(count, _prevWeather.Length == 0 ? 64 : _prevWeather.Length * 2));
            for (int i = 0; i < count; i++)
            {
                _prevWeather[i] = _siteWeather.At(i);
                var siteId = _siteWeather.EntityAt(i);
                ref var weather = ref _siteWeather.At(i);
                weather = default;
                if (_sites.Has(siteId) && _sites.Get(siteId).IsIrradiated(tick))
                    weather.SolarRadiationLevel = _irradiationSolarLevel;
            }

            for (int f = 0; f < _fronts.Count; f++)
            {
                ref var front = ref _fronts.At(f);
                if (front.ClusterId < 0 || front.ClusterId > _maxCluster)
                    continue;
                long r2 = (long)front.Radius * front.Radius;
                for (int s = _clusterFirstSite[front.ClusterId]; s >= 0; s = _siteNextInCluster[s])
                {
                    ref var site = ref _sites.At(s);
                    long dx = site.PosX - front.CenterX;
                    long dy = site.PosY - front.CenterY;
                    if (dx * dx + dy * dy > r2)
                        continue;
                    var siteId = _sites.EntityAt(s);
                    if (!_siteWeather.Has(siteId))
                        continue;
                    ref var weather = ref _siteWeather.Get(siteId);
                    switch (front.Scale)
                    {
                        case StormScale.Radio:
                            weather.RadioBlackoutLevel = Math.Max(weather.RadioBlackoutLevel, front.Level);
                            break;
                        case StormScale.Solar:
                            weather.SolarRadiationLevel = Math.Max(weather.SolarRadiationLevel, front.Level);
                            break;
                        case StormScale.Geomagnetic:
                            weather.GeomagneticStormLevel = Math.Max(weather.GeomagneticStormLevel, front.Level);
                            break;
                    }
                }
            }

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                ref var cur = ref _siteWeather.At(i);
                ref var prev = ref _prevWeather[i];
                if (cur.RadioBlackoutLevel != prev.RadioBlackoutLevel ||
                    cur.SolarRadiationLevel != prev.SolarRadiationLevel ||
                    cur.GeomagneticStormLevel != prev.GeomagneticStormLevel)
                {
                    changed = true;
                    break;
                }
            }
            return changed;
        }

        private void UpdateLaneWeather()
        {
            for (int i = 0; i < _laneWeather.Count; i++)
            {
                var laneId = _laneWeather.EntityAt(i);
                ref var weather = ref _laneWeather.At(i);
                weather = default;
                if (!_lanes.Has(laneId))
                    continue;

                ref var lane = ref _lanes.Get(laneId);
                byte cap = _laneBalance.Get(lane.Tier).MaxStormLevel;
                var a = _siteWeather.Has(lane.SiteA) ? _siteWeather.Get(lane.SiteA) : default;
                var b = _siteWeather.Has(lane.SiteB) ? _siteWeather.Get(lane.SiteB) : default;
                weather.RadioBlackoutLevel = Math.Min(cap, Math.Max(a.RadioBlackoutLevel, b.RadioBlackoutLevel));
                weather.SolarRadiationLevel = Math.Min(cap, Math.Max(a.SolarRadiationLevel, b.SolarRadiationLevel));
                weather.GeomagneticStormLevel = Math.Min(cap, Math.Max(a.GeomagneticStormLevel, b.GeomagneticStormLevel));
            }
        }
    }
}
