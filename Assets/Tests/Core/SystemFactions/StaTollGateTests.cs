using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.SystemFactions
{
    public class StaTollGateTests
    {
        private const int Me = 1;
        private const int Stranger = 9;

        private SimulationWorld _sim;
        private EntityId _hq;
        private StarlaneEdge _highway;
        private StarlaneEdge _bypass;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 3);
            _hq = _sim.CreatePlayer(Me, default);
            _highway = new StarlaneEdge { Tier = StarlaneTier.StaHighway, Length = 250 };
            _bypass = new StarlaneEdge { Tier = StarlaneTier.OuterRimBypass, Length = 250 };
        }

        private static FleetUnit Fleet(int owner, int ships) =>
            new FleetUnit { OwnerPlayerId = owner, TechTier = 1, LineShips = ships };

        [Test]
        public void TollCents_IsShipsTimesRateTimesLength_LessTheBracketDiscount()
        {
            int rate = _sim.Balance.Sta.TollCentsPerShipPer100Units;
            long full = 3L * rate * 250 / 100;
            Assert.AreEqual(full, _sim.Tolls.TollCents(Fleet(Me, 3), _highway, 0));
            Assert.AreEqual(full * 90 / 100, _sim.Tolls.TollCents(Fleet(Me, 3), _highway, 10));
            Assert.AreEqual(0, _sim.Tolls.TollCents(Fleet(Me, 3), _bypass, 0), "only STA highways charge");
        }

        [Test]
        public void TryCharge_PaysTheStaPool_WithTheOwnersBracketDiscount()
        {
            long start = _sim.Ledger.Balance(_hq);
            long unrated = _sim.Tolls.TollCents(Fleet(Me, 4), _highway, 0);
            Assert.IsTrue(_sim.Tolls.TryCharge(Fleet(Me, 4), _highway));
            Assert.AreEqual(start - unrated, _sim.Ledger.Balance(_hq));
            Assert.AreEqual(unrated, _sim.Ledger.Balance(_sim.StaTollPool));

            _sim.Standings.Get(_hq).Bracket = 6;
            long apex = _sim.Tolls.TollCents(Fleet(Me, 4), _highway,
                _sim.Balance.Leaderboard.Bracket(6).TollDiscountPercent);
            Assert.Less(apex, unrated);
            Assert.IsTrue(_sim.Tolls.TryCharge(Fleet(Me, 4), _highway));
            Assert.AreEqual(unrated + apex, _sim.Ledger.Balance(_sim.StaTollPool));
        }

        [Test]
        public void TryCharge_Refuses_WithoutAWallet_OrMoney_ButBypassIsFree()
        {
            Assert.IsFalse(_sim.Tolls.TryCharge(Fleet(Stranger, 1), _highway), "no wallet");
            Assert.IsTrue(_sim.Tolls.TryCharge(Fleet(Stranger, 1), _bypass), "bypass needs no wallet");

            var huge = Fleet(Me, 1_000_000);
            long before = _sim.Ledger.Balance(_hq);
            Assert.IsFalse(_sim.Tolls.TryCharge(huge, _highway), "can't afford");
            Assert.AreEqual(before, _sim.Ledger.Balance(_hq), "nothing paid on refusal");
            Assert.AreEqual(0, _sim.Ledger.Balance(_sim.StaTollPool));
        }
    }
}
