using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The selected fleet's card: ship classes, gun temperature against the release/lock thresholds, the hold's
    /// burnables, firepower (Ψ) and point defence (Ω), and its orders (Raid convoys toggles WTO piracy; Merge takes in
    /// another own warship fleet of its tier docked at the same site; Split detaches half of each ship class, rounded down,
    /// into a new fleet there; Nuclear strike fires the hold's nuclear core at the rival site a strike-tier fleet is docked at).
    /// Buttons only forward commands through
    /// <see cref="CommandIssuer"/>; the order lands after signal latency and shows in the in-flight strip.
    /// </summary>
    public sealed class FleetCard
    {
        private const int GaugeTopMilli = 110_000; // display range of the gun-temperature gauge
        private const int RefusalShowTicks = 5;
        private const string OnClass = "seg__item--on";
        private const string FillNominal = "gauge__fill--nominal";
        private const string FillLocked = "gauge__fill--locked";

        private readonly VisualElement _card;
        private readonly Label _name, _state;
        private readonly Label _line, _ew, _supply, _cargo;
        private readonly Label _thermalValue, _thermalMin, _thermalLimits, _thermalMax;
        private readonly VisualElement _fill, _release, _lock;
        private readonly Label _ammo, _battery, _coolant, _fuel, _psi, _omega;
        private readonly Button[] _rof = new Button[3];
        private readonly Button _move, _hold, _reverse, _vent, _raid, _merge, _split, _nuke;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _fleet;
        private EntityId _moveTarget;
        private string _refusal;
        private long _refusalUntil;
        private long _tempCache = long.MinValue;
        private int _rofShown = -1;
        private int _raidShown = -1;
        private readonly long[] _numbers = new long[10];

        public FleetCard(VisualElement root)
        {
            _card = TopTelemetryHud.Require<VisualElement>(root, "fleet-card");
            _name = TopTelemetryHud.Require<Label>(root, "fleet-name");
            _state = TopTelemetryHud.Require<Label>(root, "fleet-state");
            _line = TopTelemetryHud.Require<Label>(root, "part-line");
            _ew = TopTelemetryHud.Require<Label>(root, "part-ew");
            _supply = TopTelemetryHud.Require<Label>(root, "part-supply");
            _cargo = TopTelemetryHud.Require<Label>(root, "part-cargo");
            _thermalValue = TopTelemetryHud.Require<Label>(root, "thermal-value");
            _thermalMin = TopTelemetryHud.Require<Label>(root, "thermal-min");
            _thermalLimits = TopTelemetryHud.Require<Label>(root, "thermal-limits");
            _thermalMax = TopTelemetryHud.Require<Label>(root, "thermal-max");
            _fill = TopTelemetryHud.Require<VisualElement>(root, "thermal-fill");
            _release = TopTelemetryHud.Require<VisualElement>(root, "thermal-release");
            _lock = TopTelemetryHud.Require<VisualElement>(root, "thermal-lock");
            _ammo = TopTelemetryHud.Require<Label>(root, "stat-ammo");
            _battery = TopTelemetryHud.Require<Label>(root, "stat-battery");
            _coolant = TopTelemetryHud.Require<Label>(root, "stat-coolant");
            _fuel = TopTelemetryHud.Require<Label>(root, "stat-fuel");
            _psi = TopTelemetryHud.Require<Label>(root, "stat-psi");
            _omega = TopTelemetryHud.Require<Label>(root, "stat-omega");
            _rof[0] = TopTelemetryHud.Require<Button>(root, "rof-50");
            _rof[1] = TopTelemetryHud.Require<Button>(root, "rof-100");
            _rof[2] = TopTelemetryHud.Require<Button>(root, "rof-150");
            _move = TopTelemetryHud.Require<Button>(root, "order-move");
            _hold = TopTelemetryHud.Require<Button>(root, "order-hold");
            _reverse = TopTelemetryHud.Require<Button>(root, "order-reverse");
            _vent = TopTelemetryHud.Require<Button>(root, "order-vent");
            _raid = TopTelemetryHud.Require<Button>(root, "order-raid");
            _merge = TopTelemetryHud.Require<Button>(root, "order-merge");
            _split = TopTelemetryHud.Require<Button>(root, "order-split");
            _nuke = TopTelemetryHud.Require<Button>(root, "order-nuke");

            _rof[0].clicked += () => Issue(CommandType.SetRof, (int)RofStance.Conservation50);
            _rof[1].clicked += () => Issue(CommandType.SetRof, (int)RofStance.Standard100);
            _rof[2].clicked += () => Issue(CommandType.SetRof, (int)RofStance.Overdrive150);
            _move.clicked += IssueMove;
            _hold.clicked += IssueHold;
            _reverse.clicked += () => Issue(CommandType.Reverse, 0);
            _vent.clicked += () => Issue(CommandType.VentCoolant, 0);
            _raid.clicked += () => Issue(CommandType.RaidConvoys, _raidShown == 1 ? 0 : 1);
            _merge.clicked += IssueMerge;
            _split.clicked += IssueSplit;
            _nuke.clicked += IssueNuke;
        }

        public EntityId Fleet => _fleet;

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            var combat = sim.Balance.Combat;
            _thermalMin.text = UiFormat.Number(combat.AmbientMilli / 1000);
            _thermalMax.text = UiFormat.Number(GaugeTopMilli / 1000);
            _thermalLimits.text = "release " + UiFormat.Number(combat.OverheatReleaseMilli / 1000) + " · lock " +
                                  UiFormat.Number(combat.OverheatMilli / 1000);
            _release.style.left = Length.Percent(GaugePercent(combat.OverheatReleaseMilli));
            _lock.style.left = Length.Percent(GaugePercent(combat.OverheatMilli));
            Select(HudQueries.FirstFleet(sim, playerId));
        }

        public void Select(EntityId fleet)
        {
            _fleet = fleet;
            _moveTarget = default;
            _refusal = null;
            _tempCache = long.MinValue;
            _rofShown = -1;
            _raidShown = -1;
            for (int i = 0; i < _numbers.Length; i++)
                _numbers[i] = long.MinValue;
            bool has = _sim != null && _sim.Fleets.Has(fleet);
            _card.SetEnabled(has);
            _name.text = has ? UiFormat.FleetName(HudQueries.FleetOrdinal(_sim, fleet)) : "NO FLEET";
        }

        /// <summary>Destination for Move: a site (routed) or an adjacent lane. A null id disables Move.</summary>
        public void SetMoveTarget(EntityId siteOrLane) => _moveTarget = siteOrLane;

        public void Refresh()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            long tick = _sim.Clock.CurrentTick;
            ref var unit = ref _sim.Fleets.Get(_fleet);
            ref var motion = ref _sim.FleetMotions.Get(_fleet);
            ref var combat = ref _sim.FleetCombat.Get(_fleet);
            ref var readiness = ref _sim.FleetReadiness.Get(_fleet);
            ref var hold = ref _sim.Stockpiles.Get(_fleet);
            var balance = _sim.Balance.Combat;

            UiFormat.Set(_state, _refusal != null && tick < _refusalUntil
                ? _refusal
                : "TIER " + UiFormat.Number(unit.TechTier) + " · " + StateText(in motion, in combat, in readiness, tick));

            SetNumber(0, _line, unit.LineShips);
            SetNumber(1, _ew, unit.EwShips);
            SetNumber(2, _supply, unit.SupplyTenders);
            SetNumber(3, _cargo, unit.CargoConvoys);
            SetNumber(4, _ammo, hold.Get(ItemId.ItemAmmo));
            SetNumber(5, _battery, hold.Get(ItemId.ItemBattery));
            SetNumber(6, _coolant, hold.Get(ItemId.ItemCoolant));
            SetNumber(7, _fuel, hold.Get(ItemId.ItemFuel));
            SetNumber(8, _psi, (long)unit.LineShips * balance.LineAlpha * balance.RofPercent(combat.Rof) / 100);
            SetNumber(9, _omega, (long)unit.EwShips * balance.EwBeta);

            if (combat.TemperatureMilli != _tempCache)
            {
                _tempCache = combat.TemperatureMilli;
                _thermalValue.text = UiFormat.Number(combat.TemperatureMilli / 1000) + " °C";
                _fill.style.width = Length.Percent(GaugePercent(combat.TemperatureMilli));
                bool locked = combat.IsOverheated;
                _fill.EnableInClassList(FillLocked, locked);
                _fill.EnableInClassList(FillNominal, !locked && combat.TemperatureMilli < balance.OverheatReleaseMilli);
            }

            int rof = (int)combat.Rof;
            if (rof != _rofShown)
            {
                for (int i = 0; i < _rof.Length; i++)
                    _rof[i].EnableInClassList(OnClass, i == rof);
                _rofShown = rof;
            }

            int raid = combat.RaidsConvoys ? 1 : 0;
            if (raid != _raidShown)
            {
                _raid.text = raid == 1 ? "Raid convoys: ON" : "Raid convoys: off";
                _raidShown = raid;
            }

            bool onLane = !motion.Lane.IsNull;
            _move.SetEnabled(!_moveTarget.IsNull);
            _hold.text = motion.IsHolding ? "Resume" : "Hold";
            _hold.SetEnabled(onLane);
            _reverse.SetEnabled(onLane);
        }

        private static string StateText(in FleetMotion motion, in FleetCombatState combat, in FleetReadiness readiness, long tick)
        {
            if (readiness.IsMothballed) return "MOTHBALLED";
            if (tick < readiness.WarmupUntilTick) return "WARMING UP";
            if (readiness.IsUnsupplied) return "UNSUPPLIED";
            if (combat.IsOverheated) return "GUNS LOCKED";
            if (tick < combat.EngagedUntilTick) return "ENGAGED";
            if (motion.Lane.IsNull) return "DOCKED";
            if (motion.IsHolding) return "HOLDING";
            return motion.IsReversing ? "REVERSING" : "IN TRANSIT";
        }

        private void SetNumber(int index, Label label, long value)
        {
            if (_numbers[index] != value)
                UiFormat.Set(label, ref _numbers[index], value, UiFormat.Thousands(value));
        }

        private float GaugePercent(int milli)
        {
            int ambient = _sim.Balance.Combat.AmbientMilli;
            float t = (milli - ambient) / (float)(GaugeTopMilli - ambient);
            return 100f * (t < 0f ? 0f : t > 1f ? 1f : t);
        }

        private void IssueMove()
        {
            if (_moveTarget.IsNull)
                return;
            var command = NewCommand(CommandType.Move);
            command.ParamEntity = _moveTarget;
            Send(command);
        }

        private void IssueMerge()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            var other = HudQueries.FleetToMerge(_sim, _fleet);
            if (other.IsNull)
            {
                _refusal = "REFUSED · NO FLEET TO MERGE";
                _refusalUntil = _sim.Clock.CurrentTick + RefusalShowTicks;
                _state.text = _refusal;
                return;
            }
            var command = NewCommand(CommandType.MergeFleets);
            command.ParamEntity = other;
            Send(command);
        }

        private void IssueSplit()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            ref var unit = ref _sim.Fleets.Get(_fleet);
            if (unit.CargoConvoys > 0 || unit.LineShips / 2 + unit.EwShips / 2 + unit.SupplyTenders / 2 == 0)
            {
                _refusal = "REFUSED · TOO FEW SHIPS TO SPLIT";
                _refusalUntil = _sim.Clock.CurrentTick + RefusalShowTicks;
                _state.text = _refusal;
                return;
            }
            var command = NewCommand(CommandType.SplitFleet);
            command.ParamA = unit.LineShips / 2;
            command.ParamB = unit.EwShips / 2;
            command.ParamC = unit.SupplyTenders / 2;
            Send(command);
        }

        private void IssueNuke()
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            var lifecycle = _sim.Balance.Lifecycle;
            if (_sim.Fleets.Get(_fleet).TechTier < lifecycle.StrikeMinFleetTier)
                Refuse("REFUSED · NEEDS A TIER " + UiFormat.Number(lifecycle.StrikeMinFleetTier) + " FLEET");
            else if (!_sim.Stockpiles.Get(_fleet).Has(ItemId.ItemNuclearCore, lifecycle.StrikeNuclearCores))
                Refuse("REFUSED · NO NUCLEAR CORE IN THE HOLD");
            else
                Issue(CommandType.NuclearStrike, 0);
        }

        private void Refuse(string text)
        {
            _refusal = text;
            _refusalUntil = _sim.Clock.CurrentTick + RefusalShowTicks;
            _state.text = _refusal;
        }

        private void IssueHold()
        {
            if (_sim == null || !_sim.FleetMotions.Has(_fleet))
                return;
            Issue(CommandType.Hold, _sim.FleetMotions.Get(_fleet).IsHolding ? 0 : 1);
        }

        private void Issue(CommandType type, int paramA)
        {
            var command = NewCommand(type);
            command.ParamA = paramA;
            Send(command);
        }

        private DelayedPlayerCommand NewCommand(CommandType type) =>
            new DelayedPlayerCommand { PlayerId = _playerId, CommandType = type, Target = _fleet };

        private void Send(DelayedPlayerCommand command)
        {
            if (_sim == null || !_sim.Fleets.Has(_fleet))
                return;
            var result = _sim.Issuer.Issue(command, out _);
            if (result == IssueResult.Issued)
            {
                _refusal = null;
                return;
            }
            _refusal = "REFUSED · " + RefusalText(result);
            _refusalUntil = _sim.Clock.CurrentTick + RefusalShowTicks;
            _state.text = _refusal;
        }

        internal static string RefusalText(IssueResult result)
        {
            switch (result)
            {
                case IssueResult.NoHq: return "NO HQ";
                case IssueResult.Unreachable: return "NO SIGNAL PATH";
                case IssueResult.Blackout: return "RADIO BLACKOUT";
                case IssueResult.UnknownTarget: return "UNKNOWN TARGET";
                case IssueResult.Paralysed: return "COMMAND PARALYSED";
                default: return "ERROR";
            }
        }
    }
}
