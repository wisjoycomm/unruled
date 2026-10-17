using System;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns <see cref="RelayStation"/> (GDD §2.4 A, data sheet tab 04 "Relay Battery Upkeep"; GDD §2.1 B keeps husks as
    /// relay sites). <see cref="CommandType.BuildRelay"/> (Target: an own site without a relay) takes the build cost from
    /// the site's stock and raises a relay there. Every upkeep interval each relay burns its batteries from its site's stock;
    /// a relay whose site can't pay, or whose site changed hands, goes dark and is removed.
    /// </summary>
    public sealed class RelayStationSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<RelayStation> _relays;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly LatencyBalance _balance;

        public RelayStationSystem(
            ComponentStore<RelayStation> relays, ComponentStore<SiteInfo> sites, ComponentStore<Stockpile> stockpiles,
            LatencyBalance balance)
        {
            _relays = relays ?? throw new ArgumentNullException(nameof(relays));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.BuildRelay)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var site = command.Target;
            if (!_sites.Has(site) || !_stockpiles.Has(site) || _relays.Has(site) ||
                _sites.Get(site).OwnerPlayerId != command.PlayerId)
                return;
            ref var stock = ref _stockpiles.Get(site);
            if (!stock.Has(ItemId.ItemSteel, _balance.RelayBuildSteel) ||
                !stock.Has(ItemId.ItemBattery, _balance.RelayBuildBatteries))
                return;
            stock.Add(ItemId.ItemSteel, -_balance.RelayBuildSteel);
            stock.Add(ItemId.ItemBattery, -_balance.RelayBuildBatteries);
            _relays.Add(site, new RelayStation { OwnerPlayerId = command.PlayerId });
        }

        public void OnTick(long tick)
        {
            if (tick == 0 || tick % _balance.RelayUpkeepIntervalTicks != 0)
                return;
            int i = 0;
            while (i < _relays.Count)
            {
                var site = _relays.EntityAt(i);
                if (Pay(site, _relays.At(i).OwnerPlayerId))
                    i++;
                else
                    _relays.Remove(site);
            }
        }

        private bool Pay(EntityId site, int owner)
        {
            if (!_sites.Has(site) || _sites.Get(site).OwnerPlayerId != owner || !_stockpiles.Has(site))
                return false;
            ref var stock = ref _stockpiles.Get(site);
            if (!stock.Has(ItemId.ItemBattery, _balance.RelayUpkeepBatteries))
                return false;
            stock.Add(ItemId.ItemBattery, -_balance.RelayUpkeepBatteries);
            return true;
        }
    }
}
