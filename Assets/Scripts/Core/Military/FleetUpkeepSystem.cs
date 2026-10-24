using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns <see cref="FleetReadiness"/> (data sheet tab 03). Every upkeep interval each fleet pays its tier's upkeep per
    /// ship from its hold, each item rounded up, with the mothball discount while mothballed. A fleet docked at a site
    /// its owner holds draws what its hold lacks from the site's stock first: the shortfall only, and only what the site
    /// holds above its supply reserve, so a fleet parked at the capital never starves the base's own life support. Payment is all or nothing; a fleet that cannot pay is unsupplied (can't move or fire)
    /// until a later payment succeeds.
    /// Mothball (target: own fleet at a site) stands the fleet down; Activate restarts it after the tier's warm-up.
    /// Invalid orders change nothing.
    /// </summary>
    public sealed class FleetUpkeepSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly FleetBalance _balance;
        private readonly int _siteReserve;

        public FleetUpkeepSystem(
            ComponentStore<FleetUnit> units, ComponentStore<FleetReadiness> readiness, ComponentStore<FleetMotion> motions,
            ComponentStore<Stockpile> stockpiles, ComponentStore<SiteInfo> sites, FleetBalance balance, int siteReserve)
        {
            if (siteReserve < 0) throw new ArgumentOutOfRangeException(nameof(siteReserve));
            _siteReserve = siteReserve;
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            if (tick == 0 || tick % _balance.UpkeepIntervalTicks != 0)
                return;

            for (int i = 0; i < _units.Count; i++)
            {
                var fleet = _units.EntityAt(i);
                ref var unit = ref _units.At(i);
                ref var readiness = ref _readiness.Get(fleet);
                ref var hold = ref _stockpiles.Get(fleet);
                var upkeep = _balance.Tier(unit.TechTier).UpkeepPerShipHour;
                int discount = readiness.IsMothballed ? _balance.Tier(unit.TechTier).MothballUpkeepDiscountPercent : 0;

                var depot = OwnDockedSite(fleet, unit.OwnerPlayerId);
                if (!depot.IsNull)
                {
                    ref var stock = ref _stockpiles.Get(depot);
                    for (int k = 0; k < upkeep.Length; k++)
                    {
                        var item = upkeep[k].Item;
                        int top = Math.Min(Due(unit.TotalShips, upkeep[k].Milli, discount) - hold.Get(item),
                            stock.Get(item) - _siteReserve);
                        if (top <= 0)
                            continue;
                        stock.Add(item, -top);
                        hold.Add(item, top);
                    }
                }
                bool canPay = true;
                for (int k = 0; k < upkeep.Length; k++)
                    canPay &= hold.Has(upkeep[k].Item, Due(unit.TotalShips, upkeep[k].Milli, discount));
                readiness.IsUnsupplied = !canPay;
                if (!canPay)
                    continue;
                for (int k = 0; k < upkeep.Length; k++)
                    hold.Add(upkeep[k].Item, -Due(unit.TotalShips, upkeep[k].Milli, discount));
            }
        }

        // The site the fleet is docked at, when its owner holds it and it has a stock; else null.
        private EntityId OwnDockedSite(EntityId fleet, int owner)
        {
            if (owner <= 0 || !_motions.Has(fleet))
                return default;
            ref var motion = ref _motions.Get(fleet);
            var site = motion.Site;
            if (!motion.Lane.IsNull || !_sites.Has(site) || _sites.Get(site).OwnerPlayerId != owner || !_stockpiles.Has(site))
                return default;
            return site;
        }

        /// <summary>Whole items due for <paramref name="ships"/> at <paramref name="milliPerShip"/>, after a discount, rounded up.</summary>
        public static int Due(int ships, int milliPerShip, int discountPercent)
        {
            long milli = (long)ships * milliPerShip * (100 - discountPercent) / 100;
            return (int)((milli + 999) / 1000);
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.Mothball:
                    Mothball(command.PlayerId, command.Target);
                    break;
                case CommandType.Activate:
                    Activate(command.PlayerId, command.Target, tick);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        private void Mothball(int playerId, EntityId fleet)
        {
            if (!IsOwnFleet(playerId, fleet) || _motions.Get(fleet).Site.IsNull)
                return;
            _readiness.Get(fleet).IsMothballed = true;
        }

        private void Activate(int playerId, EntityId fleet, long tick)
        {
            if (!IsOwnFleet(playerId, fleet))
                return;
            ref var readiness = ref _readiness.Get(fleet);
            if (!readiness.IsMothballed)
                return;
            readiness.IsMothballed = false;
            readiness.WarmupUntilTick = tick + _balance.Tier(_units.Get(fleet).TechTier).MothballWarmupTicks;
        }

        private bool IsOwnFleet(int playerId, EntityId fleet) =>
            _units.Has(fleet) && _units.Get(fleet).OwnerPlayerId == playerId;
    }
}
