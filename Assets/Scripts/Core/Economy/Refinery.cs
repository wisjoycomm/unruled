using Unruled.Core.Ecs;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Refinery state machine (GDD §2.2 A). NotStarted until the first 10-tick cycle runs. IdleFull (not in the GDD):
    /// the site holds the refinery's stock cap of its output.
    /// </summary>
    public enum RefineryState : byte
    {
        NotStarted = 0,
        IdleStarved = 1,
        ActiveProduction = 2,
        GridBrownout = 3,
        IdleFull = 4
    }

    /// <summary>ECS component: one refinery producing <see cref="Output"/> from its site's stockpile.</summary>
    public struct Refinery
    {
        public EntityId Site;
        public ItemId Output;
        public RefineryState State;
        public long AccruedUpkeepCents; // upkeep cents owed that the site owner could not pay yet
        public int StockCap; // idles while the site holds this much of the output; 0 = no cap
    }
}
