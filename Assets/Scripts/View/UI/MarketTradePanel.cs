using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// The MARKET tab's trade block: an item picker, a quantity and a price step around the WTO reference price, and
    /// the orders a player places from the core base: a resting bid or ask (escrowed by the engine), cancel of the
    /// player's newest open order of the item, and the instant trades with THE SYSTEM (its bid and ask). Buttons only
    /// forward commands through <see cref="CommandIssuer"/>; the engine decides fills, bands and deposits.
    /// </summary>
    public sealed class MarketTradePanel
    {
        private const string OnClass = "seg__item--on";
        private static readonly int[] Quantities = { 10, 100, 1_000 };
        private static readonly int[] PriceSteps = { -10, 0, 10 }; // percent off the reference price

        private readonly Button[] _qty = new Button[3];
        private readonly Button[] _price = new Button[3];
        private readonly Label _item, _priceText, _status, _feedback;
        private readonly Button _bid, _ask, _sellSystem, _buySystem, _cancel;
        private readonly ModalPopup _popup;
        private readonly UiArt _art;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _hq;
        private int _itemSlot = ItemIds.ToSlot(ItemId.ItemFuel);
        private int _qtyIndex;
        private int _priceIndex = 1;
        private long _shownKey = long.MinValue;

        public MarketTradePanel(VisualElement root, ModalPopup popup, UiArt art)
        {
            _popup = popup;
            _art = art;
            _item = TopTelemetryHud.Require<Label>(root, "trade-item");
            _priceText = TopTelemetryHud.Require<Label>(root, "trade-price");
            _status = TopTelemetryHud.Require<Label>(root, "trade-status");
            _feedback = TopTelemetryHud.Require<Label>(root, "trade-feedback");
            _bid = TopTelemetryHud.Require<Button>(root, "do-bid");
            _ask = TopTelemetryHud.Require<Button>(root, "do-ask");
            _sellSystem = TopTelemetryHud.Require<Button>(root, "do-sell-system");
            _buySystem = TopTelemetryHud.Require<Button>(root, "do-buy-system");
            _cancel = TopTelemetryHud.Require<Button>(root, "do-cancel-order");
            for (int i = 0; i < _qty.Length; i++)
            {
                int index = i;
                _qty[i] = TopTelemetryHud.Require<Button>(root, "trade-qty-" + UiFormat.Number(i));
                _qty[i].clicked += () => Pick(_qty, ref _qtyIndex, index);
                _price[i] = TopTelemetryHud.Require<Button>(root, "trade-price-" + UiFormat.Number(i));
                _price[i].clicked += () => Pick(_price, ref _priceIndex, index);
            }
            TopTelemetryHud.Require<Button>(root, "trade-prev").clicked += () => StepItem(-1);
            TopTelemetryHud.Require<Button>(root, "trade-next").clicked += () => StepItem(1);
            _bid.clicked += () => Place(CommandType.PlaceBuyOrder);
            _ask.clicked += () => Place(CommandType.PlaceSellOrder);
            _sellSystem.clicked += () => Instant(CommandType.SellToSystem);
            _buySystem.clicked += () => Instant(CommandType.BuyFromSystem);
            _cancel.clicked += CancelNewest;
        }

        private ItemId Item => ItemIds.FromSlot(_itemSlot);

        private EntityId CoreBase => _sim != null && _sim.Players.Has(_hq) ? _sim.Players.Get(_hq).CoreBase : default;

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            HudQueries.TryFindHq(sim, playerId, out _hq);
            _shownKey = long.MinValue;
        }

        public void Refresh()
        {
            if (_sim == null || !_sim.Stockpiles.Has(CoreBase))
                return;
            long tick = _sim.Clock.CurrentTick;
            var item = Item;
            long reference = _sim.Market.ReferencePriceCents(item, tick);
            int held = _sim.Stockpiles.Get(CoreBase).Get(item);
            int open = 0, openOfItem = 0;
            for (int o = 0; o < _sim.WtoOrders.Count; o++)
            {
                ref var order = ref _sim.WtoOrders.At(o);
                if (order.OwnerPlayerId != _playerId || order.Remaining <= 0)
                    continue;
                open++;
                if (order.Item == item)
                    openOfItem++;
            }
            bool mayTrade = _sim.Authority.MayTrade(_playerId, tick);
            long cash = _sim.Ledger.TryFindPlayerWallet(_playerId, out var wallet) ? _sim.Ledger.Balance(wallet) : 0;
            int quantity = Quantities[_qtyIndex];
            long limit = LimitPrice(reference);
            long bidCost = limit * quantity;
            long askDeposit = limit * quantity * _sim.Rates.EscrowRatePercent(_playerId) / 100;
            long key = reference ^ ((long)held << 24) ^ ((long)open << 48) ^ ((long)openOfItem << 56) ^
                       ((long)_itemSlot << 40) ^ ((long)_priceIndex << 60) ^ (mayTrade ? 0 : 1L << 62) ^
                       (cash << 8) ^ ((long)_qtyIndex << 58);
            if (key == _shownKey)
                return;
            _shownKey = key;
            UiFormat.Set(_item, ContractAndMarketUI.ItemCode(item));
            UiFormat.Set(_priceText, "REF " + UiFormat.Price(reference) + " · LIMIT " + UiFormat.Price(limit) +
                                     " · SYSTEM " + UiFormat.Price(_sim.Market.SystemBidCents(item, tick)) + " / " +
                                     UiFormat.Price(_sim.Market.SystemAskCents(item, tick)) + " · YOU HOLD " +
                                     UiFormat.Thousands(held) + "\nBID LOCKS " + UiFormat.Price(bidCost) + " CR · ASK DEPOSIT " +
                                     UiFormat.Price(askDeposit) + " CR · CASH " + UiFormat.Price(cash) + " CR");
            UiFormat.Set(_status, mayTrade ? UiFormat.Number(open) + " OPEN" : "WTO BAR · NO TRADING");
            _bid.SetEnabled(mayTrade && cash >= bidCost);
            _ask.SetEnabled(mayTrade && held >= quantity && cash >= askDeposit);
            _sellSystem.SetEnabled(mayTrade && held > 0);
            _buySystem.SetEnabled(mayTrade && _sim.Market.SystemStock(item) > 0);
            _cancel.SetEnabled(openOfItem > 0);
        }

        private long LimitPrice(long reference) => reference * (100 + PriceSteps[_priceIndex]) / 100;

        private void Pick(Button[] buttons, ref int field, int index)
        {
            field = index;
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].EnableInClassList(OnClass, i == index);
            _shownKey = long.MinValue;
            Refresh();
        }

        private void StepItem(int step)
        {
            _itemSlot = (_itemSlot + step + ItemIds.Count) % ItemIds.Count;
            _shownKey = long.MinValue;
            Refresh();
        }

        private void Place(CommandType type)
        {
            if (_sim == null)
                return;
            long price = LimitPrice(_sim.Market.ReferencePriceCents(Item, _sim.Clock.CurrentTick));
            var command = Command(type);
            command.ParamB = Quantities[_qtyIndex];
            command.ParamC = (int)System.Math.Min(int.MaxValue, price);
            Send(command);
        }

        private void Instant(CommandType type)
        {
            if (_sim == null)
                return;
            long tick = _sim.Clock.CurrentTick;
            int quantity = Quantities[_qtyIndex];
            bool selling = type == CommandType.SellToSystem;
            long unit = selling ? _sim.Market.SystemBidCents(Item, tick) : _sim.Market.SystemAskCents(Item, tick);
            string body = (selling ? "Sell up to " : "Buy up to ") + UiFormat.Thousands(quantity) + " " +
                          ContractAndMarketUI.ItemCode(Item) + (selling ? " to" : " from") + " THE SYSTEM at about " +
                          UiFormat.Price(unit) + " cr each (" + UiFormat.Price(unit * quantity) + " cr).\n\n" +
                          "THE SYSTEM pays " + UiFormat.Number(_sim.Balance.Wto.SystemBidPercent) + "% and sells at " +
                          UiFormat.Number(_sim.Balance.Wto.SystemAskPercent) +
                          "% of the reference price: a quick trade, never a good one. Fills at the price when the order lands.";
            var command = Command(type);
            command.ParamB = quantity;
            _popup.Confirm(selling ? "SELL TO THE SYSTEM" : "BUY FROM THE SYSTEM", body, _art.Market,
                selling ? "Sell" : "Buy", () => Send(command), ModalPopup.Tone.Normal);
        }

        private void CancelNewest()
        {
            if (_sim == null)
                return;
            EntityId newest = default;
            long placed = long.MinValue;
            for (int o = 0; o < _sim.WtoOrders.Count; o++)
            {
                ref var order = ref _sim.WtoOrders.At(o);
                if (order.OwnerPlayerId != _playerId || order.Remaining <= 0 || order.Item != Item || order.PlacedTick < placed)
                    continue;
                placed = order.PlacedTick;
                newest = _sim.WtoOrders.EntityAt(o);
            }
            if (newest.IsNull)
                return;
            ref var picked = ref _sim.WtoOrders.Get(newest);
            var command = new DelayedPlayerCommand
            {
                PlayerId = _playerId, CommandType = CommandType.CancelOrder, Target = picked.Site, ParamEntity = newest
            };
            Send(command);
        }

        private DelayedPlayerCommand Command(CommandType type) =>
            new DelayedPlayerCommand { PlayerId = _playerId, CommandType = type, Target = CoreBase, ParamA = (int)Item };

        private void Send(DelayedPlayerCommand command)
        {
            string name = InFlightStrip.CommandName(command.CommandType).ToUpperInvariant();
            var result = _sim.Issuer.Issue(command, out long executeAt);
            _feedback.text = result == IssueResult.Issued
                ? "SENT · " + name + " · LANDS IN " + UiFormat.Number(executeAt - _sim.Clock.CurrentTick) + "t"
                : "REFUSED · " + name + " · " + FleetCard.RefusalText(result);
        }
    }
}
