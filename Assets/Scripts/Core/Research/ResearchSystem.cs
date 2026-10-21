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
    /// <item>Field telemetry: every tick, each of the player's fleets carrying enough microchips in a NOAA storm rolls
    /// to capture storm telemetry (chance and yield scale with the highest R/S/G level at its site or lane), up to the
    /// highest next-tier target across incomplete branches.</item>
    /// <item>Stress test (command <see cref="CommandType.StressTest"/>, target: the player's core base): once the branch's
    /// next-tier telemetry target is reached, pays the tier's cost from the core base and enters retooling (or rolls
    /// against <see cref="ResearchBalance.StressChancePermille"/> when configured below 100%).</item>
    /// <item>Retooling: after a successful test the tier unlocks once the retool time has passed.</item>
    /// </list>
    /// Rolls are stateless per (world seed, tick, entity), so saves need no RNG state.
    /// </summary>
    public sealed class ResearchSystem : ISimSystem, ICommandHandler
    {
        private const ulong RollSalt = 0x5EA2C47E57UL;
        private const ulong TelemetrySalt = 0x7E1E4E7299UL;

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
        private EntityId[] _hqByPlayer = new EntityId[16];

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

        public void OnTick(long tick)
        {
            for (int i = 0; i < _progress.Count; i++)
            {
                ref var p = ref _progress.At(i);
                if (p.Stage != ResearchStage.Retooling || tick < p.RetoolUntilTick)
                    continue;
                var branch = p.ActiveBranch;
                byte next = (byte)Math.Min((int)ResearchBalance.TopTier, p.Tier(branch) + 1);
                p.SetTier(branch, next);
                p.TelemetryMilli = Math.Max(0, p.TelemetryMilli - _balance.TierTelemetryTargetMilli(next));
                p.FailedTests = 0;
                p.RetoolUntilTick = 0;
                if (p.AllAtOrAbove(ResearchBalance.TopTier))
                    p.Stage = ResearchStage.Complete;
                else if (p.TelemetryMilli >= MinTargetMilli(in p))
                    p.Stage = ResearchStage.StressTest;
                else
                    p.Stage = ResearchStage.FieldTelemetry;
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
                if (p.Stage != ResearchStage.FieldTelemetry && p.Stage != ResearchStage.StressTest)
                    continue;
                int maxTarget = MaxTargetMilli(in p);
                if (p.TelemetryMilli >= maxTarget)
                    continue;
                int chance = _balance.EffectiveTelemetryChancePermille(level, p.Tier(ResearchBranch.Signals));
                if (chance < 1000)
                {
                    var rng = new SplitMix64(_worldSeed ^ TelemetrySalt ^ ((ulong)(uint)fleet.Index << 32) ^ (ulong)tick);
                    if (rng.NextInt(0, 999) >= chance)
                        continue;
                }
                p.TelemetryMilli = Math.Min(maxTarget,
                    p.TelemetryMilli + level * _balance.TelemetryMilliPerStormLevel);
                p.LastTelemetryFleet = fleet;
                if (p.Stage == ResearchStage.FieldTelemetry && p.TelemetryMilli >= MinTargetMilli(in p))
                    p.Stage = ResearchStage.StressTest;
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.StressTest)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (command.ParamA < 0 || command.ParamA >= TechProgress.BranchCount)
                return;
            var branch = (ResearchBranch)command.ParamA;
            var hq = FindHq(command.PlayerId);
            if (hq.IsNull || !_progress.Has(hq))
                return;
            ref var p = ref _progress.Get(hq);
            var coreBase = _players.Get(hq).CoreBase;
            byte currentTier = p.Tier(branch);
            if (p.Stage != ResearchStage.StressTest || currentTier >= ResearchBalance.TopTier ||
                command.Target != coreBase || !_stockpiles.Has(coreBase) ||
                !_sites.Has(coreBase) || _sites.Get(coreBase).OwnerPlayerId != command.PlayerId)
                return;

            byte tier = (byte)(currentTier + 1);
            if (p.TelemetryMilli > 0 && p.TelemetryMilli < _balance.TierTelemetryTargetMilli(tier))
                return;
            var rules = _balance.Tier(tier);
            ref var stock = ref _stockpiles.Get(coreBase);
            for (int i = 0; i < rules.StressTestCost.Length; i++)
                if (!stock.Has(rules.StressTestCost[i].Item, rules.StressTestCost[i].Quantity))
                    return;
            for (int i = 0; i < rules.StressTestCost.Length; i++)
                stock.Add(rules.StressTestCost[i].Item, -rules.StressTestCost[i].Quantity);

            if (p.ActiveBranch != branch)
            {
                p.ActiveBranch = branch;
                p.FailedTests = 0;
            }

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

        private int MinTargetMilli(in TechProgress p)
        {
            int min = int.MaxValue;
            for (int b = 0; b < TechProgress.BranchCount; b++)
            {
                byte current = p.Tier((ResearchBranch)b);
                if (current < ResearchBalance.TopTier)
                {
                    int target = _balance.TierTelemetryTargetMilli((byte)(current + 1));
                    if (target < min)
                        min = target;
                }
            }
            return min == int.MaxValue ? _balance.TelemetryTargetMilli : min;
        }

        private int MaxTargetMilli(in TechProgress p)
        {
            int max = _balance.TelemetryTargetMilli;
            for (int b = 0; b < TechProgress.BranchCount; b++)
            {
                byte current = p.Tier((ResearchBranch)b);
                if (current < ResearchBalance.TopTier)
                {
                    int target = _balance.TierTelemetryTargetMilli((byte)(current + 1));
                    if (target > max)
                        max = target;
                }
            }
            return max;
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
            if (playerId > 0 && playerId < _hqByPlayer.Length)
            {
                var cached = _hqByPlayer[playerId];
                if (!cached.IsNull && _players.Has(cached) && _players.Get(cached).PlayerId == playerId)
                    return cached;
            }
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players.At(i).PlayerId == playerId)
                {
                    var hq = _players.EntityAt(i);
                    if (playerId > 0)
                    {
                        if (playerId >= _hqByPlayer.Length)
                            Array.Resize(ref _hqByPlayer, Math.Max(playerId + 1, _hqByPlayer.Length * 2));
                        _hqByPlayer[playerId] = hq;
                    }
                    return hq;
                }
            }
            return default;
        }
    }
}
