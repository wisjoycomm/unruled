using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>Why a Move order would leave a fleet where it is (see <see cref="FleetMovementSystem.CheckMove"/>).</summary>
    public enum MoveCheck : byte
    {
        Ok = 0,
        NotDocked = 1, // on a lane: hold or reverse instead
        CannotAct = 2, // mothballed, unsupplied or warming up
        Uncharted = 3, // the destination (or the lane's far end) is in a system the owner doesn't know
        NoRoute = 4, // no lane from here, or no route within reach of the fleet's tier (or toll-free when asked)
        TierTooLow = 5, // the first lane needs a higher tech tier (storm shortcuts)
        NoFuel = 6, // the hold lacks the first lane's fuel
        NoToll = 7 // the owner's wallet can't pay the first lane's STA toll
    }

    /// <summary>What a Move would cost: the reason it fails (or Ok) and the first lane's and the whole route's fuel and toll.</summary>
    public struct MoveQuote
    {
        public MoveCheck Check;
        public int FirstFuel;
        public long FirstTollCents;
        public int RouteFuel;
        public long RouteTollCents;
        public int Lanes;
    }

    /// <summary>
    /// Owns <see cref="FleetMotion"/> and <see cref="FleetRoute"/>.
    /// Move (target: own fleet at a site; <see cref="DelayedPlayerCommand.ParamEntity"/>: a lane from that site, or a
    /// destination site; ParamC 1 = route around STA toll lanes) departs at once. A destination is routed by
    /// <see cref="RoutePlanner"/> once, at the order;
    /// on each arrival the fleet departs on the next lane of its route the same tick, and its owner learns the system
    /// it arrived in (<see cref="IStarCharts"/>). A destination, or a lane's far end, in a system the owner doesn't
    /// know is refused: fleets fly only to charted space (they may pass through, and so learn, systems on the way).
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
        private readonly IStarCharts _charts;
        private readonly StarlaneBalance _laneBalance;
        private readonly FleetBalance _balance;

        public FleetMovementSystem(
            ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions, ComponentStore<FleetRoute> routes,
            ComponentStore<FleetReadiness> readiness, ComponentStore<FleetCombatState> combat,
            ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles,
            RoutePlanner planner, ITollGate tolls, IStarCharts charts, StarlaneBalance laneBalance, FleetBalance balance)
        {
            _charts = charts ?? throw new ArgumentNullException(nameof(charts));
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
                _charts.FleetArrived(_units.Get(fleet).OwnerPlayerId, motion.Site, tick);

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
                    Move(command.PlayerId, command.Target, command.ParamEntity, command.ParamC != 0, tick);
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

        /// <summary>
        /// What a Move to <paramref name="laneOrSite"/> would do now, without changing anything: why it would be refused
        /// (the same checks Move and departure run, in their order), and the fuel and STA toll of the first lane and of
        /// the whole route. The route's later lanes are paid on the way, so only the first lane must be affordable now.
        /// </summary>
        public MoveQuote CheckMove(EntityId fleet, EntityId laneOrSite, bool tollFree, long tick)
        {
            var quote = new MoveQuote();
            if (!_units.Has(fleet))
            {
                quote.Check = MoveCheck.NoRoute;
                return quote;
            }
            ref var unit = ref _units.Get(fleet);
            ref var motion = ref _motions.Get(fleet);
            if (motion.Site.IsNull)
            {
                quote.Check = MoveCheck.NotDocked;
                return quote;
            }
            if (!_readiness.Get(fleet).CanAct(tick))
            {
                quote.Check = MoveCheck.CannotAct;
                return quote;
            }
            var route = default(FleetRoute);
            if (_lanes.Has(laneOrSite))
            {
                ref var edge = ref _lanes.Get(laneOrSite);
                if (edge.SiteA != motion.Site && edge.SiteB != motion.Site)
                {
                    quote.Check = MoveCheck.NoRoute;
                    return quote;
                }
                if (!_charts.KnowsSite(unit.OwnerPlayerId, edge.OtherEnd(motion.Site)))
                {
                    quote.Check = MoveCheck.Uncharted;
                    return quote;
                }
                route.Append(laneOrSite);
            }
            else if (!_sites.Has(laneOrSite) || !_charts.KnowsSite(unit.OwnerPlayerId, laneOrSite))
            {
                quote.Check = _sites.Has(laneOrSite) ? MoveCheck.Uncharted : MoveCheck.NoRoute;
                return quote;
            }
            else if (!_planner.TryPlan(motion.Site, laneOrSite, unit.TechTier, tollFree, ref route))
            {
                quote.Check = MoveCheck.NoRoute;
                return quote;
            }

            quote.Lanes = route.Count;
            for (int i = 0; i < route.Count; i++)
            {
                ref var edge = ref _lanes.Get(route.LaneAt(i));
                int fuel = FuelFor(unit, edge);
                long toll = _tolls.QuoteCents(unit, edge);
                quote.RouteFuel += fuel;
                quote.RouteTollCents += toll;
                if (i == 0)
                {
                    quote.FirstFuel = fuel;
                    quote.FirstTollCents = toll;
                }
            }
            ref var first = ref _lanes.Get(route.LaneAt(0));
            if (unit.TechTier < _laneBalance.Get(first.Tier).MinTechTier)
                quote.Check = MoveCheck.TierTooLow;
            else if (!_stockpiles.Get(fleet).Has(ItemId.ItemFuel, quote.FirstFuel))
                quote.Check = MoveCheck.NoFuel;
            else if (!_tolls.CanPay(unit, first))
                quote.Check = MoveCheck.NoToll;
            return quote;
        }

        /// <summary>Fuel a fleet pays to cross a lane, in whole units (rounded up).</summary>
        public int FuelFor(in FleetUnit unit, in StarlaneEdge edge) => _planner.FuelFor(unit, edge);

        private long SpeedMilli(StarlaneTier tier) =>
            (long)_balance.BaseSpeedMilliPerTick * _laneBalance.Get(tier).SpeedPercent / 100;

        private void Move(int playerId, EntityId fleet, EntityId lane, bool tollFree, long tick)
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
                if (_charts.KnowsSite(playerId, _lanes.Get(lane).OtherEnd(motion.Site)))
                    TryDepart(fleet, ref motion, lane);
                return;
            }
            TryMoveTo(fleet, lane, tick, tollFree);
        }

        /// <summary>
        /// Sends a docked fleet that can act to a site along a planned route, as a Move order to a destination does.
        /// For standing orders run by other systems (convoy routes); false if it could not leave. tollFree routes around
        /// STA toll lanes.
        /// </summary>
        public bool TryMoveTo(EntityId fleet, EntityId site, long tick, bool tollFree = false)
        {
            if (!_units.Has(fleet) || !_readiness.Get(fleet).CanAct(tick) || !_sites.Has(site))
                return false;
            ref var motion = ref _motions.Get(fleet);
            if (motion.Site.IsNull)
                return false;
            ref var route = ref _routes.Get(fleet);
            ref var unit = ref _units.Get(fleet);
            if (!_charts.KnowsSite(unit.OwnerPlayerId, site) ||
                !_planner.TryPlan(motion.Site, site, unit.TechTier, tollFree, ref route))
                return false;
            if (TryDepart(fleet, ref motion, route.LaneAt(route.Next++)))
                return true;
            route.Clear();
            return false;
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
