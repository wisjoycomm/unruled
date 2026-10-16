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
            _system = new RefinerySystem(
                _refineries, _stockpiles, _weather, DefaultGameBalance.CreateRecipeDatabase(), _balance);

            _site = _world.Create();
            _stockpiles.Add(_site, default);
            _oxygenPlant = _world.Create();
            _refineries.Add(_oxygenPlant, new Refinery { Site = _site, Output = ItemId.ItemOxygen });
        }

        private ref Stockpile Stock => ref _stockpiles.Get(_site);
        private ref Refinery Plant => ref _refineries.Get(_oxygenPlant);

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
        public void ActiveProduction_ConsumesLeontiefInputs_AddsOutput_FullUpkeep()
        {
            Stock.Add(ItemId.RawWater, 5);
            Stock.Add(ItemId.RawNitrogen, 5);

            RunCycles(3);

            Assert.AreEqual(RefineryState.ActiveProduction, Plant.State);
            Assert.AreEqual(3, Stock.Get(ItemId.ItemOxygen));
            Assert.AreEqual(2, Stock.Get(ItemId.RawWater));
            Assert.AreEqual(2, Stock.Get(ItemId.RawNitrogen));
            Assert.AreEqual(3L * _balance.BaseUpkeepPerCycle, Plant.AccruedUpkeep);
        }

        [Test]
        public void IdleStarved_WhenAnyInputShort_ChargesIdleUpkeepOnly()
        {
            Stock.Add(ItemId.RawWater, 5);

            RunCycles(2);

            Assert.AreEqual(RefineryState.IdleStarved, Plant.State);
            Assert.AreEqual(0, Stock.Get(ItemId.ItemOxygen));
            Assert.AreEqual(5, Stock.Get(ItemId.RawWater));
            Assert.AreEqual(2L * _balance.IdleUpkeepPerCycle, Plant.AccruedUpkeep);
            Assert.AreEqual(_balance.BaseUpkeepPerCycle * 20 / 100, _balance.IdleUpkeepPerCycle);
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
            Assert.AreEqual(_balance.IdleUpkeepPerCycle, Plant.AccruedUpkeep);
        }

        [Test]
        public void GeoStorm_WithBattery_BurnsBatteryAndKeepsProducing()
        {
            Stock.Add(ItemId.RawWater, 5);
            Stock.Add(ItemId.RawNitrogen, 5);
            Stock.Add(ItemId.ItemBattery, 1);
            _weather.Add(_site, new SiteWeather { GeomagneticStormLevel = 5 });

            RunCycles(2);

            Assert.AreEqual(RefineryState.GridBrownout, Plant.State, "second cycle has no battery left");
            Assert.AreEqual(1, Stock.Get(ItemId.ItemOxygen));
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

        private sealed class NoCommands : ICommandHandler
        {
            public void Handle(in DelayedPlayerCommand command, long tick) =>
                Assert.Fail("No commands expected.");
        }
    }
}
