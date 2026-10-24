using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    /// <summary>
    /// The players' star charts as orders and movement see them: whether a player knows the system a site is in (a
    /// system it doesn't know can't be targeted or routed to), and the arrivals that teach it one.
    /// </summary>
    public interface IStarCharts
    {
        /// <summary>Whether the player knows the site's system; THE SYSTEM (id 0 or less) and unknown sites count as known.</summary>
        bool KnowsSite(int playerId, EntityId site);

        /// <summary>A fleet of the player arrived at a site: the player learns that system.</summary>
        void FleetArrived(int playerId, EntityId site, long tick);
    }
}
