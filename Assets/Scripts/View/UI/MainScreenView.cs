using Unruled.Core.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// Owns the main screen's UIDocument and its panel controllers. After <see cref="Bind"/>, it refreshes every panel
    /// once per sim tick (not per frame) and routes selection from the board to the panels. One <see cref="ModalPopup"/>
    /// serves every confirm dialog, the bulletin log, the game menu and the end screen.
    /// </summary>
    public sealed class MainScreenView : MonoBehaviour
    {
        private const string ActiveTabClass = "board__tab--active";

        [SerializeField] private UIDocument _document;

        private TopTelemetryHud _top;
        private LeftInventoryDock _dock;
        private ContractAndMarketUI _market;
        private FleetCard _fleet;
        private InFlightStrip _inFlight;
        private OperationsPanel _ops;
        private MarketTradePanel _trade;
        private FleetOpsPanel _fleetOps;
        private BulletinFeed _bulletins;
        private BoardPreviewCard _preview;
        private ModalPopup _popup;
        private UiArt _art;
        private SimulationWorld _sim;
        private long _shownTick = -1;

        /// <summary>The board's screen area (the UI leaves it transparent for the board renderer).</summary>
        public VisualElement BoardView { get; private set; }

        public FleetCard Fleet => _fleet;

        /// <summary>The planet / star-system preview plate over the board.</summary>
        public BoardPreviewCard Preview => _preview;

        /// <summary>Phone layout switch; null before <see cref="Bind"/>.</summary>
        public CompactLayout Layout { get; private set; }

        public Button SystemTab { get; private set; }

        public Button GalaxyTab { get; private set; }

        /// <summary>The game menu and end screen; <see cref="GameSession"/> binds its session callbacks.</summary>
        public GameMenu Menu { get; private set; }

        public void Bind(SimulationWorld sim, int playerId)
        {
            if (_document == null)
                throw new MissingReferenceException("MainScreenView needs its UIDocument.");
            var root = _document.rootVisualElement;
            if (_popup == null)
            {
                _art = new UiArt();
                _popup = new ModalPopup(root);
                _top = new TopTelemetryHud(root);
                _dock = new LeftInventoryDock(root);
                _market = new ContractAndMarketUI(root);
                _fleet = new FleetCard(root, _popup, _art);
                _inFlight = new InFlightStrip(root);
                _ops = new OperationsPanel(root, _popup, _art);
                _trade = new MarketTradePanel(root, _popup, _art);
                _fleetOps = new FleetOpsPanel(root, _popup, _art);
                _bulletins = new BulletinFeed(root, _popup, _art);
                Menu = new GameMenu(root, _popup, _art);
            }
            _popup.Close();
            if (Layout == null)
                Layout = new CompactLayout(root);
            BoardView = TopTelemetryHud.Require<VisualElement>(root, "board-view");
            if (_preview == null)
                _preview = new BoardPreviewCard(BoardView, _art);
            SystemTab = TopTelemetryHud.Require<Button>(root, "tab-system");
            GalaxyTab = TopTelemetryHud.Require<Button>(root, "tab-galaxy");

            _sim = sim;
            _top.Bind(sim, playerId);
            _dock.Bind(sim, playerId);
            _market.Bind(sim);
            _fleet.Bind(sim, playerId);
            _inFlight.Bind(sim, playerId);
            _ops.Bind(sim, playerId);
            _trade.Bind(sim, playerId);
            _fleetOps.Bind(sim, playerId);
            _bulletins.Bind(sim, playerId);
            Menu.Bind(sim, playerId);
            _preview.Bind(sim, playerId);
            _ops.SelectFleet(_fleet.Fleet);
            _fleetOps.SelectFleet(_fleet.Fleet);
            _top.SetSelection(_fleet.Fleet);
            _shownTick = -1;
            RefreshAll();
        }

        /// <summary>Marks the board tab that is showing; the system tab names the last system shown.</summary>
        public void ShowBoardTab(bool galaxy, int clusterId)
        {
            SystemTab.text = "SYSTEM " + UiFormat.StarName(clusterId);
            SystemTab.EnableInClassList(ActiveTabClass, !galaxy);
            GalaxyTab.EnableInClassList(ActiveTabClass, galaxy);
        }

        /// <summary>Selects a fleet for the card; the latency readout follows it.</summary>
        public void SelectFleet(EntityId fleet)
        {
            if (_fleet == null)
                return;
            _fleet.Select(fleet);
            _ops.SelectFleet(fleet);
            _fleetOps.SelectFleet(fleet);
            _top.SetSelection(fleet);
            RefreshAll();
        }

        /// <summary>
        /// Selects a site or lane: latency to it, the selected fleet's Move destination and, for a site, the SITE tab.
        /// </summary>
        public void SelectTarget(EntityId siteOrLane)
        {
            if (_fleet == null)
                return;
            _fleet.SetMoveTarget(siteOrLane);
            _ops.SelectSite(siteOrLane);
            _fleetOps.SelectTarget(siteOrLane);
            _top.SetSelection(siteOrLane);
            if (_sim.Sites.Has(siteOrLane))
                _preview.ShowSite(siteOrLane);
            else
                _preview.Hide();
            RefreshAll();
        }

        private void LateUpdate()
        {
            if (_sim == null)
                return;
            _bulletins.Tick(Time.unscaledTime);
            if (_sim.Clock.CurrentTick != _shownTick)
                RefreshAll();
        }

        private void RefreshAll()
        {
            _shownTick = _sim.Clock.CurrentTick;
            _top.Refresh();
            _dock.Refresh();
            _preview.Refresh();
            _market.Refresh();
            _fleet.Refresh();
            _inFlight.Refresh();
            _ops.Refresh();
            _trade.Refresh();
            _fleetOps.Refresh();
            _bulletins.Refresh();
            Menu.Refresh();
        }
    }
}
