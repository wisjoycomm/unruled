using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Runs every refinery once per cycle (GDD §2.2 A): the site holds its stock cap of the output -> IdleFull (idle
    /// upkeep); starved inputs -> IdleStarved (idle upkeep);
    /// geomagnetic storm without spare batteries -> GridBrownout (idle upkeep); otherwise one Leontief batch.
    /// Upkeep is added to <see cref="Refinery.AccruedUpkeepCents"/>, then the whole debt is paid from the site owner's wallet
    /// if it can be (all or nothing). An unowned site, or an owner without a wallet, keeps the debt.
    /// BuildRefinery (target: a site the player owns; ParamA = the refined item to make) pays the build cost from the
    /// site's stock and adds a refinery with the default stock cap, up to the cap per site. SetRefineryCap (target: a site
    /// the player owns; ParamA = the refined item; ParamB = the stock cap, 0 = none) sets the cap of every refinery there
    /// making that item. Invalid or unaffordable orders change nothing.
    /// </summary>
    public sealed class RefinerySystem : ISimSystem, ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<Refinery> _refineries;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteWeather> _weather;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly Ledger _ledger;
        private readonly RecipeDatabase _recipes;
        private readonly RefineryBalance _balance;

        public RefinerySystem(
            World world, ComponentStore<Refinery> refineries, ComponentStore<Stockpile> stockpiles,
            ComponentStore<SiteWeather> weather, ComponentStore<SiteInfo> sites, Ledger ledger, RecipeDatabase recipes,
            RefineryBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _refineries = refineries ?? throw new ArgumentNullException(nameof(refineries));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _weather = weather ?? throw new ArgumentNullException(nameof(weather));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
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
                if (refinery.StockCap > 0 && stock.Get(refinery.Output) >= refinery.StockCap)
                {
                    refinery.State = RefineryState.IdleFull;
                    refinery.AccruedUpkeepCents += _balance.IdleUpkeepCentsPerCycle;
                    PayUpkeep(ref refinery);
                    continue;
                }

                int batches = Math.Min(_balance.MaxBatchesPerCycle,
                    Math.Min(stock.Get(recipe.Input1) / recipe.Qty1, stock.Get(recipe.Input2) / recipe.Qty2));
                if (batches <= 0)
                {
                    refinery.State = RefineryState.IdleStarved;
                    refinery.AccruedUpkeepCents += _balance.IdleUpkeepCentsPerCycle;
                    PayUpkeep(ref refinery);
                    continue;
                }

                bool storm = _weather.Has(refinery.Site) &&
                             _weather.Get(refinery.Site).GeomagneticStormLevel >= _balance.BrownoutMinGeoLevel;
                if (storm)
                {
                    if (!stock.Has(ItemId.ItemBattery, _balance.BatteryPerBrownoutCycle))
                    {
                        refinery.State = RefineryState.GridBrownout;
                        refinery.AccruedUpkeepCents += _balance.IdleUpkeepCentsPerCycle;
                        PayUpkeep(ref refinery);
                        continue;
                    }
                    stock.Add(ItemId.ItemBattery, -_balance.BatteryPerBrownoutCycle);
                }

                stock.Add(recipe.Input1, -recipe.Qty1 * batches);
                stock.Add(recipe.Input2, -recipe.Qty2 * batches);
                stock.Add(recipe.Output, recipe.OutputQty * batches);
                refinery.State = RefineryState.ActiveProduction;
                refinery.AccruedUpkeepCents += _balance.BaseUpkeepCentsPerCycle;
                PayUpkeep(ref refinery);
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.BuildRefinery && command.CommandType != CommandType.SetRefineryCap)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var site = command.Target;
            var output = (ItemId)command.ParamA;
            if (command.ParamA < 0 || command.ParamA > byte.MaxValue || !ItemIds.IsRefined(output) ||
                !_sites.Has(site) || _sites.Get(site).OwnerPlayerId != command.PlayerId || !_stockpiles.Has(site))
                return;
            if (command.CommandType == CommandType.SetRefineryCap)
            {
                if (command.ParamB < 0)
                    return;
                for (int i = 0; i < _refineries.Count; i++)
                    if (_refineries.At(i).Site == site && _refineries.At(i).Output == output)
                        _refineries.At(i).StockCap = command.ParamB;
                return;
            }
            int built = 0;
            for (int i = 0; i < _refineries.Count; i++)
                if (_refineries.At(i).Site == site)
                    built++;
            if (built >= _balance.MaxRefineriesPerSite)
                return;
            ref var stock = ref _stockpiles.Get(site);
            for (int i = 0; i < _balance.BuildCostCount; i++)
                if (!stock.Has(_balance.BuildCost(i).Item, _balance.BuildCost(i).Quantity))
                    return;
            for (int i = 0; i < _balance.BuildCostCount; i++)
                stock.Add(_balance.BuildCost(i).Item, -_balance.BuildCost(i).Quantity);
            _refineries.Add(_world.Create(), new Refinery { Site = site, Output = output, StockCap = _balance.DefaultStockCap });
        }

        private void PayUpkeep(ref Refinery refinery)
        {
            if (refinery.AccruedUpkeepCents == 0 || !_sites.Has(refinery.Site))
                return;
            if (!_ledger.TryFindPlayerWallet(_sites.Get(refinery.Site).OwnerPlayerId, out var wallet))
                return;
            if (_ledger.Burn(wallet, refinery.AccruedUpkeepCents))
                refinery.AccruedUpkeepCents = 0;
        }
    }
}
