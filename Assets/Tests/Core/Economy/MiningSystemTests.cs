using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Research;

namespace Unruled.Tests.Core.Economy
{
    public class MiningSystemTests
    {
        private World _world;
        private ComponentStore<SiteInfo> _sites;
        private ComponentStore<Stockpile> _stockpiles;
        private ComponentStore<MineralDeposit> _deposits;
        private MiningBalance _balance;
        private MiningSystem _system;
        private PlanetLifecycleSystem _lifecycle;
        private EntityId _site;
        private long _tick;

        private const int StartEsi = 800;

        private FixedTechGates _tech;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _sites = _world.CreateStore<SiteInfo>();
            _stockpiles = _world.CreateStore<Stockpile>();
            _deposits = _world.CreateStore<MineralDeposit>();
            _balance = DefaultGameBalance.CreateMiningBalance();
            _tech = new FixedTechGates(5);
            _system = new MiningSystem(_deposits, _sites, _stockpiles, _balance, _tech);
            _lifecycle = new PlanetLifecycleSystem(
                _sites, _deposits, _stockpiles, _balance, DefaultGameBalance.CreateLifecycleBalance(), _tech);
            _tick = 0;

            _site = _world.Create();
            _sites.Add(_site, new SiteInfo
            {
                BaseEsiMilli = StartEsi,
                CurrentEsiMilli = StartEsi,
                LifecycleState = PlanetLifecycleState.PristineWorld
            });
            _stockpiles.Add(_site, default);
            Stock.Add(ItemId.ItemFuel, 1000);
            Stock.Add(ItemId.ItemCoolant, 1000);
        }

        private ref Stockpile Stock => ref _stockpiles.Get(_site);
        private ref SiteInfo Site => ref _sites.Get(_site);

        private EntityId AddDeposit(int surface, int deep, int rigs = 1, ItemId raw = ItemId.RawIron)
        {
            var deposit = _world.Create();
            _deposits.Add(deposit, new MineralDeposit
            {
                Site = _site, RawItem = raw, SurfaceReserve = surface, DeepCrustReserve = deep, RigCount = rigs
            });
            return deposit;
        }

        private void RunCycles(int cycles)
        {
            for (int c = 0; c < cycles; c++)
            {
                for (int t = 0; t < _balance.CycleTicks; t++)
                {
                    _system.OnTick(++_tick);
                    _lifecycle.OnTick(_tick);
                }
            }
        }

        private int Yield(MiningDepth depth, int rigs) => rigs * _balance.YieldPerRigPerCycle * depth.SpeedPercent / 100;
        private int Fuel(MiningDepth depth, int rigs) => rigs * _balance.FuelPerRigPerCycle * depth.FuelCoolantMultiplier;

        [Test]
        public void Pristine_MinesSurfaceAtFullSpeed_PaysBaseFuelAndCoolant()
        {
            var deposit = AddDeposit(surface: 1000, deep: 1000, rigs: 2);

            RunCycles(3);

            int expected = 3 * Yield(_balance.Surface, 2);
            Assert.AreEqual(expected, Stock.Get(ItemId.RawIron));
            Assert.AreEqual(1000 - expected, _deposits.Get(deposit).SurfaceReserve);
            Assert.AreEqual(1000 - 3 * Fuel(_balance.Surface, 2), Stock.Get(ItemId.ItemFuel));
            Assert.AreEqual(Stock.Get(ItemId.ItemFuel), Stock.Get(ItemId.ItemCoolant));
            Assert.AreEqual(PlanetLifecycleState.PristineWorld, Site.LifecycleState);
            Assert.AreEqual(StartEsi, Site.CurrentEsiMilli);
        }

        [Test]
        public void NoMiningBetweenCycles()
        {
            AddDeposit(surface: 100, deep: 0);
            for (int t = 1; t < _balance.CycleTicks; t++)
                _system.OnTick(t);
            Assert.AreEqual(0, Stock.Get(ItemId.RawIron));
        }

        [Test]
        public void LastSurfaceOre_IsPartialYield_ThenSiteEntersDeepCoreStripping()
        {
            int surface = Yield(_balance.Surface, 1) + 3;
            var deposit = AddDeposit(surface: surface, deep: 1000);

            RunCycles(2);

            Assert.AreEqual(surface, Stock.Get(ItemId.RawIron), "second cycle only takes the 3 left; no spill into deep");
            Assert.AreEqual(1000, _deposits.Get(deposit).DeepCrustReserve);
            Assert.AreEqual(PlanetLifecycleState.DeepCoreStripping, Site.LifecycleState);
            Assert.AreEqual(StartEsi - _balance.DeepCoreEsiDropMilli, Site.CurrentEsiMilli);
        }

        [Test]
        public void DeepCrust_MinesAtHalfSpeed_DoubleCost_EsiDropsOnlyOnce()
        {
            AddDeposit(surface: 0, deep: 1000);

            RunCycles(3);

            Assert.AreEqual(3 * Yield(_balance.DeepCrust, 1), Stock.Get(ItemId.RawIron));
            Assert.AreEqual(1000 - 3 * Fuel(_balance.DeepCrust, 1), Stock.Get(ItemId.ItemFuel));
            Assert.AreEqual(PlanetLifecycleState.DeepCoreStripping, Site.LifecycleState);
            Assert.AreEqual(StartEsi - _balance.DeepCoreEsiDropMilli, Site.CurrentEsiMilli);
        }

        [Test]
        public void AllReservesGone_BecomesBarrenHusk_StopsBurningFuel()
        {
            AddDeposit(surface: 0, deep: Yield(_balance.DeepCrust, 1));

            RunCycles(1);
            Assert.AreEqual(PlanetLifecycleState.DepletedBarrenHusk, Site.LifecycleState);
            Assert.AreEqual(StartEsi - _balance.DeepCoreEsiDropMilli - _balance.BarrenHuskEsiDropMilli, Site.CurrentEsiMilli);
            int fuelAfterLastOre = Stock.Get(ItemId.ItemFuel);

            RunCycles(5);
            Assert.AreEqual(fuelAfterLastOre, Stock.Get(ItemId.ItemFuel));
            Assert.AreEqual(PlanetLifecycleState.DepletedBarrenHusk, Site.LifecycleState);
            Assert.AreEqual(1, _sites.Count, "husk sites stay on the board");
        }

        [Test]
        public void SiteStaysPristine_WhileAnyDepositHasSurface()
        {
            AddDeposit(surface: 0, deep: 1000, raw: ItemId.RawIron);
            AddDeposit(surface: 1000, deep: 1000, raw: ItemId.RawCopper);

            RunCycles(2);

            Assert.AreEqual(PlanetLifecycleState.PristineWorld, Site.LifecycleState);
            Assert.AreEqual(2 * Yield(_balance.DeepCrust, 1), Stock.Get(ItemId.RawIron));
            Assert.AreEqual(2 * Yield(_balance.Surface, 1), Stock.Get(ItemId.RawCopper));
        }

        [Test]
        public void MissingFuelOrCoolant_StallsDeposit()
        {
            var deposit = AddDeposit(surface: 100, deep: 0);
            Stock.Add(ItemId.ItemCoolant, -1000);

            RunCycles(2);

            Assert.AreEqual(0, Stock.Get(ItemId.RawIron));
            Assert.AreEqual(100, _deposits.Get(deposit).SurfaceReserve);
            Assert.AreEqual(1000, Stock.Get(ItemId.ItemFuel));
        }

        [Test]
        public void CoreFracture_OnlyMinedWhenUnlocked_AtQuadrupleCost()
        {
            var deposit = AddDeposit(surface: 0, deep: 0);
            _deposits.Get(deposit).CoreFractureReserve = 1000;
            Site.LifecycleState = PlanetLifecycleState.IrradiatedDeadWorld;

            RunCycles(1);
            Assert.AreEqual(0, Stock.Get(ItemId.RawIron));

            _deposits.Get(deposit).IsCoreFractureUnlocked = true;
            RunCycles(1);

            Assert.AreEqual(Yield(_balance.CoreFracture, 1), Stock.Get(ItemId.RawIron));
            Assert.AreEqual(1000 - Fuel(_balance.CoreFracture, 1), Stock.Get(ItemId.ItemFuel));
            Assert.AreEqual(PlanetLifecycleState.IrradiatedDeadWorld, Site.LifecycleState);
        }

        [Test]
        public void CoreFracture_NeedsTheOwnersTierFourGate()
        {
            var deposit = AddDeposit(surface: 0, deep: 0);
            _deposits.Get(deposit).CoreFractureReserve = 1000;
            _deposits.Get(deposit).IsCoreFractureUnlocked = true;
            Site.LifecycleState = PlanetLifecycleState.IrradiatedDeadWorld;
            _tech.Tier = 3;

            RunCycles(1);
            Assert.AreEqual(0, Stock.Get(ItemId.RawIron));

            _tech.Tier = 4;
            RunCycles(1);
            Assert.AreEqual(Yield(_balance.CoreFracture, 1), Stock.Get(ItemId.RawIron));
        }

        [Test]
        public void RetractDrills_StopsMiningAtOnce_AndRecoversTheRigsAfterTheRetractTime()
        {
            Site.OwnerPlayerId = 1;
            var deposit = AddDeposit(surface: 0, deep: 1000, rigs: 2);
            RunCycles(1);
            int mined = Stock.Get(ItemId.RawIron);
            Assert.Greater(mined, 0);

            var order = new DelayedPlayerCommand { PlayerId = 2, CommandType = CommandType.RetractDrills, Target = deposit };
            _system.Handle(order, _tick);
            Assert.AreEqual(0, _deposits.Get(deposit).RetractUntilTick, "only the site owner can retract");

            order.PlayerId = 1;
            _system.Handle(order, _tick);
            long done = _tick + _balance.DrillRetractTicks;
            Assert.AreEqual(done, _deposits.Get(deposit).RetractUntilTick);

            RunCycles(_balance.DrillRetractTicks / _balance.CycleTicks - 1);
            Assert.AreEqual(mined, Stock.Get(ItemId.RawIron), "retracting drills mine nothing");
            Assert.AreEqual(2, _deposits.Get(deposit).RigCount, "rigs are still in the ground");

            var before = new int[_balance.RigCostCount];
            for (int i = 0; i < before.Length; i++)
                before[i] = Stock.Get(_balance.RigCost(i).Item);
            RunCycles(1);
            Assert.AreEqual(done, _tick);
            Assert.AreEqual(0, _deposits.Get(deposit).RigCount);
            Assert.AreEqual(0, _deposits.Get(deposit).RetractUntilTick);
            for (int i = 0; i < _balance.RigCostCount; i++)
                Assert.AreEqual(before[i] + _balance.RigCost(i).Quantity * 2, Stock.Get(_balance.RigCost(i).Item), "the rigs' cost is back");
        }

        [Test]
        public void PauseDrills_StopsMiningAndBurn_UntilResumed()
        {
            Site.OwnerPlayerId = 1;
            var deposit = AddDeposit(surface: 1000, deep: 0, rigs: 2);
            int fuel = Stock.Get(ItemId.ItemFuel);
            var order = new DelayedPlayerCommand { PlayerId = 1, CommandType = CommandType.PauseDrills, Target = deposit, ParamA = 1 };
            _system.Handle(order, _tick);
            RunCycles(2);
            Assert.AreEqual(0, Stock.Get(ItemId.RawIron), "paused drills mine nothing");
            Assert.AreEqual(fuel, Stock.Get(ItemId.ItemFuel), "and burn nothing");
            Assert.AreEqual(2, _deposits.Get(deposit).RigCount);

            order.ParamA = 0;
            _system.Handle(order, _tick);
            RunCycles(1);
            Assert.Greater(Stock.Get(ItemId.RawIron), 0);
        }

        [Test]
        public void SiteWithoutDeposits_KeepsItsState()
        {
            RunCycles(3);
            Assert.AreEqual(PlanetLifecycleState.PristineWorld, Site.LifecycleState);
        }

        [Test]
        public void RareOre_RollsMiningChanceEachCycle_AlwaysBurnsFuelAndCoolant_AndScalesWithIndustryTierUpTo99Percent()
        {
            var recipes = DefaultGameBalance.CreateRecipeDatabase();
            Assert.AreEqual(600, recipes.GetItem(ItemId.RawTitanium).EffectiveMiningChancePermille(1));
            Assert.AreEqual(900, recipes.GetItem(ItemId.RawTitanium).EffectiveMiningChancePermille(4));
            Assert.AreEqual(990, recipes.GetItem(ItemId.RawTitanium).EffectiveMiningChancePermille(5), "capped at 99%");

            _system = new MiningSystem(_deposits, _sites, _stockpiles, _balance, _tech, recipes, worldSeed: 42);
            var deposit = AddDeposit(surface: 10_000, deep: 0, rigs: 1, raw: ItemId.RawUranium);

            RunCycles(50);

            int minedTier1 = Stock.Get(ItemId.RawUranium);
            Assert.Greater(minedTier1, 0, "some cycles strike uranium");
            Assert.Less(minedTier1, 50 * Yield(_balance.Surface, 1), "10% chance skips most cycles");
            Assert.AreEqual(10_000 - minedTier1, _deposits.Get(deposit).SurfaceReserve);
            Assert.AreEqual(1000 - 50 * Fuel(_balance.Surface, 1), Stock.Get(ItemId.ItemFuel), "drills always burn fuel");
        }
    }
}
