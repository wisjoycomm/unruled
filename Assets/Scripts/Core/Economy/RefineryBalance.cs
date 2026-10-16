using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>Refinery numbers (GDD §2.2 A) and the cost of building one, up to a cap per site.</summary>
    public sealed class RefineryBalance
    {
        public readonly int CycleTicks;
        public readonly int BaseUpkeepPerCycle;
        public readonly int IdleUpkeepPercent;
        public readonly int BrownoutMinGeoLevel;
        public readonly int BatteryPerBrownoutCycle;
        public readonly int MaxRefineriesPerSite;
        private readonly ItemStack[] _buildCost;

        public RefineryBalance(
            int cycleTicks, int baseUpkeepPerCycle, int idleUpkeepPercent, int brownoutMinGeoLevel,
            int batteryPerBrownoutCycle, int maxRefineriesPerSite, ItemStack[] buildCost)
        {
            if (cycleTicks < 1) throw new ArgumentOutOfRangeException(nameof(cycleTicks));
            if (baseUpkeepPerCycle < 0) throw new ArgumentOutOfRangeException(nameof(baseUpkeepPerCycle));
            if (idleUpkeepPercent < 0 || idleUpkeepPercent > 100) throw new ArgumentOutOfRangeException(nameof(idleUpkeepPercent));
            if (brownoutMinGeoLevel < 1 || brownoutMinGeoLevel > 5) throw new ArgumentOutOfRangeException(nameof(brownoutMinGeoLevel));
            if (batteryPerBrownoutCycle < 1) throw new ArgumentOutOfRangeException(nameof(batteryPerBrownoutCycle));
            if (maxRefineriesPerSite < 1) throw new ArgumentOutOfRangeException(nameof(maxRefineriesPerSite));
            if (buildCost == null || buildCost.Length == 0) throw new ArgumentException("Build cost required.", nameof(buildCost));

            CycleTicks = cycleTicks;
            BaseUpkeepPerCycle = baseUpkeepPerCycle;
            IdleUpkeepPercent = idleUpkeepPercent;
            BrownoutMinGeoLevel = brownoutMinGeoLevel;
            BatteryPerBrownoutCycle = batteryPerBrownoutCycle;
            MaxRefineriesPerSite = maxRefineriesPerSite;
            _buildCost = (ItemStack[])buildCost.Clone();
        }

        public int BuildCostCount => _buildCost.Length;

        public ItemStack BuildCost(int index) => _buildCost[index];

        public int IdleUpkeepPerCycle => BaseUpkeepPerCycle * IdleUpkeepPercent / 100;
    }
}
