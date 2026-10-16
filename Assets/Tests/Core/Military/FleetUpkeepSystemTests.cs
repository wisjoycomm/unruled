using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>One player's fleets at S0, with a highway to S1.</summary>
    public class FleetUpkeepSystemTests
    {
        private const int Me = 1;

        private SimulationWorld _sim;
        private FleetBalance _b;
        private EntityId _s0;
        private EntityId _lane;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            _b = _sim.Balance.Fleets;
            _s0 = _sim.World.Create();
            _sim.Sites.Add(_s0, new SiteInfo());
            var s1 = _sim.World.Create();
            _sim.Sites.Add(s1, new SiteInfo { PosX = 200 });
            _lane = _sim.World.Create();
            _sim.Starlanes.Add(_lane, new StarlaneEdge { SiteA = _s0, SiteB = s1, Tier = StarlaneTier.StaHighway, Length = 200 });
        }

        private EntityId Fleet(byte tier, int ships)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = tier, LineShips = ships }, _s0);
            ref var hold = ref _sim.Stockpiles.Get(fleet);
            for (int i = 0; i < ItemIds.Count; i++)
                hold.Add(ItemIds.FromSlot(i), 1000);
            return fleet;
        }

        private int Held(EntityId fleet, ItemId item) => _sim.Stockpiles.Get(fleet).Get(item);

        private void Order(CommandType type, EntityId fleet, long at, EntityId lane = default) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = at, PlayerId = Me, CommandType = type,
                Target = fleet, ParamEntity = lane
            });

        private void RunTo(long tick) => _sim.Clock.Advance(tick - _sim.Clock.CurrentTick);

        [Test]
        public void Upkeep_IsPaidEachHour_PerShip_RoundedUp()
        {
            var fleet = Fleet(tier: 2, ships: 3); // 1 oxygen + 1 rations + 0.5 steel per ship

            RunTo(_b.UpkeepIntervalTicks - 1);
            Assert.AreEqual(1000, Held(fleet, ItemId.ItemOxygen), "nothing before the hour");

            RunTo(_b.UpkeepIntervalTicks);
            Assert.AreEqual(1000 - 3, Held(fleet, ItemId.ItemOxygen));
            Assert.AreEqual(1000 - 3, Held(fleet, ItemId.ItemRations));
            Assert.AreEqual(1000 - 2, Held(fleet, ItemId.ItemSteel), "1.5 steel rounds up to 2");
            Assert.IsFalse(_sim.FleetReadiness.Get(fleet).IsUnsupplied);
        }

        [Test]
        public void Mothball_CutsUpkeepByTheDiscount()
        {
            var fleet = Fleet(tier: 1, ships: 10);
            Order(CommandType.Mothball, fleet, 1);
            RunTo(_b.UpkeepIntervalTicks);

            int expected = FleetUpkeepSystem.Due(10, 1000, _b.Tier(1).MothballUpkeepDiscountPercent);
            Assert.AreEqual(2, expected, "80% off 10 oxygen");
            Assert.AreEqual(1000 - expected, Held(fleet, ItemId.ItemOxygen));
        }

        [Test]
        public void Activate_WaitsForTheTierWarmup_BeforeTheFleetCanMove()
        {
            var fleet = Fleet(tier: 3, ships: 1);
            Order(CommandType.Mothball, fleet, 1);
            Order(CommandType.Move, fleet, 2, _lane);
            Order(CommandType.Activate, fleet, 10);
            long ready = 10 + _b.Tier(3).MothballWarmupTicks;
            Order(CommandType.Move, fleet, ready - 1, _lane);

            RunTo(ready - 1);
            Assert.AreEqual(_s0, _sim.FleetMotions.Get(fleet).Site, "mothballed and warming fleets stay put");
            Assert.IsFalse(_sim.FleetReadiness.Get(fleet).IsMothballed);

            Order(CommandType.Move, fleet, ready, _lane);
            RunTo(ready);
            Assert.AreEqual(_lane, _sim.FleetMotions.Get(fleet).Lane);
        }

        [Test]
        public void UnpaidUpkeep_GroundsTheFleet_UntilALaterPayment()
        {
            var fleet = Fleet(tier: 1, ships: 1);
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemRations, -1000);

            RunTo(_b.UpkeepIntervalTicks);
            Assert.IsTrue(_sim.FleetReadiness.Get(fleet).IsUnsupplied);
            Assert.AreEqual(1000, Held(fleet, ItemId.ItemOxygen), "all or nothing");

            Order(CommandType.Move, fleet, _b.UpkeepIntervalTicks + 1, _lane);
            RunTo(_b.UpkeepIntervalTicks + 1);
            Assert.AreEqual(_s0, _sim.FleetMotions.Get(fleet).Site);

            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemRations, 5);
            RunTo(2L * _b.UpkeepIntervalTicks);
            Assert.IsFalse(_sim.FleetReadiness.Get(fleet).IsUnsupplied);
        }

        [Test]
        public void Mothball_OnlyAtASite_ForOwnFleets()
        {
            var fleet = Fleet(tier: 1, ships: 1);
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                ExecuteAtTick = 1, PlayerId = 2, CommandType = CommandType.Mothball, Target = fleet
            });
            Order(CommandType.Move, fleet, 2, _lane);
            Order(CommandType.Mothball, fleet, 3);
            RunTo(3);
            Assert.IsFalse(_sim.FleetReadiness.Get(fleet).IsMothballed);
        }
    }
}
