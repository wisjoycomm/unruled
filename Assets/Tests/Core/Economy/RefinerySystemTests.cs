using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Economy
{
    public class RefinerySystemTests
    {
        private World _world;
        private ComponentStore<Stockpile> _stockpiles;
        private ComponentStore<SiteWeather> _weather;
        private ComponentStore<Refinery> _refineries;
        private ComponentStore<SiteInfo> _sites;
        private ComponentStore<Wallet> _wallets;
        private Ledger _ledger;
        private RefineryBalance _balance;
        private RefinerySystem _system;
        private EntityId _site;
        private EntityId _oxygenPlant;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _stockpiles = _world.CreateStore<Stockpile>();
            _weather = _world.CreateStore<SiteWeather>();
            _refineries = _world.CreateStore<Refinery>();
            _balance = DefaultGameBalance.CreateRefineryBalance();
            _sites = _world.CreateStore<SiteInfo>();
            _wallets = _world.CreateStore<Wallet>();
            _ledger = new Ledger(_wallets);
            _system = new RefinerySystem(
                _world, _refineries, _stockpiles, _weather, _sites, _ledger, DefaultGameBalance.CreateRecipeDatabase(), _balance);

            _site = _world.Create();
            _stockpiles.Add(_site, default);
            _oxygenPlant = _world.Create();
            _refineries.Add(_oxygenPlant, new Refinery { Site = _site, Output = ItemId.ItemOxygen });
        }

        private ref Stockpile Stock => ref _stockpiles.Get(_site);
        private ref Refinery Plant => ref _refineries.Get(_oxygenPlant);

        // Upkeep cents charged over cycles first..last, active or idle.
        private long Due(int first, int last, bool idle)
        {
            long cents = 0;
            for (int c = first; c <= last; c++)
                cents += _balance.UpkeepCents(c, idle);
            return cents;
        }

        // The default balance with a whole-cent rate, so each cycle charges the same.
        private void UseWholeCentUpkeep(int centsPerCycle)
        {
            var d = DefaultGameBalance.CreateRefineryBalance();
            var cost = new ItemStack[d.BuildCostCount];
            for (int i = 0; i < cost.Length; i++)
                cost[i] = d.BuildCost(i);
            _balance = new RefineryBalance(d.CycleTicks, d.MaxBatchesPerCycle, centsPerCycle * 1_000, d.IdleUpkeepPercent,
                d.BrownoutMinGeoLevel, d.BatteryPerBrownoutCycle, d.MaxRefineriesPerSite, d.DefaultStockCap, cost);
            _system = new RefinerySystem(
                _world, _refineries, _stockpiles, _weather, _sites, _ledger, DefaultGameBalance.CreateRecipeDatabase(), _balance);
        }

        private void RunCycles(int cycles)
        {
            var clock = new SimClock(new NoCommands());
            clock.Register(_system);
            clock.Advance((long)cycles * _balance.CycleTicks);
        }

        [Test]
        public void OnlyRunsOnCycleTicks()
        {
            Stock.Add(ItemId.RawWater, 5);
            Stock.Add(ItemId.RawNitrogen, 5);
            for (long tick = 1; tick < _balance.CycleTicks; tick++)
                _system.OnTick(tick);

            Assert.AreEqual(RefineryState.NotStarted, Plant.State);
            Assert.AreEqual(0, Stock.Get(ItemId.ItemOxygen));
        }

        [Test]
        public void ActiveProduction_RunsUpToMaxBatches_ConsumesLeontiefInputs_FullUpkeep()
        {
            int max = _balance.MaxBatchesPerCycle;
            Stock.Add(ItemId.RawWater, 2 * max + 1);
            Stock.Add(ItemId.RawNitrogen, 2 * max + 5);

            RunCycles(3);

            Assert.AreEqual(RefineryState.ActiveProduction, Plant.State, "a partial batch count still runs");
            Assert.AreEqual(2 * max + 1, Stock.Get(ItemId.ItemOxygen));
            Assert.AreEqual(0, Stock.Get(ItemId.RawWater));
            Assert.AreEqual(4, Stock.Get(ItemId.RawNitrogen));
            Assert.AreEqual(Due(1, 3, idle: false), Plant.AccruedUpkeepCents);
        }

        [Test]
        public void IdleStarved_WhenAnyInputShort_ChargesIdleUpkeepOnly()
        {
            Stock.Add(ItemId.RawWater, 5);

            RunCycles(2);

            Assert.AreEqual(RefineryState.IdleStarved, Plant.State);
            Assert.AreEqual(0, Stock.Get(ItemId.ItemOxygen));
            Assert.AreEqual(5, Stock.Get(ItemId.RawWater));
            Assert.AreEqual(Due(1, 2, idle: true), Plant.AccruedUpkeepCents);
            Assert.AreEqual(_balance.BaseUpkeepMilliCentsPerCycle * 20 / 100, _balance.IdleUpkeepMilliCentsPerCycle);
        }

        [Test]
        public void StarvedThenResupplied_ReturnsToActive()
        {
            RunCycles(1);
            Assert.AreEqual(RefineryState.IdleStarved, Plant.State);

            Stock.Add(ItemId.RawWater, 1);
            Stock.Add(ItemId.RawNitrogen, 1);
            _system.OnTick(2L * _balance.CycleTicks);

            Assert.AreEqual(RefineryState.ActiveProduction, Plant.State);
            Assert.AreEqual(1, Stock.Get(ItemId.ItemOxygen));
        }

        [Test]
        public void GeoStorm_WithoutBattery_BrownsOut()
        {
            Stock.Add(ItemId.RawWater, 5);
            Stock.Add(ItemId.RawNitrogen, 5);
            _weather.Add(_site, new SiteWeather { GeomagneticStormLevel = (byte)_balance.BrownoutMinGeoLevel });

            RunCycles(1);

            Assert.AreEqual(RefineryState.GridBrownout, Plant.State);
            Assert.AreEqual(0, Stock.Get(ItemId.ItemOxygen));
            Assert.AreEqual(Due(1, 1, idle: true), Plant.AccruedUpkeepCents);
        }

        [Test]
        public void GeoStorm_WithBattery_BurnsBatteryAndKeepsProducing()
        {
            int max = _balance.MaxBatchesPerCycle;
            Stock.Add(ItemId.RawWater, 2 * max);
            Stock.Add(ItemId.RawNitrogen, 2 * max);
            Stock.Add(ItemId.ItemBattery, 1);
            _weather.Add(_site, new SiteWeather { GeomagneticStormLevel = 5 });

            RunCycles(2);

            Assert.AreEqual(RefineryState.GridBrownout, Plant.State, "second cycle has no battery left");
            Assert.AreEqual(max, Stock.Get(ItemId.ItemOxygen));
            Assert.AreEqual(0, Stock.Get(ItemId.ItemBattery));
        }

        [Test]
        public void MildStorm_BelowBrownoutLevel_DoesNotNeedBattery()
        {
            Stock.Add(ItemId.RawWater, 1);
            Stock.Add(ItemId.RawNitrogen, 1);
            _weather.Add(_site, new SiteWeather { GeomagneticStormLevel = (byte)(_balance.BrownoutMinGeoLevel - 1) });

            RunCycles(1);

            Assert.AreEqual(RefineryState.ActiveProduction, Plant.State);
        }

        [Test]
        public void StockCap_IdlesTheRefinery_WhileTheSiteHoldsThatMuchOutput()
        {
            Plant.StockCap = 2 * _balance.MaxBatchesPerCycle;
            Stock.Add(ItemId.RawWater, 100);
            Stock.Add(ItemId.RawNitrogen, 100);

            RunCycles(3);

            Assert.AreEqual(RefineryState.IdleFull, Plant.State);
            Assert.AreEqual(Plant.StockCap, Stock.Get(ItemId.ItemOxygen), "two cycles reach the cap, the third idles");
            Assert.AreEqual(100 - Plant.StockCap, Stock.Get(ItemId.RawWater));
            Assert.AreEqual(Due(1, 2, idle: false) + Due(3, 3, idle: true), Plant.AccruedUpkeepCents);
        }

        [Test]
        public void AFractionalCentRate_AddsUpToExactlyTheRate()
        {
            // 1,000 cycles at a rate in milli-cents: the rate, in cents.
            Assert.AreEqual(_balance.BaseUpkeepMilliCentsPerCycle, Due(0, 999, idle: false));
            Assert.AreEqual(_balance.IdleUpkeepMilliCentsPerCycle, Due(0, 999, idle: true));
            for (int c = 0; c < 1_000; c++)
                Assert.LessOrEqual(_balance.UpkeepCents(c, idle: false), (_balance.BaseUpkeepMilliCentsPerCycle + 999) / 1_000);
        }

        [Test]
        public void Upkeep_IsBurnedFromTheOwnersWallet_ElseCarriedAsDebt()
        {
            const int owner = 3;
            _sites.Add(_site, new SiteInfo { OwnerPlayerId = owner });
            var wallet = _world.Create();
            _wallets.Add(wallet, new Wallet { OwnerPlayerId = owner });
            UseWholeCentUpkeep(5);
            long cycleCents = 5;
            _ledger.Grant(wallet, cycleCents * 2 - 1);
            int max = _balance.MaxBatchesPerCycle;
            Stock.Add(ItemId.RawWater, 3 * max);
            Stock.Add(ItemId.RawNitrogen, 3 * max);

            RunCycles(1);
            Assert.AreEqual(0, Plant.AccruedUpkeepCents);
            Assert.AreEqual(cycleCents - 1, _ledger.Balance(wallet));

            _system.OnTick(2L * _balance.CycleTicks);
            Assert.AreEqual(cycleCents, Plant.AccruedUpkeepCents, "short by one cent: the cycle is owed");
            Assert.AreEqual(cycleCents - 1, _ledger.Balance(wallet), "nothing is paid partly");
            Assert.AreEqual(2 * max, Stock.Get(ItemId.ItemOxygen), "debt does not stop production");

            _ledger.Grant(wallet, cycleCents + 1);
            _system.OnTick(3L * _balance.CycleTicks);
            Assert.AreEqual(0, Plant.AccruedUpkeepCents, "the debt and the new cycle are paid together");
            Assert.AreEqual(0, _ledger.Balance(wallet));
        }

        [Test]
        public void HighTierRecipe_RollsCraftChancePerBatch_AlwaysConsumesInputs_AndScalesWithIndustryTierUpTo99Percent()
        {
            var recipes = DefaultGameBalance.CreateRecipeDatabase();
            Assert.AreEqual(700, recipes.GetRecipe(ItemId.ItemAlloy).EffectiveCraftChancePermille(1));
            Assert.AreEqual(900, recipes.GetRecipe(ItemId.ItemAlloy).EffectiveCraftChancePermille(3));
            Assert.AreEqual(990, recipes.GetRecipe(ItemId.ItemAlloy).EffectiveCraftChancePermille(4), "capped at 99%");
            Assert.AreEqual(990, recipes.GetRecipe(ItemId.ItemAlloy).EffectiveCraftChancePermille(5), "stays capped at 99%");
            Assert.AreEqual(990, recipes.GetRecipe(ItemId.ItemAmmo).EffectiveCraftChancePermille(5), "ammo reaches 99% at tier 5");

            Plant.Output = ItemId.ItemNuclearCore;
            Stock.Add(ItemId.RawUranium, 200);
            Stock.Add(ItemId.RawTungsten, 100);

            RunCycles(25);

            Assert.AreEqual(0, Stock.Get(ItemId.RawUranium), "all uranium consumed");
            Assert.AreEqual(0, Stock.Get(ItemId.RawTungsten), "all tungsten consumed");
            int cores = Stock.Get(ItemId.ItemNuclearCore);
            Assert.Greater(cores, 0, "some batches succeed");
            Assert.Less(cores, 100, "15% craft chance rejects most batches");
        }

        private sealed class NoCommands : ICommandHandler
        {
            public void Handle(in DelayedPlayerCommand command, long tick) =>
                Assert.Fail("No commands expected.");
        }
    }
}
