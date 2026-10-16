using Unruled.Core.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// Owns the main screen's UIDocument and its panel controllers. After <see cref="Bind"/>, it refreshes every panel
    /// once per sim tick (not per frame) and routes selection from the board to the panels.
    /// </summary>
    public sealed class MainScreenView : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;

        private TopTelemetryHud _top;
        private LeftInventoryDock _dock;
        private ContractAndMarketUI _market;
        private FleetCard _fleet;
        private InFlightStrip _inFlight;
        private SimulationWorld _sim;
        private long _shownTick = -1;

        /// <summary>The board's screen area (the UI leaves it transparent for the board renderer).</summary>
        public VisualElement BoardView { get; private set; }

        public FleetCard Fleet => _fleet;

        public void Bind(SimulationWorld sim, int playerId)
        {
            if (_document == null)
                throw new MissingReferenceException("MainScreenView needs its UIDocument.");
            var root = _document.rootVisualElement;
            _top = new TopTelemetryHud(root);
            _dock = new LeftInventoryDock(root);
            _market = new ContractAndMarketUI(root);
            _fleet = new FleetCard(root);
            _inFlight = new InFlightStrip(root);
            BoardView = TopTelemetryHud.Require<VisualElement>(root, "board-view");
            var systemTab = TopTelemetryHud.Require<Button>(root, "tab-system");
            if (HudQueries.TryFindHq(sim, playerId, out var hq) && sim.Sites.Has(sim.Players.Get(hq).CoreBase))
                systemTab.text = "SYSTEM " + UiFormat.StarName(sim.Sites.Get(sim.Players.Get(hq).CoreBase).ClusterId);

            _sim = sim;
            _top.Bind(sim, playerId);
            _dock.Bind(sim, playerId);
            _market.Bind(sim);
            _fleet.Bind(sim, playerId);
            _inFlight.Bind(sim, playerId);
            _top.SetSelection(_fleet.Fleet);
            _shownTick = -1;
            RefreshAll();
        }

        /// <summary>Selects a fleet for the card; the latency readout follows it.</summary>
        public void SelectFleet(EntityId fleet)
        {
            if (_fleet == null)
                return;
            _fleet.Select(fleet);
            _top.SetSelection(fleet);
            RefreshAll();
        }

        /// <summary>Selects a site or lane: latency to it, and the selected fleet's Move destination.</summary>
        public void SelectTarget(EntityId siteOrLane)
        {
            if (_fleet == null)
                return;
            _fleet.SetMoveTarget(siteOrLane);
            _top.SetSelection(siteOrLane);
            RefreshAll();
        }

        private void LateUpdate()
        {
            if (_sim != null && _sim.Clock.CurrentTick != _shownTick)
                RefreshAll();
        }

        private void RefreshAll()
        {
            _shownTick = _sim.Clock.CurrentTick;
            _top.Refresh();
            _dock.Refresh();
            _market.Refresh();
            _fleet.Refresh();
            _inFlight.Refresh();
        }
    }
}
