using System;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Convoy routes: how much a cargo convoy ship carries per item, how often a route fleet checks its run, how many
    /// upkeep intervals of its own oxygen and rations it keeps in the hold, and how much of each supply it leaves at the
    /// hub for the hub's own drills and fleets.
    /// </summary>
    public sealed class ConvoyBalance
    {
        public readonly int CapacityPerConvoy;
        public readonly int CheckIntervalTicks;
        public readonly int UpkeepReserveIntervals;
        public readonly int HubSupplyReserve;

        public ConvoyBalance(int capacityPerConvoy, int checkIntervalTicks, int upkeepReserveIntervals, int hubSupplyReserve)
        {
            if (capacityPerConvoy < 1) throw new ArgumentOutOfRangeException(nameof(capacityPerConvoy));
            if (checkIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(checkIntervalTicks));
            if (upkeepReserveIntervals < 1) throw new ArgumentOutOfRangeException(nameof(upkeepReserveIntervals));
            if (hubSupplyReserve < 0) throw new ArgumentOutOfRangeException(nameof(hubSupplyReserve));
            CapacityPerConvoy = capacityPerConvoy;
            CheckIntervalTicks = checkIntervalTicks;
            UpkeepReserveIntervals = upkeepReserveIntervals;
            HubSupplyReserve = hubSupplyReserve;
        }
    }
}
