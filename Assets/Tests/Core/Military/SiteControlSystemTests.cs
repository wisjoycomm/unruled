using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    public class SiteControlSystemTests
    {
        private const int Player = 1;
        private const int Rival = 2;

        private SimulationWorld _sim;
        private EntityId _core;
        private EntityId _target;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 4);
            _sim.GenerateSystem(31, clusterId: 0);
            _core = _sim.Sites.EntityAt(0);
            _target = _sim.Sites.EntityAt(1);
            for (int i = 0; i < _sim.Sites.Count; i++)
                _sim.Stockpiles.Add(_sim.Sites.EntityAt(i), default);
            _sim.Sites.Get(_core).OwnerPlayerId = Player;
            _sim.CreatePlayer(Player, _core);
        }

        private void Run(long ticks)
        {
            for (long i = 0; i < ticks; i++)
                _sim.Clock.Advance(1);
        }

        private void Claim(EntityId site)
        {
            var command = new DelayedPlayerCommand { PlayerId = Player, CommandType = CommandType.ClaimSite, Target = site };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            Run(at - _sim.Clock.CurrentTick);
        }

        private EntityId LoadedFleet(int owner, EntityId site)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = owner, TechTier = 1, LineShips = 2 }, site);
            ref var hold = ref _sim.Stockpiles.Get(fleet);
            hold.Add(ItemId.ItemSteel, 30);
            hold.Add(ItemId.ItemBattery, 10);
            return fleet;
        }

        [Test]
        public void Claim_NeutralSite_WithALoadedFleetDocked_TakesItAndUnloadsTheCost()
        {
            var fleet = LoadedFleet(Player, _target);
            Claim(_target);

            Assert.AreEqual(Player, _sim.Sites.Get(_target).OwnerPlayerId);
            Assert.AreEqual(20, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemSteel));
            Assert.AreEqual(5, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemBattery));
        }

        [Test]
        public void Claim_WithoutAFleetThere_OrWithAnEmptyHold_DoesNothing()
        {
            Claim(_target);
            Assert.AreEqual(0, _sim.Sites.Get(_target).OwnerPlayerId, "no fleet");

            _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 2 }, _target);
            Claim(_target);
            Assert.AreEqual(0, _sim.Sites.Get(_target).OwnerPlayerId, "empty hold");
        }

        [Test]
        public void Claim_RivalSite_OnlyWhileUndefended_AndNeverACoreBase()
        {
            _sim.Sites.Get(_target).OwnerPlayerId = Rival;
            LoadedFleet(Player, _target);
            var guard = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Rival, TechTier = 1, LineShips = 1 }, _target);
            _sim.FleetReadiness.Get(guard).IsMothballed = true;
            Claim(_target);
            Assert.AreEqual(Rival, _sim.Sites.Get(_target).OwnerPlayerId, "defended");

            _sim.Fleets.Get(guard).LineShips = 0;
            Claim(_target);
            Assert.AreEqual(Player, _sim.Sites.Get(_target).OwnerPlayerId, "undefended");

            var rivalCore = _sim.Sites.EntityAt(2);
            _sim.Sites.Get(rivalCore).OwnerPlayerId = Rival;
            _sim.CreatePlayer(Rival, rivalCore);
            LoadedFleet(Player, rivalCore);
            Claim(rivalCore);
            Assert.AreEqual(Rival, _sim.Sites.Get(rivalCore).OwnerPlayerId, "core base");
        }

        [Test]
        public void Claim_SystemSite_DoesNothing()
        {
            _sim.Sites.Get(_target).OwnerPlayerId = -1;
            LoadedFleet(Player, _target);
            Claim(_target);
            Assert.AreEqual(-1, _sim.Sites.Get(_target).OwnerPlayerId);
        }
    }
}
