using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>Speed and drill cost for one reserve depth (data sheet tab 02).</summary>
    public readonly struct MiningDepth
    {
        public readonly int SpeedPercent;
        public readonly int FuelCoolantMultiplier;

        public MiningDepth(int speedPercent, int fuelCoolantMultiplier)
        {
            if (speedPercent < 0) throw new ArgumentOutOfRangeException(nameof(speedPercent));
            if (fuelCoolantMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(fuelCoolantMultiplier));
            SpeedPercent = speedPercent;
            FuelCoolantMultiplier = fuelCoolantMultiplier;
        }
    }

    /// <summary>
    /// Mining numbers: per cycle each rig yields <see cref="YieldPerRigPerCycle"/> x speed% ore and burns
    /// fuel and coolant x the depth multiplier. ESI drops are milli-units. Pulling the drills out takes
    /// <see cref="DrillRetractTicks"/>. A new rig costs <see cref="RigCost"/> items from the site, up to
    /// <see cref="MaxRigsPerDeposit"/> rigs per deposit.
    /// </summary>
    public sealed class MiningBalance
    {
        public readonly int CycleTicks;
        public readonly int YieldPerRigPerCycle;
        public readonly int FuelPerRigPerCycle;
        public readonly int CoolantPerRigPerCycle;
        public readonly MiningDepth Surface;
        public readonly MiningDepth DeepCrust;
        public readonly MiningDepth CoreFracture;
        public readonly int DeepCoreEsiDropMilli;
        public readonly int BarrenHuskEsiDropMilli;
        public readonly int DrillRetractTicks;
        public readonly int MaxRigsPerDeposit;
        private readonly ItemStack[] _rigCost;

        public MiningBalance(
            int cycleTicks, int yieldPerRigPerCycle, int fuelPerRigPerCycle, int coolantPerRigPerCycle,
            MiningDepth surface, MiningDepth deepCrust, MiningDepth coreFracture,
            int deepCoreEsiDropMilli, int barrenHuskEsiDropMilli, int drillRetractTicks, int maxRigsPerDeposit,
            ItemStack[] rigCost)
        {
            if (cycleTicks < 1) throw new ArgumentOutOfRangeException(nameof(cycleTicks));
            if (yieldPerRigPerCycle < 1) throw new ArgumentOutOfRangeException(nameof(yieldPerRigPerCycle));
            if (fuelPerRigPerCycle < 0) throw new ArgumentOutOfRangeException(nameof(fuelPerRigPerCycle));
            if (coolantPerRigPerCycle < 0) throw new ArgumentOutOfRangeException(nameof(coolantPerRigPerCycle));
            if (deepCoreEsiDropMilli < 0) throw new ArgumentOutOfRangeException(nameof(deepCoreEsiDropMilli));
            if (barrenHuskEsiDropMilli < 0) throw new ArgumentOutOfRangeException(nameof(barrenHuskEsiDropMilli));
            if (drillRetractTicks < 1) throw new ArgumentOutOfRangeException(nameof(drillRetractTicks));
            if (maxRigsPerDeposit < 1) throw new ArgumentOutOfRangeException(nameof(maxRigsPerDeposit));
            if (rigCost == null || rigCost.Length == 0) throw new ArgumentException("Rig cost required.", nameof(rigCost));

            CycleTicks = cycleTicks;
            YieldPerRigPerCycle = yieldPerRigPerCycle;
            FuelPerRigPerCycle = fuelPerRigPerCycle;
            CoolantPerRigPerCycle = coolantPerRigPerCycle;
            Surface = surface;
            DeepCrust = deepCrust;
            CoreFracture = coreFracture;
            DeepCoreEsiDropMilli = deepCoreEsiDropMilli;
            BarrenHuskEsiDropMilli = barrenHuskEsiDropMilli;
            DrillRetractTicks = drillRetractTicks;
            MaxRigsPerDeposit = maxRigsPerDeposit;
            _rigCost = (ItemStack[])rigCost.Clone();
        }

        public int RigCostCount => _rigCost.Length;

        public ItemStack RigCost(int index) => _rigCost[index];
    }
}
