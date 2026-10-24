using NUnit.Framework;
using Unruled.Core.Galaxy;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    public class JoinGameTests
    {
        private SimulationWorld _sim;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 77);
            NewGameSetup.Create(_sim, 1);
            for (int i = 0; i < SimTick.TicksPerHour; i++)
            {
                _sim.Clock.Advance(1);
                _sim.Ai.Think(_sim.Clock.CurrentTick);
            }
        }

        private int Participants => 1 + _sim.Balance.NewGame.RivalCount;

        [Test]
        public void Join_MidGame_GivesTheNextPlayerAHomeAKitAGateToTheHubAndCharts()
        {
            int clustersBefore = _sim.Galaxy.NextClusterId();
            var joined = NewGameSetup.Join(_sim);

            ref var player = ref _sim.Players.Get(joined.PlayerHq);
            Assert.AreEqual(Participants + 1, player.PlayerId, "the next free id");
            int home = _sim.Sites.Get(joined.CoreBase).ClusterId;
            Assert.AreEqual(clustersBefore, home, "a new system");
            Assert.AreEqual(player.PlayerId, _sim.Sites.Get(joined.CoreBase).OwnerPlayerId);
            Assert.AreEqual(player.PlayerId, _sim.Fleets.Get(joined.Fleet).OwnerPlayerId);
            Assert.IsTrue(_sim.Ledger.TryFindPlayerWallet(player.PlayerId, out _));

            int hub = NewGameSetup.ContestedClusterId(Participants);
            bool gate = false;
            for (int g = 0; g < _sim.JumpGates.Count; g++)
            {
                ref var j = ref _sim.JumpGates.At(g);
                if (j.OwnerPlayerId != player.PlayerId)
                    continue;
                gate = true;
                Assert.AreEqual(home, _sim.Sites.Get(j.HomeSite).ClusterId);
                Assert.AreEqual(hub, _sim.Sites.Get(j.FarSite).ClusterId);
                Assert.IsFalse(_sim.Citadels.Has(j.FarSite), "never on the Citadel");
            }
            Assert.IsTrue(gate, "a dormant gate to the hub");

            Assert.IsTrue(_sim.Galaxy.Knows(player.PlayerId, home));
            Assert.IsTrue(_sim.Galaxy.Knows(player.PlayerId, hub));
            Assert.IsFalse(_sim.Galaxy.Knows(player.PlayerId, 0), "not a rival's home");
            Assert.AreEqual(clustersBefore + 1 + _sim.Balance.NewGame.UninhabitedClustersPerParticipant,
                _sim.Galaxy.NextClusterId(), "its home and its frontiers");
            for (int c = home + 1; c < _sim.Galaxy.NextClusterId(); c++)
                Assert.IsTrue(_sim.Galaxy.Knows(player.PlayerId, c), $"its frontier {c}");
        }

        [Test]
        public void Join_TwoPlayers_GetDifferentHomeStars()
        {
            var a = NewGameSetup.Join(_sim);
            var b = NewGameSetup.Join(_sim);
            Assert.AreNotEqual(_sim.Sites.Get(a.CoreBase).ClusterId, _sim.Sites.Get(b.CoreBase).ClusterId);
            var seen = new System.Collections.Generic.HashSet<long>();
            for (int i = 0; i < _sim.StarSystems.Count; i++)
                Assert.IsTrue(seen.Add(_sim.StarSystems.At(i).StarId), "no star is used twice");
        }

        [Test]
        public void Join_AsABot_TheAiPlaysIt_AndTheGameKeepsTicking()
        {
            var joined = NewGameSetup.Join(_sim, archetype: 2);
            int id = _sim.Players.Get(joined.PlayerHq).PlayerId;
            Assert.IsTrue(_sim.AiControllers.Has(joined.PlayerHq));
            bool ordered = false;
            for (int i = 0; i < 2 * SimTick.TicksPerHour; i++)
            {
                _sim.Clock.Advance(1);
                _sim.Ai.Think(_sim.Clock.CurrentTick);
                for (int o = 0; o < _sim.Ai.OrderCount; o++)
                    ordered |= _sim.Ai.OrderAt(o).PlayerId == id;
            }
            Assert.IsTrue(ordered, "the AI issues orders for the joined bot");
            int sites = 0;
            for (int i = 0; i < _sim.Sites.Count; i++)
                if (_sim.Sites.At(i).OwnerPlayerId == id)
                    sites++;
            Assert.GreaterOrEqual(sites, 1, "still holds its core base");
            Assert.IsTrue(_sim.Fleets.Has(joined.Fleet));
        }

        [Test]
        public void Join_IsDeterministic_AndSurvivesASaveRoundTrip()
        {
            var joined = NewGameSetup.Join(_sim);
            int home = _sim.Sites.Get(joined.CoreBase).ClusterId;
            int id = _sim.Players.Get(joined.PlayerHq).PlayerId;

            var again = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 77);
            NewGameSetup.Create(again, 1);
            for (int i = 0; i < SimTick.TicksPerHour; i++)
            {
                again.Clock.Advance(1);
                again.Ai.Think(again.Clock.CurrentTick);
            }
            var twin = NewGameSetup.Join(again);
            Assert.AreEqual(_sim.Galaxy.SystemOf(home).IsNull, again.Galaxy.SystemOf(home).IsNull);
            Assert.AreEqual(_sim.StarSystems.Get(_sim.Galaxy.SystemOf(home)).StarId,
                again.StarSystems.Get(again.Galaxy.SystemOf(_sim.Sites.Get(twin.CoreBase).ClusterId)).StarId);

            var loaded = SaveGame.Load(DefaultGameBalance.Create(), SaveGame.Write(_sim));
            Assert.IsTrue(loaded.Galaxy.Knows(id, home));
            Assert.IsTrue(loaded.Ledger.TryFindPlayerWallet(id, out _));
            loaded.Clock.Advance(10);
        }

        [Test]
        public void Join_PastAHubsGroupSize_OpensANewHubWithItsOwnCitadel()
        {
            int perHub = _sim.Balance.Galaxy.PlayersPerHub;
            int citadels = _sim.Citadels.Count;
            int joins = 0;
            while (_sim.Citadels.Count == citadels)
            {
                NewGameSetup.Join(_sim, 1);
                Assert.LessOrEqual(++joins, perHub + 1, "a full hub hands newcomers to a new one");
            }
            var newHub = _sim.Citadels.EntityAt(_sim.Citadels.Count - 1);
            int newCluster = _sim.Sites.Get(newHub).ClusterId;
            var last = _sim.Players.At(_sim.Players.Count - 1).PlayerId;
            bool gated = false;
            for (int g = 0; g < _sim.JumpGates.Count; g++)
                if (_sim.JumpGates.At(g).OwnerPlayerId == last)
                    gated |= _sim.Sites.Get(_sim.JumpGates.At(g).FarSite).ClusterId == newCluster;
            Assert.IsTrue(gated, "the newcomer gates into the new hub");
            Assert.IsTrue(_sim.Galaxy.Knows(last, newCluster));

            for (int t = 0; t < SimTick.TicksPerHour; t++)
            {
                _sim.Clock.Advance(1);
                _sim.Ai.Think(_sim.Clock.CurrentTick);
            }
        }
    }
}
