using Unruled.Core.Ecs;
using Unruled.Core.Simulation;
using Unruled.View.UI.Theme;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The strip under the board: the player's orders still in signal latency (blue, ETA in ticks), focused radar
    /// scans running (teal) and fleets warming up (grey). A fixed pool of dashed rows; the first ones in order show.
    /// </summary>
    public sealed class InFlightStrip
    {
        private const int Slots = 6;
        private const string RunningClass = "u-inflight--running";
        private const string WaitingClass = "u-inflight--waiting";

        private static readonly string[] CommandNames =
        {
            "", "Move", "Hold", "Reverse", "ScanBelt", "Set ROF", "Relocate base", "Terraform", "Cancel scan", "ECM jam",
            "Mothball", "Activate", "Set focus", "Vent coolant", "Retract drills", "Buy order", "Sell order",
            "Cancel order", "Audit opt-in", "Stress test",
            "Build ships", "Retrofit", "Claim site", "Build rig", "Build refinery", "Open jump gate", "Transfer cargo",
            "Convoy route", "Refinery cap", "Pause drills", "Borrow", "Repay", "Raid convoys", "Sell to THE SYSTEM", "Buy from THE SYSTEM",
            "Merge fleets", "Standing orders", "Split fleet", "Nuclear strike", "Build relay", "Survey", "Rename system"
        };

        internal static string CommandName(CommandType type) =>
            (int)type < CommandNames.Length ? CommandNames[(int)type] : "Order";

        private readonly DashedFrame[] _rows = new DashedFrame[Slots];
        private readonly Label[] _text = new Label[Slots];
        private readonly Label[] _eta = new Label[Slots];
        private readonly byte[] _kind = new byte[Slots]; // 0 hidden, 1 order, 2 running, 3 waiting

        private SimulationWorld _sim;
        private int _playerId;
        private int _used;
        private readonly VisualElement _idle;
        private bool _idleShown = true;

        public InFlightStrip(VisualElement root)
        {
            var list = TopTelemetryHud.Require<VisualElement>(root, "inflight-list");
            list.Clear();
            // Standby trace while no order, scan or warm-up is in flight.
            _idle = new VisualElement { pickingMode = PickingMode.Ignore };
            _idle.AddToClassList("inflight-idle");
            list.Add(_idle);
            for (int i = 0; i < Slots; i++)
            {
                var row = new DashedFrame();
                row.AddToClassList("u-inflight");
                var text = new Label();
                text.AddToClassList("u-text");
                var eta = new Label();
                eta.AddToClassList("u-num");
                eta.AddToClassList("u-inflight__eta");
                row.Add(text);
                row.Add(eta);
                row.style.display = DisplayStyle.None;
                list.Add(row);
                _rows[i] = row;
                _text[i] = text;
                _eta[i] = eta;
            }
        }

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
        }

        public void Refresh()
        {
            if (_sim == null)
                return;
            long tick = _sim.Clock.CurrentTick;
            _used = 0;

            for (int i = 0; i < _sim.Clock.PendingCommandCount && _used < Slots; i++)
            {
                var command = _sim.Clock.PendingAt(i);
                if (command.PlayerId != _playerId)
                    continue;
                Show(1, Describe(in command), UiFormat.Number(command.ExecuteAtTick - tick) + "t");
            }
            for (int i = 0; i < _sim.RadarScans.Count && _used < Slots; i++)
            {
                ref var scan = ref _sim.RadarScans.At(i);
                if (scan.OwnerPlayerId != _playerId)
                    continue;
                long left = scan.StartTick + scan.IntegrationTicks - tick;
                Show(2, "ScanBelt · " + LaneName(scan.Lane), left > 0 ? UiFormat.Number(left) + "t" : "running");
            }
            for (int i = 0; i < _sim.FleetReadiness.Count && _used < Slots; i++)
            {
                ref var readiness = ref _sim.FleetReadiness.At(i);
                var fleet = _sim.FleetReadiness.EntityAt(i);
                if (tick >= readiness.WarmupUntilTick || _sim.Fleets.Get(fleet).OwnerPlayerId != _playerId)
                    continue;
                Show(3, "Activate · " + FleetText(fleet), "warm-up " + UiFormat.Number(readiness.WarmupUntilTick - tick) + "t");
            }

            for (int i = _used; i < Slots; i++)
                SetKind(i, 0);
            bool idle = _used == 0;
            if (idle != _idleShown)
            {
                _idleShown = idle;
                _idle.style.display = idle ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void Show(byte kind, string text, string eta)
        {
            int i = _used++;
            SetKind(i, kind);
            UiFormat.Set(_text[i], text);
            UiFormat.Set(_eta[i], eta);
        }

        private void SetKind(int i, byte kind)
        {
            if (_kind[i] == kind)
                return;
            _kind[i] = kind;
            _rows[i].style.display = kind == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _rows[i].EnableInClassList(RunningClass, kind == 2);
            _rows[i].EnableInClassList(WaitingClass, kind == 3);
        }

        private string Describe(in DelayedPlayerCommand command)
        {
            string name = CommandName(command.CommandType);
            var target = command.Target;
            if (_sim.Fleets.Has(target))
            {
                string text = name + " · " + FleetText(target);
                if (command.CommandType == CommandType.Move && _sim.Sites.Has(command.ParamEntity))
                    text += " → " + UiFormat.SiteName(_sim, _sim.Sites.Get(command.ParamEntity));
                return text;
            }
            if (_sim.Sites.Has(target))
                return name + " · " + UiFormat.SiteName(_sim, _sim.Sites.Get(target));
            if (_sim.Starlanes.Has(target))
                return name + " · " + LaneName(target);
            return name;
        }

        private string FleetText(EntityId fleet) => UiFormat.FleetLabel(HudQueries.FleetOrdinal(_sim, fleet));

        private string LaneName(EntityId lane)
        {
            if (!_sim.Starlanes.Has(lane))
                return "lane";
            ref var edge = ref _sim.Starlanes.Get(lane);
            if (!_sim.Sites.Has(edge.SiteA) || !_sim.Sites.Has(edge.SiteB))
                return "lane";
            return UiFormat.SiteName(_sim, _sim.Sites.Get(edge.SiteA)) + "–" + UiFormat.SiteName(_sim, _sim.Sites.Get(edge.SiteB));
        }
    }
}
