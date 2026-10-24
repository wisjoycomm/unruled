using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    public class JumpGateSystemTests
    {
        private const int Player = 1;

        private SimulationWorld _sim;
        private NewGame _game;
        private EntityId _gate;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 77);
            _game = NewGameSetup.Create(_sim, Player);
            for (int i = 0; i < _sim.JumpGates.Count; i++)
                if (_sim.JumpGates.At(i).OwnerPlayerId == Player)
                    _gate = _sim.JumpGates.EntityAt(i);
        }

        private ref JumpGate Gate => ref _sim.JumpGates.Get(_gate);
        private ref Stockpile Core => ref _sim.Stockpiles.Get(_game.CoreBase);

        private void Open(int playerId, EntityId target)
        {
            var command = new DelayedPlayerCommand { PlayerId = playerId, CommandType = CommandType.OpenJumpGate, Target = target };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            for (long t = _sim.Clock.CurrentTick; t < at; t++)
                _sim.Clock.Advance(1);
        }

        private bool CanRoute(EntityId from, EntityId to)
        {
            var route = default(FleetRoute);
            return _sim.Routes.TryPlan(from, to, 1, ref route);
        }

        private void Fund()
        {
            Core.Add(ItemId.ItemBattery, 100);
            Core.Add(ItemId.ItemFuel, 100);
        }

        [Test]
        public void NewGame_EveryParticipantHasADormantGate_WithNoLaneYet()
        {
            int participants = 1 + _sim.Balance.NewGame.RivalCount;
            Assert.AreEqual(participants, _sim.JumpGates.Count);
            int contested = NewGameSetup.ContestedClusterId(participants);
            for (int i = 0; i < _sim.JumpGates.Count; i++)
            {
                ref var gate = ref _sim.JumpGates.At(i);
                Assert.IsFalse(gate.IsOpen);
                Assert.AreEqual(contested, _sim.Sites.Get(gate.FarSite).ClusterId);
                Assert.AreEqual(0, _sim.Sites.Get(gate.FarSite).OwnerPlayerId);
                for (int l = 0; l < _sim.Starlanes.Count; l++)
                {
                    ref var lane = ref _sim.Starlanes.At(l);
                    Assert.IsFalse((lane.SiteA == gate.HomeSite && lane.SiteB == gate.FarSite) ||
                                   (lane.SiteA == gate.FarSite && lane.SiteB == gate.HomeSite), "a dormant gate has no lane");
                }
            }
        }

        [Test]
        public void Open_AtTier2_ChargesTheCoreBase_AndLinksHomeToTheContestedCluster()
        {
            _sim.TechProgress.Get(_game.PlayerHq).LogisticsTier = 2;
            Fund();
            int batteries = Core.Get(ItemId.ItemBattery);
            int lanes = _sim.Starlanes.Count;

            Open(Player, Gate.HomeSite);

            Assert.IsTrue(Gate.IsOpen);
            Assert.AreEqual(lanes + 1, _sim.Starlanes.Count);
            Assert.IsTrue(_sim.LaneWeather.Has(Gate.Lane));
            Assert.AreEqual(batteries - 50, Core.Get(ItemId.ItemBattery));
            Assert.IsTrue(CanRoute(_game.CoreBase, Gate.FarSite));
        }

        [Test]
        public void Open_BelowTier2_OrUnaffordable_OrSomeoneElsesGate_DoesNothing()
        {
            Fund();
            Open(Player, Gate.HomeSite);
            Assert.IsFalse(Gate.IsOpen, "tier 1");

            _sim.TechProgress.Get(_game.PlayerHq).LogisticsTier = 2;
            Core.Add(ItemId.ItemBattery, -Core.Get(ItemId.ItemBattery));
            Open(Player, Gate.HomeSite);
            Assert.IsFalse(Gate.IsOpen, "no batteries");

            Fund();
            for (int i = 0; i < _sim.JumpGates.Count; i++)
            {
                ref var rival = ref _sim.JumpGates.At(i);
                if (rival.OwnerPlayerId == Player)
                    continue;
                var command = new DelayedPlayerCommand
                {
                    PlayerId = Player, CommandType = CommandType.OpenJumpGate, Target = rival.HomeSite,
                    ExecuteAtTick = _sim.Clock.CurrentTick + 1
                };
                _sim.Clock.Enqueue(command);
                _sim.Clock.Advance(1);
                Assert.IsFalse(_sim.JumpGates.At(i).IsOpen, "not the owner");
                break;
            }
        }
    }
}
