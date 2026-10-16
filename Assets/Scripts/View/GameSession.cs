using System;
using System.IO;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;
using Unruled.View.UI;
using UnityEngine;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View
{
    /// <summary>
    /// Hosts a local game: continues the saved game if there is one, else builds the world from a seed with
    /// <see cref="NewGameSetup"/>; binds the HUD and the board, then advances the clock one tick per real second
    /// (scaled by <see cref="_ticksPerSecond"/>) and lets the AI rivals think after each tick. Saves every
    /// <see cref="_autosaveTicks"/> and when the app pauses or quits. Runs in-process until Server 2 hosts it.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        private const int MaxTicksPerFrame = 120;
        private const string SaveFileName = "unruled.save";

        [SerializeField] private MainScreenView _screen;
        [SerializeField] private MapBoardRenderer _board;
        [SerializeField] private int _worldSeed = 1234;
        [SerializeField] private int _playerId = 1;
        [SerializeField, Range(0f, 60f)] private float _ticksPerSecond = 1f;
        [SerializeField] private bool _continueSavedGame = true;
        [SerializeField, Min(60)] private int _autosaveTicks = 3_600;

        private SimulationWorld _sim;
        private float _pending;
        private long _savedTick;

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public SimulationWorld Simulation => _sim;

        private void Start()
        {
            if (_screen == null)
                throw new MissingReferenceException("GameSession needs its MainScreenView.");
            if (_board == null)
                throw new MissingReferenceException("GameSession needs its MapBoardRenderer.");
            var balance = DefaultGameBalance.Create();
            EntityId fleet;
            if (_continueSavedGame && File.Exists(SavePath) && TryLoad(balance, out _sim))
                fleet = FirstFleet(_sim, _playerId);
            else
            {
                _sim = new SimulationWorld(balance, unchecked((ulong)_worldSeed));
                fleet = NewGameSetup.Create(_sim, _playerId).Fleet;
            }
            _savedTick = _sim.Clock.CurrentTick;
            _screen.Bind(_sim, _playerId);
            _screen.SelectFleet(fleet);
            _board.Bind(_sim, _playerId, _screen);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
        }

        private void OnApplicationQuit() => Save();

        // A save this build cannot read is moved aside (not deleted) and reported, and a new game starts.
        private static bool TryLoad(GameBalanceConfig balance, out SimulationWorld sim)
        {
            try
            {
                sim = SaveGame.Load(balance, File.ReadAllBytes(SavePath));
                return true;
            }
            catch (FormatException e)
            {
                string aside = SavePath + ".unreadable";
                File.Copy(SavePath, aside, overwrite: true);
                Debug.LogError($"Saved game {SavePath} could not be read ({e.Message}); kept as {aside}, starting a new game.");
                sim = null;
                return false;
            }
        }

        // Writes beside the save, then swaps it in, so a crash while writing keeps the previous save intact.
        private void Save()
        {
            if (_sim == null)
                return;
            string temp = SavePath + ".tmp";
            File.WriteAllBytes(temp, SaveGame.Write(_sim));
            if (File.Exists(SavePath))
                File.Delete(SavePath);
            File.Move(temp, SavePath);
            _savedTick = _sim.Clock.CurrentTick;
        }

        private static EntityId FirstFleet(SimulationWorld sim, int playerId)
        {
            for (int i = 0; i < sim.Fleets.Count; i++)
                if (sim.Fleets.At(i).OwnerPlayerId == playerId)
                    return sim.Fleets.EntityAt(i);
            return default;
        }

        private void Update()
        {
            if (_sim == null)
                return;
            _pending += Time.deltaTime * _ticksPerSecond;
            int steps = 0;
            while (_pending >= 1f && steps < MaxTicksPerFrame)
            {
                _pending -= 1f;
                steps++;
                _sim.Clock.Advance(1);
                _sim.Ai.Think(_sim.Clock.CurrentTick);
            }
            if (_pending >= 1f)
                _pending = 0f; // the frame budget ran out: drop the backlog instead of spiralling
            if (_sim.Clock.CurrentTick - _savedTick >= _autosaveTicks)
                Save();
        }
    }
}
