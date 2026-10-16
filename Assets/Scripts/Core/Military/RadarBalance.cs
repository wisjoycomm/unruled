using System;

namespace Unruled.Core.Military
{
    /// <summary>Focused 60 s radar (GDD §2.4 B, §4.1, data sheet tab 04). Durations in ticks.</summary>
    public sealed class RadarBalance
    {
        public readonly int StartupBatteries;
        public readonly int RwrDelayTicks;
        public readonly int IntegrationTicks;
        public readonly int JammedIntegrationTicks;
        public readonly int JamBatteries;
        public readonly int RevealTicks;

        public RadarBalance(
            int startupBatteries, int rwrDelayTicks, int integrationTicks, int jammedIntegrationTicks,
            int jamBatteries, int revealTicks)
        {
            if (startupBatteries < 0) throw new ArgumentOutOfRangeException(nameof(startupBatteries));
            if (integrationTicks < 1) throw new ArgumentOutOfRangeException(nameof(integrationTicks));
            if (rwrDelayTicks < 1 || rwrDelayTicks >= integrationTicks) throw new ArgumentOutOfRangeException(nameof(rwrDelayTicks));
            if (jammedIntegrationTicks < integrationTicks) throw new ArgumentOutOfRangeException(nameof(jammedIntegrationTicks));
            if (jamBatteries < 0) throw new ArgumentOutOfRangeException(nameof(jamBatteries));
            if (revealTicks < 1) throw new ArgumentOutOfRangeException(nameof(revealTicks));

            StartupBatteries = startupBatteries;
            RwrDelayTicks = rwrDelayTicks;
            IntegrationTicks = integrationTicks;
            JammedIntegrationTicks = jammedIntegrationTicks;
            JamBatteries = jamBatteries;
            RevealTicks = revealTicks;
        }
    }
}
