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
                d.Contracts, d.Authority, d.SystemFactions, d.Ai, d.NewGame, d.Research, d.Shipyard, d.SiteControl, d.JumpGates, d.Convoys, d.Citadel);
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

        private void StressTest(ResearchBranch branch = ResearchBranch.Military)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = Player, CommandType = CommandType.StressTest, Target = _core, ParamA = (int)branch
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            Run((int)(at - _sim.Clock.CurrentTick));
        }

        private void StockTestItems(int times, ResearchBranch branch = ResearchBranch.Military)
        {
            var cost = _sim.Balance.Research.Tier((byte)(Progress.Tier(branch) + 1)).StressTestCost;
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
            Assert.AreEqual(1, Progress.Tier(ResearchBranch.Industry));
            Assert.AreEqual(1, Progress.Tier(ResearchBranch.Logistics));
            Assert.AreEqual(1, Progress.Tier(ResearchBranch.Signals));
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage);
            Assert.AreEqual(1, _sim.TechGates.TechTierOf(Player));
        }

        [Test]
        public void Telemetry_FleetWithMicrochipsInAStorm_FillsItAndMovesToStressTest()
        {
            Storm(5); // level 5 = capped at 99% sample chance (990 permille), 5 milli per sample
            _sim.Stockpiles.Get(_fleet).Add(ItemId.ItemMicrochip, _sim.Balance.Research.TelemetryMinMicrochips);
            Run(10);
            Assert.Greater(Progress.TelemetryMilli, 0);
            Assert.AreEqual(_fleet, Progress.LastTelemetryFleet);

            int target2 = _sim.Balance.Research.TierTelemetryTargetMilli(2);
            Run(target2 / 5 - 10);
            Assert.Less(Progress.TelemetryMilli, target2, "99% cap means exact ticks fall slightly short");
            Run(200);
            Assert.AreEqual(target2, Progress.TelemetryMilli);
            Assert.AreEqual(ResearchStage.StressTest, Progress.Stage);
            Assert.AreEqual(5, _sim.Stockpiles.Get(_fleet).Get(ItemId.ItemMicrochip), "the chips are not used up");
        }

        [Test]
        public void Telemetry_KeepsBuildingTowardHigherTierBranches_AndEnforcesTierTarget()
        {
            Progress.TechTier = 2; // Military needs Tier 3 telemetry, others still need Tier 2
            Storm(5);
            _sim.Stockpiles.Get(_fleet).Add(ItemId.ItemMicrochip, _sim.Balance.Research.TelemetryMinMicrochips);
            int target2 = _sim.Balance.Research.TierTelemetryTargetMilli(2);
            Run(target2 / 5 + 200);
            Assert.AreEqual(ResearchStage.StressTest, Progress.Stage, "Tier 2 branches can test");

            StockTestItems(1, ResearchBranch.Military);
            StressTest(ResearchBranch.Military);
            Assert.AreEqual(ResearchStage.StressTest, Progress.Stage, "Military Tier 3 rejected until Tier 3 telemetry is gathered");

            Run(20);
            Assert.Greater(Progress.TelemetryMilli, target2, "telemetry keeps building past Tier 2 while in StressTest");
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
        public void StressChance_DefaultIsGuaranteed_AndFormulaHonoursCalibrationPity()
        {
            var r = _sim.Balance.Research;
            for (byte t = 2; t <= ResearchBalance.TopTier; t++)
                Assert.AreEqual(1000, r.StressChancePermille(t, 0));

            var cost = new[] { new ItemStack(ItemId.ItemSteel, 10) };
            var custom = new ResearchBalance(1000, 1, 5, 180, 4, 4, 5, 2, new[]
            {
                new TierResearchRules(600, cost, 100),
                new TierResearchRules(450, cost, 100),
                new TierResearchRules(300, cost, 100),
                new TierResearchRules(200, cost, 100)
            });
            Assert.AreEqual(600, custom.StressChancePermille(2, 0));
            Assert.AreEqual(780, custom.StressChancePermille(2, 1));
            Assert.AreEqual(1000, custom.StressChancePermille(2, 3));
            Assert.AreEqual(200, custom.StressChancePermille(5, 0));
            Assert.AreEqual(920, custom.StressChancePermille(5, 4));
            Assert.AreEqual(1000, custom.StressChancePermille(5, 5));
        }

        [Test]
        public void StressTest_SucceedsImmediatelyWhenMaterialsArePaid()
        {
            Progress.Stage = ResearchStage.StressTest;
            StockTestItems(1);
            StressTest();
            Assert.AreEqual(ResearchStage.Retooling, Progress.Stage);
            Assert.AreEqual(0, Progress.FailedTests);
            Assert.AreEqual(0, Bulletins(BulletinKind.PrototypeBlast));
            Assert.AreEqual(0, _sim.Stockpiles.Get(_core).Get(ItemId.ItemSteel));
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
            int expectedSteel = _sim.Balance.Research.Tier(2).StressTestCost[0].Quantity;
            StressTest();
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage);
            Assert.AreEqual(expectedSteel, _sim.Stockpiles.Get(_core).Get(ItemId.ItemSteel), "nothing paid");
        }

        [Test]
        public void Retooling_UnlocksTheNextTierOnTheActiveBranchWhenItEnds()
        {
            Progress.Stage = ResearchStage.StressTest;
            StockTestItems(1, ResearchBranch.Logistics);
            StressTest(ResearchBranch.Logistics);
            Assert.AreEqual(ResearchBranch.Logistics, Progress.ActiveBranch);
            Progress.RetoolUntilTick = _sim.Clock.CurrentTick + 50;
            Run(49);
            Assert.AreEqual(1, Progress.Tier(ResearchBranch.Logistics));
            Run(1);
            Assert.AreEqual(2, Progress.Tier(ResearchBranch.Logistics));
            Assert.AreEqual(1, Progress.Tier(ResearchBranch.Military), "other branches stay at their tier");
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage);
            Assert.AreEqual(2, _sim.TechGates.TechTierOf(Player, ResearchBranch.Logistics));
        }

        [Test]
        public void Retooling_TheTopTier_CompletesResearch_OnlyWhenEveryBranchIsDone()
        {
            Progress.TechTier = 4;
            Progress.ActiveBranch = ResearchBranch.Military;
            Progress.Stage = ResearchStage.Retooling;
            Progress.RetoolUntilTick = 1;
            Run(1);
            Assert.AreEqual(5, Progress.TechTier);
            Assert.AreEqual(ResearchStage.FieldTelemetry, Progress.Stage, "three branches still below tier 5");

            Progress.IndustryTier = 5;
            Progress.LogisticsTier = 5;
            Progress.SignalsTier = 4;
            Progress.ActiveBranch = ResearchBranch.Signals;
            Progress.Stage = ResearchStage.Retooling;
            Progress.RetoolUntilTick = 2;
            Run(1);
            Assert.AreEqual(5, Progress.SignalsTier);
            Assert.AreEqual(ResearchStage.Complete, Progress.Stage);
            Assert.AreEqual(250, _sim.Balance.Research.EffectiveTelemetryChancePermille(1, 2));
            Assert.AreEqual(990, _sim.Balance.Research.EffectiveTelemetryChancePermille(5, 5), "capped at 99%");
        }
    }
}
