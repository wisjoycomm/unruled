using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// <see cref="CommandType.TransferCargo"/> (target: a fleet; ParamA = item; ParamB = quantity, positive loads from the
    /// site into the hold, negative unloads into the site; ParamC = 1 fills the hold up to ParamB from what the site has,
    /// so a repeated fill changes nothing). The fleet must be the player's, docked at a site the player owns, out of combat
    /// and not a WTO contract convoy. A convoy-only fleet holds at most its cargo convoys x the capacity per convoy of each
    /// item (a fill stops there). A plain transfer is all or nothing. Stolen units move first, so their
    /// <see cref="StolenGoods"/> tags follow them.
    /// </summary>
    public sealed class CargoTransferSystem : ICommandHandler
    {
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<ContractCargo> _contracts;
        private readonly ComponentStore<StolenGoods> _stolen;
        private readonly int _coreSupplyReserve;
        private readonly int _capacityPerConvoy;

        public CargoTransferSystem(
            ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions, ComponentStore<FleetCombatState> combat,
            ComponentStore<SiteInfo> sites, ComponentStore<Stockpile> stockpiles, ComponentStore<ContractCargo> contracts,
            ComponentStore<StolenGoods> stolen, int capacityPerConvoy, int coreSupplyReserve = 0)
        {
            if (capacityPerConvoy < 1) throw new ArgumentOutOfRangeException(nameof(capacityPerConvoy));
            _capacityPerConvoy = capacityPerConvoy;
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
            _stolen = stolen ?? throw new ArgumentNullException(nameof(stolen));
            _coreSupplyReserve = coreSupplyReserve;
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.TransferCargo)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var fleet = command.Target;
            var item = (ItemId)command.ParamA;
            if (command.ParamA < 0 || command.ParamA > byte.MaxValue || !ItemIds.IsValid(item) || command.ParamB == 0 ||
                command.ParamB == int.MinValue || !_units.Has(fleet) || !_motions.Has(fleet) || !_stockpiles.Has(fleet) ||
                _units.Get(fleet).OwnerPlayerId != command.PlayerId || _contracts.Has(fleet))
                return;
            ref var motion = ref _motions.Get(fleet);
            var site = motion.Site;
            if (!motion.Lane.IsNull || !_sites.Has(site) || _sites.Get(site).OwnerPlayerId != command.PlayerId ||
                !_stockpiles.Has(site) || (_combat.Has(fleet) && _combat.Get(fleet).EngagedUntilTick > tick))
                return;

            bool load = command.ParamB > 0;
            var from = load ? site : fleet;
            var to = load ? fleet : site;
            int quantity = Math.Abs(command.ParamB);
            ref var source = ref _stockpiles.Get(from);
            if (command.ParamC == 1)
            {
                if (!load)
                    return;
                int reserve = item == ItemId.ItemFuel || item == ItemId.ItemCoolant
                    ? _coreSupplyReserve
                    : item == ItemId.ItemOxygen || item == ItemId.ItemRations
                        ? Math.Min(_coreSupplyReserve, 50)
                        : 0;
                quantity = Math.Min(command.ParamB - _stockpiles.Get(fleet).Get(item), source.Get(item) - reserve);
                if (_units.Get(fleet).IsConvoyOnly)
                    quantity = (int)Math.Min(quantity, Room(fleet, item));
                if (quantity <= 0)
                    return;
            }
            if (!source.Has(item, quantity) || (load && _units.Get(fleet).IsConvoyOnly && quantity > Room(fleet, item)))
                return;
            CargoMover.Move(_stockpiles, _stolen, from, to, item, quantity);
        }

        // What a convoy-only fleet's hold can still take of an item.
        private long Room(EntityId fleet, ItemId item) =>
            (long)_units.Get(fleet).CargoConvoys * _capacityPerConvoy - _stockpiles.Get(fleet).Get(item);
    }
}
