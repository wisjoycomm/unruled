using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>Sites S0, S1, S2 in a row; S0 - S1 highway, S0 - S1 bypass, S1 - S2 storm shortcut; all lanes 200 long.</summary>
    public class FleetMovementSystemTests
    {
        private const int Me = 1;
        private const long Start = 10;

        private SimulationWorld _sim;
        private EntityId _s0, _s1, _s2;
        private EntityId _highway, _bypass, _shortcut;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 1);
            _s0 = Site(0);
            _s1 = Site(1);
            _s2 = Site(2);
            _highway = Lane(_s0, _s1, StarlaneTier.StaHighway);
            _bypass = Lane(_s0, _s1, StarlaneTier.OuterRimBypass);
            _shortcut = Lane(_s1, _s2, StarlaneTier.HazardStormShortcut);
            _sim.CreatePlayer(Me, _s0);
        }

        private EntityId Site(int x)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { PosX = x * 200 });
            return site;
        }

        private EntityId Lane(EntityId a, EntityId b, StarlaneTier tier)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = tier, Length = 200 });
            return lane;
        }

        private EntityId Fleet(byte tier = 1, int ships = 10, int fuel = 100)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = tier, LineShips = ships }, _s0);
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemFuel, fuel);
            return fleet;
        }

        private void Order(CommandType type, EntityId fleet, long at, EntityId lane = default, int paramA = 0, int player = Me)
        {
            var command = new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = at, PlayerId = player, CommandType = type,
                Target = fleet
            };
            if (lane.IsNull)
                command.ParamA = paramA;
            else
                command.ParamEntity = lane;
            _sim.Clock.Enqueue(command);
        }

        private void RunTo(long tick) => _sim.Clock.Advance(tick - _sim.Clock.CurrentTick);

        private ref FleetMotion M(EntityId fleet) => ref _sim.FleetMotions.Get(fleet);

        private int Fuel(EntityId fleet) => _sim.Stockpiles.Get(fleet).Get(ItemId.ItemFuel);

        // The order runs at Start and the fleet already advances on that tick.
        private long ArrivalTick(StarlaneTier tier)
        {
            var b = _sim.Balance;
            long speed = (long)b.Fleets.BaseSpeedMilliPerTick * b.Starlanes.Get(tier).SpeedPercent / 100;
            return Start + (200_000 + speed - 1) / speed - 1;
        }

        [Test]
        public void Move_ArrivesAtTheFarEnd_AtLaneSpeed()
        {
            var fast = Fleet();
            var slow = Fleet();
            Order(CommandType.Move, fast, Start, _highway);
            Order(CommandType.Move, slow, Start, _bypass);

            RunTo(ArrivalTick(StarlaneTier.StaHighway) - 1);
            Assert.AreEqual(_highway, M(fast).Lane);
            RunTo(ArrivalTick(StarlaneTier.StaHighway));
            Assert.AreEqual(_s1, M(fast).Site);
            Assert.AreEqual(_highway, M(fast).ArrivalLane);
            Assert.IsTrue(M(fast).Lane.IsNull);

            Assert.AreEqual(_bypass, M(slow).Lane, "the bypass is slower");
            RunTo(ArrivalTick(StarlaneTier.OuterRimBypass));
            Assert.AreEqual(_s1, M(slow).Site);
        }

        [Test]
        public void Move_PaysFuel_ByShipsLengthAndTier()
        {
            var highway = Fleet();
            var bypass = Fleet();
            Order(CommandType.Move, highway, Start, _highway);
            Order(CommandType.Move, bypass, Start, _bypass);
            RunTo(Start);

            // 10 ships x fuel per ship per 100 units x 2 (200 units): x1.0 on the highway, x4.5 on the bypass.
            var b = _sim.Balance;
            int highwayFuel = _sim.Movement.FuelFor(_sim.Fleets.Get(highway), _sim.Starlanes.Get(_highway));
            int bypassFuel = _sim.Movement.FuelFor(_sim.Fleets.Get(bypass), _sim.Starlanes.Get(_bypass));
            Assert.AreEqual(10 * b.Fleets.FuelPerShipPer100UnitsMilli * 2 / 1000, highwayFuel);
            Assert.AreEqual((10 * b.Fleets.FuelPerShipPer100UnitsMilli * 2 * 4500 / 1000 + 999) / 1000, bypassFuel);
            Assert.AreEqual(100 - highwayFuel, Fuel(highway));
            Assert.AreEqual(100 - bypassFuel, Fuel(bypass));
        }

        [Test]
        public void Move_Refused_WithoutFuel_OffSite_OnShortcutBelowTier_OrForAnotherPlayer()
        {
            var dry = Fleet(fuel: 0);
            var lowTier = Fleet(tier: 1);
            var highTier = Fleet(tier: 4);
            var notMine = Fleet();
            M(lowTier).Site = _s1;
            M(highTier).Site = _s1;

            Order(CommandType.Move, dry, Start, _highway);
            Order(CommandType.Move, lowTier, Start, _shortcut);
            Order(CommandType.Move, highTier, Start, _shortcut);
            Order(CommandType.Move, notMine, Start, _highway, player: 2);
            Order(CommandType.Move, notMine, Start, _shortcut);
            RunTo(Start);

            Assert.AreEqual(_s0, M(dry).Site, "no fuel");
            Assert.AreEqual(_s1, M(lowTier).Site, "shortcuts need tech tier 4");
            Assert.AreEqual(_shortcut, M(highTier).Lane);
            Assert.AreEqual(_s0, M(notMine).Site, "another player's order and a lane not from its site");
            Assert.AreEqual(100, Fuel(lowTier), "refused orders cost nothing");
            Assert.AreEqual(100, Fuel(notMine));
        }

        [Test]
        public void Hold_StopsMidLane_ResumeContinues()
        {
            var fleet = Fleet();
            Order(CommandType.Move, fleet, Start, _highway);
            Order(CommandType.Hold, fleet, Start + 5, paramA: 1);
            Order(CommandType.Hold, fleet, Start + 100, paramA: 0);

            RunTo(Start + 99);
            Assert.AreEqual(_highway, M(fleet).Lane);
            Assert.IsTrue(M(fleet).IsHolding);
            long held = M(fleet).TravelledMilli;
            Assert.AreEqual(5L * _sim.Balance.Fleets.BaseSpeedMilliPerTick, held, "moved on ticks Start..Start+4 only");

            RunTo(ArrivalTick(StarlaneTier.StaHighway) + 95);
            Assert.AreEqual(_s1, M(fleet).Site);
        }

        [Test]
        public void Reverse_ReturnsToTheOrigin_InTheTimeAlreadyTravelled()
        {
            var fleet = Fleet();
            Order(CommandType.Move, fleet, Start, _highway);
            Order(CommandType.Reverse, fleet, Start + 10);

            RunTo(Start + 10);
            Assert.IsTrue(M(fleet).IsReversing);
            RunTo(Start + 18);
            Assert.AreEqual(_highway, M(fleet).Lane);
            RunTo(Start + 19);
            Assert.AreEqual(_s0, M(fleet).Site, "out on ticks Start..Start+9, back on Start+10..Start+19");
            Assert.IsFalse(M(fleet).IsReversing);
        }
    }
}
