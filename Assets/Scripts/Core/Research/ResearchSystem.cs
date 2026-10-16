using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.Research
{
    /// <summary>
    /// Owns <see cref="TechProgress"/>: a player climbs one tech tier at a time through three steps (GDD §2.3).
    /// <list type="bullet">
    /// <item>Field telemetry: every tick, each of the player's fleets carrying enough microchips in a NOAA storm adds
    /// (highest R/S/G level at its site or lane) × the rate, until the target is reached.</item>
    /// <item>Stress test (command <see cref="CommandType.StressTest"/>, target: the player's core base): pays the
    /// tier's cost from the core base, then succeeds with P_base + 18% per earlier failure. A failure is reported
    /// as a prototype blast bulletin.</item>
    /// <item>Retooling: after a successful test the tier unlocks once the retool time has passed.</item>
    /// </list>
    /// Rolls are stateless per (world seed, tick, player), so saves need no RNG state.
    /// </summary>
    public sealed class ResearchSystem : ISimSystem, ICommandHandler
    {
        private const ulong RollSalt = 0x5EA2C47E57UL;

        private readonly ComponentStore<TechProgress> _progress;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<SiteWeather> _siteWeather;
        private readonly ComponentStore<LaneWeather> _laneWeather;
        private readonly IResearchIncidentLog _incidents;
        private readonly ResearchBalance _balance;
        private readonly ulong _worldSeed;

        public ResearchSystem(
            ComponentStore<TechProgress> progress, ComponentStore<PlayerHq> players, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<Stockpile> stockpiles, ComponentStore<SiteInfo> sites,
            ComponentStore<SiteWeather> siteWeather, ComponentStore<LaneWeather> laneWeather,
            IResearchIncidentLog incidents, ResearchBalance balance, ulong worldSeed)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _siteWeather = siteWeather ?? throw new ArgumentNullException(nameof(siteWeather));
            _laneWeather = laneWeather ?? throw new ArgumentNullException(nameof(laneWeather));
            _incidents = incidents ?? throw new ArgumentNullException(nameof(incidents));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _worldSeed = worldSeed;
        }

        /// <summary>The player's tech tier (1 if the player has no HQ or no research yet).</summary>
        public byte TechTierOf(int playerId)
        {
            var hq = FindHq(playerId);
            return !hq.IsNull && _progress.Has(hq) ? _progress.Get(hq).TechTier : (byte)1;
        }

        public void OnTick(long tick)
        {
            for (int i = 0; i < _progress.Count; i++)
            {
                ref var p = ref _progress.At(i);
                if (p.Stage != ResearchStage.Retooling || tick < p.RetoolUntilTick)
                    continue;
                p.TechTier++;
                p.Stage = p.TechTier >= ResearchBalance.TopTier ? ResearchStage.Complete : ResearchStage.FieldTelemetry;
                p.TelemetryMilli = 0;
                p.FailedTests = 0;
                p.RetoolUntilTick = 0;
            }

            for (int i = 0; i < _units.Count; i++)
            {
                var fleet = _units.EntityAt(i);
                int level = StormLevelAt(fleet);
                if (level == 0 || !_stockpiles.Has(fleet) ||
                    !_stockpiles.Get(fleet).Has(ItemId.ItemMicrochip, _balance.TelemetryMinMicrochips))
                    continue;
                var hq = FindHq(_units.At(i).OwnerPlayerId);
                if (hq.IsNull || !_progress.Has(hq))
                    continue;
                ref var p = ref _progress.Get(hq);
                if (p.Stage != ResearchStage.FieldTelemetry)
                    continue;
                p.TelemetryMilli = Math.Min(_balance.TelemetryTargetMilli,
                    p.TelemetryMilli + level * _balance.TelemetryMilliPerStormLevel);
                p.LastTelemetryFleet = fleet;
                if (p.TelemetryMilli >= _balance.TelemetryTargetMilli)
                    p.Stage = ResearchStage.StressTest;
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.StressTest)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var hq = FindHq(command.PlayerId);
            if (hq.IsNull || !_progress.Has(hq))
                return;
            ref var p = ref _progress.Get(hq);
            var coreBase = _players.Get(hq).CoreBase;
            if (p.Stage != ResearchStage.StressTest || command.Target != coreBase || !_stockpiles.Has(coreBase) ||
                !_sites.Has(coreBase) || _sites.Get(coreBase).OwnerPlayerId != command.PlayerId)
                return;

            byte tier = (byte)(p.TechTier + 1);
            var rules = _balance.Tier(tier);
            ref var stock = ref _stockpiles.Get(coreBase);
            for (int i = 0; i < rules.StressTestCost.Length; i++)
                if (!stock.Has(rules.StressTestCost[i].Item, rules.StressTestCost[i].Quantity))
                    return;
            for (int i = 0; i < rules.StressTestCost.Length; i++)
                stock.Add(rules.StressTestCost[i].Item, -rules.StressTestCost[i].Quantity);

            var rng = new SplitMix64(_worldSeed ^ RollSalt ^ ((ulong)(uint)command.PlayerId << 32) ^ (ulong)tick);
            if (rng.NextInt(0, 999) < _balance.StressChancePermille(tier, p.FailedTests))
            {
                p.Stage = ResearchStage.Retooling;
                p.RetoolUntilTick = tick + rules.RetoolTicks;
                return;
            }
            p.FailedTests++;
            _incidents.ReportPrototypeBlast(command.PlayerId, coreBase, tick);
        }

        private int StormLevelAt(EntityId fleet)
        {
            if (!_motions.Has(fleet))
                return 0;
            ref var motion = ref _motions.Get(fleet);
            if (!motion.Lane.IsNull && _laneWeather.Has(motion.Lane))
            {
                ref var w = ref _laneWeather.Get(motion.Lane);
                return Math.Max(w.RadioBlackoutLevel, Math.Max(w.SolarRadiationLevel, w.GeomagneticStormLevel));
            }
            if (!motion.Site.IsNull && _siteWeather.Has(motion.Site))
            {
                ref var w = ref _siteWeather.Get(motion.Site);
                return Math.Max(w.RadioBlackoutLevel, Math.Max(w.SolarRadiationLevel, w.GeomagneticStormLevel));
            }
            return 0;
        }

        private EntityId FindHq(int playerId)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).PlayerId == playerId)
                    return _players.EntityAt(i);
            return default;
        }
    }
}
