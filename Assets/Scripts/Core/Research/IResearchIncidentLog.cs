using Unruled.Core.Ecs;

namespace Unruled.Core.Research
{
    /// <summary>Where research reports incidents THE SYSTEM publishes.</summary>
    public interface IResearchIncidentLog
    {
        /// <summary>A prototype blew up in a stress test at <paramref name="site"/> (NOAA/IAEA bulletin).</summary>
        void ReportPrototypeBlast(int playerId, EntityId site, long tick);
    }
}
