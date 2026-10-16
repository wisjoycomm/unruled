using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>
    /// Player 1's core base Old is linked by highways to New (also player 1's) and to the outpost X. An escort fleet
    /// sits at Old, whose stockpile holds enough for both relocation costs.
    /// </summary>
    public class CapitalRelocationSystemTests
    {
        private const int Me = 1;
        private const int Rival = 2;

        private SimulationWorld _sim;
        private RelocationBalance _b;
        private EntityId _old, _new, _outpost;
        private EntityId _toOutpost;
        private EntityId _hq;
        private EntityId _escort;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 7);
            _b = _sim.Balance.Relocation;
            _old = Site(0, Me);
            _new = Site(400, Me);
            _outpost = Site(-400, 0);
            Lane(_old, _new);
            _toOutpost = Lane(_old, _outpost);
            _sim.Graph.Rebuild(_sim.Starlanes);

            _hq = _sim.World.Create();
            _sim.Players.Add(_hq, new PlayerHq { PlayerId = Me, CoreBase = _old });
            _escort = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = 1, LineShips = 5 }, _old);

            ref var stock = ref _sim.Stockpiles.Get(_old);
            stock.Add(ItemId.ItemAlloy, 100);
            stock.Add(ItemId.ItemFuel, 100);
            stock.Add(ItemId.ItemBattery, 100);
            stock.Add(ItemId.ItemCoolant, 100);
        }

        private EntityId Site(int x, int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { PosX = x, OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private EntityId Lane(EntityId a, EntityId b)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = StarlaneTier.StaHighway, Length = 400 });
            return lane;
        }

        private void OrderRelocation(EntityId destination, long at) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = at, PlayerId = Me,
                CommandType = CommandType.RelocateBase, Target = destination
            });

        private void RunTo(long tick) => _sim.Clock.Advance(tick - _sim.Clock.CurrentTick);

        private CapitalRelocation R => _sim.Relocations.Has(_hq) ? _sim.Relocations.Get(_hq) : default;

        private ref PlayerHq Hq => ref _sim.Players.Get(_hq);

        private int Stock(ItemId item) => _sim.Stockpiles.Get(_old).Get(item);

        [Test]
        public void ThreePhases_MoveTheCoreBase_ThenCoolDown()
        {
            OrderRelocation(_new, 1);
            RunTo(1);
            Assert.AreEqual(RelocationPhase.Teardown, R.Phase);
            Assert.That(R.PhaseEndTick, Is.InRange(1 + _b.TeardownMinTicks, 1 + _b.TeardownMaxTicks));
            Assert.AreEqual(100 - 20, Stock(ItemId.ItemAlloy));
            Assert.AreEqual(100 - 50, Stock(ItemId.ItemFuel));
            Assert.AreEqual(100 - 40, Stock(ItemId.ItemBattery));
            Assert.AreEqual(100 - 20, Stock(ItemId.ItemCoolant));

            long convoyStart = R.PhaseEndTick;
            RunTo(convoyStart);
            Assert.AreEqual(RelocationPhase.Convoy, R.Phase);
            Assert.That(R.PhaseEndTick, Is.InRange(convoyStart + _b.ConvoyMinTicks, convoyStart + _b.ConvoyMaxTicks));
            Assert.IsTrue(Hq.IsCapitalConvoyInTransit);
            var convoy = R.Convoy;
            Assert.AreEqual(1, _sim.Fleets.Get(convoy).CargoConvoys);
            Assert.AreEqual(LatencyStatus.Ok, _sim.Latency.TryComputeToSite(Me, _old, out int latency));
            Assert.AreEqual(_sim.Balance.Latency.BaseTicks * _sim.Balance.Latency.RelocationMultiplier, latency);

            long bootstrapStart = R.PhaseEndTick;
            RunTo(bootstrapStart);
            Assert.AreEqual(RelocationPhase.Bootstrap, R.Phase);
            Assert.IsFalse(_sim.World.IsAlive(convoy), "the convoy has arrived");
            Assert.IsFalse(Hq.IsCapitalConvoyInTransit);
            Assert.AreEqual(_old, Hq.CoreBase);

            RunTo(bootstrapStart + _b.BootstrapTicks);
            Assert.AreEqual(RelocationPhase.None, R.Phase);
            Assert.AreEqual(_new, Hq.CoreBase);
            Assert.AreEqual(bootstrapStart + _b.BootstrapTicks + _b.CooldownTicks, R.CooldownUntilTick);
            _sim.Latency.TryComputeToSite(Me, _new, out latency);
            Assert.AreEqual(_sim.Balance.Latency.BaseTicks, latency, "latency is now measured from the new base");

            _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = 1, LineShips = 1 }, _new);
            _sim.Stockpiles.Get(_new).Add(ItemId.ItemAlloy, 100);
            OrderRelocation(_old, _sim.Clock.CurrentTick + 1);
            RunTo(_sim.Clock.CurrentTick + 1);
            Assert.AreEqual(RelocationPhase.None, R.Phase, "48 h cooldown");
        }

        [Test]
        public void Locked_ByHostilesAtTheBaseOrOnItsLanes()
        {
            var raider = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Rival, TechTier = 1, EwShips = 1 }, _outpost);
            Assert.IsFalse(_sim.Relocation.IsUnderHostileLock(Me, _old), "one hop away is fine");

            _sim.FleetMotions.Get(raider) = new FleetMotion { Lane = _toOutpost, FromSite = _outpost, IsHolding = true };
            Assert.IsTrue(_sim.Relocation.IsUnderHostileLock(Me, _old));
            OrderRelocation(_new, 1);
            RunTo(1);
            Assert.AreEqual(RelocationPhase.None, R.Phase);
            Assert.AreEqual(100, Stock(ItemId.ItemAlloy), "a refused order costs nothing");

            _sim.FleetMotions.Get(raider) = new FleetMotion { Site = _old };
            Assert.IsTrue(_sim.Relocation.IsUnderHostileLock(Me, _old));
        }

        [Test]
        public void Refused_WithoutEscort_Payment_OrAnOwnedDestination()
        {
            OrderRelocation(_outpost, 1);
            RunTo(1);
            Assert.AreEqual(RelocationPhase.None, R.Phase, "destination not owned");

            _sim.Stockpiles.Get(_old).Add(ItemId.ItemFuel, -60);
            OrderRelocation(_new, 2);
            RunTo(2);
            Assert.AreEqual(RelocationPhase.None, R.Phase, "cannot pay");
            _sim.Stockpiles.Get(_old).Add(ItemId.ItemFuel, 60);

            _sim.World.Destroy(_escort);
            OrderRelocation(_new, 3);
            RunTo(3);
            Assert.AreEqual(RelocationPhase.None, R.Phase, "no escort");
            Assert.AreEqual(100, Stock(ItemId.ItemAlloy));
        }

        [Test]
        public void ConvoyDestroyed_AbortsAndParalysesOrders()
        {
            OrderRelocation(_new, 1);
            RunTo(1);
            RunTo(R.PhaseEndTick);
            Assert.AreEqual(RelocationPhase.Convoy, R.Phase);

            _sim.World.Destroy(R.Convoy);
            long lost = _sim.Clock.CurrentTick + 1;
            RunTo(lost);
            Assert.AreEqual(RelocationPhase.None, R.Phase);
            Assert.AreEqual(lost + _b.ParalysisTicks, R.ParalysedUntilTick);
            Assert.IsFalse(Hq.IsCapitalConvoyInTransit);
            Assert.AreEqual(_old, Hq.CoreBase);

            var order = new DelayedPlayerCommand { PlayerId = Me, CommandType = CommandType.Hold, Target = _escort, ParamA = 1 };
            Assert.AreEqual(IssueResult.Paralysed, _sim.Issuer.Issue(order, out _));
            RunTo(lost + _b.ParalysisTicks);
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(order, out _));
        }

        [Test]
        public void Teardown_HalvesTheOldBaseFortification()
        {
            var combat = _sim.Balance.Combat;
            var defender = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = 1, EwShips = 5 }, _old);
            _sim.Stockpiles.Get(defender).Add(ItemId.ItemBattery, 100);
            OrderRelocation(_new, 1);
            RunTo(1);

            var attacker = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Rival, TechTier = 1, LineShips = 12 }, _old);
            _sim.Stockpiles.Get(attacker).Add(ItemId.ItemAmmo, 100);
            RunTo(combat.SalvoCycleTicks);

            // Psi 12 x 10 = 120 against Omega 5 x 20 x (1 + 0.35 x 50%) = 117: 3 points get through.
            int fortification = combat.FortificationPdPercent * _b.TeardownDefencePercent / 100;
            long omega = 5L * combat.EwBeta * 100 * (100 + fortification) / 100;
            Assert.AreEqual(12L * combat.LineAlpha * 100 - omega, _sim.FleetCombat.Get(_escort).DamageCarry);
        }
    }
}
