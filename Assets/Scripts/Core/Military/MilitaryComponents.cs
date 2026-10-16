using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    /// <summary>ECS component on a player entity: the command centre that signal latency is measured from.</summary>
    public struct PlayerHq
    {
        public int PlayerId;
        public EntityId CoreBase;
        public bool IsCapitalConvoyInTransit; // relocation phase 2: every order's latency is multiplied
    }

    /// <summary>ECS component on a site: a relay station that shortens command hops for its owner.</summary>
    public struct RelayStation
    {
        public int OwnerPlayerId;
    }

    /// <summary>
    /// ECS component: where a fleet is and what the radar knows about it. PLACEHOLDER until the Sprint 3 fleet model;
    /// exactly one of <see cref="Site"/> and <see cref="Lane"/> is set.
    /// </summary>
    public struct FleetPresence
    {
        public int OwnerPlayerId;
        public EntityId Site;
        public EntityId Lane;
        public int EwShips;
        public long RevealedUntilTick;
        public long RwrWarningTick; // tick a hostile focused scan's RWR alarm fired; 0 = none
        public bool IsReversing;
    }
}
