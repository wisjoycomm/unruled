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
    /// <see cref="GameSession"/>, which owns the clock and the save file.
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
            _setPaused?.Invoke(true);
            string body = "PAUSED · " + UiFormat.Clock(_sim.Clock.CurrentTick) +
                          "\n\nThe simulation is stopped while this menu is open. The game also saves itself every hour of " +
                          "game time and when you quit." + (status != null ? "\n\n" + status : "");
            _popup.Menu("GAME MENU", body, _art.Pause, ModalPopup.Tone.Normal, _art.MenuScreen,
                "Save", () => OpenWith(_save?.Invoke()),
                "Load saved", () => ConfirmLoad(),
                "Resume", Resume);
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
