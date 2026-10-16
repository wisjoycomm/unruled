using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Owns <see cref="WtoOrder"/> and <see cref="ItemMarket"/>: the WTO limit order book (GDD §2.2 B).
    /// PlaceBuyOrder / PlaceSellOrder (target: own site with a stockpile; ParamA item, ParamB quantity, ParamC price in
    /// cents per unit) are refused outside +/- the price band of the item's reference price (VWAP of the window, see
    /// <see cref="ItemMarket"/>). A sell order takes its goods from the site's stockpile; a buy order pays quantity x
    /// price into the WTO escrow wallet. CancelOrder (target: the order's site; ParamEntity: the order) returns what is
    /// left. Invalid or unaffordable orders change nothing.
    /// Each tick every item's book is matched: best buy against best sell while they cross, priority price, then
    /// placement tick, then entity id. A match trades at the earlier order's price; the buyer gets back the
    /// difference to their limit and the goods at their site; the seller gets the value less their bracket fee, which
    /// goes to the WTO fee pool.
    /// </summary>
    public sealed class WtoMarketEngine : ISimSystem, ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<WtoOrder> _orders;
        private readonly ComponentStore<ItemMarket> _markets;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly Ledger _ledger;
        private readonly EntityId _escrow;
        private readonly EntityId _feePool;
        private readonly IMarketRates _rates;
        private readonly WtoBalance _balance;

        public WtoMarketEngine(
            World world, ComponentStore<WtoOrder> orders, ComponentStore<ItemMarket> markets,
            ComponentStore<SiteInfo> sites, ComponentStore<Stockpile> stockpiles, Ledger ledger, EntityId escrow,
            EntityId feePool, IMarketRates rates, WtoBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _markets = markets ?? throw new ArgumentNullException(nameof(markets));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (escrow.IsNull) throw new ArgumentException("WTO escrow wallet required.", nameof(escrow));
            if (feePool.IsNull) throw new ArgumentException("WTO fee pool required.", nameof(feePool));
            _escrow = escrow;
            _feePool = feePool;
            _rates = rates ?? throw new ArgumentNullException(nameof(rates));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public long ReferencePriceCents(ItemId item, long tick) =>
            _markets.Get(Market(item)).ReferencePriceCents(tick, _balance.VwapWindowTicks);

        public bool IsInBand(ItemId item, long priceCents, long tick)
        {
            long reference = ReferencePriceCents(item, tick);
            return priceCents * 100 >= reference * (100 - _balance.PriceBandPercent) &&
                   priceCents * 100 <= reference * (100 + _balance.PriceBandPercent);
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.PlaceBuyOrder:
                    Place(command, OrderSide.Buy, tick);
                    break;
                case CommandType.PlaceSellOrder:
                    Place(command, OrderSide.Sell, tick);
                    break;
                case CommandType.CancelOrder:
                    Cancel(command.PlayerId, command.ParamEntity);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        public void OnTick(long tick)
        {
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                while (TryFindBest(item, OrderSide.Buy, out var buy) && TryFindBest(item, OrderSide.Sell, out var sell) &&
                       _orders.Get(buy).PriceCents >= _orders.Get(sell).PriceCents)
                    Match(buy, sell, tick);
            }
        }

        private void Place(in DelayedPlayerCommand command, OrderSide side, long tick)
        {
            var item = (ItemId)command.ParamA;
            int quantity = command.ParamB;
            long price = command.ParamC;
            var site = command.Target;
            if (!ItemIds.IsValid(item) || quantity <= 0 || price <= 0)
                return;
            if (!_sites.Has(site) || _sites.Get(site).OwnerPlayerId != command.PlayerId || !_stockpiles.Has(site))
                return;
            if (!_ledger.TryFindPlayerWallet(command.PlayerId, out var wallet) || !IsInBand(item, price, tick))
                return;

            if (side == OrderSide.Sell)
            {
                ref var stock = ref _stockpiles.Get(site);
                if (!stock.Has(item, quantity))
                    return;
                stock.Add(item, -quantity);
            }
            else if (!_ledger.Transfer(wallet, _escrow, price * quantity))
            {
                return;
            }

            var order = _world.Create();
            _orders.Add(order, new WtoOrder
            {
                OwnerPlayerId = command.PlayerId, Item = item, Side = side, PriceCents = price, Remaining = quantity,
                Site = site, PlacedTick = tick
            });
        }

        private void Cancel(int playerId, EntityId order)
        {
            if (!_orders.Has(order) || _orders.Get(order).OwnerPlayerId != playerId)
                return;
            ref var o = ref _orders.Get(order);
            if (o.Side == OrderSide.Sell)
                _stockpiles.Get(o.Site).Add(o.Item, o.Remaining);
            else
                Pay(_escrow, Wallet(o.OwnerPlayerId), o.PriceCents * o.Remaining);
            _world.Destroy(order);
        }

        private void Match(EntityId buyOrder, EntityId sellOrder, long tick)
        {
            ref var buy = ref _orders.Get(buyOrder);
            ref var sell = ref _orders.Get(sellOrder);
            long price = IsEarlier(sell, sellOrder, buy, buyOrder) ? sell.PriceCents : buy.PriceCents;
            int quantity = Math.Min(buy.Remaining, sell.Remaining);
            long value = price * quantity;
            long fee = value * _rates.FeeBasisPoints(sell.OwnerPlayerId) / 10_000;

            _stockpiles.Get(buy.Site).Add(buy.Item, quantity);
            Pay(_escrow, Wallet(buy.OwnerPlayerId), (buy.PriceCents - price) * quantity);
            Pay(_escrow, Wallet(sell.OwnerPlayerId), value - fee);
            Pay(_escrow, _feePool, fee);
            _markets.Get(Market(buy.Item)).Record(tick, price, quantity, _balance.VwapWindowTicks);

            buy.Remaining -= quantity;
            sell.Remaining -= quantity;
            bool buyFilled = buy.Remaining == 0;
            bool sellFilled = sell.Remaining == 0;
            if (buyFilled)
                _world.Destroy(buyOrder);
            if (sellFilled)
                _world.Destroy(sellOrder);
        }

        private bool TryFindBest(ItemId item, OrderSide side, out EntityId best)
        {
            best = default;
            for (int i = 0; i < _orders.Count; i++)
            {
                ref var o = ref _orders.At(i);
                if (o.Item != item || o.Side != side)
                    continue;
                var entity = _orders.EntityAt(i);
                if (best.IsNull || Outranks(o, entity, _orders.Get(best), best))
                    best = entity;
            }
            return !best.IsNull;
        }

        private static bool Outranks(in WtoOrder a, EntityId aId, in WtoOrder b, EntityId bId)
        {
            if (a.PriceCents != b.PriceCents)
                return a.Side == OrderSide.Buy ? a.PriceCents > b.PriceCents : a.PriceCents < b.PriceCents;
            return IsEarlier(a, aId, b, bId);
        }

        private static bool IsEarlier(in WtoOrder a, EntityId aId, in WtoOrder b, EntityId bId) =>
            a.PlacedTick != b.PlacedTick ? a.PlacedTick < b.PlacedTick : aId.Index < bId.Index;

        private EntityId Market(ItemId item)
        {
            for (int i = 0; i < _markets.Count; i++)
                if (_markets.At(i).Item == item)
                    return _markets.EntityAt(i);
            throw new InvalidOperationException($"No WTO market for {item}.");
        }

        private EntityId Wallet(int playerId)
        {
            if (!_ledger.TryFindPlayerWallet(playerId, out var wallet))
                throw new InvalidOperationException($"Player {playerId} has no wallet.");
            return wallet;
        }

        private void Pay(EntityId from, EntityId to, long cents)
        {
            if (!_ledger.Transfer(from, to, cents))
                throw new InvalidOperationException("WTO escrow does not cover its orders.");
        }
    }
}
