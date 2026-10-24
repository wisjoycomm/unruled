using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The 260 px stockpile dock: the 20 items held at the player's core base (or a selected own site), each with its
    /// change over the last hour. Rows are the 20 fixed elements of MainScreen.uxml; nothing is created per tick.
    /// </summary>
    public sealed class LeftInventoryDock
    {
        private const int SamplesPerHour = 60; // one sample per sim minute
        private const string UpClass = "u-up";
        private const string DownClass = "u-down";

        private readonly Label _site;
        private readonly Label[] _qty = new Label[ItemIds.Count];
        private readonly Label[] _trend = new Label[ItemIds.Count];
        private readonly long[] _qtyCache = new long[ItemIds.Count];
        private readonly long[] _trendCache = new long[ItemIds.Count];

        // Ring of per-minute quantity samples: [sample * Count + slot].
        private readonly int[] _samples = new int[(SamplesPerHour + 1) * ItemIds.Count];
        private int _sampleCount;
        private int _sampleHead;
        private long _lastSampleMinute = -1;

        private SimulationWorld _sim;
        private EntityId _stockSite;
        private bool _stockIsCore;
        private string _shownLabel;

        public LeftInventoryDock(VisualElement root)
        {
            _site = TopTelemetryHud.Require<Label>(root, "dock-site");
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var row = TopTelemetryHud.Require<VisualElement>(root, "stock-" + UiFormat.Number(slot + 1));
                _qty[slot] = TopTelemetryHud.Require<Label>(row, "qty");
                _trend[slot] = TopTelemetryHud.Require<Label>(row, "trend");
            }
        }

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            var site = default(EntityId);
            if (HudQueries.TryFindHq(sim, playerId, out var hq))
                site = sim.Players.Get(hq).CoreBase;
            ShowSite(site, true);
        }

        /// <summary>Shows another site's stockpile (e.g. a selected own site); the trend history restarts.</summary>
        public void ShowSite(EntityId site, bool isCore)
        {
            _stockSite = site;
            _sampleCount = 0;
            _sampleHead = 0;
            _lastSampleMinute = -1;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                _qtyCache[slot] = -1;
                _trendCache[slot] = long.MinValue;
            }
            _stockIsCore = isCore;
            _shownLabel = null;
            ShowSiteName();
        }

        // The header follows the system's label, so a rename shows here as soon as it lands.
        private void ShowSiteName()
        {
            if (_sim == null || !_sim.Sites.Has(_stockSite))
            {
                _site.text = "—";
                return;
            }
            ref var info = ref _sim.Sites.Get(_stockSite);
            var label = UiFormat.SystemLabel(_sim, info.ClusterId);
            if (ReferenceEquals(label, _shownLabel))
                return;
            _shownLabel = label;
            _site.text = UiFormat.SiteName(_sim, in info) + (_stockIsCore ? " · CORE" : string.Empty);
        }

        public void Refresh()
        {
            if (_sim == null || !_sim.Stockpiles.Has(_stockSite))
                return;
            ShowSiteName();
            ref var stock = ref _sim.Stockpiles.Get(_stockSite);
            long minute = _sim.Clock.CurrentTick / SimTick.TicksPerMinute;
            if (minute != _lastSampleMinute)
            {
                _lastSampleMinute = minute;
                Sample(in stock);
            }
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                int qty = stock.Get(ItemIds.FromSlot(slot));
                if (qty != _qtyCache[slot])
                    UiFormat.Set(_qty[slot], ref _qtyCache[slot], qty, UiFormat.Thousands(qty));
                long trend = qty - _samples[_sampleHead * ItemIds.Count + slot];
                if (trend != _trendCache[slot])
                {
                    UiFormat.Set(_trend[slot], ref _trendCache[slot], trend, UiFormat.SignedCount(trend));
                    _trend[slot].EnableInClassList(UpClass, trend > 0);
                    _trend[slot].EnableInClassList(DownClass, trend < 0);
                }
            }
        }

        private void Sample(in Stockpile stock)
        {
            int capacity = SamplesPerHour + 1;
            int at = (_sampleHead + _sampleCount) % capacity;
            if (_sampleCount == capacity)
            {
                at = _sampleHead;
                _sampleHead = (_sampleHead + 1) % capacity;
            }
            else
            {
                _sampleCount++;
            }
            for (int slot = 0; slot < ItemIds.Count; slot++)
                _samples[at * ItemIds.Count + slot] = stock.Get(ItemIds.FromSlot(slot));
        }
    }
}
