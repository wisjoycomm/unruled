using System;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The game menu (top-bar button): pauses the clock while open and offers resume, save and load of the saved game,
    /// and new game. Also the end screen: once THE SYSTEM's Citadel falls and The Directorate forms
    /// (<see cref="Unruled.Core.SystemFactions.VictoryEvaluator"/>), a seated player sees the victory screen and anyone
    /// else the defeat screen, once; play may continue after it. Session actions are callbacks into
    /// <see cref="GameSession"/>, which owns the clock and the save file. Join server asks for the server's address and
    /// a login token (issued from the server's admin page); online, the menu doesn't pause (the server's clock runs on)
    /// and offers Play offline instead of save and load.
    /// </summary>
    public sealed class GameMenu
    {
        private readonly ModalPopup _popup;
        private readonly UiArt _art;

        private SimulationWorld _sim;
        private int _playerId;
        private bool _endShown;
        private Action<bool> _setPaused;
        private Func<string> _save;
        private Func<string> _load;
        private Action _newGame;
        private Func<bool> _isOnline;
        private Func<string> _linkStatus;
        private string _address;
        private Func<string, string, string> _join;
        private Action _leave;

        public GameMenu(VisualElement root, ModalPopup popup, UiArt art)
        {
            _popup = popup;
            _art = art;
            TopTelemetryHud.Require<Button>(root, "open-menu").clicked += Open;
        }

        /// <summary>Session callbacks: pause, save and load (each returns a line for the menu) and new game.</summary>
        public void BindSession(Action<bool> setPaused, Func<string> save, Func<string> load, Action newGame)
        {
            _setPaused = setPaused;
            _save = save;
            _load = load;
            _newGame = newGame;
        }

        /// <summary>Online callbacks: whether the session is online, its link's state, join (address, token) and leave.</summary>
        public void BindOnline(Func<bool> isOnline, Func<string> linkStatus, string lastAddress,
            Func<string, string, string> join, Action leave)
        {
            _isOnline = isOnline;
            _linkStatus = linkStatus;
            _address = lastAddress;
            _join = join;
            _leave = leave;
        }

        /// <summary>A line from the session (joined, refused, connection lost), shown in the menu.</summary>
        public void ShowStatus(string status) => OpenWith(status);

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            _endShown = sim.Victory.IsDecided(out _);
        }

        /// <summary>Once per sim tick: shows the end screen the tick The Directorate forms.</summary>
        public void Refresh()
        {
            if (_sim == null || _endShown || !_sim.Victory.IsDecided(out long tick))
                return;
            _endShown = true;
            ShowEnd(_sim.Victory.HasSeat(_playerId), tick);
        }

        public void Open() => OpenWith(null);

        private void OpenWith(string status)
        {
            if (_sim == null)
                return;
            if (_isOnline != null && _isOnline())
            {
                _popup.Menu("GAME MENU · ONLINE", (_linkStatus?.Invoke() ?? "") + " · " + UiFormat.Clock(_sim.Clock.CurrentTick) +
                    "\n\nThe server keeps this world and its clock runs on while this menu is open. Leaving logs you out: " +
                    "your shield rises after a while and your standing orders keep running." + (status != null ? "\n\n" + status : ""),
                    _art.Pause, ModalPopup.Tone.Normal, _art.MenuScreen,
                    "Play offline", () => { _leave?.Invoke(); Resume(); },
                    null, null,
                    "Resume", Resume);
                return;
            }
            _setPaused?.Invoke(true);
            string body = "PAUSED · " + UiFormat.Clock(_sim.Clock.CurrentTick) +
                          "\n\nThe simulation is stopped while this menu is open. The game also saves itself every hour of " +
                          "game time and when you quit." + (status != null ? "\n\n" + status : "");
            _popup.Menu("GAME MENU", body, _art.Pause, ModalPopup.Tone.Normal, _art.MenuScreen,
                "Save", () => OpenWith(_save?.Invoke()),
                "Load or join", LoadOrJoin,
                "Resume", Resume);
        }

        private void LoadOrJoin()
        {
            _popup.Menu("LOAD OR JOIN", "Load the saved game or start a new one on this device, or join a server's world.",
                _art.Pause, ModalPopup.Tone.Normal, _art.MenuScreen,
                "Back", () => OpenWith(null),
                "Load saved", ConfirmLoad,
                "Join server", _join != null ? AskAddress : (Action)(() => OpenWith("NO SERVER LINK IN THIS BUILD")));
        }

        // Join server, step 1 of 2: the address (host:port).
        private void AskAddress()
        {
            _popup.Prompt("JOIN SERVER", "The server's address, as host:port.", _art.Pause, _address, 60,
                text => text.Length >= 3 && text.IndexOf(' ') < 0, "Next", address => { _address = address; AskToken(); });
        }

        // Step 2: the login token the server's admin page issued for this player.
        private void AskToken()
        {
            _popup.Prompt("JOIN SERVER · " + _address.ToUpperInvariant(),
                "Your login token from the server's admin page. The offline game is saved first and stays on this device.",
                _art.Pause, string.Empty, 64, text => text.Length >= 16 && text.IndexOf(' ') < 0, "Join",
                token => OpenWith(_join(_address, token)));
        }

        private void ConfirmLoad()
        {
            _popup.Menu("LOAD SAVED GAME", "Unsaved progress since the last save is lost. Or start a new game from the seed.",
                _art.Warning, ModalPopup.Tone.Danger, _art.MenuScreen,
                "Back", () => OpenWith(null),
                "New game", () => { _newGame?.Invoke(); Resume(); },
                "Load", () => { string line = _load?.Invoke(); OpenWith(line); });
        }

        private void Resume()
        {
            _setPaused?.Invoke(false);
        }

        private void ShowEnd(bool seated, long tick)
        {
            _setPaused?.Invoke(true);
            if (seated)
                _popup.Menu("THE DIRECTORATE · SEATED",
                    "THE SYSTEM's Citadel fell at " + UiFormat.Clock(tick) + ". Your fleets bombarded it in the final window: " +
                    "you take a seat on The Directorate.\n\nVictory. The galaxy keeps turning; play on if you like.",
                    _art.Citadel, ModalPopup.Tone.Victory, _art.VictoryScreen,
                    null, null, null, null, "Continue", Resume);
            else
                _popup.Menu("THE DIRECTORATE · FORMED WITHOUT YOU",
                    "THE SYSTEM's Citadel fell at " + UiFormat.Clock(tick) + ", and the seats went to the empires that " +
                    "bombarded it. You were not among them.\n\nDefeat. Play on, or start a new game from the menu.",
                    _art.Warning, ModalPopup.Tone.Danger, _art.DefeatScreen,
                    null, null, "Menu", () => OpenWith(null), "Continue", Resume);
        }
    }
}
