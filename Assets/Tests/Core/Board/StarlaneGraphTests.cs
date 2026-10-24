using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Board
{
    public class StarlaneGraphTests
    {
        private static SimulationWorld Build(ulong seed)
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            sim.GenerateSystem(seed, clusterId: 0);
            return sim;
        }

        private static List<(int, int, StarlaneTier, int)> Lanes(SimulationWorld sim)
        {
            var result = new List<(int, int, StarlaneTier, int)>();
            for (int i = 0; i < sim.Starlanes.Count; i++)
            {
                var lane = sim.Starlanes.At(i);
                result.Add((sim.Sites.Get(lane.SiteA).OrbitIndex, sim.Sites.Get(lane.SiteB).OrbitIndex, lane.Tier, lane.Length));
            }
            return result;
        }

        [Test]
        public void SameSeed_SameLanes_DifferentSeeds_Differ()
        {
            CollectionAssert.AreEqual(Lanes(Build(42)), Lanes(Build(42)));
            CollectionAssert.AreNotEqual(Lanes(Build(42)), Lanes(Build(43)));
        }

        [Test]
        public void Highways_ChainEveryOrbit_SoTheSystemIsConnected()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sim = Build(seed);
                int sites = sim.Sites.Count;
                var lanes = Lanes(sim);
                for (int orbit = 0; orbit + 1 < sites; orbit++)
                    Assert.IsTrue(lanes.Exists(l => l.Item1 == orbit && l.Item2 == orbit + 1 && l.Item3 == StarlaneTier.StaHighway),
                        $"seed {seed}: highway {orbit}-{orbit + 1}");

                Assert.AreEqual(sites, CountReachable(sim, sim.Sites.EntityAt(0)), $"seed {seed} connected");
            }
        }

        [Test]
        public void TierSpans_AndLengthsMatchPositions()
        {
            var balance = DefaultGameBalance.CreateStarlaneBalance();
            var tiersSeen = new HashSet<StarlaneTier>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sim = Build(seed);
                for (int i = 0; i < sim.Starlanes.Count; i++)
                {
                    var lane = sim.Starlanes.At(i);
                    var a = sim.Sites.Get(lane.SiteA);
                    var b = sim.Sites.Get(lane.SiteB);
                    int span = b.OrbitIndex - a.OrbitIndex;
                    int expectedSpan = lane.Tier == StarlaneTier.StaHighway ? 1
                        : lane.Tier == StarlaneTier.OuterRimBypass ? 2 : balance.ShortcutSpan;
                    Assert.AreEqual(expectedSpan, span, $"seed {seed} {lane.Tier}");

                    long dx = a.PosX - b.PosX, dy = a.PosY - b.PosY;
                    long squared = dx * dx + dy * dy;
                    Assert.That((long)lane.Length * lane.Length, Is.LessThanOrEqualTo(squared));
                    Assert.That((long)(lane.Length + 1) * (lane.Length + 1), Is.GreaterThan(squared));
                    Assert.That(lane.Length, Is.GreaterThanOrEqualTo(sim.Balance.Universe.MinSiteSpacing));
                    tiersSeen.Add(lane.Tier);
                }
            }
            Assert.AreEqual(3, tiersSeen.Count, "20 seeds should produce all three tiers");
        }

        [Test]
        public void Graph_ListsBothDirections_InEdgeOrder()
        {
            var sim = Build(7);
            int slots = 0;
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                var site = sim.Sites.EntityAt(i);
                int previousEdge = -1;
                for (int k = sim.Graph.Start(site); k < sim.Graph.End(site); k++)
                {
                    var lane = sim.Starlanes.Get(sim.Graph.EdgeAt(k));
                    Assert.AreEqual(sim.Graph.NeighborAt(k), lane.OtherEnd(site));
                    Assert.Greater(sim.Graph.EdgeAt(k).Index, previousEdge);
                    previousEdge = sim.Graph.EdgeAt(k).Index;
                    slots++;
                }
            }
            Assert.AreEqual(sim.Starlanes.Count * 2, slots);
            Assert.AreEqual(0, sim.Graph.End(new EntityId(9999, 1)), "unknown site has no lanes");
        }

        [Test]
        public void FixedMathSqrt_IsExactFloor()
        {
            long[] values = { 0, 1, 2, 3, 4, 15, 16, 17, 1_000_000, 999_999, long.MaxValue };
            foreach (var v in values)
            {
                long r = FixedMath.Sqrt(v);
                Assert.That(r * r, Is.LessThanOrEqualTo(v), $"{v}");
                Assert.That((r + 1) > 3_037_000_499L || (r + 1) * (r + 1) > v, $"{v}");
            }
        }

        private static int CountReachable(SimulationWorld sim, EntityId start)
        {
            var seen = new HashSet<EntityId> { start };
            var queue = new Queue<EntityId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var site = queue.Dequeue();
                for (int k = sim.Graph.Start(site); k < sim.Graph.End(site); k++)
                    if (seen.Add(sim.Graph.NeighborAt(k)))
                        queue.Enqueue(sim.Graph.NeighborAt(k));
            }
            return seen.Count;
        }
    }
}
