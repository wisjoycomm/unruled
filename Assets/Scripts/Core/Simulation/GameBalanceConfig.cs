using System;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Simulation
{
    /// <summary>All balance numbers the simulation reads. Built once (see <see cref="DefaultGameBalance"/>) and passed in.</summary>
    public sealed class GameBalanceConfig
    {
        public readonly RecipeDatabase Recipes;
        public readonly UniverseGenBalance Universe;
        public readonly RefineryBalance Refinery;
        public readonly MiningBalance Mining;
        public readonly LifecycleBalance Lifecycle;
        public readonly SystemFactionBalance SystemFactions;

        public GameBalanceConfig(
            RecipeDatabase recipes, UniverseGenBalance universe, RefineryBalance refinery, MiningBalance mining,
            LifecycleBalance lifecycle, SystemFactionBalance systemFactions)
        {
            Recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            Universe = universe ?? throw new ArgumentNullException(nameof(universe));
            Refinery = refinery ?? throw new ArgumentNullException(nameof(refinery));
            Mining = mining ?? throw new ArgumentNullException(nameof(mining));
            Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            SystemFactions = systemFactions ?? throw new ArgumentNullException(nameof(systemFactions));
        }
    }
}
