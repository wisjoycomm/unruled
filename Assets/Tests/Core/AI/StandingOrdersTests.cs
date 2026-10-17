using NUnit.Framework;
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
