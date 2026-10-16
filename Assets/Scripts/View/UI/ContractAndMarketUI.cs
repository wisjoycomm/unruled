using Unruled.Core.Economy;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The WTO market panel: for a watch list of items, the best resting bid and ask in the order book and the VWAP
    /// reference price's change over the last hour. Rows are the fixed elements of MainScreen.uxml.
    /// </summary>
    public sealed class ContractAndMarketUI
    {
        private const int SamplesPerHour = 60; // one VWAP sample per sim minute
        private const string UpClass = "u-up";
        private const string DownClass = "u-down";

        // The goods a fleet commander watches: what fleets burn, plus steel for the yards.
        private static readonly ItemId[] Watch =
        {
            ItemId.ItemFuel, ItemId.ItemBattery, ItemId.ItemCoolant, ItemId.ItemAmmo, ItemId.ItemSteel
        };

        private readonly Label _window;
        private readonly Label[] _bid = new Label[Watch.Length];
        private readonly Label[] _ask = new Label[Watch.Length];
        private readonly Label[] _delta = new Label[Watch.Length];
        private readonly long[] _bidCache = new long[Watch.Length];
        private readonly long[] _askCache = new long[Watch.Length];
        private readonly long[] _deltaCache = new long[Watch.Length];
        private readonly long[] _bestBid = new long[Watch.Length];
        private readonly long[] _bestAsk = new long[Watch.Length];

        // Ring of per-minute VWAP samples: [sample * Watch.Length + row].
        private readonly long[] _samples = new long[(SamplesPerHour + 1) * Watch.Length];
        private int _sampleCount;
        private int _sampleHead;
        private long _lastSampleMinute = -1;

        private SimulationWorld _sim;

        public ContractAndMarketUI(VisualElement root)
        {
            _window = TopTelemetryHud.Require<Label>(root, "market-window");
            for (int i = 0; i < Watch.Length; i++)
            {
                var row = TopTelemetryHud.Require<VisualElement>(root, "market-" + UiFormat.Number(i));
                TopTelemetryHud.Require<Label>(row, "item").text = ItemCode(Watch[i]);
                _bid[i] = TopTelemetryHud.Require<Label>(row, "bid");
                _ask[i] = TopTelemetryHud.Require<Label>(row, "ask");
                _delta[i] = TopTelemetryHud.Require<Label>(row, "delta");
            }
        }

        public void Bind(SimulationWorld sim)
        {
            _sim = sim;
            _sampleCount = 0;
            _sampleHead = 0;
            _lastSampleMinute = -1;
            for (int i = 0; i < Watch.Length; i++)
            {
                _bidCache[i] = long.MinValue;
                _askCache[i] = long.MinValue;
                _deltaCache[i] = long.MinValue;
            }
            _window.text = "VWAP · Δ 1h";
        }

        public void Refresh()
        {
            if (_sim == null)
                return;
            long tick = _sim.Clock.CurrentTick;
            ScanOrderBook();
            long minute = tick / SimTick.TicksPerMinute;
            bool sample = minute != _lastSampleMinute;
            if (sample)
                _lastSampleMinute = minute;
            int capacity = SamplesPerHour + 1;
            int at = (_sampleHead + _sampleCount) % capacity;
            if (sample && _sampleCount == capacity)
            {
                at = _sampleHead;
                _sampleHead = (_sampleHead + 1) % capacity;
            }
            else if (sample)
            {
                _sampleCount++;
            }

            for (int i = 0; i < Watch.Length; i++)
            {
                if (_bestBid[i] != _bidCache[i])
                    UiFormat.Set(_bid[i], ref _bidCache[i], _bestBid[i], _bestBid[i] > 0 ? UiFormat.Price(_bestBid[i]) : "—");
                if (_bestAsk[i] != _askCache[i])
                    UiFormat.Set(_ask[i], ref _askCache[i], _bestAsk[i], _bestAsk[i] < long.MaxValue ? UiFormat.Price(_bestAsk[i]) : "—");

                long vwap = _sim.Market.ReferencePriceCents(Watch[i], tick);
                if (sample)
                    _samples[at * Watch.Length + i] = vwap;
                long then = _samples[_sampleHead * Watch.Length + i];
                long perMille = then > 0 ? (vwap - then) * 1000 / then : 0;
                if (perMille != _deltaCache[i])
                {
                    UiFormat.Set(_delta[i], ref _deltaCache[i], perMille, UiFormat.PercentDelta(perMille));
                    _delta[i].EnableInClassList(UpClass, perMille > 0);
                    _delta[i].EnableInClassList(DownClass, perMille < 0);
                }
            }
        }

        private void ScanOrderBook()
        {
            for (int i = 0; i < Watch.Length; i++)
            {
                _bestBid[i] = 0;
                _bestAsk[i] = long.MaxValue;
            }
            for (int o = 0; o < _sim.WtoOrders.Count; o++)
            {
                ref var order = ref _sim.WtoOrders.At(o);
                if (order.Remaining <= 0)
                    continue;
                int row = RowOf(order.Item);
                if (row < 0)
                    continue;
                if (order.Side == OrderSide.Buy && order.PriceCents > _bestBid[row])
                    _bestBid[row] = order.PriceCents;
                else if (order.Side == OrderSide.Sell && order.PriceCents < _bestAsk[row])
                    _bestAsk[row] = order.PriceCents;
            }
        }

        private static int RowOf(ItemId item)
        {
            for (int i = 0; i < Watch.Length; i++)
                if (Watch[i] == item)
                    return i;
            return -1;
        }

        /// <summary>Item code without its RAW_/ITEM_ prefix, e.g. ItemNuclearCore -> "NUCLEAR CORE".</summary>
        public static string ItemCode(ItemId item) => ItemCodes[ItemIds.ToSlot(item)];

        private static readonly string[] ItemCodes =
        {
            "WATER", "NITROGEN", "METHANE", "BIOMASS", "IRON", "COPPER", "SILICON", "TITANIUM", "TUNGSTEN", "URANIUM",
            "OXYGEN", "RATIONS", "COOLANT", "FUEL", "STEEL", "ALLOY", "MICROCHIP", "BATTERY", "AMMO", "NUCLEAR CORE"
        };
    }
}
