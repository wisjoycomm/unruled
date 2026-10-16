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
            ExpireFronts(tick);
            if (tick % _balance.RollIntervalTicks == 0)
                RollFronts(tick);
            UpdateSiteWeather(tick);
            UpdateLaneWeather();
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
            int maxCluster = -1;
            for (int i = 0; i < _sites.Count; i++)
                maxCluster = Math.Max(maxCluster, _sites.At(i).ClusterId);
            return maxCluster;
        }

        /// <summary>The front a star system spawns on a roll tick, if any. Pure: the same map, seed and tick give the same front.</summary>
        public bool TryRollFront(long tick, int cluster, out StormFront front)
        {
            front = default;
            if (tick % _balance.RollIntervalTicks != 0)
                return false;
            int siteCount = 0;
            for (int i = 0; i < _sites.Count; i++)
                if (_sites.At(i).ClusterId == cluster)
                    siteCount++;
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
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites.At(i).ClusterId != cluster)
                    continue;
                if (ordinal-- == 0)
                    return ref _sites.At(i);
            }
            throw new InvalidOperationException($"Cluster {cluster} has fewer sites than expected.");
        }

        private void UpdateSiteWeather(long tick)
        {
            for (int i = 0; i < _siteWeather.Count; i++)
            {
                var siteId = _siteWeather.EntityAt(i);
                ref var weather = ref _siteWeather.At(i);
                weather = default;
                if (!_sites.Has(siteId))
                    continue;

                ref var site = ref _sites.Get(siteId);
                for (int f = 0; f < _fronts.Count; f++)
                {
                    ref var front = ref _fronts.At(f);
                    if (front.ClusterId != site.ClusterId)
                        continue;
                    long dx = site.PosX - front.CenterX;
                    long dy = site.PosY - front.CenterY;
                    if (dx * dx + dy * dy > (long)front.Radius * front.Radius)
                        continue;

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

                if (site.IsIrradiated(tick))
                    weather.SolarRadiationLevel = Math.Max(weather.SolarRadiationLevel, _irradiationSolarLevel);
            }
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
