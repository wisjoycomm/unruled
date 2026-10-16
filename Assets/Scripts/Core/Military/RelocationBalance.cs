using System;

namespace Unruled.Core.Military
{
    /// <summary>Three-phase core-base relocation (GDD §2.4 A, §4.2, data sheet tab 04). Durations in ticks.</summary>
    public sealed class RelocationBalance
    {
        public readonly int TeardownMinTicks;
        public readonly int TeardownMaxTicks;
        public readonly int ConvoyMinTicks;
        public readonly int ConvoyMaxTicks;
        public readonly int BootstrapTicks;
        public readonly int CooldownTicks;
        public readonly int ParalysisTicks; // orders refused after the capital convoy is destroyed
        public readonly int TeardownDefencePercent; // the old base keeps this share of its fortification bonus
        public readonly ItemAmount[] TeardownCost;
        public readonly ItemAmount[] BootstrapCost;

        public RelocationBalance(
            int teardownMinTicks, int teardownMaxTicks, int convoyMinTicks, int convoyMaxTicks, int bootstrapTicks,
            int cooldownTicks, int paralysisTicks, int teardownDefencePercent, ItemAmount[] teardownCost,
            ItemAmount[] bootstrapCost)
        {
            if (teardownMinTicks < 1 || teardownMaxTicks < teardownMinTicks) throw new ArgumentOutOfRangeException(nameof(teardownMaxTicks));
            if (convoyMinTicks < 1 || convoyMaxTicks < convoyMinTicks) throw new ArgumentOutOfRangeException(nameof(convoyMaxTicks));
            if (bootstrapTicks < 1) throw new ArgumentOutOfRangeException(nameof(bootstrapTicks));
            if (cooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(cooldownTicks));
            if (paralysisTicks < 0) throw new ArgumentOutOfRangeException(nameof(paralysisTicks));
            if (teardownDefencePercent < 0 || teardownDefencePercent > 100) throw new ArgumentOutOfRangeException(nameof(teardownDefencePercent));

            TeardownMinTicks = teardownMinTicks;
            TeardownMaxTicks = teardownMaxTicks;
            ConvoyMinTicks = convoyMinTicks;
            ConvoyMaxTicks = convoyMaxTicks;
            BootstrapTicks = bootstrapTicks;
            CooldownTicks = cooldownTicks;
            ParalysisTicks = paralysisTicks;
            TeardownDefencePercent = teardownDefencePercent;
            TeardownCost = teardownCost ?? throw new ArgumentNullException(nameof(teardownCost));
            BootstrapCost = bootstrapCost ?? throw new ArgumentNullException(nameof(bootstrapCost));
        }
    }
}
