using System;
using NUnit.Framework;
using Unruled.Core.Economy;
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

            Assert.AreEqual(1 + rules.RivalCount, sim.Players.Count);
            Assert.AreEqual(rules.RivalCount, sim.AiControllers.Count);
            Assert.AreEqual((2 + rules.RivalCount) * sim.Balance.Universe.SitesPerSystem, sim.Sites.Count, "homes + contested");
            for (int i = 0; i < sim.Sites.Count; i++)
                Assert.IsTrue(sim.Stockpiles.Has(sim.Sites.EntityAt(i)), "every site has a stockpile");
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
                Assert.AreEqual(sim.Balance.NewGame.SurfaceReserve, deposit.SurfaceReserve);
            }
        }

        [Test]
        public void Create_CoreBaseMinesItsLifeSupport_OtherDepositsFollowEsi()
        {
            var sim = NewWorld();
            var rules = sim.Balance.NewGame;
            var game = NewGameSetup.Create(sim, 1);
            int coreEsi = sim.Sites.Get(game.CoreBase).BaseEsiMilli;
            for (int d = 0; d < rules.CoreBaseDepositCount; d++)
            {
                int rigs = -1;
                for (int i = 0; i < sim.Deposits.Count; i++)
                    if (sim.Deposits.At(i).Site == game.CoreBase && sim.Deposits.At(i).RawItem == rules.CoreBaseDeposit(d).Item)
                        rigs = sim.Deposits.At(i).RigCount;
                Assert.AreEqual(rules.CoreBaseDeposit(d).Quantity, rigs, rules.CoreBaseDeposit(d).Item.ToString());
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
                Assert.Less(deposit.SurfaceReserve, sim.Balance.NewGame.SurfaceReserve, $"{deposit.RawItem} was mined");
            }
            Assert.Greater(rigged, 0);
        }
    }
}
