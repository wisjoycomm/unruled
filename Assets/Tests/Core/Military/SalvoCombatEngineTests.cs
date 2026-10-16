using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>
    /// Defender (player 2) owns the citadel C at (0, 0) and its vault. Approach sites N (0, 500), E (500, 0) and
    /// W (-500, 0) each have a highway into C; the open lane L runs N - E.
    /// A "standard" fleet is 10 line + 5 EW ships: its Psi exactly equals its Omega.
    /// </summary>
    public class SalvoCombatEngineTests
    {
        private const int Attacker = 1;
        private const int Defender = 2;

        private SimulationWorld _sim;
        private CombatBalance _b;
        private EntityId _citadel, _north, _east, _west;
        private EntityId _fromNorth, _fromEast, _fromWest, _open;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            _b = _sim.Balance.Combat;
            _citadel = Site(0, 0, Defender);
            _north = Site(0, 500, 0);
            _east = Site(500, 0, 0);
            _west = Site(-500, 0, 0);
            _fromNorth = Lane(_north, _citadel);
            _fromEast = Lane(_east, _citadel);
            _fromWest = Lane(_west, _citadel);
            _open = Lane(_north, _east);
        }

        private EntityId Site(int x, int y, int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { PosX = x, PosY = y, OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private EntityId Lane(EntityId a, EntityId b)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = StarlaneTier.StaHighway, Length = 500 });
            return lane;
        }

        private EntityId Fleet(int owner, int line, int ew, int supplies, int tenders = 0)
        {
            var fleet = _sim.CreateFleet(
                new FleetUnit { OwnerPlayerId = owner, TechTier = 1, LineShips = line, EwShips = ew, SupplyTenders = tenders },
                _citadel);
            Give(fleet, supplies);
            return fleet;
        }

        private void Give(EntityId holder, int each)
        {
            ref var stock = ref _sim.Stockpiles.Get(holder);
            stock.Add(ItemId.ItemAmmo, each);
            stock.Add(ItemId.ItemBattery, each);
            stock.Add(ItemId.ItemCoolant, each);
        }

        private EntityId AtCitadelFrom(EntityId fleet, EntityId arrivalLane)
        {
            _sim.FleetMotions.Get(fleet) = new FleetMotion { Site = _citadel, ArrivalLane = arrivalLane };
            return fleet;
        }

        private EntityId OnOpenLane(EntityId fleet)
        {
            _sim.FleetMotions.Get(fleet) = new FleetMotion { Lane = _open, FromSite = _north, TravelledMilli = 250_000, IsHolding = true };
            return fleet;
        }

        private void Order(CommandType type, EntityId fleet, int player, long at, int paramA = 0) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = at, PlayerId = player, CommandType = type,
                Target = fleet, ParamA = paramA
            });

        private void Salvos(int count) => _sim.Clock.Advance((long)count * _b.SalvoCycleTicks);

        private ref FleetUnit U(EntityId fleet) => ref _sim.Fleets.Get(fleet);

        private ref FleetCombatState C(EntityId fleet) => ref _sim.FleetCombat.Get(fleet);

        [Test]
        public void InTransit_OneVsOne_IsAStalemate()
        {
            var a = OnOpenLane(Fleet(Attacker, 10, 5, supplies: 100));
            var d = OnOpenLane(Fleet(Defender, 10, 5, supplies: 100));

            Salvos(40);

            Assert.AreEqual(15, U(a).TotalShips);
            Assert.AreEqual(15, U(d).TotalShips);
            Assert.Less(_sim.Stockpiles.Get(a).Get(ItemId.ItemAmmo), 100, "both sides fired");
        }

        [Test]
        public void PlanetarySiege_OneLaneOneToOne_DefenderWins()
        {
            var a = AtCitadelFrom(Fleet(Attacker, 10, 5, supplies: 20), _fromNorth);
            var d = Fleet(Defender, 10, 5, supplies: 0);
            Give(_citadel, 1000);

            Salvos(100);

            Assert.IsFalse(_sim.World.IsAlive(a), "the attacker runs dry and is destroyed");
            Assert.AreEqual(15, U(d).TotalShips, "the defender loses nothing");
            Assert.Less(_sim.Stockpiles.Get(_citadel).Get(ItemId.ItemAmmo), 1000, "fed from the vault");
        }

        [Test]
        public void ThreeLaneFlank_BreaksTheDefence_WhereOneLaneDoesNot()
        {
            Assert.AreEqual(60, _b.FlankPercent(2));

            var single = Fleet(Defender, 10, 5, supplies: 0);
            Give(_citadel, 1000);
            var one = AtCitadelFrom(Fleet(Attacker, 12, 6, supplies: 100), _fromNorth);
            Salvos(1);
            Assert.AreEqual(15, U(single).TotalShips, "12 line ships on one lane do not get through 135% PD");

            _sim.World.Destroy(one);
            AtCitadelFrom(Fleet(Attacker, 4, 2, supplies: 100), _fromNorth);
            AtCitadelFrom(Fleet(Attacker, 4, 2, supplies: 100), _fromEast);
            AtCitadelFrom(Fleet(Attacker, 4, 2, supplies: 100), _fromWest);
            Salvos(1);
            // Psi 12 x 10 = 120 against Omega 5 x 20 x 1.35 x 0.60 = 81: 39 damage on the line ships.
            Assert.AreEqual(10, U(single).LineShips);
            Assert.AreEqual(39 * 100, C(single).DamageCarry);
            Salvos(2);
            Assert.AreEqual(9, U(single).LineShips);
        }

        [Test]
        public void TwoFleetsOnTheSameLane_AreNotAFlank()
        {
            var d = Fleet(Defender, 0, 5, supplies: 0);
            Give(_citadel, 1000);
            AtCitadelFrom(Fleet(Attacker, 6, 0, supplies: 100), _fromEast);
            AtCitadelFrom(Fleet(Attacker, 6, 0, supplies: 100), _fromEast);
            Salvos(1);
            // Same lane twice: no flank. Psi 120 - Omega 135 = 0.
            Assert.AreEqual(5, U(d).EwShips);
            Assert.AreEqual(0, C(d).DamageCarry);
        }

        [Test]
        public void NoAmmo_NoFire_NoBattery_NoPointDefence()
        {
            var dry = OnOpenLane(Fleet(Attacker, 10, 5, supplies: 0));
            var d = OnOpenLane(Fleet(Defender, 10, 5, supplies: 0));
            _sim.Stockpiles.Get(d).Add(ItemId.ItemAmmo, 100);

            Salvos(1);

            Assert.AreEqual(15, U(d).TotalShips, "the dry fleet cannot fire");
            Assert.AreEqual(9, U(dry).LineShips, "and has no PD: 100 damage kills one line ship");
        }

        [Test]
        public void SupplyTenders_HotResupplyFriendlyFleets()
        {
            var empty = OnOpenLane(Fleet(Attacker, 10, 0, supplies: 0));
            var tender = OnOpenLane(Fleet(Attacker, 0, 0, supplies: 50, tenders: 1));
            var target = OnOpenLane(Fleet(Defender, 0, 1, supplies: 0));

            Salvos(1);

            Assert.AreEqual(50 - _b.TenderTransferPerSalvo, _sim.Stockpiles.Get(tender).Get(ItemId.ItemAmmo));
            Assert.AreEqual(_b.SalvoCycleTicks, C(empty).FiredAtTick, "fired with the handed-over ammo");
            Assert.IsFalse(_sim.World.IsAlive(target));
        }

        [Test]
        public void Overdrive_Overheats_LocksGuns_CooksOff_UntilVentedBelowRelease()
        {
            var hot = OnOpenLane(Fleet(Attacker, 10, 0, supplies: 100));
            OnOpenLane(Fleet(Defender, 0, 20, supplies: 1000)); // soaks every salvo
            Order(CommandType.SetRof, hot, Attacker, 1, (int)RofStance.Overdrive150);

            // +4 x 1.5² - 4 = +5 °C per salvo: 20 -> 90 after 14 salvos, over the line on the 15th.
            Salvos(14);
            Assert.IsFalse(C(hot).IsOverheated);
            Salvos(1);
            Assert.IsTrue(C(hot).IsOverheated);
            Assert.AreEqual(95_000, C(hot).TemperatureMilli);
            long carry = C(hot).DamageCarry;
            Assert.AreEqual(10 * 100 * 100 * _b.CookOffHullPercent / 100, carry, "5% of hull cooks off");

            int ammo = _sim.Stockpiles.Get(hot).Get(ItemId.ItemAmmo);
            Salvos(1);
            Assert.AreEqual(ammo, _sim.Stockpiles.Get(hot).Get(ItemId.ItemAmmo), "locked guns use no ammo");
            Assert.AreEqual(9, U(hot).LineShips, "the second cook-off destroys a ship");

            Order(CommandType.VentCoolant, hot, Attacker, _sim.Clock.CurrentTick + 1);
            Salvos(1);
            // 91 - 4 passive - 15 vent = 72 °C: below the 75 °C release line, so the guns unlock and venting stops.
            Assert.AreEqual(72_000, C(hot).TemperatureMilli);
            Assert.IsFalse(C(hot).IsOverheated);
            Assert.IsFalse(C(hot).IsVenting);
            Assert.AreEqual(99, _sim.Stockpiles.Get(hot).Get(ItemId.ItemCoolant), "9 ships x 0.2 coolant, rounded down until due");
        }

        [Test]
        public void EngagedFleets_ArePinnedOnTheirLane_UnlessReversing()
        {
            var mover = OnOpenLane(Fleet(Attacker, 1, 0, supplies: 100));
            _sim.FleetMotions.Get(mover).IsHolding = false;
            OnOpenLane(Fleet(Defender, 0, 5, supplies: 100));

            long before = _sim.FleetMotions.Get(mover).TravelledMilli;
            Salvos(2);
            long pinned = _sim.FleetMotions.Get(mover).TravelledMilli;
            Assert.AreEqual((long)_b.SalvoCycleTicks * _sim.Balance.Fleets.BaseSpeedMilliPerTick, pinned - before,
                "moves only until the first salvo");

            Order(CommandType.Reverse, mover, Attacker, _sim.Clock.CurrentTick + 1);
            Salvos(1);
            Assert.IsTrue(_sim.FleetMotions.Get(mover).IsReversing);
            Assert.Greater(_sim.FleetMotions.Get(mover).TravelledMilli, 500_000 - pinned, "reversing fleets break away");
        }
    }
}
