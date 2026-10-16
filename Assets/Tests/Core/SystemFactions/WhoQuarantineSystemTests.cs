using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.SystemFactions
{
    public class WhoQuarantineSystemTests
    {
        private ComponentStore<Stockpile> _stockpiles;
        private ComponentStore<SiteQuarantine> _quarantines;
        private SystemFactionBalance _balance;
        private WhoQuarantineSystem _system;
        private EntityId _site;
        private long _tick;

        [SetUp]
        public void SetUp()
        {
            var world = new World();
            _stockpiles = world.CreateStore<Stockpile>();
            _quarantines = world.CreateStore<SiteQuarantine>();
            _balance = DefaultGameBalance.CreateSystemFactionBalance();
            _system = new WhoQuarantineSystem(_quarantines, _stockpiles, _balance);

            _site = world.Create();
            _stockpiles.Add(_site, default);
            _quarantines.Add(_site, default);
            _tick = 0;
        }

        private ref SiteQuarantine Quarantine => ref _quarantines.Get(_site);

        private void Run(long ticks)
        {
            for (long i = 0; i < ticks; i++)
                _system.OnTick(++_tick);
        }

        private void Supply()
        {
            _stockpiles.Get(_site).Add(ItemId.RawWater, 10);
            _stockpiles.Get(_site).Add(ItemId.ItemOxygen, 10);
        }

        [Test]
        public void SuppliedSite_NeverQuarantined()
        {
            Supply();
            Run(500);
            Assert.IsFalse(Quarantine.IsExportLocked(_tick));
            Assert.AreEqual(0, Quarantine.StarvedSinceTick);
        }

        [Test]
        public void StarvedExactlyForWindow_NotYetQuarantined_OneMoreTickLocks()
        {
            _stockpiles.Get(_site).Add(ItemId.RawWater, 10);

            Run(_balance.WhoStarvationTicks + 1);
            Assert.IsFalse(Quarantine.IsExportLocked(_tick), "30 starved ticks is not more than 30");

            Run(1);
            Assert.IsTrue(Quarantine.IsExportLocked(_tick));
            Assert.AreEqual(_tick + _balance.WhoQuarantineTicks, Quarantine.QuarantineUntilTick);
        }

        [Test]
        public void Quarantine_ExpiresAfterPeriod_OnceResupplied()
        {
            Run(_balance.WhoStarvationTicks + 2);
            long lockedAt = _tick;
            Supply();

            Run(_balance.WhoQuarantineTicks - 1);
            Assert.IsTrue(Quarantine.IsExportLocked(_tick));
            Run(1);
            Assert.AreEqual(lockedAt + _balance.WhoQuarantineTicks, _tick);
            Assert.IsFalse(Quarantine.IsExportLocked(_tick));
        }

        [Test]
        public void ResupplyInsideWindow_ResetsStarvationCounter()
        {
            Run(_balance.WhoStarvationTicks);
            Supply();
            Run(1);
            _stockpiles.Get(_site).Add(ItemId.ItemOxygen, -10);

            Run(_balance.WhoStarvationTicks);

            Assert.IsFalse(Quarantine.IsExportLocked(_tick));
        }

        [Test]
        public void StillStarvingWhenQuarantineEnds_IsLockedAgain()
        {
            Run(_balance.WhoStarvationTicks + 2);
            long firstUntil = Quarantine.QuarantineUntilTick;

            Run(_balance.WhoQuarantineTicks);

            Assert.IsTrue(Quarantine.IsExportLocked(_tick));
            Assert.Greater(Quarantine.QuarantineUntilTick, firstUntil);
        }
    }
}
