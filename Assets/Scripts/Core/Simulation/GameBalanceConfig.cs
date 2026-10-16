using System;
using Unruled.Core.Economy;

namespace Unruled.Core.Simulation
{
    /// <summary>All balance numbers the simulation reads. Built once (see <see cref="DefaultGameBalance"/>) and passed in.</summary>
    public sealed class GameBalanceConfig
    {
        public readonly RecipeDatabase Recipes;

        public GameBalanceConfig(RecipeDatabase recipes)
        {
            Recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        }
    }
}
