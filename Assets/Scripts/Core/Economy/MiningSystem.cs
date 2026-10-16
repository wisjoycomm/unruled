using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Mines every deposit once per cycle (GDD §2.1 B): surface, then deep crust, then core fracture once unlocked
    /// (only for a site owner with the Tier-4 core-fracture gate).
    /// A deposit whose site lacks the fuel or coolant for this cycle stalls. A depth is never mixed in one cycle.
    /// Depletion lifecycle changes belong to <see cref="Board.PlanetLifecycleSystem"/>, which runs next.
    /// RetractDrills (target: a deposit on a site the player owns) stops the rigs at once; they are gone from the
    /// deposit when the retract time is over (GDD §2.1 B). Invalid orders change nothing.
    /// </summary>
    public sealed class MiningSystem : ISimSystem, ICommandHandler
    {
        private enum Depth : byte { Surface, DeepCrust, CoreFracture }

        private readonly ComponentStore<MineralDeposit> _deposits;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly MiningBalance _balance;
        private readonly ITechGates _tech;

        public MiningSystem(
            ComponentStore<MineralDeposit> deposits, ComponentStore<SiteInfo> sites, ComponentStore<Stockpile> stockpiles,
            MiningBalance balance, ITechGates tech)
        {
            _deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _tech = tech ?? throw new ArgumentNullException(nameof(tech));
        }

        public void OnTick(long tick)
        {
            bool cycle = tick % _balance.CycleTicks == 0;
            for (int i = 0; i < _deposits.Count; i++)
            {
                ref var deposit = ref _deposits.At(i);
                if (deposit.RetractUntilTick > 0)
                {
                    if (tick >= deposit.RetractUntilTick)
                    {
                        deposit.RigCount = 0;
                        deposit.RetractUntilTick = 0;
                    }
                    continue;
                }
                if (cycle)
                    MineDeposit(ref deposit);
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.RetractDrills)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (!_deposits.Has(command.Target))
                return;
            ref var deposit = ref _deposits.Get(command.Target);
            if (deposit.RigCount == 0 || deposit.RetractUntilTick > 0 || !_sites.Has(deposit.Site) ||
                _sites.Get(deposit.Site).OwnerPlayerId != command.PlayerId)
                return;
            deposit.RetractUntilTick = tick + _balance.DrillRetractTicks;
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
            else if (deposit.IsCoreFractureUnlocked && deposit.CoreFractureReserve > 0 &&
                     _tech.Unlocks(_sites.Get(deposit.Site).OwnerPlayerId, TechGate.CoreFractureMining))
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
