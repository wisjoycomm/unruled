using System;

namespace Unruled.Core.SystemFactions
{
    /// <summary>THE SYSTEM agency numbers. WHO: starved of RAW_WATER or ITEM_OXYGEN for more than 30 ticks -> 300-tick export lock.</summary>
    public sealed class SystemFactionBalance
    {
        public readonly int WhoStarvationTicks;
        public readonly int WhoQuarantineTicks;

        public SystemFactionBalance(int whoStarvationTicks, int whoQuarantineTicks)
        {
            if (whoStarvationTicks < 1) throw new ArgumentOutOfRangeException(nameof(whoStarvationTicks));
            if (whoQuarantineTicks < 1) throw new ArgumentOutOfRangeException(nameof(whoQuarantineTicks));
            WhoStarvationTicks = whoStarvationTicks;
            WhoQuarantineTicks = whoQuarantineTicks;
        }
    }
}
