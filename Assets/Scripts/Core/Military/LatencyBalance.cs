using System;

namespace Unruled.Core.Military
{
    /// <summary>Signal latency (GDD §2.4 A, data sheet tab 04): (Base + sum of hop weights) x relocation multiplier.</summary>
    public sealed class LatencyBalance
    {
        public readonly int BaseTicks;
        public readonly int HopTicks;
        public readonly int RelayedHopTicks;
        public readonly int RelocationMultiplier;
        public readonly byte BlackoutRadioLevel;

        public LatencyBalance(int baseTicks, int hopTicks, int relayedHopTicks, int relocationMultiplier, byte blackoutRadioLevel)
        {
            if (baseTicks < 1) throw new ArgumentOutOfRangeException(nameof(baseTicks));
            if (relayedHopTicks < 1 || hopTicks < relayedHopTicks) throw new ArgumentOutOfRangeException(nameof(hopTicks));
            if (relocationMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(relocationMultiplier));
            if (blackoutRadioLevel < 1 || blackoutRadioLevel > 5) throw new ArgumentOutOfRangeException(nameof(blackoutRadioLevel));

            BaseTicks = baseTicks;
            HopTicks = hopTicks;
            RelayedHopTicks = relayedHopTicks;
            RelocationMultiplier = relocationMultiplier;
            BlackoutRadioLevel = blackoutRadioLevel;
        }
    }
}
