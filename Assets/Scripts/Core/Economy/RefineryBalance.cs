using System;

namespace Unruled.Core.Economy
{
    /// <summary>Refinery numbers (GDD §2.2 A).</summary>
    public sealed class RefineryBalance
    {
        public readonly int CycleTicks;
        public readonly int BaseUpkeepPerCycle;
        public readonly int IdleUpkeepPercent;
        public readonly int BrownoutMinGeoLevel;
        public readonly int BatteryPerBrownoutCycle;

        public RefineryBalance(
            int cycleTicks, int baseUpkeepPerCycle, int idleUpkeepPercent, int brownoutMinGeoLevel,
            int batteryPerBrownoutCycle)
        {
            if (cycleTicks < 1) throw new ArgumentOutOfRangeException(nameof(cycleTicks));
            if (baseUpkeepPerCycle < 0) throw new ArgumentOutOfRangeException(nameof(baseUpkeepPerCycle));
            if (idleUpkeepPercent < 0 || idleUpkeepPercent > 100) throw new ArgumentOutOfRangeException(nameof(idleUpkeepPercent));
            if (brownoutMinGeoLevel < 1 || brownoutMinGeoLevel > 5) throw new ArgumentOutOfRangeException(nameof(brownoutMinGeoLevel));
            if (batteryPerBrownoutCycle < 1) throw new ArgumentOutOfRangeException(nameof(batteryPerBrownoutCycle));

            CycleTicks = cycleTicks;
            BaseUpkeepPerCycle = baseUpkeepPerCycle;
            IdleUpkeepPercent = idleUpkeepPercent;
            BrownoutMinGeoLevel = brownoutMinGeoLevel;
            BatteryPerBrownoutCycle = batteryPerBrownoutCycle;
        }

        public int IdleUpkeepPerCycle => BaseUpkeepPerCycle * IdleUpkeepPercent / 100;
    }
}
