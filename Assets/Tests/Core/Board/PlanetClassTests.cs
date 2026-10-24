using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Board
{
    public class PlanetClassTests
    {
        private static readonly UniverseGenBalance Universe = DefaultGameBalance.CreateUniverseGenBalance();

        private static PlanetClass At(int radius, ulong terrain = 0) =>
            PlanetClasses.Of(new SiteInfo { PosX = radius, TerrainSeed = terrain }, Universe);

        [Test]
        public void Class_FollowsTheOrbit_RockyInside_GiantsOutside_IceGiantsPastTheIceLine()
        {
            Assert.AreEqual(PlanetClass.Terrestrial, At(300), "hot inner world");
            Assert.AreEqual(PlanetClass.GasGiant, At(1300));
            Assert.AreEqual(PlanetClass.NeptuneLike, At(1500));
            Assert.AreEqual(PlanetClass.NeptuneLike, At(-1500), "any direction");
        }

        [Test]
        public void HabitableZone_HoldsTerrestrialsAndSuperEarths_ByTheTerrainSeed()
        {
            int super = 0;
            for (ulong seed = 0; seed < 1000; seed++)
            {
                var c = At(840, seed * 7919);
                Assert.That(c, Is.EqualTo(PlanetClass.Terrestrial).Or.EqualTo(PlanetClass.SuperEarth));
                if (c == PlanetClass.SuperEarth)
                    super++;
            }
            Assert.That(super, Is.InRange(250, 450), "about 35% Super-Earths");
        }

        [Test]
        public void NewGame_EverySystemHasRockyWorldsAndGiants()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 21);
            NewGameSetup.Create(sim, 1);
            int clusters = sim.Galaxy.NextClusterId();
            var rocky = new bool[clusters];
            var giant = new bool[clusters];
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                ref var site = ref sim.Sites.At(i);
                var c = PlanetClasses.Of(in site, sim.Balance.Universe);
                if (c == PlanetClass.Terrestrial || c == PlanetClass.SuperEarth)
                    rocky[site.ClusterId] = true;
                else
                    giant[site.ClusterId] = true;
            }
            for (int c = 0; c < clusters; c++)
            {
                Assert.IsTrue(rocky[c], $"cluster {c} has a rocky world");
                Assert.IsTrue(giant[c], $"cluster {c} has a giant");
            }
        }

        [Test]
        public void NewGame_RolledDeposits_OnlyOnPlanetsWhoseClassHoldsThem()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 21);
            NewGameSetup.Create(sim, 1);
            var rules = sim.Balance.NewGame;
            int mismatched = 0;
            for (int d = 0; d < sim.Deposits.Count; d++)
            {
                ref var deposit = ref sim.Deposits.At(d);
                var c = PlanetClasses.Of(in sim.Sites.Get(deposit.Site), sim.Balance.Universe);
                if (!rules.PlanetHolds(c, deposit.RawItem))
                    mismatched++;
            }
            // Only the fill-ins (a raw no planet of the system rolled) and the core base's own life support may break it.
            Assert.LessOrEqual(mismatched, sim.Deposits.Count / 10, "planet types decide what the ground holds");
        }

        [Test]
        public void GasGiants_HoldNoMetal_TerrestrialsNoMethane()
        {
            var rules = DefaultGameBalance.CreateNewGameBalance();
            Assert.IsTrue(rules.PlanetHolds(PlanetClass.GasGiant, Unruled.Core.Economy.ItemId.RawMethane));
            Assert.IsFalse(rules.PlanetHolds(PlanetClass.GasGiant, Unruled.Core.Economy.ItemId.RawIron));
            Assert.IsFalse(rules.PlanetHolds(PlanetClass.Terrestrial, Unruled.Core.Economy.ItemId.RawMethane));
            Assert.IsTrue(rules.PlanetHolds(PlanetClass.Terrestrial, Unruled.Core.Economy.ItemId.RawUranium));
        }
    }
}
