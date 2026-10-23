using Unruled.Core.Board;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// The FLEET tab: the selected fleet's focus-fire class, mothball / activate, and the focused radar: a belt scan of
    /// an outer-rim lane (the lane picked on the board, else the first outer-rim lane by the fleet; "Next lane" steps
    /// through them), its cancel, and ECM jamming once the fleet's RWR has fired. Buttons only forward commands through
    /// <see cref="CommandIssuer"/>; the radar and upkeep systems decide what lands.
    /// </summary>
    public sealed class FleetOpsPanel
    {
        private const string OnClass = "seg__item--on";
        private static readonly string[] FocusNames = { "LINE", "EW", "SUPPLY", "CARGO" };

        private readonly Button[] _focus = new Button[4];
        private readonly Label _focusStatus, _readiness, _radar, _radarTarget, _ecmStatus, _feedback;
        private readonly Button _mothball, _scan, _cancelScan, _nextLane, _ecm;
        private readonly VisualElement _readinessEmblem;
        private readonly ModalPopup _popup;
        private readonly UiArt _art;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _fleet;
        private EntityId _lane;
        private long _shownKey = long.MinValue;

        public FleetOpsPanel(VisualElement root, ModalPopup popup, UiArt art)
        {
            _popup = popup;
            _art = art;
            for (int i = 0; i < _focus.Length; i++)
            {
                int index = i;
                _focus[i] = TopTelemetryHud.Require<Button>(root, "focus-" + UiFormat.Number(i));
                _focus[i].clicked += () => Send(CommandType.SetFocus, _fleet, index);
            }
            _focusStatus = TopTelemetryHud.Require<Label>(root, "focus-status");
            _readiness = TopTelemetryHud.Require<Label>(root, "readiness-status");
            _readinessEmblem = TopTelemetryHud.Require<VisualElement>(root, "readiness-emblem");
            _radar = TopTelemetryHud.Require<Label>(root, "radar-status");
            _radarTarget = TopTelemetryHud.Require<Label>(root, "radar-target");
            _ecmStatus = TopTelemetryHud.Require<Label>(root, "ecm-status");
            _feedback = TopTelemetryHud.Require<Label>(root, "fleet-feedback");
            _mothball = TopTelemetryHud.Require<Button>(root, "do-mothball");
            _scan = TopTelemetryHud.Require<Button>(root, "do-scan");
            _cancelScan = TopTelemetryHud.Require<Button>(root, "do-cancel-scan");
            _nextLane = TopTelemetryHud.Require<Button>(root, "do-next-lane");
            _ecm = TopTelemetryHud.Require<Button>(root, "do-ecm");
            _mothball.clicked += ToggleMothball;
            _scan.clicked += ConfirmScan;
            _cancelScan.clicked += () => Send(CommandType.CancelScan, _lane, 0);
            _nextLane.clicked += NextLane;
            _ecm.clicked += ConfirmEcm;
        }

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            _shownKey = long.MinValue;
        }

        public void SelectFleet(EntityId fleet)
        {
            _fleet = fleet;
            if (!IsRimLane(_lane))
                _lane = FirstRimLane(default);
            _shownKey = long.MinValue;
        }

        /// <summary>A lane picked on the board becomes the scan target when it is an outer-rim bypass.</summary>
        public void SelectTarget(EntityId siteOrLane)
        {
            if (_sim != null && IsRimLane(siteOrLane))
            {
                _lane = siteOrLane;
                _shownKey = long.MinValue;
            }
        }

        public void Refresh()
        {
            if (_sim == null)
                return;
            bool has = _sim.Fleets.Has(_fleet);
            long tick = _sim.Clock.CurrentTick;
            int focus = has ? (int)_sim.FleetCombat.Get(_fleet).Focus : -1;
            bool mothballed = has && _sim.FleetReadiness.Get(_fleet).IsMothballed;
            long warmup = has ? System.Math.Max(0, _sim.FleetReadiness.Get(_fleet).WarmupUntilTick - tick) : 0;
            bool docked = has && _sim.FleetMotions.Get(_fleet).Lane.IsNull;
            bool rwr = has && _sim.FleetSensors.Get(_fleet).RwrWarningTick > 0;
            bool onLane = has && !docked;
            int ew = has ? _sim.Fleets.Get(_fleet).EwShips : 0;
            int scanIndex = FindScan(_lane);
            long scanLeft = scanIndex >= 0 ? ScanLeft(scanIndex, tick) : -1;

            long key = focus ^ (mothballed ? 1L << 4 : 0) ^ (warmup << 5) ^ (docked ? 1L << 40 : 0) ^ (rwr ? 1L << 41 : 0) ^
                       ((long)_lane.Index << 42) ^ (scanLeft << 20) ^ ((long)_fleet.Index << 52) ^ ((long)ew << 30);
            if (key == _shownKey)
                return;
            _shownKey = key;

            for (int i = 0; i < _focus.Length; i++)
            {
                _focus[i].EnableInClassList(OnClass, i == focus);
                _focus[i].SetEnabled(has);
            }
            UiFormat.Set(_focusStatus, has ? FocusNames[focus] + " FIRST" : "NO FLEET");

            UiFormat.Set(_readiness, !has ? "NO FLEET" : mothballed ? "MOTHBALLED · NO UPKEEP" :
                warmup > 0 ? "WARMING UP " + UiFormat.Duration(warmup) : "ACTIVE");
            _mothball.text = mothballed ? "Activate" : "Mothball";
            _mothball.SetEnabled(has && (mothballed || docked));
            _readinessEmblem.style.backgroundImage = new StyleBackground(mothballed ? _art.Activate : _art.Mothball);

            bool rim = IsRimLane(_lane);
            UiFormat.Set(_radarTarget, rim ? "TARGET · " + LaneName(_lane) : "NO OUTER-RIM LANE IN REACH · PICK ONE ON THE BOARD");
            UiFormat.Set(_radar, scanIndex < 0 ? "NO SCAN" : scanLeft > 0 ? "INTEGRATING " + UiFormat.Duration(scanLeft) : "REVEALING");
            _scan.SetEnabled(rim && scanIndex < 0);
            _cancelScan.SetEnabled(scanIndex >= 0);
            _nextLane.SetEnabled(FirstRimLane(_lane) != _lane);

            UiFormat.Set(_ecmStatus, !has ? "NO FLEET" : rwr ? "RWR ALARM · HOSTILE SCAN" : "RWR QUIET");
            _ecm.SetEnabled(rwr && onLane && ew > 0);
        }

        private void ToggleMothball()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            if (_sim.FleetReadiness.Get(_fleet).IsMothballed)
            {
                Send(CommandType.Activate, _fleet, 0);
                return;
            }
            var tier = _sim.Balance.Fleets.Tier(_sim.Fleets.Get(_fleet).TechTier);
            _popup.Confirm("MOTHBALL " + UiFormat.FleetName(HudQueries.FleetOrdinal(_sim, _fleet)),
                "The fleet stands down at its dock: no upkeep, no fire, no moves.\n\nWaking it again takes " +
                UiFormat.Span(tier.MothballWarmupTicks) + " of warm-up.", _art.Mothball, "Mothball",
                () => Send(CommandType.Mothball, _fleet, 0), ModalPopup.Tone.Normal);
        }

        private void ConfirmScan()
        {
            if (_sim == null || !IsRimLane(_lane))
                return;
            var radar = _sim.Balance.Radar;
            _popup.Confirm("FOCUSED SCAN · " + LaneName(_lane),
                "Spends " + UiFormat.Number(radar.StartupBatteries) + " batteries from the core base and integrates for " +
                UiFormat.Span(radar.IntegrationTicks) + ", then reveals hidden fleets on the lane for " +
                UiFormat.Span(radar.RevealTicks) + ".\n\nFleets on the lane hear the scan on their RWR after " +
                UiFormat.Span(radar.RwrDelayTicks) + " and may jam it.", _art.Scan, "Start scan",
                () => Send(CommandType.ScanBelt, _lane, 0), ModalPopup.Tone.Normal);
        }

        private void ConfirmEcm()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            var radar = _sim.Balance.Radar;
            _popup.Confirm("ECM JAM",
                "Spends " + UiFormat.Number(radar.JamBatteries) + " batteries from the core base to stretch every hostile scan " +
                "on this fleet's lane to " + UiFormat.Span(radar.JammedIntegrationTicks) + " of integration.",
                _art.Ecm, "Jam", () => Send(CommandType.EcmJam, _fleet, 0));
        }

        private void NextLane()
        {
            _lane = FirstRimLane(_lane);
            _shownKey = long.MinValue;
            Refresh();
        }

        // The next outer-rim lane after `after` (in store order) touching the fleet's site or lane, else any in its system.
        private EntityId FirstRimLane(EntityId after)
        {
            if (_sim == null)
                return default;
            int cluster = FleetCluster();
            EntityId first = default, next = default;
            bool passed = after.IsNull;
            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                ref var lane = ref _sim.Starlanes.At(i);
                if (lane.Tier != StarlaneTier.OuterRimBypass || !_sim.Sites.Has(lane.SiteA) ||
                    _sim.Sites.Get(lane.SiteA).ClusterId != cluster)
                    continue;
                var id = _sim.Starlanes.EntityAt(i);
                if (first.IsNull)
                    first = id;
                if (passed && next.IsNull)
                    next = id;
                if (id == after)
                    passed = true;
            }
            return next.IsNull ? first : next;
        }

        private int FleetCluster()
        {
            if (!_sim.Fleets.Has(_fleet))
                return -1;
            ref var motion = ref _sim.FleetMotions.Get(_fleet);
            var site = !motion.Site.IsNull ? motion.Site : _sim.Starlanes.Has(motion.Lane) ? _sim.Starlanes.Get(motion.Lane).SiteA : default;
            return _sim.Sites.Has(site) ? _sim.Sites.Get(site).ClusterId : -1;
        }

        private bool IsRimLane(EntityId id) =>
            _sim != null && _sim.Starlanes.Has(id) && _sim.Starlanes.Get(id).Tier == StarlaneTier.OuterRimBypass;

        private int FindScan(EntityId lane)
        {
            for (int i = 0; i < _sim.RadarScans.Count; i++)
            {
                ref var scan = ref _sim.RadarScans.At(i);
                if (scan.OwnerPlayerId == _playerId && scan.Lane == lane)
                    return i;
            }
            return -1;
        }

        private long ScanLeft(int index, long tick)
        {
            ref var scan = ref _sim.RadarScans.At(index);
            return System.Math.Max(0, scan.StartTick + scan.IntegrationTicks - tick);
        }

        private string LaneName(EntityId id)
        {
            ref var lane = ref _sim.Starlanes.Get(id);
            return UiFormat.SiteName(in _sim.Sites.Get(lane.SiteA)) + " ↔ " + UiFormat.SiteName(in _sim.Sites.Get(lane.SiteB));
        }

        private void Send(CommandType type, EntityId target, int paramA)
        {
            if (_sim == null || target.IsNull)
                return;
            var command = new DelayedPlayerCommand { PlayerId = _playerId, CommandType = type, Target = target, ParamA = paramA };
            string name = InFlightStrip.CommandName(type).ToUpperInvariant();
            var result = _sim.Issuer.Issue(command, out long executeAt);
            _feedback.text = result == IssueResult.Issued
                ? "SENT · " + name + " · LANDS IN " + UiFormat.Number(executeAt - _sim.Clock.CurrentTick) + "t"
                : "REFUSED · " + name + " · " + FleetCard.RefusalText(result);
            _shownKey = long.MinValue;
        }
    }
}
