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
    /// RetractDrills (target: a deposit on a site the player owns) stops the rigs at once; when the retract time is over
    /// they are recovered (GDD §2.1 B): gone from the deposit, their build cost back in the site's stock. BuildRig (target: a deposit on a site the player owns;
    /// ParamA = rigs) pays each rig's cost from the site's stock and adds the rigs, up to the cap per deposit.
    /// PauseDrills (target: a deposit on a site the player owns; ParamA 1 = pause, 0 = resume) stops or restarts its
    /// mining, and the fuel and coolant it burns, without touching the rigs. Invalid or unaffordable orders change
    /// nothing.
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
                        Recover(ref deposit);
                        deposit.RigCount = 0;
                        deposit.RetractUntilTick = 0;
                    }
                    continue;
                }
                if (cycle && !deposit.IsPaused)
                    MineDeposit(ref deposit);
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.RetractDrills && command.CommandType != CommandType.BuildRig &&
                command.CommandType != CommandType.PauseDrills)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (!_deposits.Has(command.Target))
                return;
            ref var deposit = ref _deposits.Get(command.Target);
            if (deposit.RetractUntilTick > 0 || !_sites.Has(deposit.Site) ||
                _sites.Get(deposit.Site).OwnerPlayerId != command.PlayerId)
                return;
            if (command.CommandType == CommandType.BuildRig)
            {
                BuildRigs(ref deposit, command.ParamA);
                return;
            }
            if (command.CommandType == CommandType.PauseDrills)
            {
                deposit.IsPaused = command.ParamA != 0;
                return;
            }
            if (deposit.RigCount == 0)
                return;
            deposit.RetractUntilTick = tick + _balance.DrillRetractTicks;
        }

        private void Recover(ref MineralDeposit deposit)
        {
            if (!_stockpiles.Has(deposit.Site))
                return;
            ref var stock = ref _stockpiles.Get(deposit.Site);
            for (int i = 0; i < _balance.RigCostCount; i++)
                stock.Add(_balance.RigCost(i).Item, _balance.RigCost(i).Quantity * deposit.RigCount);
        }

        private void BuildRigs(ref MineralDeposit deposit, int rigs)
        {
            if (rigs < 1 || rigs > _balance.MaxRigsPerDeposit - deposit.RigCount || !_stockpiles.Has(deposit.Site))
                return;
            ref var stock = ref _stockpiles.Get(deposit.Site);
            for (int i = 0; i < _balance.RigCostCount; i++)
                if (!stock.Has(_balance.RigCost(i).Item, _balance.RigCost(i).Quantity * rigs))
                    return;
            for (int i = 0; i < _balance.RigCostCount; i++)
                stock.Add(_balance.RigCost(i).Item, -_balance.RigCost(i).Quantity * rigs);
            deposit.RigCount += rigs;
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
