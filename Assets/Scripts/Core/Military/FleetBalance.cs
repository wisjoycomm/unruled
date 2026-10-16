using System;
using Unruled.Core.Economy;

namespace Unruled.Core.Military
{
    /// <summary>An item and a quantity in milli-units (0.5x = 500).</summary>
    public readonly struct ItemAmount
    {
        public readonly ItemId Item;
        public readonly int Milli;

        public ItemAmount(ItemId item, int milli)
        {
            if (!ItemIds.IsValid(item)) throw new ArgumentOutOfRangeException(nameof(item));
            if (milli < 0) throw new ArgumentOutOfRangeException(nameof(milli));
            Item = item;
            Milli = milli;
        }
    }

    /// <summary>One ship tech tier of data sheet tab 03.</summary>
    public sealed class ShipTierRules
    {
        public readonly ItemAmount[] UpkeepPerShipHour;
        public readonly ItemAmount[] CombatBurnMultipliers; // items not listed burn at 1.0x
        public readonly int MothballUpkeepDiscountPercent;
        public readonly int MothballWarmupTicks;
        public readonly bool IaeaGlobalReveal;

        public ShipTierRules(
            ItemAmount[] upkeepPerShipHour, ItemAmount[] combatBurnMultipliers, int mothballUpkeepDiscountPercent,
            int mothballWarmupTicks, bool iaeaGlobalReveal)
        {
            UpkeepPerShipHour = upkeepPerShipHour ?? throw new ArgumentNullException(nameof(upkeepPerShipHour));
            CombatBurnMultipliers = combatBurnMultipliers ?? throw new ArgumentNullException(nameof(combatBurnMultipliers));
            if (mothballUpkeepDiscountPercent < 0 || mothballUpkeepDiscountPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(mothballUpkeepDiscountPercent));
            if (mothballWarmupTicks < 0) throw new ArgumentOutOfRangeException(nameof(mothballWarmupTicks));

            MothballUpkeepDiscountPercent = mothballUpkeepDiscountPercent;
            MothballWarmupTicks = mothballWarmupTicks;
            IaeaGlobalReveal = iaeaGlobalReveal;
        }

        /// <summary>Combat burn multiplier of an item in milli-units; 1000 when the tier does not list it.</summary>
        public int BurnMilli(ItemId item)
        {
            for (int i = 0; i < CombatBurnMultipliers.Length; i++)
                if (CombatBurnMultipliers[i].Item == item)
                    return CombatBurnMultipliers[i].Milli;
            return 1000;
        }
    }

    /// <summary>Fleet rules: tab 03 per tech tier plus movement (speed in milli map units per tick).</summary>
    public sealed class FleetBalance
    {
        public const int TierCount = 5;

        private readonly ShipTierRules[] _tiers;
        public readonly int BaseSpeedMilliPerTick;
        public readonly int FuelPerShipPer100UnitsMilli;

        public FleetBalance(ShipTierRules[] tiers, int baseSpeedMilliPerTick, int fuelPerShipPer100UnitsMilli)
        {
            if (tiers == null) throw new ArgumentNullException(nameof(tiers));
            if (tiers.Length != TierCount) throw new ArgumentException($"Expected {TierCount} tiers.", nameof(tiers));
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i] == null) throw new ArgumentNullException(nameof(tiers), $"Tier {i + 1} is missing.");
            if (baseSpeedMilliPerTick < 1) throw new ArgumentOutOfRangeException(nameof(baseSpeedMilliPerTick));
            if (fuelPerShipPer100UnitsMilli < 0) throw new ArgumentOutOfRangeException(nameof(fuelPerShipPer100UnitsMilli));

            _tiers = tiers;
            BaseSpeedMilliPerTick = baseSpeedMilliPerTick;
            FuelPerShipPer100UnitsMilli = fuelPerShipPer100UnitsMilli;
        }

        /// <summary>Rules of tech tier 1..5.</summary>
        public ShipTierRules Tier(byte techTier)
        {
            if (techTier < 1 || techTier > TierCount) throw new ArgumentOutOfRangeException(nameof(techTier));
            return _tiers[techTier - 1];
        }
    }
}
