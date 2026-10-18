using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>ECS component on its own entity: ships being built at a site, delivered at <see cref="DoneTick"/>.</summary>
    public struct ShipBuildOrder
    {
        public int OwnerPlayerId;
        public EntityId Site;
        public TargetFocusClass ShipClass;
        public int Count;
        public byte TechTier;
        public long DoneTick;
    }

    /// <summary>
    /// Owns <see cref="ShipBuildOrder"/> and the shipyard's changes to <see cref="FleetUnit"/> (new ships, tech tier).
    /// <list type="bullet">
    /// <item><see cref="CommandType.BuildShips"/> (target: the player's core base; ParamA = ship class, ParamB =
    /// count, ParamC = tech tier up to the player's research tier): pays the tier's build items per ship from the
    /// core base at once; the ships come out after count × the tier's build time. Cargo convoys always form a new
    /// convoy fleet (one per route); other ships join a docked fleet of the same owner and tier at the site that is not
    /// convoy-only, or form a new one.</item>
    /// <item><see cref="CommandType.Retrofit"/> (target: a fleet docked at its owner's core base): raises the fleet
    /// to the player's research tier for a share of the new tier's build cost per ship (GDD §2.3 TRL 7–9).</item>
    /// <item><see cref="CommandType.MergeFleets"/> (target: an own fleet; <see cref="DelayedPlayerCommand.ParamEntity"/>:
    /// another own fleet of the same tier docked at the same site, both active, out of combat and without cargo convoys):
    /// the target takes in its ships and its hold, and the other is left empty, as a fleet whose ships were all lost.
    /// PROVISIONAL (the GDD has no merge): ships built while a battle fleet is away or mothballed form a second fleet,
    /// and only a whole battle line outguns the Citadel garrison.</item>
    /// <item><see cref="CommandType.SplitFleet"/> (target: an own fleet, docked, active, out of combat and without cargo
    /// convoys; ParamA / ParamB / ParamC = line / EW / supply ships to detach, the fleet keeping at least one ship): the
    /// detached ships form a new fleet at the same site and tier, which takes their share of every item in the hold
    /// (rounded down), stolen tags included. PROVISIONAL (GDD §2.5 has fleets split for multi-lane flanking, no rule).</item>
    /// </list>
    /// Invalid or unaffordable orders change nothing.
    /// </summary>
    public sealed class ShipyardSystem : ISimSystem, ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<ShipBuildOrder> _orders;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<StolenGoods> _stolen;
        private readonly FleetFactory _factory;
        private readonly ITechGates _tech;
        private readonly ShipyardBalance _balance;

        private EntityId[] _done = new EntityId[8];

        public ShipyardSystem(
            World world, ComponentStore<ShipBuildOrder> orders, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetReadiness> readiness,
            ComponentStore<FleetCombatState> combat, ComponentStore<PlayerHq> players, ComponentStore<SiteInfo> sites,
            ComponentStore<Stockpile> stockpiles, ComponentStore<StolenGoods> stolen, FleetFactory factory, ITechGates tech,
            ShipyardBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _stolen = stolen ?? throw new ArgumentNullException(nameof(stolen));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _tech = tech ?? throw new ArgumentNullException(nameof(tech));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            int count = 0;
            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders.At(i).DoneTick > tick)
                    continue;
                if (count == _done.Length)
                    Array.Resize(ref _done, count * 2);
                _done[count++] = _orders.EntityAt(i);
            }
            for (int i = 0; i < count; i++)
            {
                Deliver(in _orders.Get(_done[i]), tick);
                _world.Destroy(_done[i]);
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.BuildShips:
                    Build(in command, tick);
                    break;
                case CommandType.Retrofit:
                    Retrofit(in command, tick);
                    break;
                case CommandType.MergeFleets:
                    Merge(in command, tick);
                    break;
                case CommandType.SplitFleet:
                    Split(in command, tick);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        private void Build(in DelayedPlayerCommand command, long tick)
        {
            if (command.ParamA < (int)TargetFocusClass.LineShips || command.ParamA > (int)TargetFocusClass.CargoConvoys ||
                command.ParamB < 1 || command.ParamB > _balance.MaxShipsPerOrder ||
                command.ParamC < 1 || command.ParamC > _tech.TechTierOf(command.PlayerId, ResearchBranch.Military))
                return;
            var site = command.Target;
            if (!IsOwnCoreBase(command.PlayerId, site))
                return;
            byte tier = (byte)command.ParamC;
            if (!TryPay(site, tier, command.ParamB, 100))
                return;
            _orders.Add(_world.Create(), new ShipBuildOrder
            {
                OwnerPlayerId = command.PlayerId, Site = site, ShipClass = (TargetFocusClass)command.ParamA,
                Count = command.ParamB, TechTier = tier,
                DoneTick = tick + (long)command.ParamB * _balance.BuildTicksPerShip(tier)
            });
        }

        private void Retrofit(in DelayedPlayerCommand command, long tick)
        {
            var fleet = command.Target;
            if (!_units.Has(fleet) || !_motions.Has(fleet))
                return;
            ref var unit = ref _units.Get(fleet);
            byte tier = _tech.TechTierOf(command.PlayerId, ResearchBranch.Military);
            if (unit.OwnerPlayerId != command.PlayerId || unit.TechTier >= tier || unit.TotalShips == 0 ||
                !IsDocked(fleet, tick) || !IsOwnCoreBase(command.PlayerId, _motions.Get(fleet).Site))
                return;
            if (!TryPay(_motions.Get(fleet).Site, tier, unit.TotalShips, _balance.RetrofitCostPercent))
                return;
            unit.TechTier = tier;
        }

        private void Merge(in DelayedPlayerCommand command, long tick)
        {
            var into = command.Target;
            var from = command.ParamEntity;
            if (into == from || !IsMergeable(command.PlayerId, into, tick) || !IsMergeable(command.PlayerId, from, tick))
                return;
            ref var target = ref _units.Get(into);
            ref var source = ref _units.Get(from);
            if (target.TechTier != source.TechTier || _motions.Get(into).Site != _motions.Get(from).Site ||
                source.LineShips > int.MaxValue - target.LineShips || source.EwShips > int.MaxValue - target.EwShips ||
                source.SupplyTenders > int.MaxValue - target.SupplyTenders)
                return;
            target.LineShips += source.LineShips;
            target.EwShips += source.EwShips;
            target.SupplyTenders += source.SupplyTenders;
            source.LineShips = 0;
            source.EwShips = 0;
            source.SupplyTenders = 0;
            ref var hold = ref _stockpiles.Get(from);
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (hold.Get(item) > 0)
                    CargoMover.Move(_stockpiles, _stolen, from, into, item, hold.Get(item));
            }
        }

        private void Split(in DelayedPlayerCommand command, long tick)
        {
            var from = command.Target;
            if (!IsMergeable(command.PlayerId, from, tick))
                return;
            var source = _units.Get(from);
            int line = command.ParamA, ew = command.ParamB, supply = command.ParamC;
            if (line < 0 || ew < 0 || supply < 0 || line > source.LineShips || ew > source.EwShips ||
                supply > source.SupplyTenders)
                return;
            long moved = (long)line + ew + supply;
            if (moved == 0 || moved >= source.TotalShips)
                return;
            int total = source.TotalShips;
            var detached = new FleetUnit
            {
                OwnerPlayerId = source.OwnerPlayerId, TechTier = source.TechTier, LineShips = line, EwShips = ew,
                SupplyTenders = supply
            };
            var into = _factory.Create(in detached, _motions.Get(from).Site);
            ref var unit = ref _units.Get(from);
            unit.LineShips -= line;
            unit.EwShips -= ew;
            unit.SupplyTenders -= supply;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                int share = (int)((long)_stockpiles.Get(from).Get(item) * moved / total);
                if (share > 0)
                    CargoMover.Move(_stockpiles, _stolen, from, into, item, share);
            }
        }

        private bool IsMergeable(int playerId, EntityId fleet, long tick) =>
            _units.Has(fleet) && _stockpiles.Has(fleet) && _units.Get(fleet).OwnerPlayerId == playerId &&
            _units.Get(fleet).TotalShips > 0 && _units.Get(fleet).CargoConvoys == 0 && IsDocked(fleet, tick);

        private void Deliver(in ShipBuildOrder order, long tick)
        {
            if (!_sites.Has(order.Site))
                return;
            for (int i = 0; i < _units.Count && order.ShipClass != TargetFocusClass.CargoConvoys; i++)
            {
                var fleet = _units.EntityAt(i);
                ref var unit = ref _units.At(i);
                if (unit.OwnerPlayerId != order.OwnerPlayerId || unit.TechTier != order.TechTier || unit.IsConvoyOnly ||
                    !IsDocked(fleet, tick) || _motions.Get(fleet).Site != order.Site)
                    continue;
                unit.Gain(order.ShipClass, order.Count);
                return;
            }
            var created = new FleetUnit { OwnerPlayerId = order.OwnerPlayerId, TechTier = order.TechTier };
            created.Gain(order.ShipClass, order.Count);
            _factory.Create(created, order.Site);
        }

        /// <summary>Pays percent% of count ships' build items for the tier from the site, rounding up; all or nothing.</summary>
        private bool TryPay(EntityId site, byte tier, int ships, int percent)
        {
            if (!_stockpiles.Has(site))
                return false;
            ref var stock = ref _stockpiles.Get(site);
            int n = _balance.CostCount(tier);
            for (int i = 0; i < n; i++)
            {
                var cost = _balance.Cost(tier, i);
                long need = ((long)cost.Quantity * ships * percent + 99) / 100;
                if (need > int.MaxValue || !stock.Has(cost.Item, (int)need))
                    return false;
            }
            for (int i = 0; i < n; i++)
            {
                var cost = _balance.Cost(tier, i);
                stock.Add(cost.Item, -(int)(((long)cost.Quantity * ships * percent + 99) / 100));
            }
            return true;
        }

        private bool IsDocked(EntityId fleet, long tick) =>
            _motions.Has(fleet) && _motions.Get(fleet).Lane.IsNull && !_motions.Get(fleet).Site.IsNull &&
            (!_combat.Has(fleet) || _combat.Get(fleet).EngagedUntilTick <= tick) &&
            (!_readiness.Has(fleet) || !_readiness.Get(fleet).IsMothballed);

        private bool IsOwnCoreBase(int playerId, EntityId site)
        {
            if (!_sites.Has(site) || _sites.Get(site).OwnerPlayerId != playerId)
                return false;
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).PlayerId == playerId)
                    return _players.At(i).CoreBase == site;
            return false;
        }
    }
}
