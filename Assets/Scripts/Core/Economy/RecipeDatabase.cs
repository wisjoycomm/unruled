using System;

namespace Unruled.Core.Economy
{
    /// <summary>Static per-item data (data sheet tab 01). ESI values are milli-units (0..1000).</summary>
    public readonly struct ItemDefinition
    {
        public readonly ItemId Id;
        public readonly int BaseWtoPrice;
        public readonly bool HasPreferredEsi;
        public readonly int PreferredEsiMinMilli;
        public readonly int PreferredEsiMaxMilli;
        public readonly int MiningChancePermille;
        public readonly int MiningChanceBonusPerTierPermille;

        public ItemDefinition(ItemId id, int baseWtoPrice)
        {
            Id = id;
            BaseWtoPrice = baseWtoPrice;
            HasPreferredEsi = false;
            PreferredEsiMinMilli = 0;
            PreferredEsiMaxMilli = 0;
            MiningChancePermille = 1000;
            MiningChanceBonusPerTierPermille = 0;
        }

        public ItemDefinition(ItemId id, int baseWtoPrice, int preferredEsiMinMilli, int preferredEsiMaxMilli,
            int miningChancePermille = 1000, int miningChanceBonusPerTierPermille = 0)
        {
            Id = id;
            BaseWtoPrice = baseWtoPrice;
            HasPreferredEsi = true;
            PreferredEsiMinMilli = preferredEsiMinMilli;
            PreferredEsiMaxMilli = preferredEsiMaxMilli;
            MiningChancePermille = miningChancePermille;
            MiningChanceBonusPerTierPermille = miningChanceBonusPerTierPermille;
        }

        public int EffectiveMiningChancePermille(byte industryTier) =>
            MiningChancePermille >= 1000
                ? 1000
                : Math.Min(RecipeDatabase.MaxScaledChancePermille,
                    MiningChancePermille + Math.Max(0, industryTier - 1) * MiningChanceBonusPerTierPermille);
    }

    /// <summary>Leontief recipe: Qty1 x Input1 + Qty2 x Input2 -> OutputQty x Output, with CraftChancePermille yield odds.</summary>
    public readonly struct Recipe
    {
        public readonly ItemId Output;
        public readonly int OutputQty;
        public readonly ItemId Input1;
        public readonly int Qty1;
        public readonly ItemId Input2;
        public readonly int Qty2;
        public readonly int CraftChancePermille;
        public readonly int CraftChanceBonusPerTierPermille;

        public Recipe(ItemId output, ItemId input1, int qty1, ItemId input2, int qty2, int outputQty = 1,
            int craftChancePermille = 1000, int craftChanceBonusPerTierPermille = 0)
        {
            Output = output;
            OutputQty = outputQty;
            Input1 = input1;
            Qty1 = qty1;
            Input2 = input2;
            Qty2 = qty2;
            CraftChancePermille = craftChancePermille;
            CraftChanceBonusPerTierPermille = craftChanceBonusPerTierPermille;
        }

        public int EffectiveCraftChancePermille(byte industryTier) =>
            CraftChancePermille >= 1000
                ? 1000
                : Math.Min(RecipeDatabase.MaxScaledChancePermille,
                    CraftChancePermille + Math.Max(0, industryTier - 1) * CraftChanceBonusPerTierPermille);
    }

    /// <summary>Validated, read-only lookup of the 20 item definitions and the 10 refined-item recipes.</summary>
    public sealed class RecipeDatabase
    {
        public const int MaxScaledChancePermille = 990;

        private readonly ItemDefinition[] _items = new ItemDefinition[ItemIds.Count];
        private readonly Recipe[] _recipes = new Recipe[ItemIds.Count];
        private readonly bool[] _hasRecipe = new bool[ItemIds.Count];

        public RecipeDatabase(ItemDefinition[] items, Recipe[] recipes)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));
            if (items.Length != ItemIds.Count)
                throw new ArgumentException($"Expected {ItemIds.Count} item definitions, got {items.Length}.", nameof(items));

            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item.Id != ItemIds.FromSlot(i))
                    throw new ArgumentException($"Item definition {i} is {item.Id}, expected {ItemIds.FromSlot(i)}.", nameof(items));
                if (item.BaseWtoPrice <= 0)
                    throw new ArgumentException($"{item.Id} needs a positive base price.", nameof(items));
                if (item.HasPreferredEsi != ItemIds.IsRaw(item.Id))
                    throw new ArgumentException($"{item.Id}: only raw items have a preferred ESI range.", nameof(items));
                if (item.HasPreferredEsi &&
                    (item.PreferredEsiMinMilli < 0 || item.PreferredEsiMaxMilli > 1000 ||
                     item.PreferredEsiMinMilli > item.PreferredEsiMaxMilli))
                    throw new ArgumentException($"{item.Id} has an invalid ESI range.", nameof(items));
                if (item.MiningChancePermille < 1 || item.MiningChancePermille > 1000 || item.MiningChanceBonusPerTierPermille < 0)
                    throw new ArgumentOutOfRangeException(nameof(items), $"{item.Id} mining chance must be in 1..1000 with non-negative tier bonus.");
                _items[i] = item;
            }

            for (int i = 0; i < recipes.Length; i++)
            {
                var recipe = recipes[i];
                if (!ItemIds.IsRefined(recipe.Output))
                    throw new ArgumentException($"Recipe output {recipe.Output} must be a refined item.", nameof(recipes));
                if (!ItemIds.IsRaw(recipe.Input1) || !ItemIds.IsRaw(recipe.Input2) || recipe.Input1 == recipe.Input2)
                    throw new ArgumentException($"Recipe for {recipe.Output} needs two different raw inputs.", nameof(recipes));
                if (recipe.Qty1 <= 0 || recipe.Qty2 <= 0 || recipe.OutputQty <= 0)
                    throw new ArgumentException($"Recipe for {recipe.Output} needs positive quantities.", nameof(recipes));
                if (recipe.CraftChancePermille < 1 || recipe.CraftChancePermille > 1000 || recipe.CraftChanceBonusPerTierPermille < 0)
                    throw new ArgumentOutOfRangeException(nameof(recipes), $"Recipe for {recipe.Output} craft chance must be in 1..1000 with non-negative tier bonus.");

                int slot = ItemIds.ToSlot(recipe.Output);
                if (_hasRecipe[slot])
                    throw new ArgumentException($"Duplicate recipe for {recipe.Output}.", nameof(recipes));
                _recipes[slot] = recipe;
                _hasRecipe[slot] = true;
            }

            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                if (ItemIds.IsRefined(ItemIds.FromSlot(slot)) && !_hasRecipe[slot])
                    throw new ArgumentException($"Missing recipe for {ItemIds.FromSlot(slot)}.", nameof(recipes));
            }
        }

        public ref readonly ItemDefinition GetItem(ItemId id)
        {
            if (!ItemIds.IsValid(id)) throw new ArgumentOutOfRangeException(nameof(id), id, "Not a valid item.");
            return ref _items[ItemIds.ToSlot(id)];
        }

        public ref readonly Recipe GetRecipe(ItemId output)
        {
            if (!ItemIds.IsRefined(output))
                throw new ArgumentOutOfRangeException(nameof(output), output, "Only refined items have recipes.");
            return ref _recipes[ItemIds.ToSlot(output)];
        }
    }
}
