using System;

namespace Unruled.Core.Simulation
{
    /// <summary>The offline shield: when it rises after logout, how long it may last per logout and per week.</summary>
    public sealed class PresenceBalance
    {
        public const long TicksPerWeek = 7 * SimTick.TicksPerDay;

        public readonly long ShieldDelayTicks; // after logout, and after the last fight while the shield is not up yet
        public readonly long ShieldMaxTicks; // per logout
        public readonly long ShieldWeeklyTicks; // per game week (tick / TicksPerWeek)

        public PresenceBalance(long shieldDelayTicks, long shieldMaxTicks, long shieldWeeklyTicks)
        {
            if (shieldDelayTicks < 0) throw new ArgumentOutOfRangeException(nameof(shieldDelayTicks));
            if (shieldMaxTicks < 0) throw new ArgumentOutOfRangeException(nameof(shieldMaxTicks));
            if (shieldWeeklyTicks < 0 || shieldWeeklyTicks > TicksPerWeek)
                throw new ArgumentOutOfRangeException(nameof(shieldWeeklyTicks));
            ShieldDelayTicks = shieldDelayTicks;
            ShieldMaxTicks = shieldMaxTicks;
            ShieldWeeklyTicks = shieldWeeklyTicks;
        }
    }
}
