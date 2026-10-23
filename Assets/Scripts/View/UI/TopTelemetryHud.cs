using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// The 56 px top bar: sim clock, credits, signal latency to the selection, the worst NOAA R/S/G levels in sight
    /// and THE SYSTEM's tags (WHO export locks on own sites, STA toll discount, the Citadel's shield and defence tier,
    /// then The Directorate once it falls). Reads state; never writes it.
    /// </summary>
    public sealed class TopTelemetryHud
    {
        private const string NoaaLevelClassPrefix = "u-noaa--";
        private const string DirectorateClass = "u-tag--directorate";
        private const long DirectorateFormed = -1, DirectorateSeated = -2;

        private readonly Label _clock;
        private readonly Label _credits;
        private readonly Label _latency;
        private readonly Label[] _noaa = new Label[3];
        private readonly int[] _noaaShown = { -1, -1, -1 };
        private readonly Label _who;
        private readonly Label _sta;
        private readonly Label _citadel;
        private readonly VisualElement _citadelIcon;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _wallet;
        private EntityId _selection;
        private long _clockCache = -1, _creditsCache = -1, _latencyCache = long.MinValue, _whoCache = -1, _staCache = -1,
            _citadelCache = long.MinValue;

        public TopTelemetryHud(VisualElement root)
        {
            _clock = Require<Label>(root, "clock");
            _credits = Require<Label>(root, "credits");
            _latency = Require<Label>(root, "latency");
            _noaa[0] = Require<Label>(root, "noaa-r");
            _noaa[1] = Require<Label>(root, "noaa-s");
            _noaa[2] = Require<Label>(root, "noaa-g");
            _who = Require<Label>(root, "who-tag");
            _sta = Require<Label>(root, "sta-tag");
            _citadel = Require<Label>(root, "citadel-tag");
            _citadelIcon = root.Q<VisualElement>("citadel-icon");
            if (_citadelIcon != null)
            {
                var emblem = Resources.Load<Texture2D>("UI/system_citadel_emblem");
                if (emblem != null)
                    _citadelIcon.style.backgroundImage = new StyleBackground(emblem);
            }
        }

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            sim.Ledger.TryFindPlayerWallet(playerId, out _wallet);
        }

        /// <summary>Site, lane or fleet the latency readout measures to; a null id clears it.</summary>
        public void SetSelection(EntityId target) => _selection = target;

        public void Refresh()
        {
            if (_sim == null)
                return;
            long tick = _sim.Clock.CurrentTick;
            UiFormat.Set(_clock, ref _clockCache, tick, UiFormat.Clock(tick));

            long credits = _wallet.IsNull ? 0 : _sim.Ledger.Balance(_wallet) / Core.Economy.Wallet.CentsPerCredit;
            if (credits != _creditsCache)
                UiFormat.Set(_credits, ref _creditsCache, credits, UiFormat.Thousands(credits));

            long latency = SelectionLatency();
            if (latency != _latencyCache)
                UiFormat.Set(_latency, ref _latencyCache, latency, latency < 0 ? "—" : UiFormat.Number(latency) + "t");

            RefreshNoaa();

            long locks = CountExportLocks(tick);
            if (locks != _whoCache)
                UiFormat.Set(_who, ref _whoCache, locks, "WHO · " + UiFormat.Number(locks) + (locks == 1 ? " LOCK" : " LOCKS"));

            long discount = _sim.Rates.For(_playerId).TollDiscountPercent;
            if (discount != _staCache)
                UiFormat.Set(_sta, ref _staCache, discount,
                    discount > 0 ? "STA · TOLL −" + UiFormat.Number(discount) + "%" : "STA · FULL TOLL");

            RefreshCitadel();
        }

        // Shield percent and defence tier while the Citadel stands (cached as percent x 10 + tier); then The Directorate.
        private void RefreshCitadel()
        {
            if (_sim.Citadels.Count == 0)
            {
                if (_citadelCache != long.MaxValue)
                    UiFormat.Set(_citadel, ref _citadelCache, long.MaxValue, "CITADEL · —");
                return;
            }
            if (_sim.Victory.IsDecided(out _))
            {
                bool seated = _sim.Victory.HasSeat(_playerId);
                long state = seated ? DirectorateSeated : DirectorateFormed;
                if (state == _citadelCache)
                    return;
                _citadel.EnableInClassList(DirectorateClass, seated);
                UiFormat.Set(_citadel, ref _citadelCache, state, seated ? "DIRECTORATE · SEATED" : "DIRECTORATE · FORMED");
                return;
            }
            ref var citadel = ref _sim.Citadels.At(0);
            long percent = (long)citadel.Integrity * 100 / _sim.Balance.Citadel.MaxIntegrity;
            long key = percent * 10 + citadel.DefenceTier;
            if (key != _citadelCache)
                UiFormat.Set(_citadel, ref _citadelCache, key,
                    "CITADEL · " + UiFormat.Number(percent) + "% · T" + UiFormat.Number(citadel.DefenceTier));
        }

        private long SelectionLatency()
        {
            if (_selection.IsNull)
                return -1;
            int ticks;
            LatencyStatus status;
            if (_sim.Sites.Has(_selection))
                status = _sim.Latency.TryComputeToSite(_playerId, _selection, out ticks);
            else if (_sim.Starlanes.Has(_selection))
                status = _sim.Latency.TryComputeToLane(_playerId, _selection, out ticks);
            else if (_sim.FleetMotions.Has(_selection))
            {
                ref var motion = ref _sim.FleetMotions.Get(_selection);
                status = motion.Lane.IsNull
                    ? _sim.Latency.TryComputeToSite(_playerId, motion.Site, out ticks)
                    : _sim.Latency.TryComputeToLane(_playerId, motion.Lane, out ticks);
            }
            else
                return -1;
            return status == LatencyStatus.Ok ? ticks : -1;
        }

        // Worst level per scale across all sites, if the player's bracket sees current storms; hidden shows as level 0.
        private void RefreshNoaa()
        {
            int r = 0, s = 0, g = 0;
            if (_sim.Forecast.SeesCurrentStorms(_playerId))
            {
                for (int i = 0; i < _sim.Weather.Count; i++)
                {
                    ref var w = ref _sim.Weather.At(i);
                    if (w.RadioBlackoutLevel > r) r = w.RadioBlackoutLevel;
                    if (w.SolarRadiationLevel > s) s = w.SolarRadiationLevel;
                    if (w.GeomagneticStormLevel > g) g = w.GeomagneticStormLevel;
                }
            }
            SetNoaa(0, 'R', r);
            SetNoaa(1, 'S', s);
            SetNoaa(2, 'G', g);
        }

        private void SetNoaa(int index, char scale, int level)
        {
            if (_noaaShown[index] == level)
                return;
            var chip = _noaa[index];
            if (_noaaShown[index] >= 0)
                chip.RemoveFromClassList(NoaaLevelClassPrefix + UiFormat.Number(_noaaShown[index]));
            else
                for (int l = 0; l <= 5; l++)
                    chip.RemoveFromClassList(NoaaLevelClassPrefix + UiFormat.Number(l));
            chip.AddToClassList(NoaaLevelClassPrefix + UiFormat.Number(level));
            chip.text = scale + UiFormat.Number(level);
            _noaaShown[index] = level;
        }

        private long CountExportLocks(long tick)
        {
            long locks = 0;
            for (int i = 0; i < _sim.Quarantines.Count; i++)
            {
                var site = _sim.Quarantines.EntityAt(i);
                if (_sim.Quarantines.At(i).IsExportLocked(tick) && _sim.Sites.Has(site) &&
                    _sim.Sites.Get(site).OwnerPlayerId == _playerId)
                    locks++;
            }
            return locks;
        }

        internal static T Require<T>(VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new System.InvalidOperationException($"MainScreen.uxml has no {typeof(T).Name} '{name}'.");
    }
}
