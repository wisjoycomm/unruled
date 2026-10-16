using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.SystemFactions
{
    public class VictoryEvaluatorTests
    {
        private const int Ally = 1;
        private const int Latecomer = 2;
        private const int Bystander = 3;

        private SimulationWorld _sim;
        private EntityId _citadel;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 4);
            for (int p = 1; p <= 3; p++)
                _sim.CreatePlayer(p, Site(p));
            _citadel = Site(0);
            _sim.Defense.CreateCitadel(_citadel);
            ClearGarrison();
        }

        private void ClearGarrison()
        {
            var garrison = _sim.Citadels.Get(_citadel).Garrison;
            if (_sim.World.IsAlive(garrison))
                _sim.World.Destroy(garrison);
        }

        private EntityId Site(int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private void Bombarding(int player)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = player, TechTier = 1, LineShips = 5 }, _citadel);
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemAmmo, 1_000);
        }

        [Test]
        public void NothingIsDecided_WhileTheCitadelStands()
        {
            Bombarding(Ally);
            _sim.Clock.Advance(_sim.Balance.Citadel.CheckIntervalTicks * 2);
            Assert.IsFalse(_sim.Victory.IsDecided(out _));
            Assert.IsFalse(_sim.Victory.HasSeat(Ally));
        }

        [Test]
        public void TheFall_SeatsEveryPlayerWhoBombardedWithinTheWindow()
        {
            Bombarding(Ally);
            _sim.Clock.Advance(_sim.Balance.Citadel.CheckIntervalTicks);
            Assert.AreEqual(1, _sim.CitadelAssaults.Count);

            _sim.Clock.Advance(_sim.Balance.Citadel.DirectorateWindowTicks / 2);
            Bombarding(Latecomer);
            _sim.Citadels.Get(_citadel).Integrity = 1;
            _sim.Clock.Advance(_sim.Balance.Citadel.CheckIntervalTicks);

            Assert.IsTrue(_sim.Victory.IsDecided(out long tick));
            Assert.AreEqual(_sim.Citadels.Get(_citadel).FallenTick, tick);
            Assert.IsTrue(_sim.Victory.HasSeat(Ally), "still bombarding when it fell");
            Assert.IsTrue(_sim.Victory.HasSeat(Latecomer));
            Assert.IsFalse(_sim.Victory.HasSeat(Bystander));
            Assert.AreEqual(2, _sim.VictoryRecords.Get(_citadel).Seats);
        }

        [Test]
        public void APlayerWhoStoppedBeforeTheWindow_GetsNoSeat()
        {
            _sim.Clock.Advance(1);
            var hq = _sim.Players.EntityAt(0);
            _sim.CitadelAssaults.Add(hq, new CitadelAssault { Damage = 500, LastHitTick = 1 });
            _sim.Clock.Advance(_sim.Balance.Citadel.DirectorateWindowTicks * 2);
            ClearGarrison();
            Bombarding(Latecomer);
            _sim.Citadels.Get(_citadel).Integrity = 1;
            _sim.Clock.Advance(_sim.Balance.Citadel.CheckIntervalTicks);

            Assert.IsTrue(_sim.Victory.IsDecided(out _));
            Assert.IsFalse(_sim.Victory.HasSeat(_sim.Players.At(0).PlayerId));
            Assert.IsTrue(_sim.Victory.HasSeat(Latecomer));
        }
    }
}
