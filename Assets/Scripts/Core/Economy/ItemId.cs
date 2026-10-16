namespace Unruled.Core.Economy
{
    /// <summary>The 20 items from data sheet tab 01: ids 1–10 are RAW_*, ids 11–20 are ITEM_* (refined).</summary>
    public enum ItemId : byte
    {
        None = 0,
        RawWater = 1,
        RawNitrogen = 2,
        RawMethane = 3,
        RawBiomass = 4,
        RawIron = 5,
        RawCopper = 6,
        RawSilicon = 7,
        RawTitanium = 8,
        RawTungsten = 9,
        RawUranium = 10,
        ItemOxygen = 11,
        ItemRations = 12,
        ItemCoolant = 13,
        ItemFuel = 14,
        ItemSteel = 15,
        ItemAlloy = 16,
        ItemMicrochip = 17,
        ItemBattery = 18,
        ItemAmmo = 19,
        ItemNuclearCore = 20
    }

    public static class ItemIds
    {
        public const int Count = 20;
        public const int RawCount = 10;

        public static bool IsValid(ItemId id) => id >= ItemId.RawWater && id <= ItemId.ItemNuclearCore;
        public static bool IsRaw(ItemId id) => id >= ItemId.RawWater && id <= ItemId.RawUranium;
        public static bool IsRefined(ItemId id) => id >= ItemId.ItemOxygen && id <= ItemId.ItemNuclearCore;

        /// <summary>Zero-based slot for dense per-item arrays.</summary>
        public static int ToSlot(ItemId id) => (int)id - 1;
        public static ItemId FromSlot(int slot) => (ItemId)(slot + 1);
    }
}
