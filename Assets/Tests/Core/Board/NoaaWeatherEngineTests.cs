using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Board
{
    public class NoaaWeatherEngineTests
    {
        private const ulong SystemSeed = 99;

        private static GameBalanceConfig WithWeather(NoaaBalance weather)
        {
            var d = DefaultGameBalance.Create();
            return new GameBalanceConfig(d.Recipes, d.Universe, d.Starlanes, weather, d.Refinery, d.Mining, d.Lifecycle, d.Latency, d.Radar, d.Fleets, d.Combat, d.Relocation, d.Ledger, d.Sta, d.Leaderboard, d.Wto, d.Contracts, d.Authority,
                d.SystemFactions);
        }

        private static NoaaBalance NoSpawns() =>
            new NoaaBalance(600, 0, new[] { 1, 0, 0, 0, 0 }, 100, 100, 100, 100);

        private static SimulationWorld Calm()
        {
            var sim = new SimulationWorld(WithWeather(NoSpawns()), worldSeed: 1);
            sim.GenerateSystem(SystemSeed, clusterId: 0);
            return sim;
        }

        private static EntityId AddFront(SimulationWorld sim, SiteInfo over, StormScale scale, byte level, int radius = 10, long end = 1000)
        {
            var front = sim.World.Create();
            sim.StormFronts.Add(front, new StormFront
            {
                ClusterId = over.ClusterId, Scale = scale, Level = level, CenterX = over.PosX, CenterY = over.PosY,
                Radius = radius, EndTick = end
            });
            return front;
        }

        private static List<StormFront> Fronts(SimulationWorld sim)
        {
            var list = new List<StormFront>();
            for (int i = 0; i < sim.StormFronts.Count; i++)
                list.Add(sim.StormFronts.At(i));
            return list;
        }

        [Test]
        public void SameWorldSeed_SameStorms_DifferentSeed_DifferentStorms()
        {
            SimulationWorld Run(ulong worldSeed)
            {
                var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed);
                sim.GenerateSystem(SystemSeed, 0);
                sim.GenerateSystem(SystemSeed + 1, 1);
                sim.Clock.Advance(20_000);
                return sim;
            }

            var a = Fronts(Run(5));
            Assert.Greater(a.Count, 0, "default odds should spawn something in 20k ticks");
            CollectionAssert.AreEqual(a, Fronts(Run(5)));
            CollectionAssert.AreNotEqual(a, Fronts(Run(6)));
        }

        [Test]
        public void SpawnedFronts_RespectBalanceRanges_AndCentreOnASite()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 3);
            sim.GenerateSystem(SystemSeed, 0);
            var balance = sim.Balance.Weather;
            var centres = new HashSet<(int, int)>();
            for (int i = 0; i < sim.Sites.Count; i++)
                centres.Add((sim.Sites.At(i).PosX, sim.Sites.At(i).PosY));

            int seen = 0;
            for (int roll = 1; roll <= 200; roll++)
            {
                sim.Clock.Advance(balance.RollIntervalTicks);
                for (int i = 0; i < sim.StormFronts.Count; i++)
                {
                    var front = sim.StormFronts.At(i);
                    Assert.That(front.Level, Is.InRange(1, 5));
                    Assert.That(front.Radius, Is.InRange(balance.MinRadius, balance.MaxRadius));
                    Assert.That(front.EndTick - sim.Clock.CurrentTick, Is.LessThanOrEqualTo(balance.MaxDurationTicks));
                    Assert.Greater(front.EndTick, sim.Clock.CurrentTick, "expired fronts are removed");
                    Assert.IsTrue(centres.Contains((front.CenterX, front.CenterY)));
                    seen++;
                }
            }
            Assert.Greater(seen, 0);
        }

        [Test]
        public void SiteFeelsStrongestCoveringFrontPerScale_AndFrontsExpire()
        {
            var sim = Calm();
            var site = sim.Sites.EntityAt(2);
            var info = sim.Sites.Get(site);
            AddFront(sim, info, StormScale.Geomagnetic, 2);
            AddFront(sim, info, StormScale.Geomagnetic, 4, end: 5);
            AddFront(sim, info, StormScale.Radio, 3);

            sim.Clock.Advance(1);
            var weather = sim.Weather.Get(site);
            Assert.AreEqual(4, weather.GeomagneticStormLevel);
            Assert.AreEqual(3, weather.RadioBlackoutLevel);
            Assert.AreEqual(0, weather.SolarRadiationLevel);
            Assert.AreEqual(default(SiteWeather), sim.Weather.Get(sim.Sites.EntityAt(0)), "small fronts don't reach other orbits");

            sim.Clock.Advance(4);
            Assert.AreEqual(2, sim.Weather.Get(site).GeomagneticStormLevel, "the G4 front ended at tick 5");
            Assert.AreEqual(2, sim.StormFronts.Count);
        }

        [Test]
        public void IrradiatedSite_HeldAtSolarIrradiationLevel_UntilRadiationEnds()
        {
            var sim = Calm();
            var site = sim.Sites.EntityAt(1);
            sim.Lifecycle.ApplyNuclearStrike(site, 0);
            long until = sim.Sites.Get(site).IrradiatedUntilTick;

            sim.Clock.Advance(1);
            Assert.AreEqual(sim.Balance.Lifecycle.IrradiationSolarLevel, sim.Weather.Get(site).SolarRadiationLevel);

            sim.Clock.Advance(until - 1);
            Assert.AreEqual(0, sim.Weather.Get(site).SolarRadiationLevel);
        }

        [Test]
        public void LaneWeather_IsStrongerEnd_CappedByTierExposure()
        {
            var sim = Calm();
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                var info = sim.Sites.At(i);
                AddFront(sim, info, StormScale.Geomagnetic, 5);
                AddFront(sim, info, StormScale.Radio, 1);
            }

            sim.Clock.Advance(1);

            Assert.AreEqual(sim.Starlanes.Count, sim.LaneWeather.Count);
            for (int i = 0; i < sim.Starlanes.Count; i++)
            {
                var lane = sim.Starlanes.At(i);
                var weather = sim.LaneWeather.Get(sim.Starlanes.EntityAt(i));
                byte cap = sim.Balance.Starlanes.Get(lane.Tier).MaxStormLevel;
                Assert.AreEqual(cap, weather.GeomagneticStormLevel, $"{lane.Tier} G");
                Assert.AreEqual(System.Math.Min(cap, (byte)1), weather.RadioBlackoutLevel, $"{lane.Tier} R");
            }
        }

        [Test]
        public void GeomagneticStorm_BrownsOutRefinery_EndToEnd()
        {
            var sim = Calm();
            var site = sim.Sites.EntityAt(0);
            sim.Stockpiles.Add(site, default);
            ref var stock = ref sim.Stockpiles.Get(site);
            stock.Add(ItemId.RawWater, 10);
            stock.Add(ItemId.RawNitrogen, 10);
            var plant = sim.World.Create();
            sim.Refineries.Add(plant, new Refinery { Site = site, Output = ItemId.ItemOxygen });

            AddFront(sim, sim.Sites.Get(site), StormScale.Geomagnetic, (byte)sim.Balance.Refinery.BrownoutMinGeoLevel);
            sim.Clock.Advance(sim.Balance.Refinery.CycleTicks);

            Assert.AreEqual(RefineryState.GridBrownout, sim.Refineries.Get(plant).State);
        }
    }
}
