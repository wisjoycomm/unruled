using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns <see cref="FleetMotion"/>: one lane per order.
    /// Move (target: own fleet at a site, <see cref="DelayedPlayerCommand.ParamEntity"/>: a lane from that site) pays
    /// fuel from the fleet's hold at departure: ships x fuel per 100 units x length x tier fuel multiplier, rounded up.
    /// Lanes above the fleet's tech tier (storm shortcuts) are refused. Each tick a moving fleet advances base speed x
    /// tier speed %, and arrives at the far end when it has covered the lane.
    /// Fleets that cannot act (<see cref="FleetReadiness.CanAct"/>) neither depart nor advance.
    /// Hold (ParamA 1 = hold, 0 = resume) stops a fleet mid-lane. Reverse turns it back towards the site it left (and ends a hold).
    /// Invalid orders change nothing.
    /// </summary>
    public sealed class FleetMovementSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly StarlaneBalance _laneBalance;
        private readonly FleetBalance _balance;

        public FleetMovementSystem(
            ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions, ComponentStore<FleetReadiness> readiness,
            ComponentStore<StarlaneEdge> lanes,
            ComponentStore<Stockpile> stockpiles, StarlaneBalance laneBalance, FleetBalance balance)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _laneBalance = laneBalance ?? throw new ArgumentNullException(nameof(laneBalance));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            for (int i = 0; i < _motions.Count; i++)
            {
                ref var motion = ref _motions.At(i);
                if (motion.Lane.IsNull || motion.IsHolding || !_readiness.Get(_motions.EntityAt(i)).CanAct(tick))
                    continue;

                ref var edge = ref _lanes.Get(motion.Lane);
                motion.TravelledMilli += SpeedMilli(edge.Tier);
                if (motion.TravelledMilli < edge.Length * 1000L)
                    continue;

                motion.Site = edge.OtherEnd(motion.FromSite);
                motion.ArrivalLane = motion.Lane;
                motion.Lane = default;
                motion.FromSite = default;
                motion.TravelledMilli = 0;
                motion.IsReversing = false;
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.Move:
                    Move(command.PlayerId, command.Target, command.ParamEntity, tick);
                    break;
                case CommandType.Hold:
                    Hold(command.PlayerId, command.Target, command.ParamA != 0);
                    break;
                case CommandType.Reverse:
                    Reverse(command.PlayerId, command.Target);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        /// <summary>Fuel a fleet pays to cross a lane, in whole units (rounded up).</summary>
        public int FuelFor(in FleetUnit unit, in StarlaneEdge edge)
        {
            long milli = (long)unit.TotalShips * _balance.FuelPerShipPer100UnitsMilli * edge.Length *
                         _laneBalance.Get(edge.Tier).FuelMultiplierMilli / (100L * 1000L);
            return (int)((milli + 999) / 1000);
        }

        private long SpeedMilli(StarlaneTier tier) =>
            (long)_balance.BaseSpeedMilliPerTick * _laneBalance.Get(tier).SpeedPercent / 100;

        private void Move(int playerId, EntityId fleet, EntityId lane, long tick)
        {
            if (!IsOwnFleet(playerId, fleet) || !_lanes.Has(lane) || !_readiness.Get(fleet).CanAct(tick))
                return;
            ref var motion = ref _motions.Get(fleet);
            ref var edge = ref _lanes.Get(lane);
            if (motion.Site.IsNull || (edge.SiteA != motion.Site && edge.SiteB != motion.Site))
                return;
            ref var unit = ref _units.Get(fleet);
            if (unit.TechTier < _laneBalance.Get(edge.Tier).MinTechTier)
                return;

            ref var hold = ref _stockpiles.Get(fleet);
            int fuel = FuelFor(unit, edge);
            if (!hold.Has(ItemId.ItemFuel, fuel))
                return;
            hold.Add(ItemId.ItemFuel, -fuel);

            motion.FromSite = motion.Site;
            motion.Site = default;
            motion.Lane = lane;
            motion.TravelledMilli = 0;
            motion.IsHolding = false;
            motion.IsReversing = false;
        }

        private void Hold(int playerId, EntityId fleet, bool hold)
        {
            if (!IsOwnFleet(playerId, fleet))
                return;
            ref var motion = ref _motions.Get(fleet);
            if (!motion.Lane.IsNull)
                motion.IsHolding = hold;
        }

        private void Reverse(int playerId, EntityId fleet)
        {
            if (!IsOwnFleet(playerId, fleet))
                return;
            ref var motion = ref _motions.Get(fleet);
            if (motion.Lane.IsNull || motion.IsReversing)
                return;

            ref var edge = ref _lanes.Get(motion.Lane);
            motion.FromSite = edge.OtherEnd(motion.FromSite);
            motion.TravelledMilli = edge.Length * 1000L - motion.TravelledMilli;
            motion.IsReversing = true;
            motion.IsHolding = false;
        }

        private bool IsOwnFleet(int playerId, EntityId fleet) =>
            _units.Has(fleet) && _units.Get(fleet).OwnerPlayerId == playerId;
    }
}
