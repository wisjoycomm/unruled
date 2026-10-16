using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Board
{
    /// <summary>Opening a jump gate: the energy items charged from the core base and the length of the new lane.</summary>
    public sealed class JumpGateBalance
    {
        public readonly int LaneLength;
        private readonly ItemStack[] _openCost;

        public JumpGateBalance(int laneLength, ItemStack[] openCost)
        {
            if (laneLength < 1) throw new ArgumentOutOfRangeException(nameof(laneLength));
            if (openCost == null || openCost.Length == 0) throw new ArgumentException("Open cost required.", nameof(openCost));
            LaneLength = laneLength;
            _openCost = (ItemStack[])openCost.Clone();
        }

        public int OpenCostCount => _openCost.Length;

        public ItemStack OpenCost(int index) => _openCost[index];
    }
}
