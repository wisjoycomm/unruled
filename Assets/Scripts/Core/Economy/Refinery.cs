using Unruled.Core.Ecs;

namespace Unruled.Core.Economy
{
    /// <summary>Refinery state machine (GDD §2.2 A). NotStarted until the first 10-tick cycle runs.</summary>
    public enum RefineryState : byte
    {
        NotStarted = 0,
        IdleStarved = 1,
        ActiveProduction = 2,
        GridBrownout = 3
    }

    /// <summary>ECS component: one refinery producing <see cref="Output"/> from its site's stockpile.</summary>
    public struct Refinery
    {
        public EntityId Site;
        public ItemId Output;
        public RefineryState State;
        public long AccruedUpkeep; // upkeep units owed; settled by the economy later
    }
}
