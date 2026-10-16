using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>Shipyard rules (tab 03 Build_And_Repair_Items per tier): cost and build time per ship, retrofit cost.</summary>
    public sealed class ShipyardBalance
    {
        public readonly int MaxShipsPerOrder;
        public readonly int RetrofitCostPercent; // of the new tier's per-ship build cost
        private readonly ItemStack[][] _costPerShip; // index 0 = tier 1
        private readonly int[] _buildTicksPerShip;

        public ShipyardBalance(int maxShipsPerOrder, int retrofitCostPercent, ItemStack[][] costPerShip, int[] buildTicksPerShip)
        {
            if (maxShipsPerOrder < 1) throw new ArgumentOutOfRangeException(nameof(maxShipsPerOrder));
            if (retrofitCostPercent < 0) throw new ArgumentOutOfRangeException(nameof(retrofitCostPercent));
            if (costPerShip == null || costPerShip.Length != FleetBalance.TierCount)
                throw new ArgumentException($"{FleetBalance.TierCount} tiers of costs required.", nameof(costPerShip));
            if (buildTicksPerShip == null || buildTicksPerShip.Length != FleetBalance.TierCount)
                throw new ArgumentException($"{FleetBalance.TierCount} tiers of build times required.", nameof(buildTicksPerShip));
            _costPerShip = new ItemStack[FleetBalance.TierCount][];
            for (int i = 0; i < FleetBalance.TierCount; i++)
            {
                if (costPerShip[i] == null || costPerShip[i].Length == 0)
                    throw new ArgumentException($"Tier {i + 1} cost missing.", nameof(costPerShip));
                if (buildTicksPerShip[i] < 1) throw new ArgumentOutOfRangeException(nameof(buildTicksPerShip));
                _costPerShip[i] = (ItemStack[])costPerShip[i].Clone();
            }
            MaxShipsPerOrder = maxShipsPerOrder;
            RetrofitCostPercent = retrofitCostPercent;
            _buildTicksPerShip = (int[])buildTicksPerShip.Clone();
        }

        public int CostCount(byte tier) => Costs(tier).Length;

        public ItemStack Cost(byte tier, int index) => Costs(tier)[index];

        public int BuildTicksPerShip(byte tier) => _buildTicksPerShip[TierIndex(tier)];

        private ItemStack[] Costs(byte tier) => _costPerShip[TierIndex(tier)];

        private static int TierIndex(byte tier)
        {
            if (tier < 1 || tier > FleetBalance.TierCount) throw new ArgumentOutOfRangeException(nameof(tier));
            return tier - 1;
        }
    }
}
