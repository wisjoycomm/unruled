using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;

namespace Unruled.Tests.Core.AI
{
    /// <summary>
    /// Map: my core base H (0,0) - T (200,0), a rival site, - S (400,0); and H - N (200,300) - S, a way round T.
    /// T's approaches from H and from S are 180° apart.
    /// </summary>
    public class AiFlankTests
    {
        private const int Me = 1;
        private const int Rival = 2;
        private const byte Raider = 3; // Stealth_Raid 1.9

        private SimulationWorld _sim;
        private EntityId _home, _target, _staging, _north, _hq;
        private EntityId _homeLane, _flankLane;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 2);
            _home = Site(0, 0, Me);
            _target = Site(200, 0, Rival);
            _staging = Site(400, 0, 0);
            _north = Site(200, 300, 0);
            _homeLane = Lane(_home, _target, 200);
            _flankLane = Lane(_target, _staging, 200);
            Lane(_home, _north, 360);
            Lane(_north, _staging, 360);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _hq = _sim.CreatePlayer(Me, _home);
        }

        private EntityId Site(int x, int y, int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner, PosX = x, PosY = y });
            _sim.Stockpiles.Add(site, default);
            _sim.Weather.Add(site, default);
            return site;
        }

        private EntityId Lane(EntityId a, EntityId b, int length)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = StarlaneTier.StaHighway, Length = length });
            _sim.LaneWeather.Add(lane, default);
            return lane;
        }

        private EntityId Fleet(int owner, EntityId site, int lineShips)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = owner, TechTier = 1, LineShips = lineShips }, site);
            ref var hold = ref _sim.Stockpiles.Get(fleet);
            hold.Add(ItemId.ItemAmmo, lineShips * _sim.Balance.Ai.LoadAmmoPerLineShip);
            hold.Add(ItemId.ItemFuel, _sim.Balance.Ai.LoadFuel);
            return fleet;
        }

        private int Think()
        {
            _sim.Ai.Assign(_hq, Raider);
            while (_sim.Clock.CurrentTick % _sim.Balance.Ai.ThinkIntervalTicks != 0)
                _sim.Clock.Advance(1);
            return _sim.Ai.Think(_sim.Clock.CurrentTick);
        }

        private AiOrder? Find(AiAction action, EntityId subject)
        {
            int n = Think();
            for (int i = 0; i < n; i++)
                if (_sim.Ai.OrderAt(i).Action == action && _sim.Ai.OrderAt(i).Subject == subject)
                    return _sim.Ai.OrderAt(i);
            return null;
        }

        private void RunPendingOrders()
        {
            for (int i = 0; i < 1_000 && _sim.Clock.PendingCommandCount > 0; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(0, _sim.Clock.PendingCommandCount, "orders arrived");
        }

        private EntityId OtherFleet(EntityId fleet)
        {
            for (int i = 0; i < _sim.Fleets.Count; i++)
                if (_sim.Fleets.At(i).OwnerPlayerId == Me && _sim.Fleets.EntityAt(i) != fleet)
                    return _sim.Fleets.EntityAt(i);
            return default;
        }

        [Test]
        public void ARaidOnADefendedSite_WithASecondApproach_SplitsTheFleet_ThenGoesInOnBothLanes()
        {
            var fleet = Fleet(Me, _home, 20);
            Fleet(Rival, _target, 8);

            var split = Find(AiAction.FlankSplit, fleet);
            Assert.IsTrue(split.HasValue);
            Assert.AreEqual(IssueResult.Issued, split.Value.Result);
            RunPendingOrders();
            var half = OtherFleet(fleet);
            Assert.IsFalse(half.IsNull);
            Assert.AreEqual(10, _sim.Fleets.Get(fleet).LineShips);
            Assert.AreEqual(10, _sim.Fleets.Get(half).LineShips);

            int n = Think();
            AiOrder? raid = null, flank = null;
            for (int i = 0; i < n; i++)
            {
                var order = _sim.Ai.OrderAt(i);
                if (order.Action == AiAction.StealthRaid && order.Subject == fleet) raid = order;
                if (order.Action == AiAction.FlankMove && order.Subject == half) flank = order;
                Assert.AreNotEqual(AiAction.FlankSplit, order.Action, "the halves don't split again");
            }
            Assert.IsTrue(raid.HasValue, "one half goes straight in");
            Assert.IsTrue(flank.HasValue, "the other takes the flank");

            bool passedTarget = false;
            for (int i = 0; i < 3_000 && _sim.FleetMotions.Get(half).ArrivalLane != _flankLane; i++)
            {
                _sim.Clock.Advance(1);
                if (_sim.Clock.CurrentTick % _sim.Balance.Ai.ThinkIntervalTicks == 0)
                    _sim.Ai.Think(_sim.Clock.CurrentTick);
                var at = _sim.FleetMotions.Get(half);
                passedTarget |= at.Lane.IsNull && at.Site == _target && at.ArrivalLane != _flankLane;
            }
            Assert.AreEqual(_flankLane, _sim.FleetMotions.Get(half).ArrivalLane, "came in on the flank lane");
            Assert.IsFalse(passedTarget, "round T by the north, not through it");
        }

        [Test]
        public void AtTheStagingSite_TheFleetGoesInAlongTheFlankLane_WhileItsPartnerIsBoundForTheTarget()
        {
            Fleet(Rival, _target, 2);
            var partner = Fleet(Me, _home, 10);
            var flanker = Fleet(Me, _staging, 10);
            var move = new DelayedPlayerCommand { PlayerId = Me, CommandType = CommandType.Move, Target = partner };
            move.ParamEntity = _target;
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(move, out _));
            RunPendingOrders();
            Assert.IsFalse(_sim.FleetMotions.Get(partner).Lane.IsNull, "the partner is on its way");

            var order = Find(AiAction.FlankMove, flanker);
            Assert.IsTrue(order.HasValue);
            Assert.AreEqual(IssueResult.Issued, order.Value.Result);
            RunPendingOrders();
            for (int i = 0; i < 2_000 && _sim.FleetMotions.Get(flanker).Site != _target; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(_flankLane, _sim.FleetMotions.Get(flanker).ArrivalLane, "came in on the flank lane");
        }

        [Test]
        public void AtTheStagingSite_WithoutAPartnerBoundForTheTarget_TheFleetDoesNotFlank()
        {
            Fleet(Rival, _target, 2);
            var flanker = Fleet(Me, _staging, 10);

            Assert.IsFalse(Find(AiAction.FlankMove, flanker).HasValue);
        }

        [Test]
        public void NoSplit_ForAFleetUnderTheFlankMinimum()
        {
            var fleet = Fleet(Me, _home, _sim.Balance.Ai.FlankMinLineShips - 1);
            Fleet(Rival, _target, 1);
            Assert.IsTrue(Find(AiAction.StealthRaid, fleet).HasValue);
        }

        [Test]
        public void NoSplit_WhenAHalfWouldBeOutgunned()
        {
            var fleet = Fleet(Me, _home, 20);
            Fleet(Rival, _target, 15);
            Assert.IsTrue(Find(AiAction.StealthRaid, fleet).HasValue);
        }

        [Test]
        public void NoSplit_ForATargetWithOneApproach()
        {
            _sim.Starlanes.Remove(_flankLane);
            _sim.Graph.Rebuild(_sim.Starlanes);
            var fleet = Fleet(Me, _home, 20);
            Fleet(Rival, _target, 2);
            Assert.IsTrue(Find(AiAction.StealthRaid, fleet).HasValue);
        }
    }
}
