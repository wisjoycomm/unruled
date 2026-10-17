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
    /// convoys x capacity; 0 or less ends the route). Every check interval a docked fleet out of combat works its route
    /// without signal latency; at the hub it unloads even when no route out is open. Supplies are fuel, coolant, oxygen and rations (GDD §1); the fleet keeps a reserve of
    /// its own: the fuel for the trip ahead, and its oxygen and rations upkeep for the reserve intervals. At the hub it
    /// unloads everything else, takes on each supply up to the batch plus its reserve (even while grounded for unpaid
    /// upkeep) from what the hub holds above its supply reserve, and leaves, once it can act, when it carries half a batch of a supply above its reserve for an outpost below half a batch of it (so a run never
    /// burns more fuel than it hauls), or the
    /// outpost holds a batch of goods and what a run would load there is worth more than the fuel of the round trip (base
    /// WTO prices: hauling cheap ore down a long route would burn more value than it brings home), or the hub is short of
    /// one (holds less than a batch of a good the outpost has a batch of: it is needed whatever its price). At the outpost it tops each supply up to the batch from what it carries above
    /// its reserve, loads up to a batch of every other item there and heads home. Docked anywhere else (its route was cut
    /// short) it heads back to the hub; moving or engaged, the route waits; it ends when either site is lost. Each run takes the fastest route whose STA tolls the owner's wallet
    /// covers there and back, else the toll-free one (bypass lanes: 0 credits, more fuel); the fuel reserve follows the
    /// route taken, and a run out carries the fuel to come home by the toll-free route too, in case the owner can no longer
    /// pay the tolls by then. Contract convoys can't run a route. Stolen units keep their tags.
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
        private readonly ITollGate _tolls;
        private readonly Ledger _ledger;
        private readonly ConvoyBalance _balance;
        private readonly FleetBalance _fleetBalance;
        private readonly RecipeDatabase _recipes;
        private FleetRoute _scratch;

        public ConvoyRouteSystem(
            ComponentStore<ConvoyRoute> routes, ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions,
            ComponentStore<FleetReadiness> readiness, ComponentStore<FleetCombatState> combat,
            ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles,
            ComponentStore<ContractCargo> contracts, ComponentStore<StolenGoods> stolen, RoutePlanner planner,
            FleetMovementSystem movement, ITollGate tolls, Ledger ledger, ConvoyBalance balance, FleetBalance fleetBalance,
            RecipeDatabase recipes)
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
            _tolls = tolls ?? throw new ArgumentNullException(nameof(tolls));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
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
                if (!motion.Lane.IsNull || tick <= _combat.Get(fleet).EngagedUntilTick)
                    continue;
                if (motion.Site == route.Hub)
                    AtHub(fleet, in route, tick);
                else if (motion.Site == route.Outpost)
                    AtOutpost(fleet, in route, tick);
                else if (OneWayFuel(fleet, motion.Site, route.Hub, out bool tollFree) >= 0)
                    _movement.TryMoveTo(fleet, route.Hub, tick, tollFree);
            }
        }

        private void AtHub(EntityId fleet, in ConvoyRoute route, long tick)
        {
            ref var hold = ref _stockpiles.Get(fleet);
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item) && hold.Get(item) > 0)
                    CargoMover.Move(_stockpiles, _stolen, fleet, route.Hub, item, hold.Get(item));
            }
            long oneWay = OneWayFuel(fleet, route.Hub, route.Outpost, out bool tollFree);
            long back = HomeFuel(fleet, route.Outpost, route.Hub);
            if (oneWay < 0 || back < 0)
                return;
            long roundTrip = oneWay + back;
            for (int s = 0; s < Supplies.Length; s++)
                Fill(fleet, route.Hub, Supplies[s], (int)Math.Min(route.Batch + Reserve(fleet, Supplies[s], roundTrip), int.MaxValue),
                    _balance.HubSupplyReserve);

            if (!_readiness.Get(fleet).CanAct(tick) || hold.Get(ItemId.ItemFuel) < roundTrip)
                return;
            ref var outpost = ref _stockpiles.Get(route.Outpost);
            bool run = Goods(ref outpost) >= route.Batch &&
                       (GoodsWorth(ref outpost, route.Batch) > roundTrip * _recipes.GetItem(ItemId.ItemFuel).BaseWtoPrice ||
                        IsShort(ref _stockpiles.Get(route.Hub), ref outpost, route.Batch));
            for (int s = 0; s < Supplies.Length && !run; s++)
                run = outpost.Get(Supplies[s]) < route.Batch / 2 &&
                      hold.Get(Supplies[s]) - Reserve(fleet, Supplies[s], roundTrip) >= route.Batch / 2;
            if (run)
                _movement.TryMoveTo(fleet, route.Outpost, tick, tollFree);
        }

        private void AtOutpost(EntityId fleet, in ConvoyRoute route, long tick)
        {
            long home = OneWayFuel(fleet, route.Outpost, route.Hub, out bool tollFree);
            if (home < 0)
                return;
            ref var hold = ref _stockpiles.Get(fleet);
            ref var outpost = ref _stockpiles.Get(route.Outpost);
            for (int s = 0; s < Supplies.Length; s++)
            {
                var item = Supplies[s];
                long spare = Math.Min(hold.Get(item) - Reserve(fleet, item, home), route.Batch - outpost.Get(item));
                if (spare > 0)
                    CargoMover.Move(_stockpiles, _stolen, fleet, route.Outpost, item, (int)spare);
            }
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item))
                    Fill(fleet, route.Outpost, item, route.Batch, 0);
            }
            _movement.TryMoveTo(fleet, route.Hub, tick, tollFree);
        }

        // Tops the hold up to target from what the site holds above its reserve.
        private void Fill(EntityId fleet, EntityId site, ItemId item, int target, int siteReserve)
        {
            int quantity = Math.Min(target - _stockpiles.Get(fleet).Get(item), _stockpiles.Get(site).Get(item) - siteReserve);
            if (quantity > 0)
                CargoMover.Move(_stockpiles, _stolen, site, fleet, item, quantity);
        }

        // Fuel for one leg on the route the run takes (see the class summary); -1 if there is none.
        private long OneWayFuel(EntityId fleet, EntityId from, EntityId to, out bool tollFree)
        {
            ref var unit = ref _units.Get(fleet);
            tollFree = false;
            if (!_planner.TryPlan(from, to, unit.TechTier, ref _scratch))
                return -1;
            long tolls = 0;
            for (int i = 0; i < _scratch.Count; i++)
                tolls += _tolls.QuoteCents(unit, _lanes.Get(_scratch.LaneAt(i)));
            if (tolls > 0 && (!_ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var wallet) ||
                              2 * tolls > _ledger.Balance(wallet)))
            {
                tollFree = true;
                if (!_planner.TryPlan(from, to, unit.TechTier, tollFree: true, ref _scratch))
                    return -1;
            }
            long fuel = 0;
            for (int i = 0; i < _scratch.Count; i++)
                fuel += _planner.FuelFor(unit, _lanes.Get(_scratch.LaneAt(i)));
            return fuel;
        }

        // Fuel for the leg home on whichever route the wallet allows by then: the toll-free one when it takes more.
        private long HomeFuel(EntityId fleet, EntityId from, EntityId to)
        {
            long fuel = OneWayFuel(fleet, from, to, out bool tollFree);
            ref var unit = ref _units.Get(fleet);
            if (fuel < 0 || tollFree || !_planner.TryPlan(from, to, unit.TechTier, tollFree: true, ref _scratch))
                return fuel;
            long bypass = 0;
            for (int i = 0; i < _scratch.Count; i++)
                bypass += _planner.FuelFor(unit, _lanes.Get(_scratch.LaneAt(i)));
            return Math.Max(fuel, bypass);
        }

        // What a run would load at the site (up to a batch of each good), at base WTO prices.
        private long GoodsWorth(ref Stockpile stock, int batch)
        {
            long worth = 0;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item))
                    worth += (long)Math.Min(stock.Get(item), batch) * _recipes.GetItem(item).BaseWtoPrice;
            }
            return worth;
        }

        // Whether the hub holds less than a batch of a good the outpost holds a batch of.
        private static bool IsShort(ref Stockpile hub, ref Stockpile outpost, int batch)
        {
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (!IsSupply(item) && outpost.Get(item) >= batch && hub.Get(item) < batch)
                    return true;
            }
            return false;
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

        private static readonly ItemId[] Supplies = { ItemId.ItemFuel, ItemId.ItemCoolant, ItemId.ItemOxygen, ItemId.ItemRations };

        private static bool IsSupply(ItemId item) =>
            item == ItemId.ItemFuel || item == ItemId.ItemCoolant || item == ItemId.ItemOxygen || item == ItemId.ItemRations;

        // What the fleet keeps of a supply for itself: the trip's fuel, or its upkeep for the reserve intervals.
        private long Reserve(EntityId fleet, ItemId item, long tripFuel)
        {
            if (item == ItemId.ItemFuel)
                return tripFuel;
            ref var unit = ref _units.Get(fleet);
            var upkeep = _fleetBalance.Tier(unit.TechTier).UpkeepPerShipHour;
            for (int k = 0; k < upkeep.Length; k++)
                if (upkeep[k].Item == item)
                    return (long)FleetUpkeepSystem.Due(unit.TotalShips, upkeep[k].Milli, 0) * _balance.UpkeepReserveIntervals;
            return 0;
        }

        private bool IsOwnSite(int playerId, EntityId site) =>
            _sites.Has(site) && _sites.Get(site).OwnerPlayerId == playerId && _stockpiles.Has(site);
    }
}
