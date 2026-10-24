using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.Galaxy;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Galaxy
{
    public class GalaxySystemTests
    {
        private static StarRecord FirstStar(SimulationWorld sim, int sectorX, int sectorY)
        {
            var stars = new List<StarRecord>();
            sim.Galaxy.Catalog.GetSector(sectorX, sectorY, stars);
            Assert.Greater(stars.Count, 0, "test sector has stars");
            return stars[0];
        }

        [Test]
        public void MaterialiseStar_GeneratesItsSystem_OnceUnderANewCluster()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 17);
            var star = FirstStar(sim, 0, 0);

            int cluster = sim.MaterialiseStar(star.Id);
            Assert.AreEqual(0, cluster);
            Assert.AreEqual(sim.Balance.Universe.SitesPerSystem, sim.Sites.Count);
            Assert.AreEqual(1, sim.StarSystems.Count);
            Assert.AreEqual(star.Id.Packed, sim.StarSystems.At(0).StarId);
            Assert.AreEqual(star.X, sim.StarSystems.At(0).X);
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                Assert.AreEqual(cluster, sim.Sites.At(i).ClusterId);
                Assert.IsTrue(sim.Stockpiles.Has(sim.Sites.EntityAt(i)));
            }

            Assert.AreEqual(cluster, sim.MaterialiseStar(star.Id), "a star materialises once");
            Assert.AreEqual(1, sim.StarSystems.Count);
            Assert.IsTrue(sim.Galaxy.IsMaterialised(star.Id));
        }

        [Test]
        public void SameStar_SameSystem_InAnyGameWithTheSameSeed()
        {
            var a = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 5);
            var b = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 5);
            var star = FirstStar(a, 3, 2);
            a.MaterialiseStar(star.Id);
            b.MaterialiseStar(star.Id);
            for (int i = 0; i < a.Sites.Count; i++)
            {
                Assert.AreEqual(a.Sites.At(i).PosX, b.Sites.At(i).PosX);
                Assert.AreEqual(a.Sites.At(i).BaseEsiMilli, b.Sites.At(i).BaseEsiMilli);
            }
        }

        [Test]
        public void SystemOwner_IsThePlayerHoldingMostPlanets_TiesKeepTheOwner()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 17);
            int cluster = sim.MaterialiseStar(FirstStar(sim, 0, 0).Id);
            sim.Sites.At(0).OwnerPlayerId = 2;
            sim.Clock.Advance(GalaxySystem.OwnerRefreshTicks);
            Assert.AreEqual(2, sim.StarSystems.Get(sim.Galaxy.SystemOf(cluster)).OwnerPlayerId);

            sim.Sites.At(1).OwnerPlayerId = 3; // a tie: player 2 keeps it
            sim.Clock.Advance(GalaxySystem.OwnerRefreshTicks);
            Assert.AreEqual(2, sim.StarSystems.Get(sim.Galaxy.SystemOf(cluster)).OwnerPlayerId);

            sim.Sites.At(2).OwnerPlayerId = 3;
            sim.Clock.Advance(GalaxySystem.OwnerRefreshTicks);
            Assert.AreEqual(3, sim.StarSystems.Get(sim.Galaxy.SystemOf(cluster)).OwnerPlayerId);
        }

        [TestCase("NEW EDEN", true)]
        [TestCase("ab", false)]
        [TestCase("lowercase", false)]
        [TestCase(" EDGE", false)]
        [TestCase("THIS NAME IS FAR TOO LONG", false)]
        [TestCase("K-9 STATION", true)]
        public void SystemName_Validation(string text, bool valid)
        {
            Assert.AreEqual(valid, SystemName.IsValid(text));
        }

        [Test]
        public void NewGame_GivesEveryClusterASystem()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 9);
            NewGameSetup.Create(sim, 1);
            int clusters = sim.Galaxy.NextClusterId();
            Assert.AreEqual(clusters, sim.StarSystems.Count);
            for (int c = 0; c < clusters; c++)
                Assert.IsFalse(sim.Galaxy.SystemOf(c).IsNull);
        }

        [Test]
        public void SavedSystemsAndNames_RoundTrip()
        {
            var balance = DefaultGameBalance.Create();
            var sim = new SimulationWorld(balance, worldSeed: 17);
            var star = FirstStar(sim, 0, 0);
            int cluster = sim.MaterialiseStar(star.Id);
            var system = sim.Galaxy.SystemOf(cluster);
            sim.SystemNames.Add(system, SystemName.From("NEW EDEN", 4, 99));

            var loaded = SaveGame.Load(balance, SaveGame.Write(sim));
            Assert.IsTrue(loaded.Galaxy.IsMaterialised(star.Id));
            Assert.AreEqual(system, loaded.Galaxy.SystemOf(cluster));
            var name = loaded.SystemNames.Get(system);
            var chars = new char[SystemName.Capacity];
            Assert.AreEqual("NEW EDEN", new string(chars, 0, name.CopyTo(chars)));
            Assert.AreEqual(4, name.RenamedByPlayerId);
            CollectionAssert.AreEqual(SaveGame.Write(sim), SaveGame.Write(loaded));
        }

        // Migration: schema 5 had 38 stores and no galaxy; its clusters load as uncatalogued systems.
        [Test]
        public void Schema5Save_GivesEachClusterAnUncataloguedSystem()
        {
            var balance = DefaultGameBalance.Create();
            var sim = new SimulationWorld(balance, worldSeed: 9);
            NewGameSetup.Create(sim, 1);
            int clusters = sim.Galaxy.NextClusterId();

            var loaded = SaveGame.Load(balance, SaveGame.Write(sim, schemaVersion: 5, storeCount: 38));
            Assert.AreEqual(clusters, loaded.StarSystems.Count);
            for (int i = 0; i < loaded.StarSystems.Count; i++)
                Assert.AreEqual(0L, loaded.StarSystems.At(i).StarId);
            Assert.AreEqual(0, loaded.SystemNames.Count);
            CollectionAssert.AreEqual(SaveGame.Write(loaded), SaveGame.Write(SaveGame.Load(balance, SaveGame.Write(loaded))),
                "resaved as the current schema");
        }
    }
}
