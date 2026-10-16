using System;

namespace Unruled.Core.Military
{
    /// <summary>Convoy routes: how much a cargo convoy ship carries per item, and how often a route fleet checks its run.</summary>
    public sealed class ConvoyBalance
    {
        public readonly int CapacityPerConvoy;
        public readonly int CheckIntervalTicks;

        public ConvoyBalance(int capacityPerConvoy, int checkIntervalTicks)
        {
            if (capacityPerConvoy < 1) throw new ArgumentOutOfRangeException(nameof(capacityPerConvoy));
            if (checkIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(checkIntervalTicks));
            CapacityPerConvoy = capacityPerConvoy;
            CheckIntervalTicks = checkIntervalTicks;
        }
    }
}
