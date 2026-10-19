using NUnit.Framework;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    /// <summary>
    /// Long AI-only games on several seeds, checking what keeps a long game playable: no negative stock or cash, no
    /// fleet left in pieces (WTO contract convoys stranded at home), the core never out of fuel. Slow (minutes), so
    /// Explicit: run on request or through tools/simlab batch.
    /// </summary>
    [Explicit("Slow: AI-only multi-day soak")]
    public class LongRunSoakTests
    {
        [TestCase(1234UL)]
        [TestCase(1236UL)]
        public void AiOnlyGame_ThreeDays_StaysWhole(ulong seed)
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), seed);
            NewGameSetup.Create(sim, 1);
            sim.Ai.Assign(sim.Players.EntityAt(0), 2);
            for (long t = 0; t < 3 * SimTick.TicksPerDay; t++)
            {
                sim.Clock.Advance(1);
                sim.Ai.Think(sim.Clock.CurrentTick);
            }

            for (int i = 0; i < sim.Stockpiles.Count; i++)
                for (int slot = 0; slot < ItemIds.Count; slot++)
                    Assert.GreaterOrEqual(sim.Stockpiles.At(i).Get(ItemIds.FromSlot(slot)), 0, "no negative stock");
            for (int i = 0; i < sim.Wallets.Count; i++)
                Assert.GreaterOrEqual(sim.Wallets.At(i).Cents, 0, "no negative wallet");
            for (int p = 0; p < sim.Players.Count; p++)
            {
                int playerId = sim.Players.At(p).PlayerId;
                int fleets = 0, ships = 0;
                for (int f = 0; f < sim.Fleets.Count; f++)
                    if (sim.Fleets.At(f).OwnerPlayerId == playerId)
                    {
                        fleets++;
                        ships += sim.Fleets.At(f).TotalShips;
                    }
                Assert.LessOrEqual(fleets, 12 + ships / 10, $"player {playerId}: fleets left in pieces ({fleets} fleets, {ships} ships)");
                Assert.Greater(sim.Stockpiles.Get(sim.Players.At(p).CoreBase).Get(ItemId.ItemFuel), 0,
                    $"player {playerId}: core fuel ran out");
            }
        }
    }
}
