using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Galaxy;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// THE SYSTEM's public bulletins (<see cref="SystemBulletin"/>) and NOAA storm fronts in the player's systems,
    /// shown as toasts over the board for a few real seconds each, and kept as a log the ALERTS button opens in
    /// <see cref="ModalPopup"/>. Reads the bulletin and storm stores once per sim tick; writes nothing.
    /// </summary>
    public sealed class BulletinFeed
    {
        private const int MaxToasts = 4;
        private const float ToastSeconds = 8f;
        private const int LogCapacity = 40;
        private const string AlertsTitle = "SYSTEM BULLETINS";

        private readonly VisualElement _toasts;
        private readonly Button _openAlerts;
        private readonly ModalPopup _popup;
        private readonly UiArt _art;
        private readonly List<VisualElement> _shown = new List<VisualElement>(MaxToasts);
        private readonly List<float> _shownUntil = new List<float>(MaxToasts);
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly List<string> _log = new List<string>(LogCapacity);

        private SimulationWorld _sim;
        private int _playerId;
        private int _unread;
        private bool _logOpen;

        public BulletinFeed(VisualElement root, ModalPopup popup, UiArt art)
        {
            _toasts = TopTelemetryHud.Require<VisualElement>(root, "toasts");
            _openAlerts = TopTelemetryHud.Require<Button>(root, "open-alerts");
            _popup = popup;
            _art = art;
            _openAlerts.clicked += OpenLog;
            _toasts.Clear();
        }

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            _seen.Clear();
            _log.Clear();
            _unread = 0;
            // Bulletins already up when the game loads are history, not news: log them without a toast.
            Scan(false);
            UpdateButton();
        }

        /// <summary>Once per sim tick.</summary>
        public void Refresh()
        {
            if (_sim == null)
                return;
            Scan(true);
            if (_logOpen && _popup.IsOpen)
                _popup.SetBody(LogText());
            else
                _logOpen = false;
        }

        /// <summary>Every frame: retires toasts whose time is up.</summary>
        public void Tick(float now)
        {
            for (int i = _shown.Count - 1; i >= 0; i--)
            {
                if (now < _shownUntil[i])
                    continue;
                _shown[i].RemoveFromHierarchy();
                _shown.RemoveAt(i);
                _shownUntil.RemoveAt(i);
            }
        }

        private void Scan(bool toast)
        {
            long tick = _sim.Clock.CurrentTick;
            for (int i = 0; i < _sim.Bulletins.Count; i++)
            {
                var id = _sim.Bulletins.EntityAt(i);
                if (!_seen.Add(id))
                    continue;
                ref var bulletin = ref _sim.Bulletins.At(i);
                Post(bulletin.Agency, BulletinText(in bulletin), toast, bulletin.PlayerId == _playerId, tick);
            }
            int home = HomeCluster();
            for (int i = 0; i < _sim.StormFronts.Count; i++)
            {
                var id = _sim.StormFronts.EntityAt(i);
                if (!_seen.Add(id))
                    continue;
                ref var storm = ref _sim.StormFronts.At(i);
                if (storm.ClusterId != home || storm.Level < 3)
                    continue;
                Post(SystemAgency.Noaa, StormText(in storm, tick), toast, storm.Level >= 4, tick);
            }
            if (_seen.Count > 4096)
                _seen.Clear();
        }

        private void Post(SystemAgency agency, string text, bool toast, bool danger, long tick)
        {
            if (_log.Count == LogCapacity)
                _log.RemoveAt(0);
            _log.Add(UiFormat.Clock(tick) + " · " + AgencyCode(agency) + " · " + text);
            if (!toast)
                return;
            _unread++;
            UpdateButton();
            ShowToast(agency, text, danger);
        }

        private void ShowToast(SystemAgency agency, string text, bool danger)
        {
            if (_shown.Count == MaxToasts)
            {
                _shown[0].RemoveFromHierarchy();
                _shown.RemoveAt(0);
                _shownUntil.RemoveAt(0);
            }
            var toast = new VisualElement { pickingMode = PickingMode.Ignore };
            toast.AddToClassList("toast");
            toast.AddToClassList(danger ? "toast--danger" : "toast--" + AgencyCode(agency).ToLowerInvariant());
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("toast__icon");
            var texture = _art.Agency(agency);
            if (texture != null)
                icon.style.backgroundImage = new StyleBackground(texture);
            var label = new Label(AgencyCode(agency) + " · " + text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("u-text");
            label.AddToClassList("toast__text");
            toast.Add(icon);
            toast.Add(label);
            _toasts.Add(toast);
            _shown.Add(toast);
            _shownUntil.Add(Time.unscaledTime + ToastSeconds);
        }

        private void OpenLog()
        {
            _unread = 0;
            UpdateButton();
            _logOpen = true;
            _popup.Inform(AlertsTitle, LogText(), _art.Warning);
        }

        private string LogText()
        {
            if (_log.Count == 0)
                return "No bulletins yet. THE SYSTEM reports nuclear strikes (IAEA), prototype blasts and storms (NOAA) and Call-to-Arms (WTO) here.";
            var text = new System.Text.StringBuilder(_log.Count * 64);
            for (int i = _log.Count - 1; i >= 0; i--)
                text.Append(_log[i]).Append('\n');
            return text.ToString();
        }

        private void UpdateButton() =>
            _openAlerts.text = _unread > 0 ? UiFormat.Number(_unread) + " " : "";

        private string BulletinText(in SystemBulletin bulletin)
        {
            string where = _sim.Sites.Has(bulletin.Site) ? UiFormat.SiteName(in _sim.Sites.Get(bulletin.Site)) : "UNKNOWN SITE";
            string who = bulletin.PlayerId == _playerId ? "YOU" : bulletin.PlayerId > 0 ? "P" + UiFormat.Number(bulletin.PlayerId) : "—";
            switch (bulletin.Kind)
            {
                case BulletinKind.NuclearStrike:
                    return "NUCLEAR STRIKE AT " + where + " · S4 RADIATION UNTIL " + UiFormat.Clock(bulletin.ExpiresTick);
                case BulletinKind.CallToArms:
                    return "CALL TO ARMS · DEFENCE FLEET AT " + where + " AGAINST " + who;
                case BulletinKind.PrototypeBlast:
                    return "PROTOTYPE BLAST AT " + where + " · STRESS TEST FAILED (" + who + ")";
                case BulletinKind.SystemRenamed:
                    return "STA REGISTRY · " + RenamedText(bulletin.Site) + " (" + who + ")";
                default:
                    return "BULLETIN AT " + where;
            }
        }

        private readonly char[] _nameChars = new char[SystemName.Capacity];

        private string RenamedText(EntityId system)
        {
            if (!_sim.StarSystems.Has(system))
                return "A SYSTEM WAS RENAMED";
            string star = UiFormat.StarName(_sim.StarSystems.Get(system).ClusterId);
            if (!_sim.SystemNames.Has(system))
                return star + " RENAMED";
            int length = _sim.SystemNames.Get(system).CopyTo(_nameChars);
            return star + " IS NOW " + new string(_nameChars, 0, length);
        }

        private static string StormText(in StormFront storm, long tick)
        {
            string scale = storm.Scale == StormScale.Radio ? "R" : storm.Scale == StormScale.Solar ? "S" : "G";
            return "STORM " + scale + UiFormat.Number(storm.Level) + " IN " + UiFormat.StarName(storm.ClusterId) +
                   " · " + UiFormat.Duration(storm.EndTick - tick) + " LEFT";
        }

        private int HomeCluster()
        {
            if (!HudQueries.TryFindHq(_sim, _playerId, out var hq))
                return -1;
            var core = _sim.Players.Get(hq).CoreBase;
            return _sim.Sites.Has(core) ? _sim.Sites.Get(core).ClusterId : -1;
        }

        private static string AgencyCode(SystemAgency agency)
        {
            switch (agency)
            {
                case SystemAgency.Sta: return "STA";
                case SystemAgency.Wto: return "WTO";
                case SystemAgency.Who: return "WHO";
                case SystemAgency.Iaea: return "IAEA";
                default: return "NOAA";
            }
        }
    }
}
