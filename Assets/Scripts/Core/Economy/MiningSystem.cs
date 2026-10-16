using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Mines every deposit once per cycle (GDD §2.1 B), then moves sites down the lifecycle:
    /// all surface gone -> DeepCoreStripping (ESI drop); deep crust gone too -> DepletedBarrenHusk (ESI drop).
    /// A deposit whose site lacks the fuel or coolant for this cycle stalls. A depth is never mixed in one cycle.
    /// </summary>
    public sealed class MiningSystem : ISimSystem
    {
        private enum Depth : byte { Surface, DeepCrust, CoreFracture }

        private readonly ComponentStore<MineralDeposit> _deposits;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly MiningBalance _balance;

        // Per-site totals for the lifecycle pass, indexed by site entity index. Grows only when new indices appear.
        private long[] _surfaceLeft = new long[64];
        private long[] _deepLeft = new long[64];
        private int[] _depositCount = new int[64];

        public MiningSystem(
            ComponentStore<MineralDeposit> deposits, ComponentStore<Stockpile> stockpiles,
            ComponentStore<SiteInfo> sites, MiningBalance balance)
        {
            _deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.CycleTicks != 0)
                return;

            Array.Clear(_surfaceLeft, 0, _surfaceLeft.Length);
            Array.Clear(_deepLeft, 0, _deepLeft.Length);
            Array.Clear(_depositCount, 0, _depositCount.Length);

            for (int i = 0; i < _deposits.Count; i++)
            {
                ref var deposit = ref _deposits.At(i);
                MineDeposit(ref deposit);

                int siteIndex = deposit.Site.Index;
                EnsureScratch(siteIndex);
                _surfaceLeft[siteIndex] += deposit.SurfaceReserve;
                _deepLeft[siteIndex] += deposit.DeepCrustReserve;
                _depositCount[siteIndex]++;
            }

            for (int i = 0; i < _sites.Count; i++)
            {
                int siteIndex = _sites.EntityAt(i).Index;
                if (siteIndex >= _depositCount.Length || _depositCount[siteIndex] == 0)
                    continue;

                ref var site = ref _sites.At(i);
                if (site.LifecycleState == PlanetLifecycleState.PristineWorld && _surfaceLeft[siteIndex] == 0)
                {
                    site.LifecycleState = PlanetLifecycleState.DeepCoreStripping;
                    site.CurrentEsiMilli = Math.Max(0, site.CurrentEsiMilli - _balance.DeepCoreEsiDropMilli);
                }
                if (site.LifecycleState == PlanetLifecycleState.DeepCoreStripping && _deepLeft[siteIndex] == 0)
                {
                    site.LifecycleState = PlanetLifecycleState.DepletedBarrenHusk;
                    site.CurrentEsiMilli = Math.Max(0, site.CurrentEsiMilli - _balance.BarrenHuskEsiDropMilli);
                }
            }
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

        private void EnsureScratch(int siteIndex)
        {
            if (siteIndex < _depositCount.Length)
                return;
            int size = _depositCount.Length;
            while (size <= siteIndex) size *= 2;
            Array.Resize(ref _surfaceLeft, size);
            Array.Resize(ref _deepLeft, size);
            Array.Resize(ref _depositCount, size);
        }
    }
}
