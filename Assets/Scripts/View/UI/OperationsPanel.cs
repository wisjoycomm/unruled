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
    /// the audit, THE SYSTEM's loan (debt, due date, borrow and repay in steps), the shipyard (class x count at the
    /// research tier, retrofit of the selected fleet), the jump gate and standing orders (Auto-SOP: hand the empire to the
    /// Utility AI under a picked preset, or take it back).
    /// SITE (the selected site, the core base until one is picked): claim, an item picker, drills (rig, pause/resume,
    /// retract), refineries (build, output cap) and the selected fleet's cargo and convoy route. Buttons only forward
    /// commands through <see cref="CommandIssuer"/>; the feedback line says when an order lands or why it was refused.
    /// </summary>
    public sealed class OperationsPanel
    {
        private const string OnClass = "seg__item--on";
        private const int CargoStep = 100;
        private const int LoanStep = 1_000; // credits per borrow or repay click
        private const long Unset = long.MinValue;
        private static readonly int[] ShipCounts = { 1, 5, 10 };
        private static readonly int[] Caps = { 0, 1_000, 5_000 };

        private static readonly string[] BranchCodes = { "MIL", "IND", "LOG", "SIG" };

        private readonly Button[] _tabs = new Button[3];
        private readonly VisualElement[] _panels = new VisualElement[3];
        private readonly Button[] _researchBranches = new Button[TechProgress.BranchCount];
        private readonly Button[] _shipClass = new Button[4];
        private readonly Button[] _shipCount = new Button[3];
        private readonly Button[] _caps = new Button[3];
        private readonly Label _research, _ships, _gate, _loan, _sop, _baseFeedback;
        private readonly Label _siteName, _siteOwner, _item, _drills, _refinery, _cargo, _siteFeedback;
        private readonly Button _stressTest, _audit, _borrow, _repay, _build, _retrofit, _openGate, _sopPreset, _sopToggle;
        private readonly Button _claim, _relay, _rig, _pause, _retract, _buildRefinery, _load, _unload, _route;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _hq;
        private EntityId _fleet;
        private EntityId _site;
        private int _tab;
        private int _branchIndex;
        private int _classIndex;
        private int _countIndex;
        private int _itemSlot;
        private int _presetIndex = 1; // Industrialist_Trader
        private bool _drillsPaused;
        private readonly long[] _cache = new long[11];

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
            for (int i = 0; i < _researchBranches.Length; i++)
            {
                int index = i;
                _researchBranches[i] = TopTelemetryHud.Require<Button>(root, "research-branch-" + UiFormat.Number(i));
                _researchBranches[i].clicked += () =>
                {
                    Pick(_researchBranches, ref _branchIndex, index);
                    ResetCache();
                    Refresh();
                };
            }
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
            _loan = TopTelemetryHud.Require<Label>(root, "loan-status");
            _sop = TopTelemetryHud.Require<Label>(root, "sop-status");
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
            _borrow = TopTelemetryHud.Require<Button>(root, "do-borrow");
            _repay = TopTelemetryHud.Require<Button>(root, "do-repay");
            _build = TopTelemetryHud.Require<Button>(root, "do-build-ships");
            _retrofit = TopTelemetryHud.Require<Button>(root, "do-retrofit");
            _openGate = TopTelemetryHud.Require<Button>(root, "do-open-gate");
            _sopPreset = TopTelemetryHud.Require<Button>(root, "sop-preset");
            _sopToggle = TopTelemetryHud.Require<Button>(root, "do-sop");
            _claim = TopTelemetryHud.Require<Button>(root, "do-claim");
            _relay = TopTelemetryHud.Require<Button>(root, "do-relay");
            _rig = TopTelemetryHud.Require<Button>(root, "do-rig");
            _pause = TopTelemetryHud.Require<Button>(root, "do-pause");
            _retract = TopTelemetryHud.Require<Button>(root, "do-retract");
            _buildRefinery = TopTelemetryHud.Require<Button>(root, "do-refinery");
            _load = TopTelemetryHud.Require<Button>(root, "do-load");
            _unload = TopTelemetryHud.Require<Button>(root, "do-unload");
            _route = TopTelemetryHud.Require<Button>(root, "do-route");
            var prev = TopTelemetryHud.Require<Button>(root, "item-prev");
            var next = TopTelemetryHud.Require<Button>(root, "item-next");

            _stressTest.clicked += () => SendBase(CommandType.StressTest, CoreBase, _branchIndex);
            _audit.clicked += () => SendBase(CommandType.AuditOptIn, CoreBase, IsAudited ? 0 : 1);
            _borrow.clicked += () => SendBase(CommandType.Borrow, CoreBase, LoanStep);
            _repay.clicked += () => SendBase(CommandType.Repay, CoreBase, LoanStep);
            _build.clicked += IssueBuild;
            _retrofit.clicked += () => SendBase(CommandType.Retrofit, _fleet, 0);
            _openGate.clicked += IssueOpenGate;
            _sopPreset.clicked += StepPreset;
            _sopToggle.clicked += IssueStandingOrders;
            _claim.clicked += () => SendSite(CommandType.ClaimSite, _site, 0, 0);
            _relay.clicked += () => SendSite(CommandType.BuildRelay, _site, 0, 0);
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
                var picked = (ResearchBranch)_branchIndex;
                byte nextTier = (byte)System.Math.Min((int)ResearchBalance.TopTier, progress.Tier(picked) + 1);
                int targetMilli = _sim.Balance.Research.TierTelemetryTargetMilli(nextTier);
                bool ready = progress.Stage == ResearchStage.StressTest &&
                             (progress.TelemetryMilli >= targetMilli || progress.TelemetryMilli == 0);
                long value = progress.Stage == ResearchStage.Retooling
                    ? (progress.RetoolUntilTick - tick)
                    : ready ? progress.FailedTests : System.Math.Min(100L, (long)progress.TelemetryMilli * 100 / targetMilli);
                long tiers = ((long)progress.TechTier << 24) | ((long)progress.IndustryTier << 16) |
                             ((long)progress.LogisticsTier << 8) | progress.SignalsTier;
                long key = ((long)progress.Stage << 60) | ((long)_branchIndex << 56) |
                           ((long)progress.ActiveBranch << 52) | (ready ? 1L << 51 : 0) | (tiers << 20) | (value & 0xFFFFF);
                if (key != _cache[0])
                {
                    for (int i = 0; i < _researchBranches.Length; i++)
                        _researchBranches[i].text = BranchCodes[i] + " T" + UiFormat.Number(progress.Tier((ResearchBranch)i));
                    UiFormat.Set(_research, ref _cache[0], key, ResearchText(in progress, picked, value, ready));
                }
                _stressTest.SetEnabled(ready && progress.Tier(picked) < ResearchBalance.TopTier);
            }
            _audit.text = IsAudited ? "Leave audit" : "Join audit";
            RefreshLoan(tick);

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
            RefreshStandingOrders();
        }

        private bool IsAuto => _sim.AiControllers.Has(_hq);

        private void RefreshStandingOrders()
        {
            bool auto = IsAuto;
            int archetype = auto ? _sim.AiControllers.Get(_hq).Archetype : 0;
            bool pending = _sim.Issuer.IsInFlight(_playerId, CommandType.SetStandingOrders, CoreBase);
            long key = (uint)archetype | (pending ? 1L << 8 : 0) | (long)_presetIndex << 16;
            if (key == _cache[10])
                return;
            UiFormat.Set(_sop, ref _cache[10], key, (auto ? "AUTO · " + PresetName(archetype) : "OFF · YOU COMMAND") +
                (pending ? " · ORDER SENT" : ""));
            _sopPreset.text = PresetName(_presetIndex + 1);
            _sopPreset.SetEnabled(!auto);
            _sopToggle.text = auto ? "Take command" : "Hand over";
            _sopToggle.SetEnabled(!pending);
        }

        // "Industrialist_Trader" -> "INDUSTRIALIST"
        private string PresetName(int archetype)
        {
            string code = _sim.Balance.Ai.Preset((byte)archetype).Code;
            int cut = code.IndexOf('_');
            return (cut > 0 ? code.Substring(0, cut) : code).ToUpperInvariant();
        }

        private void StepPreset()
        {
            if (_sim == null)
                return;
            _presetIndex = (_presetIndex + 1) % _sim.Balance.Ai.Count;
            Refresh();
        }

        private void IssueStandingOrders()
        {
            if (_sim != null)
                SendBase(CommandType.SetStandingOrders, CoreBase, IsAuto ? 0 : _presetIndex + 1);
        }

        private void RefreshLoan(long tick)
        {
            if (!_sim.Loans.Has(_hq))
                return;
            ref var loan = ref _sim.Loans.Get(_hq);
            bool overdue = LoanSystem.IsOverdue(in loan, tick);
            long left = loan.DebtCents == 0 ? 0 : (loan.DueTick - tick) / SimTick.TicksPerMinute;
            long key = (loan.DebtCents << 20) ^ left ^ (overdue ? 1L << 62 : 0);
            if (key != _cache[9])
                UiFormat.Set(_loan, ref _cache[9], key, LoanText(in loan, overdue, tick));
            _borrow.SetEnabled(_sim.Lending.HeadroomCents(_hq, tick) > 0);
            _repay.SetEnabled(loan.DebtCents > 0 && _sim.Ledger.Balance(_hq) > 0);
        }

        private string LoanText(in Loan loan, bool overdue, long tick)
        {
            long limit = _sim.Balance.Ledger.CreditLimitCents / Wallet.CentsPerCredit;
            if (loan.DebtCents == 0)
                return "NO DEBT · LIMIT " + UiFormat.Thousands(limit) + " CR";
            string debt = "OWES " + UiFormat.Price(loan.DebtCents) + " CR";
            if (overdue)
                return debt + " · OVERDUE · GOODS SEIZED DAILY";
            string free = tick < _sim.Balance.Ledger.InterestFreeTicks ? " · INTEREST-FREE" : "";
            return debt + " · DUE IN " + UiFormat.Duration(loan.DueTick - tick) + free;
        }

        private string ResearchText(in TechProgress progress, ResearchBranch picked, long value, bool ready)
        {
            byte pickedTier = progress.Tier(picked);
            string head = BranchCodes[(int)picked] + " T" + UiFormat.Number(pickedTier) + " · ";
            if (progress.Stage == ResearchStage.Retooling)
            {
                var active = progress.ActiveBranch;
                return BranchCodes[(int)active] + " T" + UiFormat.Number(progress.Tier(active) + 1) +
                       " · RETOOLING " + UiFormat.Duration(value);
            }
            if (pickedTier >= ResearchBalance.TopTier || progress.Stage == ResearchStage.Complete)
                return head + "COMPLETE";
            if (!ready)
                return head + "TELEMETRY " + UiFormat.Number(value) + "%";
            var cost = _sim.Balance.Research.Tier((byte)(pickedTier + 1)).StressTestCost;
            string text = head + "TEST";
            for (int i = 0; i < cost.Length; i++)
                text += " · " + UiFormat.Number(cost[i].Quantity) + " " + ContractAndMarketUI.ItemCode(cost[i].Item);
            return text;
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
            bool lit = _sim.Relays.Has(_site);
            _relay.text = lit ? "Relay lit" : "Build relay";
            _relay.SetEnabled(own && !lit);
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
            if (owner == 0) return "NEUTRAL";
            if (owner == _playerId) return "YOURS";
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
