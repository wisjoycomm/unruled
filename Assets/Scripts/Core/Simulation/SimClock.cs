using System;
using System.Collections.Generic;

namespace Unruled.Core.Simulation
{
    /// <summary>
    /// Deterministic tick clock and delayed command queue. Each tick: advance the counter, dispatch commands due
    /// on that tick (ordered by ExecuteAtTick, then enqueue order), then run systems in registration order.
    /// </summary>
    public sealed class SimClock
    {
        private readonly ICommandHandler _handler;
        private readonly List<ISimSystem> _systems = new List<ISimSystem>();
        private readonly List<DelayedPlayerCommand> _queue = new List<DelayedPlayerCommand>();

        public long CurrentTick { get; private set; }
        public int PendingCommandCount => _queue.Count;

        /// <summary>Read access to the queue, in execution order (orders still in signal latency).</summary>
        public DelayedPlayerCommand PendingAt(int index) => _queue[index];

        public SimClock(ICommandHandler handler, long startTick = 0)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            if (startTick < 0) throw new ArgumentOutOfRangeException(nameof(startTick));
            CurrentTick = startTick;
        }

        public void Register(ISimSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            _systems.Add(system);
        }

        public void Enqueue(DelayedPlayerCommand command)
        {
            if (command.ExecuteAtTick <= CurrentTick)
                throw new ArgumentException(
                    $"ExecuteAtTick {command.ExecuteAtTick} must be after CurrentTick {CurrentTick}.", nameof(command));
            if (command.IssuedAtTick > command.ExecuteAtTick)
                throw new ArgumentException("IssuedAtTick must not be after ExecuteAtTick.", nameof(command));

            int index = _queue.Count;
            while (index > 0 && _queue[index - 1].ExecuteAtTick > command.ExecuteAtTick)
                index--;
            _queue.Insert(index, command);
        }

        public void Advance(long ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            for (long i = 0; i < ticks; i++)
                Step();
        }

        private void Step()
        {
            CurrentTick++;

            int due = 0;
            while (due < _queue.Count && _queue[due].ExecuteAtTick <= CurrentTick)
                due++;
            for (int i = 0; i < due; i++)
            {
                var command = _queue[i];
                _handler.Handle(in command, CurrentTick);
            }
            if (due > 0)
                _queue.RemoveRange(0, due);

            for (int i = 0; i < _systems.Count; i++)
                _systems[i].OnTick(CurrentTick);
        }
    }
}
