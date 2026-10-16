namespace Unruled.Core.Simulation
{
    /// <summary>A domain engine advanced by <see cref="SimClock"/> once per tick.</summary>
    public interface ISimSystem
    {
        void OnTick(long tick);
    }

    /// <summary>Applies due commands to the owning engines. The only door for player-driven state changes.</summary>
    public interface ICommandHandler
    {
        void Handle(in DelayedPlayerCommand command, long tick);
    }
}
