using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Economy
{
    public class SiteBuildTests
    {
        private const int Player = 1;

        private SimulationWorld _sim;
        private EntityId _core;
        private EntityId _deposit;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 4);
            _sim.GenerateSystem(31, clusterId: 0);
            _core = _sim.Sites.EntityAt(0);
            for (int i = 0; i < _sim.Sites.Count; i++)
                _sim.Stockpiles.Add(_sim.Sites.EntityAt(i), default);
            _sim.Sites.Get(_core).OwnerPlayerId = Player;
            _sim.CreatePlayer(Player, _core);
            _deposit = _sim.World.Create();
            _sim.Deposits.Add(_deposit, new MineralDeposit { Site = _core, RawItem = ItemId.RawIron, RigCount = 8 });
        }

        private ref Stockpile Core => ref _sim.Stockpiles.Get(_core);

        private void Order(CommandType type, EntityId target, int a, int b = 0)
        {
            var command = new DelayedPlayerCommand { PlayerId = Player, CommandType = type, Target = target, ParamA = a, ParamB = b };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            for (long t = _sim.Clock.CurrentTick; t < at; t++)
                _sim.Clock.Advance(1);
        }

        private int RefineriesAtCore()
        {
            int n = 0;
            for (int i = 0; i < _sim.Refineries.Count; i++)
                if (_sim.Refineries.At(i).Site == _core)
                    n++;
            return n;
        }

        [Test]
        public void BuildRig_PaysPerRig_UpToTheCap()
        {
            Core.Add(ItemId.ItemSteel, 100);
            Core.Add(ItemId.ItemCoolant, 100);

            Order(CommandType.BuildRig, _deposit, 3);
            Assert.AreEqual(8, _sim.Deposits.Get(_deposit).RigCount, "11 rigs is over the cap of 10");

            Order(CommandType.BuildRig, _deposit, 2);
            Assert.AreEqual(10, _sim.Deposits.Get(_deposit).RigCount);
            Assert.AreEqual(80, Core.Get(ItemId.ItemSteel));
        }

        [Test]
        public void BuildRig_Unaffordable_OrOnARivalSite_DoesNothing()
        {
            Core.Add(ItemId.ItemSteel, 5);
            Core.Add(ItemId.ItemCoolant, 100);
            Order(CommandType.BuildRig, _deposit, 1);
            Assert.AreEqual(8, _sim.Deposits.Get(_deposit).RigCount, "unaffordable");

            Core.Add(ItemId.ItemSteel, 100);
            _sim.Sites.Get(_core).OwnerPlayerId = 2;
            Order(CommandType.BuildRig, _deposit, 1);
            Assert.AreEqual(8, _sim.Deposits.Get(_deposit).RigCount, "not the owner");
        }

        [Test]
        public void BuildRefinery_AddsARefineryForTheItem_UpToTheCap()
        {
            Core.Add(ItemId.ItemSteel, 200);
            Core.Add(ItemId.ItemBattery, 100);
            int before = RefineriesAtCore();
            int cap = _sim.Balance.Refinery.MaxRefineriesPerSite;

            Order(CommandType.BuildRefinery, _core, (int)ItemId.ItemOxygen);
            Assert.AreEqual(before + 1, RefineriesAtCore());
            Assert.AreEqual(180, Core.Get(ItemId.ItemSteel));
            Assert.AreEqual(ItemId.ItemOxygen, _sim.Refineries.At(_sim.Refineries.Count - 1).Output);
            Assert.AreEqual(_sim.Balance.Refinery.DefaultStockCap, _sim.Refineries.At(_sim.Refineries.Count - 1).StockCap);

            for (int i = before + 1; i < cap + 1; i++)
                Order(CommandType.BuildRefinery, _core, (int)ItemId.ItemSteel);
            Assert.AreEqual(cap, RefineriesAtCore());
        }

        [Test]
        public void BuildRefinery_ForARawItem_OrUnaffordable_DoesNothing()
        {
            Core.Add(ItemId.ItemSteel, 200);
            Order(CommandType.BuildRefinery, _core, (int)ItemId.RawIron);
            Order(CommandType.BuildRefinery, _core, (int)ItemId.ItemOxygen);
            Assert.AreEqual(0, RefineriesAtCore());
            Assert.AreEqual(200, Core.Get(ItemId.ItemSteel));
        }

        [Test]
        public void SetRefineryCap_ChangesTheCapOfThatItemsRefineries_AtAnOwnSite()
        {
            Core.Add(ItemId.ItemSteel, 200);
            Core.Add(ItemId.ItemBattery, 100);
            Order(CommandType.BuildRefinery, _core, (int)ItemId.ItemOxygen);
            Order(CommandType.BuildRefinery, _core, (int)ItemId.ItemFuel);
            var oxygen = _sim.Refineries.EntityAt(_sim.Refineries.Count - 2);
            var fuel = _sim.Refineries.EntityAt(_sim.Refineries.Count - 1);

            Order(CommandType.SetRefineryCap, _core, (int)ItemId.ItemOxygen, 0);
            Assert.AreEqual(0, _sim.Refineries.Get(oxygen).StockCap, "no cap");
            Assert.AreEqual(_sim.Balance.Refinery.DefaultStockCap, _sim.Refineries.Get(fuel).StockCap);

            Order(CommandType.SetRefineryCap, _core, (int)ItemId.ItemOxygen, -1);
            Assert.AreEqual(0, _sim.Refineries.Get(oxygen).StockCap, "a negative cap is refused");

            _sim.Sites.Get(_core).OwnerPlayerId = 2;
            Order(CommandType.SetRefineryCap, _core, (int)ItemId.ItemFuel, 7);
            Assert.AreEqual(_sim.Balance.Refinery.DefaultStockCap, _sim.Refineries.Get(fuel).StockCap, "not the owner");
        }
    }
}
