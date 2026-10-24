using Unruled.Core.Ecs;

namespace Unruled.Core.Galaxy
{
    /// <summary>Where the chart market reports a world-shop registration for THE SYSTEM to publish.</summary>
    public interface IChartRegistryLog
    {
        /// <summary>The first surveyor registered <paramref name="system"/> (a <see cref="StarSystem"/> entity) on the world shop.</summary>
        void ReportChartRegistered(int playerId, EntityId system, long tick);
    }
}
