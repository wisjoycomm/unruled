using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Research
{
    public class TechGatesTests
    {
        private const int Player = 1;

        private SimulationWorld _sim;
        private EntityId _hq;
        private EntityId _target;

        [SetUp]
        public void SetUp()
        {
            var d = DefaultGameBalance.Create();
            var calm = new NoaaBalance(600, 0, new[] { 1, 0, 0, 0, 0 }, 100, 100, 100, 100);
            var balance = new GameBalanceConfig(d.Recipes, d.Universe, d.Starlanes, calm, d.Refinery, d.Mining,
                d.Lifecycle, d.Latency, d.Radar, d.Fleets, d.Combat, d.Relocation, d.Ledger, d.Sta, d.Leaderboard, d.Wto,
                d.Contracts, d.Authority, d.SystemFactions, d.Ai, d.NewGame, d.Research, d.Shipyard, d.SiteControl);
            _sim = new SimulationWorld(balance, worldSeed: 9);
            _sim.GenerateSystem(21, clusterId: 0);
            var core = _sim.Sites.EntityAt(0);
            _target = _sim.Sites.EntityAt(1);
            _hq = _sim.CreatePlayer(Player, core);

            ref var site = ref _sim.Sites.Get(_target);
            _sim.StormFronts.Add(_sim.World.Create(), new StormFront
            {
                ClusterId = site.ClusterId, Scale = StormScale.Radio, Level = 4, CenterX = site.PosX, CenterY = site.PosY,
                Radius = 5, EndTick = 10_000
            });
            _sim.Clock.Advance(1);
            Assert.GreaterOrEqual(_sim.Weather.Get(_target).RadioBlackoutLevel, 3, "the target is blacked out");
        }

        private IssueResult Order() => _sim.Issuer.Issue(
            new DelayedPlayerCommand { PlayerId = Player, CommandType = CommandType.Hold, Target = _target }, out _);

        [Test]
        public void TierDefaultsToOne_ForUnknownAndSystemPlayers()
        {
            Assert.AreEqual(1, _sim.TechGates.TechTierOf(Player));
            Assert.AreEqual(1, _sim.TechGates.TechTierOf(42));
            Assert.AreEqual(1, _sim.TechGates.TechTierOf(-4));
        }

        [Test]
        public void Blackout_RefusesOrders_UntilLaserComms()
        {
            Assert.AreEqual(IssueResult.Blackout, Order());
            _sim.TechProgress.Get(_hq).TechTier = 3;
            Assert.AreEqual(IssueResult.Blackout, Order());
            _sim.TechProgress.Get(_hq).TechTier = 4;
            Assert.AreEqual(IssueResult.Issued, Order());
        }

        [Test]
        public void Gates_FollowTheirTiers()
        {
            ref var p = ref _sim.TechProgress.Get(_hq);
            p.TechTier = 4;
            Assert.IsTrue(_sim.TechGates.Unlocks(Player, TechGate.LaserComms));
            Assert.IsTrue(_sim.TechGates.Unlocks(Player, TechGate.CoreFractureMining));
            Assert.IsFalse(_sim.TechGates.Unlocks(Player, TechGate.Terraform));
            p.TechTier = 5;
            Assert.IsTrue(_sim.TechGates.Unlocks(Player, TechGate.Terraform));
        }
    }
}
