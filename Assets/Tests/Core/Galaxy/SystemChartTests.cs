using NUnit.Framework;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Galaxy
{
    public class SystemChartTests
    {
        private SimulationWorld _sim;
        private NewGame _game;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 31);
            _game = NewGameSetup.Create(_sim, 1);
            _sim.Stockpiles.Get(_game.Fleet).Add(ItemId.ItemFuel, _sim.Balance.Galaxy.SurveyFuel * 4);
        }

        private int Participants => 1 + _sim.Balance.NewGame.RivalCount;
        private int HomeCluster => _sim.Sites.Get(_game.CoreBase).ClusterId;

        private void Survey(StarId star, int playerId, Unruled.Core.Ecs.EntityId fleet)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = CommandType.Survey, Target = fleet, ExecuteAtTick = _sim.Clock.CurrentTick + 1
            };
            StarId.Split(star.Packed, out command.ParamA, out command.ParamB);
            _sim.Clock.Enqueue(command);
            _sim.Clock.Advance(1);
        }

        [Test]
        public void NewGame_EachPlayerKnowsItsHomeTheHubAndItsFrontiers_NotRivalHomes()
        {
            int hub = NewGameSetup.ContestedClusterId(Participants);
            int perParticipant = _sim.Balance.NewGame.UninhabitedClustersPerParticipant;
            for (int p = 0; p < Participants; p++)
            {
                int player = 1 + p;
                Assert.IsTrue(_sim.Galaxy.Knows(player, p), $"player {player} knows its home");
                Assert.IsTrue(_sim.Galaxy.Knows(player, hub), $"player {player} knows the hub");
                for (int k = 0; k < perParticipant; k++)
                    Assert.IsTrue(_sim.Galaxy.Knows(player, NewGameSetup.FirstUninhabitedClusterId(Participants) + p * perParticipant + k));
                for (int q = 0; q < Participants; q++)
                    if (q != p)
                        Assert.IsFalse(_sim.Galaxy.Knows(player, q), $"player {player} doesn't know home {q}");
            }
            Assert.IsTrue(_sim.Galaxy.Knows(0, 0), "THE SYSTEM knows everything");
        }

        [Test]
        public void Survey_OfANewStar_ChartsItForTheSurveyorOnly_AndRecordsTheFirstSurveyor()
        {
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, HomeCluster, out var star));
            Survey(star, 1, _game.Fleet);

            Assert.IsTrue(_sim.Galaxy.TryGetCluster(star, out int cluster));
            Assert.IsTrue(_sim.Galaxy.Knows(1, cluster));
            Assert.IsFalse(_sim.Galaxy.Knows(2, cluster));
            Assert.AreEqual(1, _sim.StarSystems.Get(_sim.Galaxy.SystemOf(cluster)).FirstSurveyorPlayerId);
            Assert.IsFalse(_sim.Galaxy.CanSurvey(1, HomeCluster, star), "already known");
        }

        [Test]
        public void Survey_OfASystemSomeoneElseCharted_OnlyAddsItToTheChart_ForFuel()
        {
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, HomeCluster, out var star));
            Survey(star, 1, _game.Fleet);
            Assert.IsTrue(_sim.Galaxy.TryGetCluster(star, out int cluster));

            // A second player standing in the same system (as if it had flown here) re-charts it.
            var rivalFleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = 2, TechTier = 1, LineShips = 1 }, _game.CoreBase);
            _sim.Stockpiles.Get(rivalFleet).Add(ItemId.ItemFuel, _sim.Balance.Galaxy.SurveyFuel);
            int systems = _sim.StarSystems.Count;
            Assert.IsTrue(_sim.Galaxy.CanSurvey(2, HomeCluster, star));
            Survey(star, 2, rivalFleet);

            Assert.AreEqual(systems, _sim.StarSystems.Count, "no new system");
            Assert.IsTrue(_sim.Galaxy.Knows(2, cluster));
            Assert.AreEqual(0, _sim.Stockpiles.Get(rivalFleet).Get(ItemId.ItemFuel));
            Assert.AreEqual(1, _sim.StarSystems.Get(_sim.Galaxy.SystemOf(cluster)).FirstSurveyorPlayerId);
        }

        [Test]
        public void FleetArriving_TeachesItsOwnerTheSystem()
        {
            int unknown = -1;
            for (int c = 0; c < _sim.Galaxy.NextClusterId() && unknown < 0; c++)
                if (!_sim.Galaxy.Knows(1, c))
                    unknown = c;
            Assert.GreaterOrEqual(unknown, 0);
            _sim.Galaxy.FleetArrived(1, _sim.Galaxy.SiteAtOrbit(unknown, 0), _sim.Clock.CurrentTick);
            Assert.IsTrue(_sim.Galaxy.Knows(1, unknown));
            Assert.AreEqual(ChartSource.Arrival, FindChart(1, unknown).Source);
        }

        private SystemChart FindChart(int player, int cluster)
        {
            for (int i = 0; i < _sim.SystemCharts.Count; i++)
                if (_sim.SystemCharts.At(i).PlayerId == player && _sim.SystemCharts.At(i).ClusterId == cluster)
                    return _sim.SystemCharts.At(i);
            Assert.Fail($"no chart for player {player} cluster {cluster}");
            return default;
        }

        [Test]
        public void Charts_RoundTripThroughASave()
        {
            var balance = _sim.Balance;
            var loaded = SaveGame.Load(balance, SaveGame.Write(_sim));
            Assert.AreEqual(_sim.SystemCharts.Count, loaded.SystemCharts.Count);
            for (int c = 0; c < _sim.Galaxy.NextClusterId(); c++)
                for (int p = 1; p <= Participants; p++)
                    Assert.AreEqual(_sim.Galaxy.Knows(p, c), loaded.Galaxy.Knows(p, c));
            CollectionAssert.AreEqual(SaveGame.Write(_sim), SaveGame.Write(loaded));
        }

        // Migration: schema 6 had 40 stores and no charts; players learn what they hold, stand in or gate to, and every hub.
        [Test]
        public void Schema6Save_ChartsWhatEachPlayerHolds()
        {
            var balance = _sim.Balance;
            var loaded = SaveGame.Load(balance, SaveGame.Write(_sim, schemaVersion: 6, storeCount: 40));
            int hub = NewGameSetup.ContestedClusterId(Participants);
            for (int p = 0; p < Participants; p++)
            {
                Assert.IsTrue(loaded.Galaxy.Knows(1 + p, p), $"player {1 + p} knows its home");
                Assert.IsTrue(loaded.Galaxy.Knows(1 + p, hub), $"player {1 + p} knows the hub");
                for (int q = 0; q < Participants; q++)
                    if (q != p)
                        Assert.IsFalse(loaded.Galaxy.Knows(1 + p, q));
            }
            CollectionAssert.AreEqual(SaveGame.Write(loaded), SaveGame.Write(SaveGame.Load(balance, SaveGame.Write(loaded))),
                "resaved as the current schema");
        }
    }
}
