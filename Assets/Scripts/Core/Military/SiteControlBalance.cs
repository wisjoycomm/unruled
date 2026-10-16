using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>Claiming a site: the items a fleet docked there must unload from its hold to raise an outpost.</summary>
    public sealed class SiteControlBalance
    {
        private readonly ItemStack[] _claimCost;

        public SiteControlBalance(ItemStack[] claimCost)
        {
            if (claimCost == null || claimCost.Length == 0) throw new ArgumentException("Claim cost required.", nameof(claimCost));
            _claimCost = (ItemStack[])claimCost.Clone();
        }

        public int ClaimCostCount => _claimCost.Length;

        public ItemStack ClaimCost(int index) => _claimCost[index];
    }
}
