namespace Unruled.Core.Simulation
{
    public enum CommandType : byte
    {
        Move = 1,
        Hold = 2,
        Reverse = 3,
        ScanBelt = 4,
        SetRof = 5,
        RelocateBase = 6
    }

    /// <summary>A player or AI command that takes effect after signal latency. Shared by players and the Server 2 AI.</summary>
    public struct DelayedPlayerCommand
    {
        public long IssuedAtTick;
        public long ExecuteAtTick;
        public int PlayerId;
        public CommandType CommandType;
        public int TargetEntityId;
        public int ParamA;
        public int ParamB;
    }
}
