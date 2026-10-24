using System;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    public class NewGameSetupTests
    {
        private static SimulationWorld NewWorld(ulong seed = 77) => new SimulationWorld(DefaultGameBalance.Create(), seed);

        [Test]
        public void Create_GivesEveryParticipantAHomeSystemAndRivalsAnAi()
        {
            var sim = NewWorld();
            var rules = sim.Balance.NewGame;
            NewGameSetup.Create(sim, 1);

            int participants = 1 + rules.RivalCount;
            int totalClusters = participants + 1 + participants * rules.UninhabitedClustersPerParticipant;
            Assert.AreEqual(participants, sim.Players.Count);
            Assert.AreEqual(rules.RivalCount, sim.AiControllers.Count);
            Assert.AreEqual(totalClusters, sim.Galaxy.NextClusterId(), "homes + contested + uninhabited");
            var planets = new int[totalClusters];
            for (int i = 0; i < sim.Sites.Count; i++)
                planets[sim.Sites.At(i).ClusterId]++;
            var universe = sim.Balance.Universe;
            for (int c = 0; c < totalClusters; c++)
                Assert.That(planets[c], Is.InRange(c <= participants ? universe.SettledMinSites : universe.MinSitesPerSystem,
                    universe.SitesPerSystem), $"cluster {c} planets");
            for (int i = 0; i < sim.Sites.Count; i++)
                Assert.IsTrue(sim.Stockpiles.Has(sim.Sites.EntityAt(i)), "every site has a stockpile");
        }

        [Test]
        public void Create_UninhabitedClustersAreLifelessNeutralAndReachableFromContestedGate()
        {
            var sim = NewWorld();
            var rules = sim.Balance.NewGame;
            NewGameSetup.Create(sim, 1);

            int participants = 1 + rules.RivalCount;
            int firstUninhabited = NewGameSetup.FirstUninhabitedClusterId(participants);
            int totalClusters = firstUninhabited + participants * rules.UninhabitedClustersPerParticipant;
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                ref var site = ref sim.Sites.At(i);
                if (site.ClusterId >= firstUninhabited && site.ClusterId < totalClusters)
                    Assert.AreEqual(0, site.OwnerPlayerId, $"uninhabited cluster {site.ClusterId} must start neutral");
            }

            var route = default(FleetRoute);
            var gateFar = sim.JumpGates.At(0).FarSite;
            bool foundRoute = false;
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                if (sim.Sites.At(i).ClusterId == firstUninhabited &&
                    sim.Routes.TryPlan(gateFar, sim.Sites.EntityAt(i), 1, ref route))
                {
                    foundRoute = true;
                    break;
                }
            }
            Assert.IsTrue(foundRoute, "uninhabited system is reachable from the participant's contested gate endpoint");
        }

        [Test]
        public void Create_CoreBaseIsTheHighestEsiHomeSiteAndOwned()
        {
            var sim = NewWorld();
            var game = NewGameSetup.Create(sim, 1);

            ref var core = ref sim.Sites.Get(game.CoreBase);
            Assert.AreEqual(0, core.ClusterId);
            Assert.AreEqual(1, core.OwnerPlayerId);
            Assert.AreEqual(game.CoreBase, sim.Players.Get(game.PlayerHq).CoreBase);
            for (int i = 0; i < sim.Sites.Count; i++)
                if (sim.Sites.At(i).ClusterId == 0)
                    Assert.LessOrEqual(sim.Sites.At(i).BaseEsiMilli, core.BaseEsiMilli);
        }

        [Test]
        public void Create_DepositsFitTheSiteEsiAndHomesHoldNoTungstenOrUranium()
        {
            var sim = NewWorld();
            NewGameSetup.Create(sim, 1);
            var rules = sim.Balance.NewGame;

            Assert.Greater(sim.Deposits.Count, 0);
            for (int i = 0; i < sim.Deposits.Count; i++)
            {
                ref var deposit = ref sim.Deposits.At(i);
                if (sim.Sites.Get(deposit.Site).ClusterId <= sim.Balance.NewGame.RivalCount)
                {
                    Assert.AreNotEqual(ItemId.RawTungsten, deposit.RawItem);
                    Assert.AreNotEqual(ItemId.RawUranium, deposit.RawItem);
                }
                Assert.IsTrue(ItemIds.IsRaw(deposit.RawItem));
                bool lifeSupport = IsAnyCoreBase(sim, deposit.Site) && IsCoreBaseDeposit(rules, deposit.RawItem);
                if (lifeSupport)
                {
                    Assert.AreEqual(rules.SurfaceReserve * rules.CoreBaseReserveMultiplier, deposit.SurfaceReserve);
                    continue;
                }
                long richness = (long)rules.RichnessPercent(PlanetClasses.Of(in sim.Sites.Get(deposit.Site), sim.Balance.Universe)) *
                                rules.RichnessPercent(deposit.RawItem);
                Assert.That(deposit.SurfaceReserve, Is.InRange(
                    rules.SurfaceReserve * richness * rules.ReserveRollMinPercent / 1_000_000L,
                    rules.SurfaceReserve * richness * rules.ReserveRollMaxPercent / 1_000_000L), deposit.RawItem.ToString());
            }
        }

        [Test]
        public void Create_GeneratedDepositsDiffer_ByPlanetOreAndRoll()
        {
            var sim = NewWorld();
            NewGameSetup.Create(sim, 1);
            var rules = sim.Balance.NewGame;
            var seen = new System.Collections.Generic.HashSet<long>();
            int generated = 0;
            for (int i = 0; i < sim.Deposits.Count; i++)
            {
                ref var deposit = ref sim.Deposits.At(i);
                if (IsAnyCoreBase(sim, deposit.Site) && IsCoreBaseDeposit(rules, deposit.RawItem))
                    continue;
                generated++;
                seen.Add((long)deposit.SurfaceReserve << 32 ^ deposit.DeepCrustReserve);
            }
            Assert.Greater(generated, 20);
            Assert.Greater(seen.Count, generated * 9 / 10, "almost every deposit holds its own amounts");

            var again = NewWorld();
            NewGameSetup.Create(again, 1);
            Assert.AreEqual(sim.Deposits.Count, again.Deposits.Count);
            for (int i = 0; i < sim.Deposits.Count; i++)
                Assert.AreEqual(sim.Deposits.At(i).DeepCrustReserve, again.Deposits.At(i).DeepCrustReserve, "same seed, same rolls");
        }

        [Test]
        public void Create_HomesHoldEveryOtherRawItem_TheContestedClusterHoldsAll_OutsideTheCitadel()
        {
            foreach (ulong seed in new ulong[] { 1, 1851, 2468, 3702, 2026 })
                AssertEveryClusterHoldsItsRawItems(seed);
        }

        private static void AssertEveryClusterHoldsItsRawItems(ulong seed)
        {
            var sim = NewWorld(seed);
            var rules = sim.Balance.NewGame;
            NewGameSetup.Create(sim, 1);
            int participants = 1 + rules.RivalCount;
            int clusters = participants + 1 + participants * rules.UninhabitedClustersPerParticipant;
            for (int cluster = 0; cluster < clusters; cluster++)
            {
                bool home = cluster < participants;
                for (int slot = 0; slot < ItemIds.RawCount; slot++)
                {
                    var raw = ItemIds.FromSlot(slot);
                    if (!sim.Balance.Recipes.GetItem(raw).HasPreferredEsi || (home && rules.IsExcludedAtHome(raw)))
                        continue;
                    bool found = false;
                    for (int d = 0; d < sim.Deposits.Count && !found; d++)
                        found = sim.Deposits.At(d).RawItem == raw && !sim.Citadels.Has(sim.Deposits.At(d).Site) &&
                                sim.Sites.Get(sim.Deposits.At(d).Site).ClusterId == cluster;
                    Assert.IsTrue(found, $"seed {seed}: cluster {cluster} lacks {raw}");
                }
            }
        }

        [Test]
        public void Create_CoreBaseMinesItsLifeSupport_FromDeepReserves_OtherDepositsFollowEsi()
        {
            var sim = NewWorld();
            var rules = sim.Balance.NewGame;
            var game = NewGameSetup.Create(sim, 1);
            int coreEsi = sim.Sites.Get(game.CoreBase).BaseEsiMilli;
            for (int d = 0; d < rules.CoreBaseDepositCount; d++)
            {
                MineralDeposit found = default;
                int count = 0;
                for (int i = 0; i < sim.Deposits.Count; i++)
                    if (sim.Deposits.At(i).Site == game.CoreBase && sim.Deposits.At(i).RawItem == rules.CoreBaseDeposit(d).Item)
                    {
                        found = sim.Deposits.At(i);
                        count++;
                    }
                string raw = rules.CoreBaseDeposit(d).Item.ToString();
                Assert.AreEqual(1, count, raw);
                Assert.AreEqual(rules.CoreBaseDeposit(d).Quantity, found.RigCount, raw);
                Assert.AreEqual(rules.SurfaceReserve * rules.CoreBaseReserveMultiplier, found.SurfaceReserve, raw);
                Assert.AreEqual(rules.DeepCrustReserve * rules.CoreBaseReserveMultiplier, found.DeepCrustReserve, raw);
                Assert.AreEqual(rules.CoreFractureReserve * rules.CoreBaseReserveMultiplier, found.CoreFractureReserve, raw);
            }
            for (int i = 0; i < sim.Deposits.Count; i++)
            {
                ref var deposit = ref sim.Deposits.At(i);
                if (deposit.Site != game.CoreBase || IsCoreBaseDeposit(rules, deposit.RawItem))
                    continue;
                ref readonly var item = ref sim.Balance.Recipes.GetItem(deposit.RawItem);
                Assert.IsTrue(item.PreferredEsiMinMilli <= coreEsi && coreEsi <= item.PreferredEsiMaxMilli,
                    $"{deposit.RawItem} window {item.PreferredEsiMinMilli}..{item.PreferredEsiMaxMilli} vs ESI {coreEsi}");
                Assert.AreEqual(0, deposit.RigCount);
            }
        }

        private static bool IsAnyCoreBase(SimulationWorld sim, Unruled.Core.Ecs.EntityId site)
        {
            for (int p = 0; p < sim.Players.Count; p++)
                if (sim.Players.At(p).CoreBase == site)
                    return true;
            return false;
        }

        private static bool IsCoreBaseDeposit(NewGameBalance rules, ItemId raw)
        {
            for (int d = 0; d < rules.CoreBaseDepositCount; d++)
                if (rules.CoreBaseDeposit(d).Item == raw)
                    return true;
            return false;
        }

        [Test]
        public void Create_CoreBaseHasRefineriesStockAndAStockedFleet()
        {
            var sim = NewWorld();
            var rules = sim.Balance.NewGame;
            var game = NewGameSetup.Create(sim, 1);

            int refineries = 0;
            for (int i = 0; i < sim.Refineries.Count; i++)
                if (sim.Refineries.At(i).Site == game.CoreBase)
                    refineries++;
            Assert.AreEqual(rules.StartingRefineryCount, refineries);
            for (int i = 0; i < rules.CoreBaseStockCount; i++)
                Assert.AreEqual(rules.CoreBaseStock(i).Quantity, sim.Stockpiles.Get(game.CoreBase).Get(rules.CoreBaseStock(i).Item));

            ref var fleet = ref sim.Fleets.Get(game.Fleet);
            Assert.AreEqual(1, fleet.OwnerPlayerId);
            Assert.AreEqual(rules.FleetLineShips, fleet.LineShips);
            Assert.AreEqual(game.CoreBase, sim.FleetMotions.Get(game.Fleet).Site);
            for (int i = 0; i < rules.FleetStockCount; i++)
                Assert.AreEqual(rules.FleetStock(i).Quantity, sim.Stockpiles.Get(game.Fleet).Get(rules.FleetStock(i).Item));
        }

        [Test]
        public void Create_IsDeterministicForAWorldSeed()
        {
            var a = NewWorld(5);
            var b = NewWorld(5);
            NewGameSetup.Create(a, 1);
            NewGameSetup.Create(b, 1);

            Assert.AreEqual(a.Deposits.Count, b.Deposits.Count);
            for (int i = 0; i < a.Deposits.Count; i++)
                Assert.AreEqual(a.Deposits.At(i).RawItem, b.Deposits.At(i).RawItem);
            for (int i = 0; i < a.Sites.Count; i++)
                Assert.AreEqual(a.Sites.At(i).BaseEsiMilli, b.Sites.At(i).BaseEsiMilli);
        }

        [Test]
        public void Create_TwiceOnOneWorld_Throws()
        {
            var sim = NewWorld();
            NewGameSetup.Create(sim, 1);
            Assert.Throws<InvalidOperationException>(() => NewGameSetup.Create(sim, 1));
        }

        [Test]
        public void NewGame_RunsAnHourWithAiThinking()
        {
            var sim = NewWorld();
            var game = NewGameSetup.Create(sim, 1);
            for (int t = 0; t < 3_600; t++)
            {
                sim.Clock.Advance(1);
                sim.Ai.Think(sim.Clock.CurrentTick);
            }
            Assert.AreEqual(3_600, sim.Clock.CurrentTick);
            int rigged = 0;
            for (int i = 0; i < sim.Deposits.Count; i++)
            {
                ref var deposit = ref sim.Deposits.At(i);
                if (deposit.Site != game.CoreBase || deposit.RigCount == 0)
                    continue;
                rigged++;
                Assert.Less(deposit.SurfaceReserve, sim.Balance.NewGame.SurfaceReserve * sim.Balance.NewGame.CoreBaseReserveMultiplier,
                    $"{deposit.RawItem} was mined");
            }
            Assert.Greater(rigged, 0);
        }
    }
}
