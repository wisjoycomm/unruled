using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>A fleet's standing supply run between its hub and an outpost. Owned by <see cref="ConvoyRouteSystem"/>.</summary>
    public struct ConvoyRoute
    {
        public EntityId Hub;
        public EntityId Outpost;
        public int Batch;
    }

    /// <summary>
    /// Owns <see cref="ConvoyRoute"/> (GDD §1: CargoConvoys feed remote mines). <see cref="CommandType.SetConvoyRoute"/>
    /// (target: own fleet with cargo convoys, docked at an own site, which becomes the hub;
    /// <see cref="DelayedPlayerCommand.ParamEntity"/>: another own site, the outpost; ParamC: the batch, at most cargo
    /// convoys x capacity; 0 or less ends the route). Every check interval a docked fleet that can act and is out of
    /// combat works its route without signal latency. At the hub it unloads everything but fuel and coolant, takes on
    /// coolant up to the batch and fuel up to the batch plus the round trip, and leaves when it can top up an outpost
    /// below half a batch of fuel or coolant, or the outpost holds a batch of goods. At the outpost it tops the
    /// outpost's fuel and coolant up to the batch (keeping its fuel home), loads up to a batch of every other item
    /// there and heads home. Elsewhere, moving or engaged, the route waits; it ends when either site is lost.
    /// Contract convoys can't run a route. Stolen units keep their tags.
    /// </summary>
    public sealed class ConvoyRouteSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<ConvoyRoute> _routes;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<ContractCargo> _contracts;
        private readonly ComponentStore<StolenGoods> _stolen;
        private readonly RoutePlanner _planner;
        private readonly FleetMovementSystem _movement;
        private readonly ConvoyBalance _balance;
        private FleetRoute _scratch;

        public ConvoyRouteSystem(
            ComponentStore<ConvoyRoute> routes, ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions,
            ComponentStore<FleetReadiness> readiness, ComponentStore<FleetCombatState> combat,
            ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles,
            ComponentStore<ContractCargo> contracts, ComponentStore<StolenGoods> stolen, RoutePlanner planner,
            FleetMovementSystem movement, ConvoyBalance balance)
        {
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
            _stolen = stolen ?? throw new ArgumentNullException(nameof(stolen));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.SetConvoyRoute)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var fleet = command.Target;
            if (!_units.Has(fleet) || _units.Get(fleet).OwnerPlayerId != command.PlayerId || _contracts.Has(fleet))
                return;
            if (command.ParamC <= 0)
            {
                if (_routes.Has(fleet))
                    _routes.Remove(fleet);
                return;
            }

            ref var motion = ref _motions.Get(fleet);
            ref var unit = ref _units.Get(fleet);
            var hub = motion.Site;
            var outpost = command.ParamEntity;
            if (!motion.Lane.IsNull || hub == outpost || !IsOwnSite(command.PlayerId, hub) ||
                !IsOwnSite(command.PlayerId, outpost) || unit.CargoConvoys <= 0 ||
                command.ParamC > (long)unit.CargoConvoys * _balance.CapacityPerConvoy ||
                !_planner.TryPlan(hub, outpost, unit.TechTier, ref _scratch))
                return;

            var route = new ConvoyRoute { Hub = hub, Outpost = outpost, Batch = command.ParamC };
            if (_routes.Has(fleet))
                _routes.Get(fleet) = route;
            else
                _routes.Add(fleet, route);
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.CheckIntervalTicks != 0)
                return;
            for (int i = _routes.Count - 1; i >= 0; i--)
            {
                var fleet = _routes.EntityAt(i);
                var route = _routes.At(i);
                int owner = _units.Get(fleet).OwnerPlayerId;
                if (!IsOwnSite(owner, route.Hub) || !IsOwnSite(owner, route.Outpost))
                {
                    _routes.Remove(fleet);
                    continue;
                }
                ref var motion = ref _motions.Get(fleet);
                if (!motion.Lane.IsNull || !_readiness.Get(fleet).CanAct(tick) || tick <= _combat.Get(fleet).EngagedUntilTick)
                    continue;
                if (motion.Site == route.Hub)
                    AtHub(fleet, in route, tick);
                else if (motion.Site == route.Outpost)
                    AtOutpost(fleet, in route, tick);
            }
        }

        private void AtHub(EntityId fleet, in ConvoyRoute route, long tick)
        {
            long roundTrip = 2 * OneWayFuel(fleet, route.Hub, route.Outpost);
            if (roundTrip < 0)
                return;
            ref var hold = ref _stockpiles.Get(fleet);
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item) && hold.Get(item) > 0)
                    CargoMover.Move(_stockpiles, _stolen, fleet, route.Hub, item, hold.Get(item));
            }
            Fill(fleet, route.Hub, ItemId.ItemCoolant, route.Batch);
            Fill(fleet, route.Hub, ItemId.ItemFuel, (int)Math.Min(route.Batch + roundTrip, int.MaxValue));

            if (hold.Get(ItemId.ItemFuel) < roundTrip)
                return;
            ref var outpost = ref _stockpiles.Get(route.Outpost);
            int half = route.Batch / 2;
            bool coolantRun = outpost.Get(ItemId.ItemCoolant) < half && hold.Get(ItemId.ItemCoolant) > 0;
            bool fuelRun = outpost.Get(ItemId.ItemFuel) < half && hold.Get(ItemId.ItemFuel) > roundTrip;
            if (coolantRun || fuelRun || Goods(ref outpost) >= route.Batch)
                _movement.TryMoveTo(fleet, route.Outpost, tick);
        }

        private void AtOutpost(EntityId fleet, in ConvoyRoute route, long tick)
        {
            long home = OneWayFuel(fleet, route.Outpost, route.Hub);
            if (home < 0)
                return;
            ref var hold = ref _stockpiles.Get(fleet);
            ref var outpost = ref _stockpiles.Get(route.Outpost);
            int coolant = Math.Min(hold.Get(ItemId.ItemCoolant), route.Batch - outpost.Get(ItemId.ItemCoolant));
            if (coolant > 0)
                CargoMover.Move(_stockpiles, _stolen, fleet, route.Outpost, ItemId.ItemCoolant, coolant);
            long spare = Math.Min(hold.Get(ItemId.ItemFuel) - home, route.Batch - outpost.Get(ItemId.ItemFuel));
            if (spare > 0)
                CargoMover.Move(_stockpiles, _stolen, fleet, route.Outpost, ItemId.ItemFuel, (int)spare);
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item))
                    Fill(fleet, route.Outpost, item, route.Batch);
            }
            _movement.TryMoveTo(fleet, route.Hub, tick);
        }

        // Tops the hold up to target from the site's stock, as far as the site has it.
        private void Fill(EntityId fleet, EntityId site, ItemId item, int target)
        {
            int quantity = Math.Min(target - _stockpiles.Get(fleet).Get(item), _stockpiles.Get(site).Get(item));
            if (quantity > 0)
                CargoMover.Move(_stockpiles, _stolen, site, fleet, item, quantity);
        }

        private long OneWayFuel(EntityId fleet, EntityId from, EntityId to)
        {
            ref var unit = ref _units.Get(fleet);
            if (!_planner.TryPlan(from, to, unit.TechTier, ref _scratch))
                return -1;
            long fuel = 0;
            for (int i = 0; i < _scratch.Count; i++)
                fuel += _planner.FuelFor(unit, _lanes.Get(_scratch.LaneAt(i)));
            return fuel;
        }

        private static long Goods(ref Stockpile stock)
        {
            long total = 0;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item))
                    total += stock.Get(item);
            }
            return total;
        }

        private static bool IsSupply(ItemId item) => item == ItemId.ItemFuel || item == ItemId.ItemCoolant;

        private bool IsOwnSite(int playerId, EntityId site) =>
            _sites.Has(site) && _sites.Get(site).OwnerPlayerId == playerId && _stockpiles.Has(site);
    }
}
