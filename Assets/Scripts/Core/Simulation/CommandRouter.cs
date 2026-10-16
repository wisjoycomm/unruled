using System;

namespace Unruled.Core.Simulation
{
    /// <summary>Sends each due command to the one handler registered for its <see cref="CommandType"/>.</summary>
    public sealed class CommandRouter : ICommandHandler
    {
        private readonly ICommandHandler[] _handlers = new ICommandHandler[byte.MaxValue + 1];

        public void Register(CommandType type, ICommandHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_handlers[(byte)type] != null)
                throw new InvalidOperationException($"A handler for {type} is already registered.");
            _handlers[(byte)type] = handler;
        }

        public bool IsRegistered(CommandType type) => _handlers[(byte)type] != null;

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            var handler = _handlers[(byte)command.CommandType];
            if (handler == null)
                throw new InvalidOperationException($"No handler registered for {command.CommandType}.");
            handler.Handle(in command, tick);
        }
    }
}
