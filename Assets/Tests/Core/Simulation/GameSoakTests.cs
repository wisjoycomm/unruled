using NUnit.Framework;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    /// <summary>A whole new game with AI rivals, run like the host runs it: tick, then think.</summary>
    public class GameSoakTests
    {
        [Test]
        public void NewGame_RunsSixHoursWithAiRivals_WithoutBreakingInvariants()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1234);
            NewGameSetup.Create(sim, 1);
            int orders = 0;
            for (long t = 0; t < 6 * 3_600; t++)
            {
                sim.Clock.Advance(1);
                orders += sim.Ai.Think(sim.Clock.CurrentTick);
            }

            Assert.Greater(orders, 0, "the rivals acted");
            for (int i = 0; i < sim.Stockpiles.Count; i++)
                for (int slot = 0; slot < ItemIds.Count; slot++)
                    Assert.GreaterOrEqual(sim.Stockpiles.At(i).Get(ItemIds.FromSlot(slot)), 0, "no negative stock");
            for (int i = 0; i < sim.Wallets.Count; i++)
                Assert.GreaterOrEqual(sim.Wallets.At(i).Cents, 0, "no negative wallet");
        }
    }
}
