using System;
using Unruled.Core.Board;
using Unruled.Core.Economy;

namespace Unruled.Core.Simulation
{
    /// <summary>All balance numbers the simulation reads. Built once (see <see cref="DefaultGameBalance"/>) and passed in.</summary>
    public sealed class GameBalanceConfig
    {
        public readonly RecipeDatabase Recipes;
        public readonly UniverseGenBalance Universe;

        public GameBalanceConfig(RecipeDatabase recipes, UniverseGenBalance universe)
        {
            Recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        }
    }
}
