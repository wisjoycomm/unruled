using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>
    /// A - B - C by highways (200 each: 40 + 40 ticks), A - D - C by bypasses (150 each at 60%: 50 + 50 ticks) and a
    /// direct A - C storm shortcut (300 at 90%: 67 ticks, tech tier 4+). E is cut off.
    /// </summary>
    public class FleetRouteTests
    {
        private const int Me = 1;
        private const long Start = 10;
        private const int ShipCount = 10;

        private SimulationWorld _sim;
        private EntityId _a, _b, _c, _d, _e;
        private EntityId _ab, _bc, _ad, _dc, _ac;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            _a = Site();
            _b = Site();
            _c = Site();
            _d = Site();
            _e = Site();
            _ab = Lane(_a, _b, StarlaneTier.StaHighway, 200);
            _bc = Lane(_b, _c, StarlaneTier.StaHighway, 200);
            _ad = Lane(_a, _d, StarlaneTier.OuterRimBypass, 150);
            _dc = Lane(_d, _c, StarlaneTier.OuterRimBypass, 150);
            _ac = Lane(_a, _c, StarlaneTier.HazardStormShortcut, 300);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _sim.CreatePlayer(Me, _a);
        }

        private EntityId Site()
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, default);
            return site;
        }

        private EntityId Lane(EntityId a, EntityId b, StarlaneTier tier, int length)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = tier, Length = length });
            return lane;
        }

        private EntityId Fleet(byte tier = 1, int fuel = 100)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = tier, LineShips = ShipCount }, _a);
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemFuel, fuel);
            return fleet;
        }

        private void Order(CommandType type, EntityId fleet, long at, EntityId param = default) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = at, PlayerId = Me, CommandType = type,
                Target = fleet, ParamEntity = param
            });

        private void RunTo(long tick) => _sim.Clock.Advance(tick - _sim.Clock.CurrentTick);

        private ref FleetMotion M(EntityId fleet) => ref _sim.FleetMotions.Get(fleet);

        private int Fuel(EntityId fleet) => _sim.Stockpiles.Get(fleet).Get(ItemId.ItemFuel);

        private int HighwayFuel => _sim.Movement.FuelFor(new FleetUnit { LineShips = ShipCount }, _sim.Starlanes.Get(_ab));

        [Test]
        public void Planner_PicksTheFastestLegalRoute()
        {
            var route = default(FleetRoute);
            Assert.IsTrue(_sim.Routes.TryPlan(_a, _c, 1, ref route));
            Assert.AreEqual(2, route.Count);
            Assert.AreEqual(_ab, route.LaneAt(0), "80 ticks of highway beat 100 of bypass");
            Assert.AreEqual(_bc, route.LaneAt(1));

            Assert.IsTrue(_sim.Routes.TryPlan(_a, _c, 4, ref route));
            Assert.AreEqual(1, route.Count);
            Assert.AreEqual(_ac, route.LaneAt(0), "tier 4 may take the 67-tick shortcut");

            Assert.IsFalse(_sim.Routes.TryPlan(_a, _e, 5, ref route), "cut off");
            Assert.IsFalse(_sim.Routes.TryPlan(_a, _a, 5, ref route), "already there");
            Assert.AreEqual(0, route.Count);
        }

        [Test]
        public void Planner_TollFree_SkipsTheStaHighways()
        {
            var route = default(FleetRoute);
            Assert.IsTrue(_sim.Routes.TryPlan(_a, _c, 1, tollFree: true, ref route));
            Assert.AreEqual(2, route.Count);
            Assert.AreEqual(_ad, route.LaneAt(0), "the bypass charges no toll");
            Assert.AreEqual(_dc, route.LaneAt(1));
            Assert.IsFalse(_sim.Routes.TryPlan(_a, _b, 1, tollFree: true, ref route), "B is only on highways");
        }

        [Test]
        public void MoveToSite_TollFree_TakesTheBypass()
        {
            var fleet = Fleet(fuel: 1_000);
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = Start, PlayerId = Me, CommandType = CommandType.Move,
                Target = fleet, ParamEntity = _c, ParamC = 1
            });
            RunTo(Start);
            Assert.AreEqual(_ad, M(fleet).Lane);
        }

        [Test]
        public void MoveToSite_DepartsEachLaneOnArrival_PayingFuelPerLane()
        {
            var fleet = Fleet();
            Order(CommandType.Move, fleet, Start, _c);

            RunTo(Start);
            Assert.AreEqual(_ab, M(fleet).Lane);
            Assert.AreEqual(100 - HighwayFuel, Fuel(fleet), "only the first lane is paid at the order");

            RunTo(Start + 39);
            Assert.AreEqual(_bc, M(fleet).Lane, "arrived at B and left again the same tick");
            Assert.AreEqual(100 - 2 * HighwayFuel, Fuel(fleet));

            RunTo(Start + 79);
            Assert.AreEqual(_c, M(fleet).Site);
            Assert.AreEqual(_bc, M(fleet).ArrivalLane);
            Assert.AreEqual(0, _sim.FleetRoutes.Get(fleet).Count, "the route is used up");
        }

        [Test]
        public void OutOfFuelMidRoute_StopsAtTheSite_AndDropsTheRoute()
        {
            var fleet = Fleet(fuel: HighwayFuel);
            Order(CommandType.Move, fleet, Start, _c);
            RunTo(Start + 60);

            Assert.AreEqual(_b, M(fleet).Site);
            Assert.AreEqual(0, _sim.FleetRoutes.Get(fleet).Count);
        }

        [Test]
        public void Refused_MidLane_Unreachable_OrWithoutFuel_AndReverseDropsTheRoute()
        {
            var cutOff = Fleet();
            var dry = Fleet(fuel: 0);
            var moving = Fleet();
            Order(CommandType.Move, cutOff, Start, _e);
            Order(CommandType.Move, dry, Start, _c);
            Order(CommandType.Move, moving, Start, _c);
            Order(CommandType.Move, moving, Start + 5, _d);
            RunTo(Start + 5);

            Assert.AreEqual(_a, M(cutOff).Site);
            Assert.AreEqual(_a, M(dry).Site);
            Assert.AreEqual(0, _sim.FleetRoutes.Get(dry).Count);
            Assert.AreEqual(_ab, M(moving).Lane, "no re-plan in the middle of a lane");
            Assert.AreEqual(2, _sim.FleetRoutes.Get(moving).Count);

            Order(CommandType.Reverse, moving, Start + 6);
            RunTo(Start + 20);
            Assert.AreEqual(_a, M(moving).Site, "reversed home, not on to C");
            Assert.AreEqual(0, _sim.FleetRoutes.Get(moving).Count);
        }
    }
}
