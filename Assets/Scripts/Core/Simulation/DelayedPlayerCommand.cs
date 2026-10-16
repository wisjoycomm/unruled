using Unruled.Core.Ecs;

namespace Unruled.Core.Simulation
{
    public enum CommandType : byte
    {
        Move = 1,
        Hold = 2,
        Reverse = 3,
        ScanBelt = 4,
        SetRof = 5,
        RelocateBase = 6,
        Terraform = 7,
        CancelScan = 8,
        EcmJam = 9
    }

    /// <summary>A player or AI command that takes effect after signal latency. Shared by players and the Server 2 AI.</summary>
    public struct DelayedPlayerCommand
    {
        public long IssuedAtTick;
        public long ExecuteAtTick;
        public int PlayerId;
        public CommandType CommandType;
        public int TargetEntityId;
        public int TargetGeneration;
        public int ParamA;
        public int ParamB;

        /// <summary>Target as an entity id; the generation makes commands aimed at a destroyed entity miss.</summary>
        public EntityId Target
        {
            get => new EntityId(TargetEntityId, TargetGeneration);
            set
            {
                TargetEntityId = value.Index;
                TargetGeneration = value.Generation;
            }
        }

        /// <summary>A second entity carried in <see cref="ParamA"/> (index) and <see cref="ParamB"/> (generation), e.g. Move's lane.</summary>
        public EntityId ParamEntity
        {
            get => new EntityId(ParamA, ParamB);
            set
            {
                ParamA = value.Index;
                ParamB = value.Generation;
            }
        }
    }
}
