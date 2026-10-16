namespace Unruled.Core.SystemFactions
{
    /// <summary>ECS component: WHO life-support watch for a site. Tick 0 means "not set" (ticks start at 1).</summary>
    public struct SiteQuarantine
    {
        public long StarvedSinceTick;
        public long QuarantineUntilTick;

        /// <summary>WHO blocks export launches off the site while the quarantine runs.</summary>
        public bool IsExportLocked(long tick) => tick < QuarantineUntilTick;
    }
}
