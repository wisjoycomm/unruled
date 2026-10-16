using System;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Board
{
    public class SystemSeedGeneratorTests
    {
        private static readonly UniverseGenBalance Balance = DefaultGameBalance.CreateUniverseGenBalance();

        private static SiteInfo[] Generate(ulong seed, UniverseGenBalance balance = null)
        {
            var world = new World();
            var sites = world.CreateStore<SiteInfo>();
            SystemSeedGenerator.GenerateSystem(world, sites, balance ?? Balance, seed, clusterId: 7);
            var result = new SiteInfo[sites.Count];
            for (int i = 0; i < sites.Count; i++)
                result[i] = sites.At(i);
            return result;
        }

        [Test]
        public void SameSeed_ProducesIdenticalSystem()
        {
            var a = Generate(123456789UL);
            var b = Generate(123456789UL);
            Assert.AreEqual(Balance.SitesPerSystem, a.Length);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSystems()
        {
            CollectionAssert.AreNotEqual(Generate(1UL), Generate(2UL));
        }

        [Test]
        public void Sites_StartPristineNeutral_InOrbitOrder()
        {
            var sites = Generate(99UL);
            for (int i = 0; i < sites.Length; i++)
            {
                Assert.AreEqual(i, sites[i].OrbitIndex);
                Assert.AreEqual(7, sites[i].ClusterId);
                Assert.AreEqual(PlanetLifecycleState.PristineWorld, sites[i].LifecycleState);
                Assert.AreEqual(0, sites[i].OwnerPlayerId);
                Assert.AreEqual(sites[i].BaseEsiMilli, sites[i].CurrentEsiMilli);
                Assert.That(sites[i].BaseEsiMilli, Is.InRange(0, SystemSeedGenerator.EsiMaxMilli));
            }
        }

        [Test]
        public void AnySeed_KeepsMinimumSpacingBetweenSites()
        {
            long minSquared = (long)Balance.MinSiteSpacing * Balance.MinSiteSpacing;
            for (ulong seed = 0; seed < 300; seed++)
            {
                var sites = Generate(seed * 0x9E3779B97F4A7C15UL);
                for (int i = 0; i < sites.Length; i++)
                for (int j = i + 1; j < sites.Length; j++)
                {
                    long dx = sites[i].PosX - sites[j].PosX;
                    long dy = sites[i].PosY - sites[j].PosY;
                    Assert.GreaterOrEqual(dx * dx + dy * dy, minSquared, $"seed {seed}, sites {i}/{j}");
                }
            }
        }

        [Test]
        public void Esi_PeaksInHabitableZone_AndOuterOrbitIsHarsh()
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var sites = Generate(seed);
                int best = 0;
                for (int i = 1; i < sites.Length; i++)
                    if (sites[i].BaseEsiMilli > sites[best].BaseEsiMilli)
                        best = i;

                Assert.GreaterOrEqual(sites[best].BaseEsiMilli, 700, $"seed {seed}: habitable world (ESI >= 0.70)");
                Assert.LessOrEqual(sites[sites.Length - 1].BaseEsiMilli, 350, $"seed {seed}: harsh outer world (ESI <= 0.35)");
            }
        }

        [Test]
        public void Balance_RejectsSpacingThatCannotBeGuaranteed()
        {
            Assert.Throws<ArgumentException>(() => new UniverseGenBalance(
                sitesPerSystem: 8, minRadius: 300, radiusStep: 150, radiusNoise: 20, minSiteSpacing: 120,
                angleStep: 40503, angleJitter: 0, habitableRadius: 840, habitableWidth: 700, atmoNoiseMilli: 0));
        }
    }
}
