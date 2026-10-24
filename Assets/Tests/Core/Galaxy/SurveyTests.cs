using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Galaxy
{
    public class SurveyTests
    {
        private SimulationWorld _sim;
        private NewGame _game;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 31);
            _game = NewGameSetup.Create(_sim, 1);
            ref var hold = ref _sim.Stockpiles.Get(_game.Fleet);
            hold.Add(ItemId.ItemFuel, _sim.Balance.Galaxy.SurveyFuel * 2);
        }

        private int HomeCluster => _sim.Sites.Get(_game.CoreBase).ClusterId;

        private void Survey(StarId star, int playerId = 1)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = CommandType.Survey, Target = _game.Fleet,
                ExecuteAtTick = _sim.Clock.CurrentTick + 1
            };
            StarId.Split(star.Packed, out command.ParamA, out command.ParamB);
            _sim.Clock.Enqueue(command);
            _sim.Clock.Advance(1);
        }

        [Test]
        public void SplitAndJoin_RoundTripAPackedId()
        {
            var id = new StarId(-1234, 987, 11);
            StarId.Split(id.Packed, out int low, out int high);
            Assert.IsTrue(StarId.TryFromPacked(StarId.Join(low, high), out var back));
            Assert.AreEqual(id, back);
            Assert.IsFalse(StarId.TryFromPacked(0, out _));
            Assert.IsFalse(StarId.TryFromPacked(-5, out _));
        }

        [Test]
        public void Survey_ChartsANeighbourFrontier_LinkedToTheHomeSystem_ForFuel()
        {
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, HomeCluster, out var star), "home has an uncharted frontier neighbour");
            int clusters = _sim.Galaxy.NextClusterId();
            int lanes = _sim.Starlanes.Count;
            int fuel = _sim.Stockpiles.Get(_game.Fleet).Get(ItemId.ItemFuel);

            Survey(star);

            Assert.IsTrue(_sim.Galaxy.TryGetCluster(star, out int cluster));
            Assert.AreEqual(clusters, cluster);
            Assert.AreEqual(fuel - _sim.Balance.Galaxy.SurveyFuel, _sim.Stockpiles.Get(_game.Fleet).Get(ItemId.ItemFuel));
            Assert.Greater(_sim.Starlanes.Count, lanes);
            var entry = _sim.Galaxy.SiteAtOrbit(cluster, 0);
            Assert.IsTrue(_sim.Graph.SameComponent(_game.CoreBase, entry), "the new system is reachable from home");
            int deposits = 0;
            for (int i = 0; i < _sim.Deposits.Count; i++)
                if (_sim.Sites.Get(_sim.Deposits.At(i).Site).ClusterId == cluster)
                    deposits++;
            Assert.Greater(deposits, 0, "the new system has ore");
            Assert.IsTrue(_sim.Weather.Has(entry));
            Assert.IsTrue(_sim.Stockpiles.Has(entry));
        }

        [Test]
        public void Survey_OfAFarStar_OrWithoutFuel_OrSomeoneElsesFleet_ChangesNothing()
        {
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, HomeCluster, out var near));
            var stars = new List<StarRecord>();
            _sim.Galaxy.Catalog.GetSector(0, 0, stars);
            int clusters = _sim.Galaxy.NextClusterId();

            Survey(stars[0].Id); // the galactic core is nowhere near the spawn ring
            Survey(near, playerId: 2);
            Assert.AreEqual(clusters, _sim.Galaxy.NextClusterId());

            ref var hold = ref _sim.Stockpiles.Get(_game.Fleet);
            hold.Add(ItemId.ItemFuel, -hold.Get(ItemId.ItemFuel));
            Survey(near);
            Assert.AreEqual(clusters, _sim.Galaxy.NextClusterId());
            Assert.IsFalse(_sim.Galaxy.IsMaterialised(near));
        }

        [Test]
        public void Survey_IsDeterministic_SameSeedSameNewSystem()
        {
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, HomeCluster, out var star));
            Survey(star);
            var other = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 31);
            var game = NewGameSetup.Create(other, 1);
            other.Stockpiles.Get(game.Fleet).Add(ItemId.ItemFuel, other.Balance.Galaxy.SurveyFuel);
            var command = new DelayedPlayerCommand
            {
                PlayerId = 1, CommandType = CommandType.Survey, Target = game.Fleet, ExecuteAtTick = 1
            };
            StarId.Split(star.Packed, out command.ParamA, out command.ParamB);
            other.Clock.Enqueue(command);
            other.Clock.Advance(1);

            Assert.AreEqual(_sim.Sites.Count, other.Sites.Count);
            Assert.AreEqual(_sim.Starlanes.Count, other.Starlanes.Count);
            Assert.AreEqual(_sim.Deposits.Count, other.Deposits.Count);
        }

        [Test]
        public void ConqueringAi_WithNothingLeftToTake_SurveysTheNextFrontier()
        {
            const byte warlord = 4; // Siege_Conquest 1.8
            var hq = _sim.Players.EntityAt(0);
            Assert.AreEqual(1, _sim.Players.At(0).PlayerId);
            for (int i = 0; i < _sim.Deposits.Count; i++)
            {
                ref var deposit = ref _sim.Deposits.At(i); // mined out: nothing worth besieging or developing
                deposit.SurfaceReserve = 0;
                deposit.DeepCrustReserve = 0;
                deposit.CoreFractureReserve = 0;
            }
            ref var hold = ref _sim.Stockpiles.Get(_game.Fleet);
            for (var id = ItemId.ItemOxygen; ItemIds.IsRefined(id); id++)
                hold.Add(id, 100_000); // a full hold: nothing left to load at home
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, HomeCluster, out var expected));

            _sim.Ai.Assign(hq, warlord);
            AiOrder? survey = null;
            // It first tops up its hold (each load waits out its latency), then, ready with nowhere to go, charts.
            for (int round = 0; round < 200 && survey == null; round++)
            {
                _sim.Clock.Advance(_sim.Balance.Ai.ThinkIntervalTicks - _sim.Clock.CurrentTick % _sim.Balance.Ai.ThinkIntervalTicks);
                int n = _sim.Ai.Think(_sim.Clock.CurrentTick);
                for (int i = 0; i < n; i++)
                    if (_sim.Ai.OrderAt(i).Action == AiAction.Survey && _sim.Ai.OrderAt(i).Subject == _game.Fleet)
                        survey = _sim.Ai.OrderAt(i);
            }
            Assert.IsTrue(survey.HasValue, "the warlord charts new space");
            Assert.AreEqual(IssueResult.Issued, survey.Value.Result);
            for (int i = 0; i < 1_000 && !_sim.Galaxy.IsMaterialised(expected); i++)
                _sim.Clock.Advance(1);
            Assert.IsTrue(_sim.Galaxy.IsMaterialised(expected));
        }

        [Test]
        public void NewGame_LinksEveryChartedHyperlane_SoNoSystemIsSealed()
        {
            var neighbours = new List<int>();
            int clusters = _sim.Galaxy.NextClusterId();
            for (int c = 0; c < clusters; c++)
            {
                Assert.IsTrue(_sim.Galaxy.TryGetStar(c, out var star));
                _sim.Galaxy.MaterialisedNeighbours(star, neighbours);
                for (int i = 0; i < neighbours.Count; i++)
                    Assert.IsTrue(DirectLane(c, neighbours[i]), $"hyperlane {c}-{neighbours[i]} is a lane");
            }
            int homeExits = 0;
            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                ref var lane = ref _sim.Starlanes.At(i);
                int a = _sim.Sites.Get(lane.SiteA).ClusterId, b = _sim.Sites.Get(lane.SiteB).ClusterId;
                if (a != b && (a == HomeCluster || b == HomeCluster))
                    homeExits++;
            }
            Assert.IsTrue(homeExits > 0 || _sim.Galaxy.TryPickSurvey(1, HomeCluster, out _), "home has a way out besides its gate");
        }

        private bool DirectLane(int clusterA, int clusterB)
        {
            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                ref var lane = ref _sim.Starlanes.At(i);
                int a = _sim.Sites.Get(lane.SiteA).ClusterId, b = _sim.Sites.Get(lane.SiteB).ClusterId;
                if ((a == clusterA && b == clusterB) || (a == clusterB && b == clusterA))
                    return true;
            }
            return false;
        }

        [Test]
        public void SiteIndex_FindsOrbitsOfEveryCluster()
        {
            for (int c = 0; c < _sim.Galaxy.NextClusterId(); c++)
            {
                var entry = _sim.Galaxy.SiteAtOrbit(c, 0);
                var outer = _sim.Galaxy.OuterOrbit(c);
                Assert.IsFalse(entry.IsNull, $"cluster {c} orbit 0");
                Assert.AreEqual(c, _sim.Sites.Get(outer).ClusterId);
                int planets = 0;
                for (int i = 0; i < _sim.Sites.Count; i++)
                    if (_sim.Sites.At(i).ClusterId == c)
                        planets++;
                Assert.AreEqual(planets - 1, _sim.Sites.Get(outer).OrbitIndex);
                Assert.That(planets, Is.InRange(_sim.Balance.Universe.MinSitesPerSystem, _sim.Balance.Universe.SitesPerSystem));
            }
        }
    }
}
