using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>Scanner (player 1) and hider (player 2) each own a base; the belt lane runs S0 - S1, a highway S1 - S2.</summary>
    public class FocusedRadarSystemTests
    {
        private const int Scanner = 1;
        private const int Hider = 2;
        private const long Start = 10;

        private SimulationWorld _sim;
        private RadarBalance _b;
        private EntityId _scannerBase;
        private EntityId _hiderBase;
        private EntityId _belt;
        private EntityId _highway;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            _b = _sim.Balance.Radar;
            _scannerBase = Site(0);
            var s1 = Site(1);
            _hiderBase = Site(2);
            _belt = Lane(_scannerBase, s1, StarlaneTier.OuterRimBypass);
            _highway = Lane(s1, _hiderBase, StarlaneTier.StaHighway);
            Hq(Scanner, _scannerBase);
            Hq(Hider, _hiderBase);
            Batteries(_scannerBase, 10);
            Batteries(_hiderBase, 10);
        }

        private EntityId Site(int x)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { PosX = x * 200 });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private EntityId Lane(EntityId a, EntityId b, StarlaneTier tier)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = tier, Length = 200 });
            return lane;
        }

        private void Hq(int player, EntityId coreBase)
        {
            var hq = _sim.World.Create();
            _sim.Players.Add(hq, new PlayerHq { PlayerId = player, CoreBase = coreBase });
        }

        private void Batteries(EntityId site, int delta) => _sim.Stockpiles.Get(site).Add(ItemId.ItemBattery, delta);

        private int BatteriesAt(EntityId site) => _sim.Stockpiles.Get(site).Get(ItemId.ItemBattery);

        private EntityId Fleet(int owner, EntityId lane, int ewShips)
        {
            var fleet = _sim.World.Create();
            _sim.Fleets.Add(fleet, new FleetPresence { OwnerPlayerId = owner, Lane = lane, EwShips = ewShips });
            return fleet;
        }

        private ref FleetPresence F(EntityId fleet) => ref _sim.Fleets.Get(fleet);

        private void Order(CommandType type, int player, EntityId target, long at) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = at, PlayerId = player, CommandType = type,
                Target = target
            });

        private void RunTo(long tick) => _sim.Clock.Advance(tick - _sim.Clock.CurrentTick);

        [Test]
        public void ScanBelt_PaysStartupBatteriesAtOnce()
        {
            Order(CommandType.ScanBelt, Scanner, _belt, Start);
            RunTo(Start);

            Assert.AreEqual(10 - _b.StartupBatteries, BatteriesAt(_scannerBase));
            Assert.AreEqual(1, _sim.RadarScans.Count);
        }

        [Test]
        public void ScanBelt_Rejected_OnHighway_WithoutBatteries_OrWhenAlreadyScanning()
        {
            Order(CommandType.ScanBelt, Scanner, _highway, Start);
            RunTo(Start);
            Assert.AreEqual(0, _sim.RadarScans.Count, "highways are not belts");

            Order(CommandType.ScanBelt, Scanner, _belt, Start + 1);
            Order(CommandType.ScanBelt, Scanner, _belt, Start + 2);
            RunTo(Start + 2);
            Assert.AreEqual(1, _sim.RadarScans.Count, "one scan per lane per player");
            Assert.AreEqual(10 - _b.StartupBatteries, BatteriesAt(_scannerBase));

            Batteries(_hiderBase, -10);
            Order(CommandType.ScanBelt, Hider, _belt, Start + 3);
            RunTo(Start + 3);
            Assert.AreEqual(1, _sim.RadarScans.Count, "no batteries, no scan");
        }

        [Test]
        public void Rwr_FiresAtFifteenTicks_OnlyForHostileEwFleetsOnTheLane()
        {
            var warned = Fleet(Hider, _belt, ewShips: 1);
            var blind = Fleet(Hider, _belt, ewShips: 0);
            var own = Fleet(Scanner, _belt, ewShips: 1);
            var elsewhere = Fleet(Hider, _highway, ewShips: 1);

            Order(CommandType.ScanBelt, Scanner, _belt, Start);
            RunTo(Start + _b.RwrDelayTicks - 1);
            Assert.AreEqual(0, F(warned).RwrWarningTick);

            RunTo(Start + _b.RwrDelayTicks);
            Assert.AreEqual(Start + _b.RwrDelayTicks, F(warned).RwrWarningTick);
            Assert.AreEqual(0, F(blind).RwrWarningTick);
            Assert.AreEqual(0, F(own).RwrWarningTick);
            Assert.AreEqual(0, F(elsewhere).RwrWarningTick);
        }

        [Test]
        public void Reveal_AfterFullIntegration_ReversingFleetsSlipAway()
        {
            var stays = Fleet(Hider, _belt, ewShips: 0);
            var runs = Fleet(Hider, _belt, ewShips: 1);

            Order(CommandType.ScanBelt, Scanner, _belt, Start);
            Order(CommandType.Reverse, Hider, runs, Start + _b.RwrDelayTicks + 1);
            RunTo(Start + _b.IntegrationTicks - 1);
            Assert.AreEqual(0, F(stays).RevealedUntilTick, "nothing before the full integration time");

            RunTo(Start + _b.IntegrationTicks);
            Assert.AreEqual(Start + _b.IntegrationTicks + _b.RevealTicks, F(stays).RevealedUntilTick);
            Assert.AreEqual(0, F(runs).RevealedUntilTick);
            Assert.IsTrue(F(runs).IsReversing);
            Assert.AreEqual(0, _sim.RadarScans.Count, "finished scans are removed");
        }

        [Test]
        public void Reverse_OnlyForOwnFleet()
        {
            var fleet = Fleet(Hider, _belt, ewShips: 0);
            Order(CommandType.Reverse, Scanner, fleet, Start);
            RunTo(Start);
            Assert.IsFalse(F(fleet).IsReversing);
        }

        [Test]
        public void Ecm_StretchesScanToJammedTime_PaidByJammer_OnlyAfterRwr()
        {
            var hider = Fleet(Hider, _belt, ewShips: 1);
            Order(CommandType.EcmJam, Hider, hider, Start - 1);
            Order(CommandType.ScanBelt, Scanner, _belt, Start);
            Order(CommandType.EcmJam, Hider, hider, Start + _b.RwrDelayTicks + 1);

            RunTo(Start + _b.IntegrationTicks);
            Assert.AreEqual(10 - _b.JamBatteries, BatteriesAt(_hiderBase), "the early jam (no warning yet) was free and void");
            Assert.AreEqual(0, F(hider).RevealedUntilTick);

            RunTo(Start + _b.JammedIntegrationTicks - 1);
            Assert.AreEqual(0, F(hider).RevealedUntilTick);
            RunTo(Start + _b.JammedIntegrationTicks);
            Assert.Greater(F(hider).RevealedUntilTick, 0);
        }

        [Test]
        public void StrobeExploit_EachStartCostsBatteries_CancelledScansRevealNothing()
        {
            var hider = Fleet(Hider, _belt, ewShips: 0);
            for (int i = 0; i < 3; i++)
            {
                long at = Start + i * 10;
                Order(CommandType.ScanBelt, Scanner, _belt, at);
                Order(CommandType.CancelScan, Scanner, _belt, at + 5);
            }

            RunTo(Start + 200);

            Assert.AreEqual(10 - 3 * _b.StartupBatteries, BatteriesAt(_scannerBase));
            Assert.AreEqual(0, F(hider).RevealedUntilTick);
            Assert.AreEqual(0, _sim.RadarScans.Count);
        }
    }
}
