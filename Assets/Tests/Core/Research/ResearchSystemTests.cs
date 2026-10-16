using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.Research
{
    public class ResearchSystemTests
    {
        private const int Player = 1;

        private SimulationWorld _sim;
        private EntityId _hq;
        private EntityId _core;
        private EntityId _fleet;

        [SetUp]
        public void SetUp()
        {
            var d = DefaultGameBalance.Create();
            var calm = new NoaaBalance(600, 0, new[] { 1, 0, 0, 0, 0 }, 100, 100, 100, 100);
            var balance = new GameBalanceConfig(d.Recipes, d.Universe, d.Starlanes, calm, d.Refinery, d.Mining,
                d.Lifecycle, d.Latency, d.Radar, d.Fleets, d.Combat, d.Relocation, d.Ledger, d.Sta, d.Leaderboard, d.Wto,
                d.Contracts, d.Authority, d.SystemFactions, d.Ai, d.NewGame, d.Research, d.Shipyard, d.SiteControl, d.JumpGates, d.Convoys);
            _sim = new SimulationWorld(balance, worldSeed: 3);
            _sim.GenerateSystem(11, clusterId: 0);
            _core = _sim.Sites.EntityAt(0);
            for (int i = 0; i < _sim.Sites.Count; i++)
                _sim.Stockpiles.Add(_sim.Sites.EntityAt(i), default);
            _sim.Sites.Get(_core).OwnerPlayerId = Player;
            _hq = _sim.CreatePlayer(Player, _core);
            _fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 1 }, _core);
        }

        private ref TechProgress Progress => ref _sim.TechProgress.Get(_hq);

        private void Storm(byte level)
        {
            ref var site = ref _sim.Sites.Get(_core);
            _sim.StormFronts.Add(_sim.World.Create(), new StormFront
            {
                ClusterId = site.ClusterId, Scale = StormScale.Geomagnetic, Level = level, CenterX = site.PosX,
                CenterY = site.PosY, Radius = 10, EndTick = 100_000
            });
        }

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                _sim.Clock.Advance(1);
        }

        private void StressTest()
        {
            var command = new DelayedPlayerCommand { PlayerId = Player, CommandType = CommandType.StressTest, Target = _core };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            Run((int)(at - _sim.Clock.CurrentTick));
        }

        private void StockTestItems(int times)
        {
            var cost = _sim.Balance.Research.Tier((byte)(Progress.TechTier + 1)).StressTestCost;
            for (int i = 0; i < cost.Length; i++)
                _sim.Stockpiles.Get(_core).Add(cost[i].Item, cost[i].Quantity * times);
        }

        private int Bulletins(BulletinKind kind)
        {
            int n = 0;
            for (int i = 0; i < _sim.Bulletins.Count; i++)
                if (_sim.Bulletins.At(i).Kind == kind)
                    n++;
            return n;
        }

        [Test]
        public void NewPlayer_StartsAtTierOneGatheringTelemetry()
        {
            Assert.AreEqual(1, Progress.TechTier);
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage);
            Assert.AreEqual(1, _sim.TechGates.TechTierOf(Player));
        }

        [Test]
        public void Telemetry_FleetWithMicrochipsInAStorm_FillsItAndMovesToStressTest()
        {
            Storm(3);
            _sim.Stockpiles.Get(_fleet).Add(ItemId.ItemMicrochip, _sim.Balance.Research.TelemetryMinMicrochips);
            Run(10);
            Assert.AreEqual(30, Progress.TelemetryMilli, "3 milli per tick in a level-3 storm");
            Assert.AreEqual(_fleet, Progress.LastTelemetryFleet);

            Run(330);
            Assert.AreEqual(_sim.Balance.Research.TelemetryTargetMilli, Progress.TelemetryMilli);
            Assert.AreEqual(ResearchStage.StressTest, Progress.Stage);
            Assert.AreEqual(5, _sim.Stockpiles.Get(_fleet).Get(ItemId.ItemMicrochip), "the chips are not used up");
        }

        [Test]
        public void Telemetry_TooFewMicrochipsOrNoStorm_GathersNothing()
        {
            _sim.Stockpiles.Get(_fleet).Add(ItemId.ItemMicrochip, _sim.Balance.Research.TelemetryMinMicrochips);
            Run(20);
            Assert.AreEqual(0, Progress.TelemetryMilli, "no storm");

            Storm(2);
            _sim.Stockpiles.Get(_fleet).Add(ItemId.ItemMicrochip, -1);
            Run(20);
            Assert.AreEqual(0, Progress.TelemetryMilli, "one chip short");
        }

        [Test]
        public void StressChance_FollowsCalibrationPity()
        {
            var r = _sim.Balance.Research;
            Assert.AreEqual(600, r.StressChancePermille(2, 0));
            Assert.AreEqual(780, r.StressChancePermille(2, 1));
            Assert.AreEqual(1000, r.StressChancePermille(2, 3));
            Assert.AreEqual(200, r.StressChancePermille(5, 0));
            Assert.AreEqual(920, r.StressChancePermille(5, 4));
            Assert.AreEqual(1000, r.StressChancePermille(5, 5));
        }

        [Test]
        public void StressTest_RepeatsUntilSuccess_PayingEachTimeAndPublishingEachBlast()
        {
            Progress.Stage = ResearchStage.StressTest;
            StockTestItems(4);
            int attempts = 0;
            while (Progress.Stage == ResearchStage.StressTest)
            {
                Assert.Less(attempts, 3, "tier 2 is certain by the 3rd failure's pity");
                StressTest();
                attempts++;
            }
            Assert.AreEqual(ResearchStage.Retooling, Progress.Stage);
            Assert.AreEqual(0, Progress.FailedTests, "pity resets for the next tier");
            Assert.AreEqual(attempts - 1, Bulletins(BulletinKind.PrototypeBlast));
            int steelLeft = _sim.Stockpiles.Get(_core).Get(ItemId.ItemSteel);
            Assert.AreEqual(10 * (4 - attempts), steelLeft);
        }

        [Test]
        public void StressTest_WithoutTheItems_DoesNothing()
        {
            Progress.Stage = ResearchStage.StressTest;
            StressTest();
            Assert.AreEqual(ResearchStage.StressTest, Progress.Stage);
            Assert.AreEqual(0, Progress.FailedTests);
            Assert.AreEqual(0, Bulletins(BulletinKind.PrototypeBlast));
        }

        [Test]
        public void StressTest_BeforeTelemetryIsDone_IsIgnored()
        {
            StockTestItems(1);
            StressTest();
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage);
            Assert.AreEqual(10, _sim.Stockpiles.Get(_core).Get(ItemId.ItemSteel), "nothing paid");
        }

        [Test]
        public void Retooling_UnlocksTheNextTierWhenItEnds()
        {
            Progress.Stage = ResearchStage.Retooling;
            Progress.RetoolUntilTick = 50;
            Run(49);
            Assert.AreEqual(1, Progress.TechTier);
            Run(1);
            Assert.AreEqual(2, Progress.TechTier);
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage);
            Assert.AreEqual(2, _sim.TechGates.TechTierOf(Player));
        }

        [Test]
        public void Retooling_TheTopTier_CompletesResearch()
        {
            Progress.TechTier = 4;
            Progress.Stage = ResearchStage.Retooling;
            Progress.RetoolUntilTick = 1;
            Run(1);
            Assert.AreEqual(5, Progress.TechTier);
            Assert.AreEqual(ResearchStage.Complete, Progress.Stage);
        }
    }
}
