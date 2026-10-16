using System;
using Unruled.Core.Economy;

namespace Unruled.Core.Simulation
{
    /// <summary>A quantity of one item handed out at the start of a game.</summary>
    public readonly struct ItemStack
    {
        public readonly ItemId Item;
        public readonly int Quantity;

        public ItemStack(ItemId item, int quantity)
        {
            if (!ItemIds.IsValid(item)) throw new ArgumentOutOfRangeException(nameof(item));
            if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity));
            Item = item;
            Quantity = quantity;
        }
    }

    /// <summary>
    /// How a new game is laid out: one home system per participant, deposits by ESI (GDD §2.1 A), the core base's
    /// rigs, refineries and stock, and the starting fleet. See <see cref="NewGameSetup"/>.
    /// </summary>
    public sealed class NewGameBalance
    {
        public readonly int RivalCount;
        public readonly int DepositsPerSite;
        public readonly int SurfaceReserve;
        public readonly int DeepCrustReserve;
        public readonly int CoreFractureReserve;
        public readonly int StartingRigsPerDeposit;
        public readonly byte FleetTechTier;
        public readonly int FleetLineShips;
        public readonly int FleetEwShips;
        public readonly int FleetSupplyTenders;
        public readonly int FleetCargoConvoys;
        private readonly ItemId[] _homeExcludedRaws;
        private readonly ItemId[] _startingRefineries;
        private readonly ItemStack[] _coreBaseStock;
        private readonly ItemStack[] _fleetStock;

        public NewGameBalance(
            int rivalCount, int depositsPerSite, int surfaceReserve, int deepCrustReserve, int coreFractureReserve,
            int startingRigsPerDeposit, byte fleetTechTier, int fleetLineShips, int fleetEwShips, int fleetSupplyTenders,
            int fleetCargoConvoys, ItemId[] homeExcludedRaws, ItemId[] startingRefineries, ItemStack[] coreBaseStock,
            ItemStack[] fleetStock)
        {
            if (rivalCount < 0) throw new ArgumentOutOfRangeException(nameof(rivalCount));
            if (depositsPerSite < 1) throw new ArgumentOutOfRangeException(nameof(depositsPerSite));
            if (surfaceReserve < 0 || deepCrustReserve < 0 || coreFractureReserve < 0)
                throw new ArgumentOutOfRangeException(nameof(surfaceReserve), "Reserves cannot be negative.");
            if (startingRigsPerDeposit < 0) throw new ArgumentOutOfRangeException(nameof(startingRigsPerDeposit));
            if (fleetLineShips < 0 || fleetEwShips < 0 || fleetSupplyTenders < 0 || fleetCargoConvoys < 0)
                throw new ArgumentOutOfRangeException(nameof(fleetLineShips), "Ship counts cannot be negative.");
            if (homeExcludedRaws == null) throw new ArgumentNullException(nameof(homeExcludedRaws));
            for (int i = 0; i < homeExcludedRaws.Length; i++)
                if (!ItemIds.IsRaw(homeExcludedRaws[i]))
                    throw new ArgumentException($"{homeExcludedRaws[i]} is not a raw item.", nameof(homeExcludedRaws));
            if (startingRefineries == null) throw new ArgumentNullException(nameof(startingRefineries));
            for (int i = 0; i < startingRefineries.Length; i++)
                if (!ItemIds.IsRefined(startingRefineries[i]))
                    throw new ArgumentException($"{startingRefineries[i]} is not a refined item.", nameof(startingRefineries));
            RivalCount = rivalCount;
            DepositsPerSite = depositsPerSite;
            SurfaceReserve = surfaceReserve;
            DeepCrustReserve = deepCrustReserve;
            CoreFractureReserve = coreFractureReserve;
            StartingRigsPerDeposit = startingRigsPerDeposit;
            FleetTechTier = fleetTechTier;
            FleetLineShips = fleetLineShips;
            FleetEwShips = fleetEwShips;
            FleetSupplyTenders = fleetSupplyTenders;
            FleetCargoConvoys = fleetCargoConvoys;
            _homeExcludedRaws = (ItemId[])homeExcludedRaws.Clone();
            _startingRefineries = (ItemId[])startingRefineries.Clone();
            _coreBaseStock = (ItemStack[])(coreBaseStock ?? throw new ArgumentNullException(nameof(coreBaseStock))).Clone();
            _fleetStock = (ItemStack[])(fleetStock ?? throw new ArgumentNullException(nameof(fleetStock))).Clone();
        }

        public bool IsExcludedAtHome(ItemId raw) => Array.IndexOf(_homeExcludedRaws, raw) >= 0;

        public int StartingRefineryCount => _startingRefineries.Length;
        public ItemId StartingRefinery(int index) => _startingRefineries[index];

        public int CoreBaseStockCount => _coreBaseStock.Length;
        public ItemStack CoreBaseStock(int index) => _coreBaseStock[index];

        public int FleetStockCount => _fleetStock.Length;
        public ItemStack FleetStock(int index) => _fleetStock[index];
    }
}
