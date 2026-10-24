using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    /// <summary>Where fleet movement reports an arrival, so the arriving fleet's owner learns that system.</summary>
    public interface IChartLog
    {
        void FleetArrived(int playerId, EntityId site, long tick);
    }
}
