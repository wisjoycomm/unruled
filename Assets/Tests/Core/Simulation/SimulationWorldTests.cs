using System;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    public class SimulationWorldTests
    {
        private const ulong Seed = 0xC0FFEEUL;

        private static SimulationWorld BuildWorld(ulong seed)
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            SystemSeedGenerator.GenerateSystem(sim.World, sim.Sites, sim.Balance.Universe, seed, clusterId: 0);

            var site = sim.Sites.EntityAt(0);
            sim.Stockpiles.Add(site, default);
            ref var stock = ref sim.Stockpiles.Get(site);
            stock.Add(ItemId.ItemFuel, 500);
            stock.Add(ItemId.ItemCoolant, 500);
            stock.Add(ItemId.ItemOxygen, 50);
            stock.Add(ItemId.RawWater, 5);
            sim.Quarantines.Add(site, default);

            AddDeposit(sim, site, ItemId.RawWater, 200);
            AddDeposit(sim, site, ItemId.RawNitrogen, 200);

            var plant = sim.World.Create();
            sim.Refineries.Add(plant, new Refinery { Site = site, Output = ItemId.ItemOxygen });
            return sim;
        }

        private static void AddDeposit(SimulationWorld sim, EntityId site, ItemId raw, int deep)
        {
            var deposit = sim.World.Create();
            sim.Deposits.Add(deposit, new MineralDeposit
            {
                Site = site, RawItem = raw, SurfaceReserve = 100, DeepCrustReserve = deep, RigCount = 1
            });
        }

        [Test]
        public void SystemsRunInOrder_MinedOreIsRefinedSameCycle()
        {
            var sim = BuildWorld(Seed);
            var site = sim.Sites.EntityAt(0);
            int cycle = sim.Balance.Mining.CycleTicks;
            Assert.AreEqual(cycle, sim.Balance.Refinery.CycleTicks, "test assumes aligned cycles");

            sim.Clock.Advance(cycle);

            ref var stock = ref sim.Stockpiles.Get(site);
            Assert.AreEqual(51, stock.Get(ItemId.ItemOxygen), "nitrogen mined this tick was refined this tick");
            Assert.AreEqual(RefineryState.ActiveProduction, sim.Refineries.At(0).State);
            Assert.IsFalse(sim.Quarantines.Get(site).IsExportLocked(sim.Clock.CurrentTick));
        }

        [Test]
        public void LongRun_DepletesSite_AndStaysConsistent()
        {
            var sim = BuildWorld(Seed);
            var site = sim.Sites.EntityAt(0);

            sim.Clock.Advance(4000); // 300 nitrogen at 1 per refinery cycle runs out at cycle 300 (tick 3000)

            Assert.AreEqual(PlanetLifecycleState.DepletedBarrenHusk, sim.Sites.Get(site).LifecycleState);
            Assert.AreEqual(RefineryState.IdleStarved, sim.Refineries.At(0).State);
        }

        [Test]
        public void SameSeed_SameState_DifferentSeed_DifferentState()
        {
            var a = BuildWorld(Seed);
            var b = BuildWorld(Seed);
            var c = BuildWorld(Seed + 1);
            a.Clock.Advance(1000);
            b.Clock.Advance(1000);
            c.Clock.Advance(1000);

            Assert.AreEqual(Hash(a), Hash(b));
            Assert.AreNotEqual(Hash(a), Hash(c));
        }

        [Test]
        public void Router_SendsCommandToItsHandler_AndRejectsUnknownTypes()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            var handler = new CountingHandler();
            sim.Commands.Register(CommandType.Hold, handler);
            Assert.Throws<InvalidOperationException>(() => sim.Commands.Register(CommandType.Hold, handler));

            sim.Clock.Enqueue(new DelayedPlayerCommand { ExecuteAtTick = 5, CommandType = CommandType.Hold });
            sim.Clock.Advance(5);
            Assert.AreEqual(1, handler.Count);

            sim.Clock.Enqueue(new DelayedPlayerCommand { ExecuteAtTick = 6, CommandType = CommandType.Move });
            Assert.Throws<InvalidOperationException>(() => sim.Clock.Advance(1));
        }

        private sealed class CountingHandler : ICommandHandler
        {
            public int Count;
            public void Handle(in DelayedPlayerCommand command, long tick) => Count++;
        }

        private static ulong Hash(SimulationWorld sim)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long value)
            {
                h ^= (ulong)value;
                h *= 1099511628211UL;
            }

            for (int i = 0; i < sim.Sites.Count; i++)
            {
                ref var s = ref sim.Sites.At(i);
                Mix(s.PosX); Mix(s.PosY); Mix(s.CurrentEsiMilli); Mix((long)s.LifecycleState);
            }
            for (int i = 0; i < sim.Stockpiles.Count; i++)
            {
                ref var stock = ref sim.Stockpiles.At(i);
                for (int item = 1; item <= ItemIds.Count; item++)
                    Mix(stock.Get((ItemId)item));
            }
            for (int i = 0; i < sim.Deposits.Count; i++)
            {
                ref var d = ref sim.Deposits.At(i);
                Mix(d.SurfaceReserve); Mix(d.DeepCrustReserve);
            }
            for (int i = 0; i < sim.Refineries.Count; i++)
            {
                ref var r = ref sim.Refineries.At(i);
                Mix((long)r.State); Mix(r.AccruedUpkeep);
            }
            return h;
        }
    }
}
