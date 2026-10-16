using Unruled.Core.Ecs;

namespace Unruled.Core.Board
{
    /// <summary>The three route tiers of data sheet tab 04.</summary>
    public enum StarlaneTier : byte
    {
        StaHighway = 1,
        OuterRimBypass = 2,
        HazardStormShortcut = 3
    }

    /// <summary>ECS component: an undirected lane between two sites. Length in map units.</summary>
    public struct StarlaneEdge
    {
        public EntityId SiteA;
        public EntityId SiteB;
        public StarlaneTier Tier;
        public int Length;

        public EntityId OtherEnd(EntityId site) => site == SiteA ? SiteB : SiteA;
    }
}
