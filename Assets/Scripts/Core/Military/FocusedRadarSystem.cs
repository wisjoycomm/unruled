using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>ECS component: one focused scan of a belt lane in progress.</summary>
    public struct RadarScan
    {
        public int OwnerPlayerId;
        public EntityId Lane;
        public long StartTick;
        public int IntegrationTicks; // grows to the jammed value under ECM
        public bool RwrFired;
    }

    /// <summary>
    /// Focused radar timeline (GDD §2.4 B, anti-strobe §4.1). Owns <see cref="RadarScan"/> and <see cref="FleetSensors"/>.
    /// Fleets of a tier with the IAEA global reveal (tab 03, Tier 5) are revealed every tick.
    /// ScanBelt (target: a bypass lane) pays the startup batteries at once from the scanner's core-base stockpile.
    /// At +RWR ticks, hostile fleets on the lane with EW ships get the RWR alarm. EcmJam (target: a warned EW fleet,
    /// paid from its owner's core base) stretches every hostile scan on its lane to the jammed time. When a scan's
    /// time is up, hostile fleets on the lane that are not reversing are revealed. CancelScan ends it with no refund
    /// and no result. Invalid orders change nothing.
    /// </summary>
    public sealed class FocusedRadarSystem : ISimSystem, ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<RadarScan> _scans;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetSensors> _sensors;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly RadarBalance _balance;
        private readonly FleetBalance _fleetBalance;

        private EntityId[] _finished = new EntityId[16];

        public FocusedRadarSystem(
            World world, ComponentStore<RadarScan> scans, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetSensors> sensors, ComponentStore<StarlaneEdge> lanes,
            ComponentStore<PlayerHq> players, ComponentStore<Stockpile> stockpiles, RadarBalance balance,
            FleetBalance fleetBalance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _scans = scans ?? throw new ArgumentNullException(nameof(scans));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
        }

        public void OnTick(long tick)
        {
            for (int f = 0; f < _units.Count; f++)
            {
                if (!_fleetBalance.Tier(_units.At(f).TechTier).IaeaGlobalReveal)
                    continue;
                ref var sensors = ref _sensors.Get(_units.EntityAt(f));
                sensors.RevealedUntilTick = Math.Max(sensors.RevealedUntilTick, tick + 1);
            }

            int finished = 0;
            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                long elapsed = tick - scan.StartTick;

                if (!scan.RwrFired && elapsed >= _balance.RwrDelayTicks)
                {
                    scan.RwrFired = true;
                    for (int f = 0; f < _units.Count; f++)
                    {
                        ref var unit = ref _units.At(f);
                        var fleet = _units.EntityAt(f);
                        if (unit.OwnerPlayerId != scan.OwnerPlayerId && unit.EwShips > 0 && _motions.Get(fleet).Lane == scan.Lane)
                            _sensors.Get(fleet).RwrWarningTick = tick;
                    }
                }

                if (elapsed < scan.IntegrationTicks)
                    continue;

                for (int f = 0; f < _units.Count; f++)
                {
                    var fleet = _units.EntityAt(f);
                    ref var motion = ref _motions.Get(fleet);
                    if (_units.At(f).OwnerPlayerId == scan.OwnerPlayerId || motion.Lane != scan.Lane || motion.IsReversing)
                        continue;
                    ref var sensors = ref _sensors.Get(fleet);
                    sensors.RevealedUntilTick = Math.Max(sensors.RevealedUntilTick, tick + _balance.RevealTicks);
                }
                if (finished == _finished.Length)
                    Array.Resize(ref _finished, finished * 2);
                _finished[finished++] = _scans.EntityAt(i);
            }

            for (int i = 0; i < finished; i++)
                _world.Destroy(_finished[i]);
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.ScanBelt:
                    StartScan(command.PlayerId, command.Target, tick);
                    break;
                case CommandType.CancelScan:
                    CancelScan(command.PlayerId, command.Target);
                    break;
                case CommandType.EcmJam:
                    Jam(command.PlayerId, command.Target);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        private void StartScan(int playerId, EntityId lane, long tick)
        {
            if (!_lanes.Has(lane) || _lanes.Get(lane).Tier != StarlaneTier.OuterRimBypass)
                return;
            if (FindScan(playerId, lane) >= 0)
                return;
            if (!TryPay(playerId, _balance.StartupBatteries))
                return;

            var scan = _world.Create();
            _scans.Add(scan, new RadarScan
            {
                OwnerPlayerId = playerId, Lane = lane, StartTick = tick, IntegrationTicks = _balance.IntegrationTicks
            });
        }

        private void CancelScan(int playerId, EntityId lane)
        {
            int index = FindScan(playerId, lane);
            if (index >= 0)
                _world.Destroy(_scans.EntityAt(index));
        }

        private void Jam(int playerId, EntityId fleetId)
        {
            if (!_units.Has(fleetId))
                return;
            ref var unit = ref _units.Get(fleetId);
            var lane = _motions.Get(fleetId).Lane;
            if (unit.OwnerPlayerId != playerId || unit.EwShips <= 0 || _sensors.Get(fleetId).RwrWarningTick == 0 || lane.IsNull)
                return;

            bool anyHostileScan = false;
            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                if (scan.Lane == lane && scan.OwnerPlayerId != playerId &&
                    scan.IntegrationTicks < _balance.JammedIntegrationTicks)
                    anyHostileScan = true;
            }
            if (!anyHostileScan || !TryPay(playerId, _balance.JamBatteries))
                return;

            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                if (scan.Lane == lane && scan.OwnerPlayerId != playerId)
                    scan.IntegrationTicks = Math.Max(scan.IntegrationTicks, _balance.JammedIntegrationTicks);
            }
        }

        private int FindScan(int playerId, EntityId lane)
        {
            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                if (scan.OwnerPlayerId == playerId && scan.Lane == lane)
                    return i;
            }
            return -1;
        }

        private bool TryPay(int playerId, int batteries)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                ref var hq = ref _players.At(i);
                if (hq.PlayerId != playerId)
                    continue;
                if (!_stockpiles.Has(hq.CoreBase))
                    return false;
                ref var stock = ref _stockpiles.Get(hq.CoreBase);
                if (!stock.Has(ItemId.ItemBattery, batteries))
                    return false;
                stock.Add(ItemId.ItemBattery, -batteries);
                return true;
            }
            return false;
        }
    }
}
