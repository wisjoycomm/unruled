using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    public class ShipyardSystemTests
    {
        private const int Player = 1;

        private SimulationWorld _sim;
        private EntityId _hq;
        private EntityId _core;
        private EntityId _other;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 4);
            _sim.GenerateSystem(31, clusterId: 0);
            _core = _sim.Sites.EntityAt(0);
            _other = _sim.Sites.EntityAt(1);
            for (int i = 0; i < _sim.Sites.Count; i++)
                _sim.Stockpiles.Add(_sim.Sites.EntityAt(i), default);
            _sim.Sites.Get(_core).OwnerPlayerId = Player;
            _hq = _sim.CreatePlayer(Player, _core);
        }

        private ref Stockpile Core => ref _sim.Stockpiles.Get(_core);

        private void Run(long ticks)
        {
            for (long i = 0; i < ticks; i++)
                _sim.Clock.Advance(1);
        }

        private void Order(CommandType type, EntityId target, int a = 0, int b = 0, int c = 0, EntityId with = default)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = Player, CommandType = type, Target = target, ParamA = a, ParamB = b, ParamC = c
            };
            if (!with.IsNull)
                command.ParamEntity = with;
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            Run(at - _sim.Clock.CurrentTick);
        }

        private int FleetCount()
        {
            int n = 0;
            for (int i = 0; i < _sim.Fleets.Count; i++)
                if (_sim.Fleets.At(i).OwnerPlayerId == Player)
                    n++;
            return n;
        }

        [Test]
        public void BuildShips_PaysUpfront_DeliversANewFleetAfterTheBuildTime()
        {
            Core.Add(ItemId.ItemSteel, 100);
            Order(CommandType.BuildShips, _core, (int)TargetFocusClass.LineShips, 4, 1);

            Assert.AreEqual(80, Core.Get(ItemId.ItemSteel), "5 steel per tier-1 ship");
            Assert.AreEqual(1, _sim.ShipOrders.Count);
            Assert.AreEqual(0, FleetCount());

            Run(4 * _sim.Balance.Shipyard.BuildTicksPerShip(1));
            Assert.AreEqual(0, _sim.ShipOrders.Count);
            Assert.AreEqual(1, FleetCount());
            ref var fleet = ref _sim.Fleets.At(0);
            Assert.AreEqual(4, fleet.LineShips);
            Assert.AreEqual(1, fleet.TechTier);
            Assert.AreEqual(_core, _sim.FleetMotions.Get(_sim.Fleets.EntityAt(0)).Site);
        }

        [Test]
        public void BuildShips_JoinsADockedFleetOfTheSameTier()
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 3 }, _core);
            Core.Add(ItemId.ItemSteel, 100);
            Order(CommandType.BuildShips, _core, (int)TargetFocusClass.EwShips, 2, 1);
            Run(2 * _sim.Balance.Shipyard.BuildTicksPerShip(1));

            Assert.AreEqual(1, FleetCount());
            Assert.AreEqual(2, _sim.Fleets.Get(fleet).EwShips);
            Assert.AreEqual(3, _sim.Fleets.Get(fleet).LineShips);
        }

        [Test]
        public void BuildShips_AboveTheResearchTier_OrUnaffordable_OrAwayFromTheCore_DoesNothing()
        {
            Core.Add(ItemId.ItemSteel, 100);
            Core.Add(ItemId.ItemBattery, 100);
            Order(CommandType.BuildShips, _core, (int)TargetFocusClass.LineShips, 2, 2);
            Assert.AreEqual(0, _sim.ShipOrders.Count, "tier 2 not researched");

            Order(CommandType.BuildShips, _core, (int)TargetFocusClass.LineShips, 21, 1);
            Assert.AreEqual(0, _sim.ShipOrders.Count, "105 steel needed");

            _sim.Stockpiles.Get(_other).Add(ItemId.ItemSteel, 100);
            Order(CommandType.BuildShips, _other, (int)TargetFocusClass.LineShips, 1, 1);
            Assert.AreEqual(0, _sim.ShipOrders.Count, "not the core base");
            Assert.AreEqual(100, Core.Get(ItemId.ItemSteel));
        }

        [Test]
        public void Retrofit_RaisesADockedFleetToTheResearchTier_ForHalfTheBuildCost()
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 3 }, _core);
            _sim.TechProgress.Get(_hq).TechTier = 2;
            Core.Add(ItemId.ItemSteel, 100);
            Core.Add(ItemId.ItemBattery, 100);

            Order(CommandType.Retrofit, fleet);

            Assert.AreEqual(2, _sim.Fleets.Get(fleet).TechTier);
            Assert.AreEqual(100 - 9, Core.Get(ItemId.ItemSteel), "3 ships × 6 steel × 50%");
            Assert.AreEqual(100 - 3, Core.Get(ItemId.ItemBattery), "3 ships × 2 batteries × 50%");
        }

        [Test]
        public void Retrofit_AwayFromTheCoreOrAlreadyCurrent_DoesNothing()
        {
            var away = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 3 }, _other);
            var current = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 2, LineShips = 3 }, _core);
            _sim.TechProgress.Get(_hq).TechTier = 2;
            Core.Add(ItemId.ItemSteel, 100);
            Core.Add(ItemId.ItemBattery, 100);

            Order(CommandType.Retrofit, away);
            Order(CommandType.Retrofit, current);

            Assert.AreEqual(1, _sim.Fleets.Get(away).TechTier);
            Assert.AreEqual(100, Core.Get(ItemId.ItemSteel));
        }

        [Test]
        public void MergeFleets_TheTargetTakesInTheOthersShipsAndHold_StolenTagsIncluded()
        {
            var main = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 3, LineShips = 40 }, _core);
            var extra = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 3, LineShips = 20, EwShips = 2 }, _core);
            _sim.Stockpiles.Get(main).Add(ItemId.ItemAmmo, 100);
            _sim.Stockpiles.Get(extra).Add(ItemId.ItemAmmo, 50);
            _sim.StolenGoods.Add(extra, default);
            _sim.StolenGoods.Get(extra).Add(ItemId.ItemAmmo, 10);

            Order(CommandType.MergeFleets, main, with: extra);

            Assert.AreEqual(60, _sim.Fleets.Get(main).LineShips);
            Assert.AreEqual(2, _sim.Fleets.Get(main).EwShips);
            Assert.AreEqual(0, _sim.Fleets.Get(extra).TotalShips, "left empty, like a fleet that lost every ship");
            Assert.AreEqual(150, _sim.Stockpiles.Get(main).Get(ItemId.ItemAmmo));
            Assert.AreEqual(0, _sim.Stockpiles.Get(extra).Get(ItemId.ItemAmmo));
            Assert.AreEqual(10, _sim.StolenGoods.Get(main).Get(ItemId.ItemAmmo));
        }

        [Test]
        public void MergeFleets_OfAnotherTier_AnotherSite_WithCargoConvoys_OrMothballed_DoesNothing()
        {
            var main = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 3, LineShips = 40 }, _core);
            var tier = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 2, LineShips = 5 }, _core);
            var away = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 3, LineShips = 5 }, _other);
            var cargo = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 3, LineShips = 5, CargoConvoys = 1 }, _core);
            var mothballed = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 3, LineShips = 5 }, _core);
            _sim.FleetReadiness.Get(mothballed).IsMothballed = true;

            Order(CommandType.MergeFleets, main, with: tier);
            Order(CommandType.MergeFleets, main, with: away);
            Order(CommandType.MergeFleets, main, with: cargo);
            Order(CommandType.MergeFleets, main, with: mothballed);
            Order(CommandType.MergeFleets, main, with: main);

            Assert.AreEqual(40, _sim.Fleets.Get(main).LineShips);
        }
    }
}
