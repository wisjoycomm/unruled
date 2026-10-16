using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns <see cref="FleetMotion"/> and <see cref="FleetRoute"/>.
    /// Move (target: own fleet at a site; <see cref="DelayedPlayerCommand.ParamEntity"/>: a lane from that site, or a
    /// destination site) departs at once. A destination is routed by <see cref="RoutePlanner"/> once, at the order;
    /// on each arrival the fleet departs on the next lane of its route the same tick.
    /// Every departure pays fuel from the fleet's hold (ships x fuel per 100 units x length x tier fuel multiplier,
    /// rounded up) and whatever the <see cref="ITollGate"/> charges for the lane; if either can't be paid the fleet
    /// stays at the site, nothing is paid, and the rest of its route is dropped. Lanes above the
    /// fleet's tech tier (storm shortcuts) are refused. Each tick a moving fleet advances base speed x tier speed %.
    /// Fleets that cannot act (<see cref="FleetReadiness.CanAct"/>) neither depart nor advance; a fleet engaged in
    /// combat on its lane is pinned there unless it is reversing.
    /// Hold (ParamA 1 = hold, 0 = resume) stops a fleet mid-lane and keeps its route. Reverse turns it back towards
    /// the site it left, ends a hold and drops the route. Invalid orders change nothing.
    /// </summary>
    public sealed class FleetMovementSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetRoute> _routes;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly RoutePlanner _planner;
        private readonly ITollGate _tolls;
        private readonly StarlaneBalance _laneBalance;
        private readonly FleetBalance _balance;

        public FleetMovementSystem(
            ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions, ComponentStore<FleetRoute> routes,
            ComponentStore<FleetReadiness> readiness, ComponentStore<FleetCombatState> combat,
            ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles,
            RoutePlanner planner, ITollGate tolls, StarlaneBalance laneBalance, FleetBalance balance)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _tolls = tolls ?? throw new ArgumentNullException(nameof(tolls));
            _laneBalance = laneBalance ?? throw new ArgumentNullException(nameof(laneBalance));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            for (int i = 0; i < _motions.Count; i++)
            {
                ref var motion = ref _motions.At(i);
                var fleet = _motions.EntityAt(i);
                if (motion.Lane.IsNull || motion.IsHolding || !_readiness.Get(fleet).CanAct(tick))
                    continue;
                if (!motion.IsReversing && tick <= _combat.Get(fleet).EngagedUntilTick)
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

                ref var route = ref _routes.Get(fleet);
                if (!route.HasNext)
                    route.Clear();
                else if (!TryDepart(fleet, ref motion, route.LaneAt(route.Next++)))
                    route.Clear();
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
            if (!IsOwnFleet(playerId, fleet) || !_readiness.Get(fleet).CanAct(tick))
                return;
            ref var motion = ref _motions.Get(fleet);
            if (motion.Site.IsNull)
                return;

            ref var route = ref _routes.Get(fleet);
            if (_lanes.Has(lane))
            {
                route.Clear();
                TryDepart(fleet, ref motion, lane);
                return;
            }
            if (!_sites.Has(lane) || !_planner.TryPlan(motion.Site, lane, _units.Get(fleet).TechTier, ref route))
                return;
            if (!TryDepart(fleet, ref motion, route.LaneAt(route.Next++)))
                route.Clear();
        }

        private bool TryDepart(EntityId fleet, ref FleetMotion motion, EntityId lane)
        {
            ref var edge = ref _lanes.Get(lane);
            if (edge.SiteA != motion.Site && edge.SiteB != motion.Site)
                return false;
            ref var unit = ref _units.Get(fleet);
            if (unit.TechTier < _laneBalance.Get(edge.Tier).MinTechTier)
                return false;

            ref var hold = ref _stockpiles.Get(fleet);
            int fuel = FuelFor(unit, edge);
            if (!hold.Has(ItemId.ItemFuel, fuel) || !_tolls.TryCharge(unit, edge))
                return false;
            hold.Add(ItemId.ItemFuel, -fuel);

            motion.FromSite = motion.Site;
            motion.Site = default;
            motion.Lane = lane;
            motion.TravelledMilli = 0;
            motion.IsHolding = false;
            motion.IsReversing = false;
            return true;
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
            _routes.Get(fleet).Clear();
        }

        private bool IsOwnFleet(int playerId, EntityId fleet) =>
            _units.Has(fleet) && _units.Get(fleet).OwnerPlayerId == playerId;
    }
}
