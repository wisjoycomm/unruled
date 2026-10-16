using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Economy
{
    public class LedgerTests
    {
        private SimulationWorld _sim;

        [SetUp]
        public void SetUp() => _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 11);

        private long TotalCents()
        {
            long total = 0;
            for (int i = 0; i < _sim.Wallets.Count; i++)
                total += _sim.Wallets.At(i).Cents;
            return total;
        }

        [Test]
        public void CreatePlayer_GrantsStartingCredits_AndPoolsStartEmpty()
        {
            var hq = _sim.CreatePlayer(1, default);
            Assert.AreEqual(_sim.Balance.Ledger.StartingCents, _sim.Ledger.Balance(hq));
            Assert.IsTrue(_sim.Ledger.TryFindPlayerWallet(1, out var found));
            Assert.AreEqual(hq, found);
            Assert.AreEqual(0, _sim.Ledger.Balance(_sim.WtoFeePool));
            Assert.AreEqual(0, _sim.Ledger.Balance(_sim.StaTollPool));
            Assert.IsTrue(_sim.Ledger.TryFindPool(TaxPoolKind.StaTollGates, out var sta));
            Assert.AreEqual(_sim.StaTollPool, sta);
            Assert.IsFalse(_sim.Ledger.TryFindPlayerWallet(0, out _), "player 0 is nobody");
            Assert.Throws<System.InvalidOperationException>(() => _sim.CreatePlayer(1, default));
        }

        [Test]
        public void Transfer_MovesMoney_RefusesOverdraft_AndNeverCreatesAny()
        {
            var a = _sim.CreatePlayer(1, default);
            var b = _sim.CreatePlayer(2, default);
            long start = TotalCents();

            Assert.IsTrue(_sim.Ledger.Transfer(a, _sim.WtoFeePool, 250));
            Assert.IsTrue(_sim.Ledger.Transfer(a, b, 1_000));
            Assert.IsFalse(_sim.Ledger.Transfer(b, a, _sim.Ledger.Balance(b) + 1), "overdraft");
            Assert.IsFalse(_sim.Ledger.Transfer(a, _sim.World.Create(), 1), "no wallet there");
            Assert.AreEqual(start, TotalCents());
            Assert.AreEqual(250, _sim.Ledger.Balance(_sim.WtoFeePool));

            Assert.IsTrue(_sim.Ledger.Burn(b, 300));
            Assert.IsFalse(_sim.Ledger.Burn(a, _sim.Ledger.Balance(a) + 1));
            Assert.AreEqual(start - 300, TotalCents());
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _sim.Ledger.Transfer(a, b, -1));
        }

        [Test]
        public void SeededRun_OnlyUpkeepRemovesMoney()
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = 1 });
            _sim.Stockpiles.Add(site, default);
            _sim.Stockpiles.Get(site).Add(ItemId.RawWater, 50);
            _sim.Stockpiles.Get(site).Add(ItemId.RawNitrogen, 50);
            var plant = _sim.World.Create();
            _sim.Refineries.Add(plant, new Refinery { Site = site, Output = ItemId.ItemOxygen });
            _sim.CreatePlayer(1, site);
            long start = TotalCents();

            var refinery = _sim.Balance.Refinery;
            _sim.Clock.Advance(5L * refinery.CycleTicks);

            Assert.AreEqual(start - 5L * refinery.BaseUpkeepCentsPerCycle, TotalCents());
            Assert.AreEqual(0, _sim.Refineries.Get(plant).AccruedUpkeepCents);
        }
    }
}
