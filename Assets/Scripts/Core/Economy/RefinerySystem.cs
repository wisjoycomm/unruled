using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Research;
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
        private const ulong RollSalt = 0x524546494E455259UL;

        private readonly World _world;
        private readonly ComponentStore<Refinery> _refineries;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteWeather> _weather;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly Ledger _ledger;
        private readonly RecipeDatabase _recipes;
        private readonly RefineryBalance _balance;
        private readonly ITechGates _tech;
        private readonly ulong _worldSeed;

        public RefinerySystem(
            World world, ComponentStore<Refinery> refineries, ComponentStore<Stockpile> stockpiles,
            ComponentStore<SiteWeather> weather, ComponentStore<SiteInfo> sites, Ledger ledger, RecipeDatabase recipes,
            RefineryBalance balance, ulong worldSeed = 0, ITechGates tech = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _refineries = refineries ?? throw new ArgumentNullException(nameof(refineries));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _weather = weather ?? throw new ArgumentNullException(nameof(weather));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _tech = tech;
            _worldSeed = worldSeed;
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.CycleTicks != 0)
                return;

            long cycle = tick / _balance.CycleTicks;
            long idleCents = _balance.UpkeepCents(cycle, idle: true);
            long activeCents = _balance.UpkeepCents(cycle, idle: false);
            int remBatches = Math.Max(0, (60 - _balance.CycleTicks - (int)(tick % 60)) / _balance.CycleTicks) * _balance.MaxBatchesPerCycle;
            BeginTickReserve();

            for (int i = 0; i < _refineries.Count; i++)
            {
                ref var refinery = ref _refineries.At(i);
                ref var stock = ref _stockpiles.Get(refinery.Site);
                ref readonly var recipe = ref _recipes.GetRecipe(refinery.Output);
                if (refinery.StockCap > 0 && stock.Get(refinery.Output) >= refinery.StockCap)
                {
                    refinery.State = RefineryState.IdleFull;
                    refinery.AccruedUpkeepCents += idleCents;
                    PayUpkeep(ref refinery);
                    continue;
                }

                bool lifeline = refinery.Output == ItemId.ItemCoolant || refinery.Output == ItemId.ItemFuel;
                int avail1 = lifeline ? stock.Get(recipe.Input1) : Math.Max(0, stock.Get(recipe.Input1) - GetReserve(refinery.Site, recipe.Input1));
                int avail2 = lifeline ? stock.Get(recipe.Input2) : Math.Max(0, stock.Get(recipe.Input2) - GetReserve(refinery.Site, recipe.Input2));
                int batches = Math.Min(_balance.MaxBatchesPerCycle,
                    Math.Min(avail1 / recipe.Qty1, avail2 / recipe.Qty2));
                if (lifeline && remBatches > 0)
                {
                    AddReserve(refinery.Site, recipe.Input1, remBatches * recipe.Qty1);
                    AddReserve(refinery.Site, recipe.Input2, remBatches * recipe.Qty2);
                }
                if (batches <= 0)
                {
                    refinery.State = RefineryState.IdleStarved;
                    refinery.AccruedUpkeepCents += idleCents;
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
                        refinery.AccruedUpkeepCents += idleCents;
                        PayUpkeep(ref refinery);
                        continue;
                    }
                    stock.Add(ItemId.ItemBattery, -_balance.BatteryPerBrownoutCycle);
                }

                stock.Add(recipe.Input1, -recipe.Qty1 * batches);
                stock.Add(recipe.Input2, -recipe.Qty2 * batches);

                int succeeded = batches;
                byte industryTier = _tech != null && _sites.Has(refinery.Site)
                    ? _tech.TechTierOf(_sites.Get(refinery.Site).OwnerPlayerId, ResearchBranch.Industry)
                    : (byte)1;
                int chance = recipe.EffectiveCraftChancePermille(industryTier);
                if (chance < 1000)
                {
                    succeeded = 0;
                    var entity = _refineries.EntityAt(i);
                    for (int b = 0; b < batches; b++)
                    {
                        var rng = new SplitMix64(_worldSeed ^ RollSalt ^ ((ulong)(uint)entity.Index << 32) ^ (ulong)(tick + b));
                        if (rng.NextInt(0, 999) < chance)
                            succeeded++;
                    }
                }
                if (succeeded > 0)
                    stock.Add(recipe.Output, recipe.OutputQty * succeeded);

                refinery.State = RefineryState.ActiveProduction;
                refinery.AccruedUpkeepCents += activeCents;
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

        private int _reserveStamp;
        private int[] _siteReserveStamp = Array.Empty<int>();
        private int[] _siteReserveWater = Array.Empty<int>();
        private int[] _siteReserveNitrogen = Array.Empty<int>();
        private int[] _siteReserveMethane = Array.Empty<int>();

        private void BeginTickReserve()
        {
            if (++_reserveStamp == int.MaxValue)
            {
                Array.Clear(_siteReserveStamp, 0, _siteReserveStamp.Length);
                _reserveStamp = 1;
            }
        }

        private void AddReserve(EntityId site, ItemId item, int amount)
        {
            if (site.IsNull || amount <= 0)
                return;
            int s = site.Index;
            if (s >= _siteReserveStamp.Length)
            {
                int cap = Math.Max(s + 1, _siteReserveStamp.Length == 0 ? 64 : _siteReserveStamp.Length * 2);
                Array.Resize(ref _siteReserveStamp, cap);
                Array.Resize(ref _siteReserveWater, cap);
                Array.Resize(ref _siteReserveNitrogen, cap);
                Array.Resize(ref _siteReserveMethane, cap);
            }
            if (_siteReserveStamp[s] != _reserveStamp)
            {
                _siteReserveStamp[s] = _reserveStamp;
                _siteReserveWater[s] = 0;
                _siteReserveNitrogen[s] = 0;
                _siteReserveMethane[s] = 0;
            }
            if (item == ItemId.RawWater) _siteReserveWater[s] += amount;
            else if (item == ItemId.RawNitrogen) _siteReserveNitrogen[s] += amount;
            else if (item == ItemId.RawMethane) _siteReserveMethane[s] += amount;
        }

        private int GetReserve(EntityId site, ItemId item)
        {
            if (site.IsNull || site.Index >= _siteReserveStamp.Length || _siteReserveStamp[site.Index] != _reserveStamp)
                return 0;
            if (item == ItemId.RawWater) return _siteReserveWater[site.Index];
            if (item == ItemId.RawNitrogen) return _siteReserveNitrogen[site.Index];
            if (item == ItemId.RawMethane) return _siteReserveMethane[site.Index];
            return 0;
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
