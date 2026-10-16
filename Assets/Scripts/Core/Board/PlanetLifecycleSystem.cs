using System;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Owns <see cref="SiteInfo.LifecycleState"/>, ESI changes and irradiation (GDD §2.1 B, data sheet tab 02).
    /// After each mining cycle: all surface gone -> DeepCoreStripping; deep crust gone too -> DepletedBarrenHusk.
    /// A nuclear strike makes an IrradiatedDeadWorld; the Terraform command turns it into TerraformedRecovery.
    /// </summary>
    public sealed class PlanetLifecycleSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<MineralDeposit> _deposits;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly MiningBalance _mining;
        private readonly LifecycleBalance _balance;

        // Per-site totals for the depletion pass, indexed by site entity index. Grows only when new indices appear.
        private long[] _surfaceLeft = new long[64];
        private long[] _deepLeft = new long[64];
        private int[] _depositCount = new int[64];

        public PlanetLifecycleSystem(
            ComponentStore<SiteInfo> sites, ComponentStore<MineralDeposit> deposits,
            ComponentStore<Stockpile> stockpiles, MiningBalance mining, LifecycleBalance balance)
        {
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _mining = mining ?? throw new ArgumentNullException(nameof(mining));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            if (tick % _mining.CycleTicks != 0)
                return;

            Array.Clear(_surfaceLeft, 0, _surfaceLeft.Length);
            Array.Clear(_deepLeft, 0, _deepLeft.Length);
            Array.Clear(_depositCount, 0, _depositCount.Length);

            for (int i = 0; i < _deposits.Count; i++)
            {
                ref var deposit = ref _deposits.At(i);
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
                    site.CurrentEsiMilli = Math.Max(0, site.CurrentEsiMilli - _mining.DeepCoreEsiDropMilli);
                }
                if (site.LifecycleState == PlanetLifecycleState.DeepCoreStripping && _deepLeft[siteIndex] == 0)
                {
                    site.LifecycleState = PlanetLifecycleState.DepletedBarrenHusk;
                    site.CurrentEsiMilli = Math.Max(0, site.CurrentEsiMilli - _mining.BarrenHuskEsiDropMilli);
                }
            }
        }

        /// <summary>
        /// State 3B: wipes all biomass, sets ESI to the irradiated value, unlocks every deposit's core fracture and
        /// irradiates the site for a seeded 24–72h. Called by the combat engine inside the tick order.
        /// </summary>
        public void ApplyNuclearStrike(EntityId siteId, long tick)
        {
            if (!_sites.Has(siteId))
                throw new ArgumentException($"Entity {siteId} is not a site.", nameof(siteId));

            ref var site = ref _sites.Get(siteId);
            var rng = new SplitMix64(site.TerrainSeed ^ (ulong)tick);
            site.LifecycleState = PlanetLifecycleState.IrradiatedDeadWorld;
            site.CurrentEsiMilli = _balance.IrradiatedEsiMilli;
            site.IrradiatedUntilTick = tick + rng.NextInt(_balance.IrradiationMinTicks, _balance.IrradiationMaxTicks);

            for (int i = 0; i < _deposits.Count; i++)
            {
                ref var deposit = ref _deposits.At(i);
                if (deposit.Site != siteId)
                    continue;
                deposit.IsCoreFractureUnlocked = true;
                if (deposit.RawItem == ItemId.RawBiomass)
                {
                    deposit.SurfaceReserve = 0;
                    deposit.DeepCrustReserve = 0;
                    deposit.CoreFractureReserve = 0;
                }
            }

            if (_stockpiles.Has(siteId))
            {
                ref var stock = ref _stockpiles.Get(siteId);
                stock.Add(ItemId.RawBiomass, -stock.Get(ItemId.RawBiomass));
            }
        }

        /// <summary>
        /// Terraform (state 4): the site owner pays water, nitrogen, biomass and nuclear cores from the site stockpile;
        /// ESI rises to the recovery value and radiation is cleared. Depleted reserves stay depleted.
        /// Rejected (no change) for a stale target, a non-owner, a site that is not a dead world, or missing items.
        /// </summary>
        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.Terraform)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));

            var siteId = command.Target;
            if (!_sites.Has(siteId) || !_stockpiles.Has(siteId))
                return;
            ref var site = ref _sites.Get(siteId);
            if (site.OwnerPlayerId != command.PlayerId || site.LifecycleState != PlanetLifecycleState.IrradiatedDeadWorld)
                return;

            ref var stock = ref _stockpiles.Get(siteId);
            if (!stock.Has(ItemId.RawWater, _balance.TerraformWater) ||
                !stock.Has(ItemId.RawNitrogen, _balance.TerraformNitrogen) ||
                !stock.Has(ItemId.RawBiomass, _balance.TerraformBiomass) ||
                !stock.Has(ItemId.ItemNuclearCore, _balance.TerraformNuclearCores))
                return;

            stock.Add(ItemId.RawWater, -_balance.TerraformWater);
            stock.Add(ItemId.RawNitrogen, -_balance.TerraformNitrogen);
            stock.Add(ItemId.RawBiomass, -_balance.TerraformBiomass);
            stock.Add(ItemId.ItemNuclearCore, -_balance.TerraformNuclearCores);

            site.LifecycleState = PlanetLifecycleState.TerraformedRecovery;
            site.CurrentEsiMilli = _balance.TerraformedEsiMilli;
            site.IrradiatedUntilTick = tick;
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
