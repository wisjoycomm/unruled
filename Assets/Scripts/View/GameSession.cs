using Unruled.Core.Simulation;
using Unruled.View.UI;
using UnityEngine;

namespace Unruled.View
{
    /// <summary>
    /// Hosts a local game: builds the world from a seed with <see cref="NewGameSetup"/>, binds the HUD and the board,
    /// then advances the clock one tick per real second (scaled by <see cref="_ticksPerSecond"/>) and lets the AI
    /// rivals think after each tick. The simulation runs in-process until Server 2 hosts it.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        private const int MaxTicksPerFrame = 120;

        [SerializeField] private MainScreenView _screen;
        [SerializeField] private MapBoardRenderer _board;
        [SerializeField] private int _worldSeed = 1234;
        [SerializeField] private int _playerId = 1;
        [SerializeField, Range(0f, 60f)] private float _ticksPerSecond = 1f;

        private SimulationWorld _sim;
        private float _pending;

        public SimulationWorld Simulation => _sim;

        private void Start()
        {
            if (_screen == null)
                throw new MissingReferenceException("GameSession needs its MainScreenView.");
            if (_board == null)
                throw new MissingReferenceException("GameSession needs its MapBoardRenderer.");
            _sim = new SimulationWorld(DefaultGameBalance.Create(), unchecked((ulong)_worldSeed));
            var game = NewGameSetup.Create(_sim, _playerId);
            _screen.Bind(_sim, _playerId);
            _screen.SelectFleet(game.Fleet);
            _board.Bind(_sim, _playerId, _screen);
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
        }
    }
}
