using System;
using NUnit.Framework;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Economy
{
    public class RecipeDatabaseTests
    {
        [Test]
        public void Default_HasRecipeForEveryRefinedItem_FromTwoRawInputs()
        {
            var db = DefaultGameBalance.CreateRecipeDatabase();
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var id = ItemIds.FromSlot(slot);
                Assert.AreEqual(id, db.GetItem(id).Id);
                if (!ItemIds.IsRefined(id))
                    continue;

                ref readonly var recipe = ref db.GetRecipe(id);
                Assert.AreEqual(id, recipe.Output);
                Assert.IsTrue(ItemIds.IsRaw(recipe.Input1));
                Assert.IsTrue(ItemIds.IsRaw(recipe.Input2));
            }
        }

        [Test]
        public void GetRecipe_RejectsRawItems()
        {
            var db = DefaultGameBalance.CreateRecipeDatabase();
            Assert.Throws<ArgumentOutOfRangeException>(() => db.GetRecipe(ItemId.RawWater));
        }

        [Test]
        public void Constructor_RejectsMissingDuplicateAndInvalidRecipes()
        {
            var valid = DefaultGameBalance.CreateRecipeDatabase();
            var items = new ItemDefinition[ItemIds.Count];
            var recipes = new Recipe[ItemIds.Count - ItemIds.RawCount];
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var id = ItemIds.FromSlot(slot);
                items[slot] = valid.GetItem(id);
                if (ItemIds.IsRefined(id))
                    recipes[slot - ItemIds.RawCount] = valid.GetRecipe(id);
            }

            Assert.DoesNotThrow(() => new RecipeDatabase(items, recipes));
            Assert.Throws<ArgumentException>(() => new RecipeDatabase(items, recipes[..^1]));

            var duplicate = (Recipe[])recipes.Clone();
            duplicate[1] = duplicate[0];
            Assert.Throws<ArgumentException>(() => new RecipeDatabase(items, duplicate));

            var refinedInput = (Recipe[])recipes.Clone();
            refinedInput[0] = new Recipe(ItemId.ItemOxygen, ItemId.ItemFuel, 1, ItemId.RawWater, 1);
            Assert.Throws<ArgumentException>(() => new RecipeDatabase(items, refinedInput));

            var badItems = (ItemDefinition[])items.Clone();
            badItems[0] = new ItemDefinition(ItemId.RawWater, 10);
            Assert.Throws<ArgumentException>(() => new RecipeDatabase(badItems, recipes));
        }
    }
}
