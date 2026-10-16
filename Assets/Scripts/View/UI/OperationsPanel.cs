using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The side column's tabs (MARKET, BASE, SITE) and the two operations panels. BASE: research and the stress test,
    /// the audit, the shipyard (class x count at the research tier, retrofit of the selected fleet) and the jump gate.
    /// SITE (the selected site, the core base until one is picked): claim, an item picker, drills (rig, pause/resume,
    /// retract), refineries (build, output cap) and the selected fleet's cargo and convoy route. Buttons only forward
    /// commands through <see cref="CommandIssuer"/>; the feedback line says when an order lands or why it was refused.
    /// </summary>
    public sealed class OperationsPanel
    {
        private const string OnClass = "seg__item--on";
        private const int CargoStep = 100;
        private const long Unset = long.MinValue;
        private static readonly int[] ShipCounts = { 1, 5, 10 };
        private static readonly int[] Caps = { 0, 1_000, 5_000 };

        private readonly Button[] _tabs = new Button[3];
        private readonly VisualElement[] _panels = new VisualElement[3];
        private readonly Button[] _shipClass = new Button[4];
        private readonly Button[] _shipCount = new Button[3];
        private readonly Button[] _caps = new Button[3];
        private readonly Label _research, _ships, _gate, _baseFeedback;
        private readonly Label _siteName, _siteOwner, _item, _drills, _refinery, _cargo, _siteFeedback;
        private readonly Button _stressTest, _audit, _build, _retrofit, _openGate;
        private readonly Button _claim, _rig, _pause, _retract, _buildRefinery, _load, _unload, _route;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _hq;
        private EntityId _fleet;
        private EntityId _site;
        private int _tab;
        private int _classIndex;
        private int _countIndex;
        private int _itemSlot;
        private bool _drillsPaused;
        private readonly long[] _cache = new long[9];

        public OperationsPanel(VisualElement root)
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                int tab = i;
                _tabs[i] = TopTelemetryHud.Require<Button>(root, "side-tab-" + UiFormat.Number(i));
                _tabs[i].clicked += () => ShowTab(tab);
            }
            _panels[0] = TopTelemetryHud.Require<VisualElement>(root, "market");
            _panels[1] = TopTelemetryHud.Require<VisualElement>(root, "ops-base");
            _panels[2] = TopTelemetryHud.Require<VisualElement>(root, "ops-site");
            for (int i = 0; i < _shipClass.Length; i++)
            {
                int index = i;
                _shipClass[i] = TopTelemetryHud.Require<Button>(root, "ship-class-" + UiFormat.Number(i));
                _shipClass[i].clicked += () => Pick(_shipClass, ref _classIndex, index);
            }
            for (int i = 0; i < _shipCount.Length; i++)
            {
                int index = i;
                _shipCount[i] = TopTelemetryHud.Require<Button>(root, "ship-count-" + UiFormat.Number(i));
                _shipCount[i].clicked += () => Pick(_shipCount, ref _countIndex, index);
            }
            for (int i = 0; i < _caps.Length; i++)
            {
                int cap = Caps[i];
                _caps[i] = TopTelemetryHud.Require<Button>(root, "cap-" + UiFormat.Number(i));
                _caps[i].clicked += () => SendSite(CommandType.SetRefineryCap, _site, (int)Item, cap);
            }

            _research = TopTelemetryHud.Require<Label>(root, "research-status");
            _ships = TopTelemetryHud.Require<Label>(root, "ship-status");
            _gate = TopTelemetryHud.Require<Label>(root, "gate-status");
            _baseFeedback = TopTelemetryHud.Require<Label>(root, "base-feedback");
            _siteName = TopTelemetryHud.Require<Label>(root, "site-name");
            _siteOwner = TopTelemetryHud.Require<Label>(root, "site-owner");
            _item = TopTelemetryHud.Require<Label>(root, "item-name");
            _drills = TopTelemetryHud.Require<Label>(root, "drill-status");
            _refinery = TopTelemetryHud.Require<Label>(root, "refinery-status");
            _cargo = TopTelemetryHud.Require<Label>(root, "cargo-status");
            _siteFeedback = TopTelemetryHud.Require<Label>(root, "site-feedback");

            _stressTest = TopTelemetryHud.Require<Button>(root, "do-stress-test");
            _audit = TopTelemetryHud.Require<Button>(root, "do-audit");
            _build = TopTelemetryHud.Require<Button>(root, "do-build-ships");
            _retrofit = TopTelemetryHud.Require<Button>(root, "do-retrofit");
            _openGate = TopTelemetryHud.Require<Button>(root, "do-open-gate");
            _claim = TopTelemetryHud.Require<Button>(root, "do-claim");
            _rig = TopTelemetryHud.Require<Button>(root, "do-rig");
            _pause = TopTelemetryHud.Require<Button>(root, "do-pause");
            _retract = TopTelemetryHud.Require<Button>(root, "do-retract");
            _buildRefinery = TopTelemetryHud.Require<Button>(root, "do-refinery");
            _load = TopTelemetryHud.Require<Button>(root, "do-load");
            _unload = TopTelemetryHud.Require<Button>(root, "do-unload");
            _route = TopTelemetryHud.Require<Button>(root, "do-route");
            var prev = TopTelemetryHud.Require<Button>(root, "item-prev");
            var next = TopTelemetryHud.Require<Button>(root, "item-next");

            _stressTest.clicked += () => SendBase(CommandType.StressTest, CoreBase, 0);
            _audit.clicked += () => SendBase(CommandType.AuditOptIn, CoreBase, IsAudited ? 0 : 1);
            _build.clicked += IssueBuild;
            _retrofit.clicked += () => SendBase(CommandType.Retrofit, _fleet, 0);
            _openGate.clicked += IssueOpenGate;
            _claim.clicked += () => SendSite(CommandType.ClaimSite, _site, 0, 0);
            _rig.clicked += IssueRig;
            _pause.clicked += IssuePause;
            _retract.clicked += IssueRetract;
            _buildRefinery.clicked += () => SendSite(CommandType.BuildRefinery, _site, (int)Item, 0);
            _load.clicked += () => SendSite(CommandType.TransferCargo, _fleet, (int)Item, CargoStep);
            _unload.clicked += () => SendSite(CommandType.TransferCargo, _fleet, (int)Item, -CargoStep);
            _route.clicked += IssueRoute;
            prev.clicked += () => StepItem(-1);
            next.clicked += () => StepItem(1);
        }

        private ItemId Item => ItemIds.FromSlot(_itemSlot);

        private EntityId CoreBase => _sim != null && _sim.Players.Has(_hq) ? _sim.Players.Get(_hq).CoreBase : default;

        private bool IsAudited => _sim != null && _sim.Standings.Has(_hq) && _sim.Standings.Get(_hq).IsAudited;

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            HudQueries.TryFindHq(sim, playerId, out _hq);
            _site = CoreBase;
            ResetCache();
        }

        /// <summary>The fleet for retrofit, cargo and convoy routes.</summary>
        public void SelectFleet(EntityId fleet)
        {
            _fleet = fleet;
            ResetCache();
        }

        /// <summary>The site the SITE tab works on; lanes are ignored.</summary>
        public void SelectSite(EntityId siteOrLane)
        {
            if (_sim == null || !_sim.Sites.Has(siteOrLane))
                return;
            _site = siteOrLane;
            ResetCache();
        }

        public void Refresh()
        {
            if (_sim == null)
                return;
            if (_tab == 1)
                RefreshBase();
            else if (_tab == 2)
                RefreshSite();
        }

        private void ShowTab(int tab)
        {
            _tab = tab;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].EnableInClassList(OnClass, i == tab);
                _panels[i].style.display = i == tab ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ResetCache();
            Refresh();
        }

        private static void Pick(Button[] buttons, ref int field, int index)
        {
            field = index;
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].EnableInClassList(OnClass, i == index);
        }

        private void StepItem(int step)
        {
            _itemSlot = (_itemSlot + step + ItemIds.Count) % ItemIds.Count;
            ResetCache();
            Refresh();
        }

        private void ResetCache()
        {
            for (int i = 0; i < _cache.Length; i++)
                _cache[i] = Unset;
        }

        // ---------- BASE ----------

        private void RefreshBase()
        {
            long tick = _sim.Clock.CurrentTick;
            byte tier = _sim.TechGates.TechTierOf(_playerId);
            if (_sim.TechProgress.Has(_hq))
            {
                ref var progress = ref _sim.TechProgress.Get(_hq);
                long value = progress.Stage == ResearchStage.FieldTelemetry
                    ? (long)progress.TelemetryMilli * 100 / _sim.Balance.Research.TelemetryTargetMilli
                    : progress.Stage == ResearchStage.Retooling ? (progress.RetoolUntilTick - tick) : progress.FailedTests;
                long key = ((long)progress.Stage << 56) | ((long)progress.TechTier << 48) | (value & 0xFFFFFFFFFFFF);
                if (key != _cache[0])
                    UiFormat.Set(_research, ref _cache[0], key, ResearchText(in progress, value));
                _stressTest.SetEnabled(progress.Stage == ResearchStage.StressTest);
            }
            _audit.text = IsAudited ? "Leave audit" : "Join audit";

            int queued = 0;
            for (int i = 0; i < _sim.ShipOrders.Count; i++)
                if (_sim.ShipOrders.At(i).OwnerPlayerId == _playerId)
                    queued += _sim.ShipOrders.At(i).Count;
            long shipKey = (long)tier << 32 | (uint)queued;
            if (shipKey != _cache[1])
                UiFormat.Set(_ships, ref _cache[1], shipKey,
                    "TIER " + UiFormat.Number(tier) + " · " + UiFormat.Number(queued) + " QUEUED");
            _retrofit.SetEnabled(_sim.Fleets.Has(_fleet) && _sim.Fleets.Get(_fleet).TechTier < tier);

            bool hasGate = TryFindGate(out var gate);
            bool unlocked = _sim.TechGates.Unlocks(_playerId, TechGate.JumpGate);
            long gateKey = !hasGate ? 0 : gate.IsOpen ? 1 : unlocked ? 2 : 3;
            if (gateKey != _cache[2])
                UiFormat.Set(_gate, ref _cache[2], gateKey, GateText(gateKey));
            _openGate.SetEnabled(hasGate && !gate.IsOpen && unlocked);
        }

        private string ResearchText(in TechProgress progress, long value)
        {
            string tier = "TIER " + UiFormat.Number(progress.TechTier) + " · ";
            switch (progress.Stage)
            {
                case ResearchStage.FieldTelemetry:
                    return tier + "TELEMETRY " + UiFormat.Number(value) + "%";
                case ResearchStage.StressTest:
                    var cost = _sim.Balance.Research.Tier((byte)(progress.TechTier + 1)).StressTestCost;
                    string text = tier + "TEST";
                    for (int i = 0; i < cost.Length; i++)
                        text += " · " + UiFormat.Number(cost[i].Quantity) + " " + ContractAndMarketUI.ItemCode(cost[i].Item);
                    return text;
                case ResearchStage.Retooling:
                    return tier + "RETOOLING " + UiFormat.Duration(value);
                default:
                    return tier + "COMPLETE";
            }
        }

        private string GateText(long state)
        {
            switch (state)
            {
                case 0: return "NO GATE";
                case 1: return "OPEN";
                case 3: return "NEEDS TIER " + UiFormat.Number(_sim.Balance.Research.GateTier(TechGate.JumpGate));
                default:
                    var rules = _sim.Balance.JumpGates;
                    string text = "DORMANT";
                    for (int i = 0; i < rules.OpenCostCount; i++)
                        text += " · " + UiFormat.Number(rules.OpenCost(i).Quantity) + " " +
                                ContractAndMarketUI.ItemCode(rules.OpenCost(i).Item);
                    return text;
            }
        }

        private bool TryFindGate(out JumpGate gate)
        {
            for (int i = 0; i < _sim.JumpGates.Count; i++)
                if (_sim.JumpGates.At(i).OwnerPlayerId == _playerId)
                {
                    gate = _sim.JumpGates.At(i);
                    return true;
                }
            gate = default;
            return false;
        }

        private void IssueBuild()
        {
            if (_sim == null)
                return;
            var command = Command(CommandType.BuildShips, CoreBase, _classIndex);
            command.ParamB = ShipCounts[_countIndex];
            command.ParamC = _sim.TechGates.TechTierOf(_playerId);
            Send(command, _baseFeedback);
        }

        private void IssueOpenGate()
        {
            if (_sim != null && TryFindGate(out var gate))
                SendBase(CommandType.OpenJumpGate, gate.HomeSite, 0);
        }

        // ---------- SITE ----------

        private void RefreshSite()
        {
            if (!_sim.Sites.Has(_site))
                return;
            ref var site = ref _sim.Sites.Get(_site);
            long nameKey = (long)_site.Index << 32 | (uint)(site.OwnerPlayerId + int.MaxValue / 2);
            if (nameKey != _cache[3])
            {
                UiFormat.Set(_siteName, ref _cache[3], nameKey, UiFormat.SiteName(in site));
                _siteOwner.text = OwnerText(site.OwnerPlayerId);
            }
            bool own = site.OwnerPlayerId == _playerId;
            _claim.SetEnabled(site.OwnerPlayerId >= 0 && !own);
            if (_itemSlot != _cache[4])
                UiFormat.Set(_item, ref _cache[4], _itemSlot, ContractAndMarketUI.ItemCode(Item));

            int deposits = 0, rigs = 0, paused = 0;
            for (int d = 0; d < _sim.Deposits.Count; d++)
            {
                ref var deposit = ref _sim.Deposits.At(d);
                if (deposit.Site != _site)
                    continue;
                deposits++;
                rigs += deposit.RigCount;
                if (deposit.IsPaused)
                    paused++;
            }
            _drillsPaused = deposits > 0 && paused == deposits;
            long drillKey = (long)deposits << 40 | (long)rigs << 8 | (_drillsPaused ? 1L : 0L);
            if (drillKey != _cache[5])
                UiFormat.Set(_drills, ref _cache[5], drillKey,
                    UiFormat.Number(deposits) + " DEPOSITS · " + UiFormat.Number(rigs) + " RIGS" + (_drillsPaused ? " · PAUSED" : ""));
            _pause.text = _drillsPaused ? "Resume drills" : "Pause drills";
            _rig.SetEnabled(own && deposits > 0);
            _pause.SetEnabled(own && rigs > 0);
            _retract.SetEnabled(own && rigs > 0);

            int refineries = 0, cap = -1;
            for (int r = 0; r < _sim.Refineries.Count; r++)
            {
                ref var refinery = ref _sim.Refineries.At(r);
                if (refinery.Site != _site || refinery.Output != Item)
                    continue;
                refineries++;
                cap = refinery.StockCap;
            }
            long refineryKey = (long)_itemSlot << 48 | (long)refineries << 32 | (uint)cap;
            if (refineryKey != _cache[6])
            {
                UiFormat.Set(_refinery, ref _cache[6], refineryKey, refineries == 0
                    ? "NONE FOR " + ContractAndMarketUI.ItemCode(Item)
                    : ContractAndMarketUI.ItemCode(Item) + " ×" + UiFormat.Number(refineries) + " · CAP " +
                      (cap == 0 ? "NONE" : UiFormat.Thousands(cap)));
                for (int i = 0; i < _caps.Length; i++)
                    _caps[i].EnableInClassList(OnClass, refineries > 0 && Caps[i] == cap);
            }
            bool refined = _itemSlot >= ItemIds.RawCount;
            _buildRefinery.SetEnabled(own && refined);
            for (int i = 0; i < _caps.Length; i++)
                _caps[i].SetEnabled(own && refineries > 0);

            RefreshCargo();
        }

        private void RefreshCargo()
        {
            bool has = _sim.Fleets.Has(_fleet);
            long hold = has ? _sim.Stockpiles.Get(_fleet).Get(Item) : -1;
            bool routed = has && _sim.ConvoyRoutes.Has(_fleet);
            long cargoKey = has ? (long)_fleet.Index << 40 | (long)_itemSlot << 32 | (uint)hold : -1;
            if (cargoKey != _cache[7] || routed != (_cache[8] == 1))
            {
                _cache[8] = routed ? 1 : 0;
                string text = !has ? "NO FLEET" : UiFormat.FleetName(HudQueries.FleetOrdinal(_sim, _fleet)) + " · " +
                    UiFormat.Thousands(hold) + " " + ContractAndMarketUI.ItemCode(Item);
                if (routed)
                    text += " · ROUTE → " + UiFormat.SiteName(in _sim.Sites.Get(_sim.ConvoyRoutes.Get(_fleet).Outpost));
                UiFormat.Set(_cargo, ref _cache[7], cargoKey, text);
            }
            bool docked = has && _sim.FleetMotions.Get(_fleet).Lane.IsNull;
            _load.SetEnabled(docked);
            _unload.SetEnabled(docked && hold > 0);
            _route.SetEnabled(has && _sim.Fleets.Get(_fleet).CargoConvoys > 0 && _sim.Sites.Has(_site));
        }

        private string OwnerText(int owner)
        {
            if (owner == _playerId) return "YOURS";
            if (owner == 0) return "NEUTRAL";
            if (owner < 0) return _sim.Citadels.Has(_site) ? "CITADEL" : "THE SYSTEM";
            return "RIVAL P" + UiFormat.Number(owner);
        }

        // The deposit of the picked ore at the site, or its first deposit.
        private EntityId PickDeposit()
        {
            EntityId first = default;
            for (int d = 0; d < _sim.Deposits.Count; d++)
            {
                ref var deposit = ref _sim.Deposits.At(d);
                if (deposit.Site != _site)
                    continue;
                if (deposit.RawItem == Item)
                    return _sim.Deposits.EntityAt(d);
                if (first.IsNull)
                    first = _sim.Deposits.EntityAt(d);
            }
            return first;
        }

        private void IssueRig()
        {
            if (_sim == null)
                return;
            var deposit = PickDeposit();
            if (!deposit.IsNull)
                SendSite(CommandType.BuildRig, deposit, 1, 0);
        }

        private void IssuePause() => ForEachDrilledDeposit(CommandType.PauseDrills, _drillsPaused ? 0 : 1);

        private void IssueRetract() => ForEachDrilledDeposit(CommandType.RetractDrills, 0);

        private void ForEachDrilledDeposit(CommandType type, int paramA)
        {
            if (_sim == null)
                return;
            for (int d = 0; d < _sim.Deposits.Count; d++)
                if (_sim.Deposits.At(d).Site == _site && _sim.Deposits.At(d).RigCount > 0)
                    SendSite(type, _sim.Deposits.EntityAt(d), paramA, 0);
        }

        private void IssueRoute()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            var command = Command(CommandType.SetConvoyRoute, _fleet, 0);
            command.ParamEntity = _site;
            command.ParamC = _sim.Fleets.Get(_fleet).CargoConvoys * _sim.Balance.Convoys.CapacityPerConvoy;
            Send(command, _siteFeedback);
        }

        // ---------- Sending ----------

        private DelayedPlayerCommand Command(CommandType type, EntityId target, int paramA) =>
            new DelayedPlayerCommand { PlayerId = _playerId, CommandType = type, Target = target, ParamA = paramA };

        private void SendBase(CommandType type, EntityId target, int paramA)
        {
            if (_sim != null)
                Send(Command(type, target, paramA), _baseFeedback);
        }

        private void SendSite(CommandType type, EntityId target, int paramA, int paramB)
        {
            if (_sim == null)
                return;
            var command = Command(type, target, paramA);
            command.ParamB = paramB;
            Send(command, _siteFeedback);
        }

        private void Send(DelayedPlayerCommand command, Label feedback)
        {
            string name = InFlightStrip.CommandName(command.CommandType).ToUpperInvariant();
            var result = _sim.Issuer.Issue(command, out long executeAt);
            feedback.text = result == IssueResult.Issued
                ? "SENT · " + name + " · LANDS IN " + UiFormat.Number(executeAt - _sim.Clock.CurrentTick) + "t"
                : "REFUSED · " + name + " · " + FleetCard.RefusalText(result);
        }
    }
}
