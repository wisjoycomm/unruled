using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Runs every refinery once per cycle (GDD §2.2 A): starved inputs -> IdleStarved (idle upkeep);
    /// geomagnetic storm without spare batteries -> GridBrownout (idle upkeep); otherwise one Leontief batch.
    /// </summary>
    public sealed class RefinerySystem : ISimSystem
    {
        private readonly ComponentStore<Refinery> _refineries;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteWeather> _weather;
        private readonly RecipeDatabase _recipes;
        private readonly RefineryBalance _balance;

        public RefinerySystem(
            ComponentStore<Refinery> refineries, ComponentStore<Stockpile> stockpiles,
            ComponentStore<SiteWeather> weather, RecipeDatabase recipes, RefineryBalance balance)
        {
            _refineries = refineries ?? throw new ArgumentNullException(nameof(refineries));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _weather = weather ?? throw new ArgumentNullException(nameof(weather));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.CycleTicks != 0)
                return;

            for (int i = 0; i < _refineries.Count; i++)
            {
                ref var refinery = ref _refineries.At(i);
                ref var stock = ref _stockpiles.Get(refinery.Site);
                ref readonly var recipe = ref _recipes.GetRecipe(refinery.Output);

                if (!stock.Has(recipe.Input1, recipe.Qty1) || !stock.Has(recipe.Input2, recipe.Qty2))
                {
                    refinery.State = RefineryState.IdleStarved;
                    refinery.AccruedUpkeep += _balance.IdleUpkeepPerCycle;
                    continue;
                }

                bool storm = _weather.Has(refinery.Site) &&
                             _weather.Get(refinery.Site).GeomagneticStormLevel >= _balance.BrownoutMinGeoLevel;
                if (storm)
                {
                    if (!stock.Has(ItemId.ItemBattery, _balance.BatteryPerBrownoutCycle))
                    {
                        refinery.State = RefineryState.GridBrownout;
                        refinery.AccruedUpkeep += _balance.IdleUpkeepPerCycle;
                        continue;
                    }
                    stock.Add(ItemId.ItemBattery, -_balance.BatteryPerBrownoutCycle);
                }

                stock.Add(recipe.Input1, -recipe.Qty1);
                stock.Add(recipe.Input2, -recipe.Qty2);
                stock.Add(recipe.Output, recipe.OutputQty);
                refinery.State = RefineryState.ActiveProduction;
                refinery.AccruedUpkeep += _balance.BaseUpkeepPerCycle;
            }
        }
    }
}
