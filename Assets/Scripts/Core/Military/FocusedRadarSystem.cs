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
    /// Focused radar timeline (GDD §2.4 B, anti-strobe §4.1). Owns <see cref="RadarScan"/> and the radar fields of
    /// <see cref="FleetPresence"/>.
    /// ScanBelt (target: a bypass lane) pays the startup batteries at once from the scanner's core-base stockpile.
    /// At +RWR ticks, hostile fleets on the lane with EW ships get the RWR alarm. EcmJam (target: a warned EW fleet,
    /// paid from its owner's core base) stretches every hostile scan on its lane to the jammed time. When a scan's
    /// time is up, hostile fleets on the lane that are not reversing are revealed. CancelScan ends it with no refund
    /// and no result. Reverse (target: own fleet) flags the fleet as withdrawing. Invalid orders change nothing.
    /// </summary>
    public sealed class FocusedRadarSystem : ISimSystem, ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<RadarScan> _scans;
        private readonly ComponentStore<FleetPresence> _fleets;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly RadarBalance _balance;

        private EntityId[] _finished = new EntityId[16];

        public FocusedRadarSystem(
            World world, ComponentStore<RadarScan> scans, ComponentStore<FleetPresence> fleets,
            ComponentStore<StarlaneEdge> lanes, ComponentStore<PlayerHq> players, ComponentStore<Stockpile> stockpiles,
            RadarBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _scans = scans ?? throw new ArgumentNullException(nameof(scans));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            int finished = 0;
            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                long elapsed = tick - scan.StartTick;

                if (!scan.RwrFired && elapsed >= _balance.RwrDelayTicks)
                {
                    scan.RwrFired = true;
                    for (int f = 0; f < _fleets.Count; f++)
                    {
                        ref var fleet = ref _fleets.At(f);
                        if (fleet.Lane == scan.Lane && fleet.OwnerPlayerId != scan.OwnerPlayerId && fleet.EwShips > 0)
                            fleet.RwrWarningTick = tick;
                    }
                }

                if (elapsed < scan.IntegrationTicks)
                    continue;

                for (int f = 0; f < _fleets.Count; f++)
                {
                    ref var fleet = ref _fleets.At(f);
                    if (fleet.Lane == scan.Lane && fleet.OwnerPlayerId != scan.OwnerPlayerId && !fleet.IsReversing)
                        fleet.RevealedUntilTick = Math.Max(fleet.RevealedUntilTick, tick + _balance.RevealTicks);
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
                case CommandType.Reverse:
                    Reverse(command.PlayerId, command.Target);
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
            if (!_fleets.Has(fleetId))
                return;
            ref var fleet = ref _fleets.Get(fleetId);
            if (fleet.OwnerPlayerId != playerId || fleet.EwShips <= 0 || fleet.RwrWarningTick == 0 || fleet.Lane.IsNull)
                return;

            bool anyHostileScan = false;
            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                if (scan.Lane == fleet.Lane && scan.OwnerPlayerId != playerId &&
                    scan.IntegrationTicks < _balance.JammedIntegrationTicks)
                    anyHostileScan = true;
            }
            if (!anyHostileScan || !TryPay(playerId, _balance.JamBatteries))
                return;

            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                if (scan.Lane == fleet.Lane && scan.OwnerPlayerId != playerId)
                    scan.IntegrationTicks = Math.Max(scan.IntegrationTicks, _balance.JammedIntegrationTicks);
            }
        }

        private void Reverse(int playerId, EntityId fleetId)
        {
            if (!_fleets.Has(fleetId))
                return;
            ref var fleet = ref _fleets.Get(fleetId);
            if (fleet.OwnerPlayerId == playerId)
                fleet.IsReversing = true;
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
