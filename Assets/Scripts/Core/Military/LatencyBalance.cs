using System;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Signal latency (GDD §2.4 A, data sheet tab 04): (Base + sum of hop weights) x relocation multiplier; and what a relay
    /// station costs to raise and keep lit (see <see cref="RelayStationSystem"/>).
    /// </summary>
    public sealed class LatencyBalance
    {
        public readonly int BaseTicks;
        public readonly int HopTicks;
        public readonly int RelayedHopTicks;
        public readonly int RelocationMultiplier;
        public readonly byte BlackoutRadioLevel;
        public readonly int RelayBuildSteel;
        public readonly int RelayBuildBatteries;
        public readonly int RelayUpkeepBatteries;
        public readonly int RelayUpkeepIntervalTicks;

        public LatencyBalance(
            int baseTicks, int hopTicks, int relayedHopTicks, int relocationMultiplier, byte blackoutRadioLevel,
            int relayBuildSteel, int relayBuildBatteries, int relayUpkeepBatteries, int relayUpkeepIntervalTicks)
        {
            if (baseTicks < 1) throw new ArgumentOutOfRangeException(nameof(baseTicks));
            if (relayedHopTicks < 1 || hopTicks < relayedHopTicks) throw new ArgumentOutOfRangeException(nameof(hopTicks));
            if (relocationMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(relocationMultiplier));
            if (blackoutRadioLevel < 1 || blackoutRadioLevel > 5) throw new ArgumentOutOfRangeException(nameof(blackoutRadioLevel));
            if (relayBuildSteel < 0) throw new ArgumentOutOfRangeException(nameof(relayBuildSteel));
            if (relayBuildBatteries < 0) throw new ArgumentOutOfRangeException(nameof(relayBuildBatteries));
            if (relayUpkeepBatteries < 1) throw new ArgumentOutOfRangeException(nameof(relayUpkeepBatteries));
            if (relayUpkeepIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(relayUpkeepIntervalTicks));

            BaseTicks = baseTicks;
            HopTicks = hopTicks;
            RelayedHopTicks = relayedHopTicks;
            RelocationMultiplier = relocationMultiplier;
            BlackoutRadioLevel = blackoutRadioLevel;
            RelayBuildSteel = relayBuildSteel;
            RelayBuildBatteries = relayBuildBatteries;
            RelayUpkeepBatteries = relayUpkeepBatteries;
            RelayUpkeepIntervalTicks = relayUpkeepIntervalTicks;
        }
    }
}
