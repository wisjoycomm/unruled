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

    public enum RelocationPhase : byte
    {
        None = 0,
        Teardown = 1,
        Convoy = 2,
        Bootstrap = 3
    }

    /// <summary>ECS component on a player entity: the three-phase core-base relocation and its after-effects.</summary>
    public struct CapitalRelocation
    {
        public RelocationPhase Phase;
        public long PhaseEndTick;
        public EntityId OldBase;
        public EntityId Destination;
        public EntityId Convoy;
        public long CooldownUntilTick;
        public long ParalysedUntilTick; // orders refused after the capital convoy was destroyed
    }
}
