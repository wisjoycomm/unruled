using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Refinery numbers (GDD §2.2 A) and the cost of building one, up to a cap per site. Each cycle a refinery runs as
    /// many recipe batches as its inputs allow, up to <see cref="MaxBatchesPerCycle"/>. A new refinery idles once its
    /// site holds <see cref="DefaultStockCap"/> of its output (0 = no cap) until its owner sets another cap.
    /// </summary>
    public sealed class RefineryBalance
    {
        public readonly int CycleTicks;
        public readonly int MaxBatchesPerCycle;
        public readonly int BaseUpkeepCentsPerCycle;
        public readonly int IdleUpkeepPercent;
        public readonly int BrownoutMinGeoLevel;
        public readonly int BatteryPerBrownoutCycle;
        public readonly int MaxRefineriesPerSite;
        public readonly int DefaultStockCap;
        private readonly ItemStack[] _buildCost;

        public RefineryBalance(
            int cycleTicks, int maxBatchesPerCycle, int baseUpkeepCentsPerCycle, int idleUpkeepPercent, int brownoutMinGeoLevel,
            int batteryPerBrownoutCycle, int maxRefineriesPerSite, int defaultStockCap, ItemStack[] buildCost)
        {
            if (cycleTicks < 1) throw new ArgumentOutOfRangeException(nameof(cycleTicks));
            if (maxBatchesPerCycle < 1) throw new ArgumentOutOfRangeException(nameof(maxBatchesPerCycle));
            if (baseUpkeepCentsPerCycle < 0) throw new ArgumentOutOfRangeException(nameof(baseUpkeepCentsPerCycle));
            if (idleUpkeepPercent < 0 || idleUpkeepPercent > 100) throw new ArgumentOutOfRangeException(nameof(idleUpkeepPercent));
            if (brownoutMinGeoLevel < 1 || brownoutMinGeoLevel > 5) throw new ArgumentOutOfRangeException(nameof(brownoutMinGeoLevel));
            if (batteryPerBrownoutCycle < 1) throw new ArgumentOutOfRangeException(nameof(batteryPerBrownoutCycle));
            if (maxRefineriesPerSite < 1) throw new ArgumentOutOfRangeException(nameof(maxRefineriesPerSite));
            if (defaultStockCap < 0) throw new ArgumentOutOfRangeException(nameof(defaultStockCap));
            if (buildCost == null || buildCost.Length == 0) throw new ArgumentException("Build cost required.", nameof(buildCost));

            CycleTicks = cycleTicks;
            MaxBatchesPerCycle = maxBatchesPerCycle;
            BaseUpkeepCentsPerCycle = baseUpkeepCentsPerCycle;
            IdleUpkeepPercent = idleUpkeepPercent;
            BrownoutMinGeoLevel = brownoutMinGeoLevel;
            BatteryPerBrownoutCycle = batteryPerBrownoutCycle;
            MaxRefineriesPerSite = maxRefineriesPerSite;
            DefaultStockCap = defaultStockCap;
            _buildCost = (ItemStack[])buildCost.Clone();
        }

        public int BuildCostCount => _buildCost.Length;

        public ItemStack BuildCost(int index) => _buildCost[index];

        public int IdleUpkeepCentsPerCycle => BaseUpkeepCentsPerCycle * IdleUpkeepPercent / 100;
    }
}
