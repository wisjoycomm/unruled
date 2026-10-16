using System;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Mines every deposit once per cycle (GDD §2.1 B): surface, then deep crust, then core fracture once unlocked.
    /// A deposit whose site lacks the fuel or coolant for this cycle stalls. A depth is never mixed in one cycle.
    /// Depletion lifecycle changes belong to <see cref="Board.PlanetLifecycleSystem"/>, which runs next.
    /// </summary>
    public sealed class MiningSystem : ISimSystem
    {
        private enum Depth : byte { Surface, DeepCrust, CoreFracture }

        private readonly ComponentStore<MineralDeposit> _deposits;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly MiningBalance _balance;

        public MiningSystem(
            ComponentStore<MineralDeposit> deposits, ComponentStore<Stockpile> stockpiles, MiningBalance balance)
        {
            _deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.CycleTicks != 0)
                return;

            for (int i = 0; i < _deposits.Count; i++)
                MineDeposit(ref _deposits.At(i));
        }

        private void MineDeposit(ref MineralDeposit deposit)
        {
            Depth depth;
            MiningDepth rules;
            int reserve;
            if (deposit.SurfaceReserve > 0)
            {
                depth = Depth.Surface;
                rules = _balance.Surface;
                reserve = deposit.SurfaceReserve;
            }
            else if (deposit.DeepCrustReserve > 0)
            {
                depth = Depth.DeepCrust;
                rules = _balance.DeepCrust;
                reserve = deposit.DeepCrustReserve;
            }
            else if (deposit.IsCoreFractureUnlocked && deposit.CoreFractureReserve > 0)
            {
                depth = Depth.CoreFracture;
                rules = _balance.CoreFracture;
                reserve = deposit.CoreFractureReserve;
            }
            else
            {
                return;
            }

            int yield = deposit.RigCount * _balance.YieldPerRigPerCycle * rules.SpeedPercent / 100;
            if (yield <= 0)
                return;

            int fuel = deposit.RigCount * _balance.FuelPerRigPerCycle * rules.FuelCoolantMultiplier;
            int coolant = deposit.RigCount * _balance.CoolantPerRigPerCycle * rules.FuelCoolantMultiplier;
            ref var stock = ref _stockpiles.Get(deposit.Site);
            if (!stock.Has(ItemId.ItemFuel, fuel) || !stock.Has(ItemId.ItemCoolant, coolant))
                return;

            int mined = Math.Min(yield, reserve);
            stock.Add(ItemId.ItemFuel, -fuel);
            stock.Add(ItemId.ItemCoolant, -coolant);
            stock.Add(deposit.RawItem, mined);

            switch (depth)
            {
                case Depth.Surface:
                    deposit.SurfaceReserve -= mined;
                    break;
                case Depth.DeepCrust:
                    deposit.DeepCrustReserve -= mined;
                    break;
                case Depth.CoreFracture:
                    deposit.CoreFractureReserve -= mined;
                    break;
            }
        }
    }
}
