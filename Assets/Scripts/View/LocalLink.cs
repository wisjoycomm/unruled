using Unruled.Core.Simulation;

namespace Unruled.View
{
    /// <summary>An in-process world: the clock advances by real time (scaled) and the AI rivals think after each tick.</summary>
    public sealed class LocalLink : IGameLink
    {
        private const int MaxTicksPerFrame = 120;

        private readonly float _ticksPerSecond;
        private float _pending;

        public LocalLink(SimulationWorld world, int playerId, float ticksPerSecond)
        {
            World = world;
            PlayerId = playerId;
            _ticksPerSecond = ticksPerSecond;
        }

        public SimulationWorld World { get; }
        public int PlayerId { get; }
        public bool IsOnline => false;
        public string Status => "OFFLINE · THIS DEVICE";

        public bool Paused { get; set; }

        public bool Update(float deltaSeconds)
        {
            if (Paused)
            {
                _pending = 0f;
                return false;
            }
            _pending += deltaSeconds * _ticksPerSecond;
            int steps = 0;
            while (_pending >= 1f && steps < MaxTicksPerFrame)
            {
                _pending -= 1f;
                steps++;
                World.Clock.Advance(1);
                World.Ai.Think(World.Clock.CurrentTick);
            }
            if (_pending >= 1f)
                _pending = 0f; // the frame budget ran out: drop the backlog instead of spiralling
            return steps > 0;
        }

        public void Close()
        {
        }
    }
}
