using NUnit.Framework;
using Unruled.Core.Galaxy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Galaxy
{
    public class GalaxySpawnTests
    {
        [Test]
        public void NewGame_EverySystemIsACatalogStar_OfItsRole()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 77);
            NewGameSetup.Create(sim, 1);
            int participants = 1 + sim.Balance.NewGame.RivalCount;
            int contested = NewGameSetup.ContestedClusterId(participants);
            for (int c = 0; c < sim.Galaxy.NextClusterId(); c++)
            {
                var system = sim.StarSystems.Get(sim.Galaxy.SystemOf(c));
                Assert.AreNotEqual(0L, system.StarId, $"cluster {c} is a catalog star");
                Assert.IsTrue(sim.Galaxy.Catalog.TryGetStar(StarId.FromPacked(system.StarId), out var star));
                var expected = c < participants ? StarClass.Home : c == contested ? StarClass.Hub : StarClass.Frontier;
                Assert.AreEqual(expected, star.Class, $"cluster {c}");
            }
        }

        [Test]
        public void NewGame_SameSeed_SameStars_DifferentSeed_DifferentStars()
        {
            long First(ulong seed)
            {
                var sim = new SimulationWorld(DefaultGameBalance.Create(), seed);
                NewGameSetup.Create(sim, 1);
                return sim.StarSystems.Get(sim.Galaxy.SystemOf(0)).StarId;
            }
            Assert.AreEqual(First(5), First(5));
            Assert.AreNotEqual(First(5), First(6));
        }

        [Test]
        public void SpawnGroup_SitsTogether_OnTheSpawnRing()
        {
            var balance = DefaultGameBalance.CreateGalaxyBalance();
            var catalog = new GalaxyCatalog(balance, 1234);
            var picks = NewGameSetup.PickStars(catalog, homes: 6, frontiers: 6);
            var hub = picks[6];
            long ring = (long)balance.SpawnRingSectors * balance.SectorSize;
            long hubR = (long)System.Math.Sqrt((double)hub.X * hub.X + (double)hub.Y * hub.Y);
            Assert.That(hubR, Is.InRange(ring - 50L * balance.SectorSize, ring + 50L * balance.SectorSize), "hub near the ring");
            long reach = 2L * balance.SearchRadiusSectors * balance.SectorSize;
            for (int i = 0; i < picks.Length; i++)
            {
                long dx = picks[i].X - hub.X, dy = picks[i].Y - hub.Y;
                Assert.LessOrEqual(dx * dx + dy * dy, reach * reach, $"pick {i} close to its hub");
                for (int j = i + 1; j < picks.Length; j++)
                    Assert.AreNotEqual(picks[i].Id, picks[j].Id, "no star picked twice");
            }
        }
    }
}
