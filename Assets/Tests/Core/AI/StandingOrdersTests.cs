using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.AI
{
    public class StandingOrdersTests
    {
        private SimulationWorld _sim;
        private NewGame _game;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), 77);
            _game = NewGameSetup.Create(_sim, 1);
        }

        private void Order(int archetype)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = 1, CommandType = CommandType.SetStandingOrders, Target = _game.CoreBase, ParamA = archetype
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            while (_sim.Clock.CurrentTick < at)
                _sim.Clock.Advance(1);
        }

        [Test]
        public void SetStandingOrders_HandsThePlayerToTheAi_AfterTheSignalLatency_AndBack()
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = 1, CommandType = CommandType.SetStandingOrders, Target = _game.CoreBase, ParamA = 2
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            Assert.Greater(at, _sim.Clock.CurrentTick);
            Assert.IsFalse(_sim.AiControllers.Has(_game.PlayerHq), "not before the order lands");
            while (_sim.Clock.CurrentTick < at)
                _sim.Clock.Advance(1);
            Assert.IsTrue(_sim.AiControllers.Has(_game.PlayerHq));
            Assert.AreEqual(2, _sim.AiControllers.Get(_game.PlayerHq).Archetype);

            Order(5);
            Assert.AreEqual(5, _sim.AiControllers.Get(_game.PlayerHq).Archetype, "a new preset replaces the old");

            Order(0);
            Assert.IsFalse(_sim.AiControllers.Has(_game.PlayerHq));
        }

        [Test]
        public void SetStandingOrders_WithAnUnknownPreset_ChangesNothing()
        {
            Order(99);
            Assert.IsFalse(_sim.AiControllers.Has(_game.PlayerHq));
            Order(-1);
            Assert.IsFalse(_sim.AiControllers.Has(_game.PlayerHq));
        }

        private int Ships(int playerId)
        {
            int ships = 0;
            for (int f = 0; f < _sim.Fleets.Count; f++)
                if (_sim.Fleets.At(f).OwnerPlayerId == playerId)
                    ships += _sim.Fleets.At(f).TotalShips;
            return ships;
        }

        private int Sites(int playerId)
        {
            int sites = 0;
            for (int i = 0; i < _sim.Sites.Count; i++)
                if (_sim.Sites.At(i).OwnerPlayerId == playerId)
                    sites++;
            return sites;
        }

        [Test]
        public void AGovernor_ThinksRarely_AndOnlyManagesWhatTheEmpireHas()
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = 1, CommandType = CommandType.SetStandingOrders, Target = _game.CoreBase, ParamA = 4, ParamB = 1
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            while (_sim.Clock.CurrentTick < at)
                _sim.Clock.Advance(1);
            Assert.IsTrue(_sim.AiControllers.Get(_game.PlayerHq).IsGovernor);
            for (int p = 0; p < _sim.Players.Count; p++)
                if (_sim.Players.At(p).PlayerId != 1)
                    _sim.Ai.Release(_sim.Players.EntityAt(p));

            int ships = Ships(1), sites = Sites(1), thinks = 0;
            var interval = _sim.Balance.Ai.GovernorThinkIntervalTicks;
            for (long t = 0; t < 2 * SimTick.TicksPerDay; t++)
            {
                _sim.Clock.Advance(1);
                long tick = _sim.Clock.CurrentTick;
                int n = _sim.Ai.Think(tick);
                if (n > 0)
                {
                    thinks++;
                    Assert.AreEqual(0, tick % interval, "only on governor ticks");
                }
                for (int i = 0; i < n; i++)
                {
                    var action = _sim.Ai.OrderAt(i).Action;
                    Assert.AreNotEqual(AiAction.BuildWarships, action);
                    Assert.AreNotEqual(AiAction.Siege, action);
                    Assert.AreNotEqual(AiAction.StealthRaid, action);
                    Assert.AreNotEqual(AiAction.ClaimSite, action);
                    Assert.AreNotEqual(AiAction.BuildRig, action);
                    Assert.AreNotEqual(AiAction.StressTest, action);
                }
            }
            Assert.Greater(thinks, 0, "it still manages the empire");
            Assert.LessOrEqual(Ships(1), ships, "no new ships");
            Assert.LessOrEqual(Sites(1), sites, "no new sites");
        }

        [Test]
        public void UnderStandingOrders_TheAiGivesThePlayersFleetsOrders()
        {
            Order(2);
            bool ordered = false;
            for (int t = 0; t < 3_600 && !ordered; t++)
            {
                _sim.Clock.Advance(1);
                int n = _sim.Ai.Think(_sim.Clock.CurrentTick);
                for (int i = 0; i < n; i++)
                    ordered |= _sim.Ai.OrderAt(i).PlayerId == 1 && _sim.Ai.OrderAt(i).Result == IssueResult.Issued;
            }
            Assert.IsTrue(ordered);
        }
    }
}
