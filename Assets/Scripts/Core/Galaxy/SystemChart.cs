namespace Unruled.Core.Galaxy
{
    /// <summary>How a player came to know a system.</summary>
    public enum ChartSource : byte
    {
        Spawn = 0, // known from the start of the game (home, hub, the frontiers wired to them)
        Survey = 1, // charted by the player's own fleet
        Arrival = 2, // a fleet of the player arrived there
        Share = 3, // given by another player
        Purchase = 4, // bought on a chart shop
        Migration = 5 // a save from before charts: what the player held or stood in
    }

    /// <summary>
    /// ECS component on its own entity: one player knows one simulated system (cluster). The world is shared; only
    /// knowledge is per player, so a system charted by someone else stays hidden until the player learns it.
    /// Owned by <see cref="GalaxySystem"/>.
    /// </summary>
    public struct SystemChart
    {
        public int PlayerId;
        public int ClusterId;
        public long KnownTick;
        public ChartSource Source;
    }
}
