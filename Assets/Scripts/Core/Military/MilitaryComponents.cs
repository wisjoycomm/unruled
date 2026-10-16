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
}
