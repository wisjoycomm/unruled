using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>Map: hub H (own) - highway 200 - outpost O (own).</summary>
    public class ConvoyRouteSystemTests
    {
        private const int Player = 1;
        private const int Batch = 100;

        private SimulationWorld _sim;
        private EntityId _hub, _outpost, _fleet;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 3);
            _hub = Site(0);
            _outpost = Site(200);
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = _hub, SiteB = _outpost, Tier = StarlaneTier.StaHighway, Length = 200 });
            _sim.LaneWeather.Add(lane, default);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _sim.CreatePlayer(Player, _hub);
            _fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, CargoConvoys = 2 }, _hub);
            Hub.Add(ItemId.ItemFuel, 5_000);
            Hub.Add(ItemId.ItemCoolant, 1_000);
        }

        private EntityId Site(int x)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = Player, PosX = x });
            _sim.Stockpiles.Add(site, default);
            _sim.Weather.Add(site, default);
            return site;
        }

        private ref Stockpile Hub => ref _sim.Stockpiles.Get(_hub);
        private ref Stockpile Outpost => ref _sim.Stockpiles.Get(_outpost);

        private void SetRoute(EntityId fleet, EntityId outpost, int batch)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = Player, CommandType = CommandType.SetConvoyRoute, Target = fleet, ParamEntity = outpost, ParamC = batch
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            for (long t = _sim.Clock.CurrentTick; t < at; t++)
                _sim.Clock.Advance(1);
        }

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                _sim.Clock.Advance(1);
        }

        [Test]
        public void Route_SuppliesTheOutpost_AndHaulsItsGoodsHome_ThenWaits()
        {
            Outpost.Add(ItemId.RawIron, 250);
            SetRoute(_fleet, _outpost, Batch);
            Assert.IsTrue(_sim.ConvoyRoutes.Has(_fleet));

            Run(5_000);

            Assert.AreEqual(Batch, Outpost.Get(ItemId.ItemCoolant), "coolant topped up to the batch");
            Assert.AreEqual(Batch, Outpost.Get(ItemId.ItemFuel), "fuel topped up to the batch");
            Assert.AreEqual(200, Hub.Get(ItemId.RawIron), "a batch per run");
            Assert.AreEqual(50, Outpost.Get(ItemId.RawIron), "less than a batch waits for the next");
            Assert.AreEqual(_hub, _sim.FleetMotions.Get(_fleet).Site, "waits at the hub with nothing to do");
            Assert.AreEqual(0, _sim.Stockpiles.Get(_fleet).Get(ItemId.RawIron));
        }

        [Test]
        public void Route_CarriesLifeSupport_AndKeepsItsOwnUpkeep_EvenWhenGrounded()
        {
            Hub.Add(ItemId.ItemOxygen, 1_000);
            Hub.Add(ItemId.ItemRations, 1_000);
            int hubReserve = _sim.Balance.Convoys.HubSupplyReserve;
            Outpost.Add(ItemId.RawIron, Batch);
            _sim.FleetReadiness.Get(_fleet).IsUnsupplied = true;
            SetRoute(_fleet, _outpost, Batch);
            Run(_sim.Balance.Convoys.CheckIntervalTicks);

            int reserve = 2 * _sim.Balance.Convoys.UpkeepReserveIntervals; // 2 ships x 1 oxygen and 1 rations an interval
            ref var hold = ref _sim.Stockpiles.Get(_fleet);
            Assert.AreEqual(Batch + reserve, hold.Get(ItemId.ItemOxygen), "loads while grounded");
            Assert.AreEqual(Batch + reserve, hold.Get(ItemId.ItemRations));
            Assert.AreEqual(_hub, _sim.FleetMotions.Get(_fleet).Site, "a grounded fleet does not leave");

            _sim.FleetReadiness.Get(_fleet).IsUnsupplied = false;
            Run(3_000);

            Assert.AreEqual(Batch, Outpost.Get(ItemId.ItemOxygen));
            Assert.AreEqual(Batch, Outpost.Get(ItemId.ItemRations));
            Assert.AreEqual(Batch, Hub.Get(ItemId.RawIron));
            Assert.GreaterOrEqual(_sim.Stockpiles.Get(_fleet).Get(ItemId.ItemOxygen), reserve, "its own upkeep stays aboard");
            Assert.GreaterOrEqual(Hub.Get(ItemId.ItemOxygen), hubReserve, "the hub keeps its reserve");
        }

        [Test]
        public void Refused_WithoutCargoConvoys_OverCapacity_OrToASiteNotOwned()
        {
            var warships = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 4 }, _hub);
            SetRoute(warships, _outpost, Batch);
            Assert.IsFalse(_sim.ConvoyRoutes.Has(warships), "no cargo convoys");

            SetRoute(_fleet, _outpost, 2 * _sim.Balance.Convoys.CapacityPerConvoy + 1);
            Assert.IsFalse(_sim.ConvoyRoutes.Has(_fleet), "over capacity");

            _sim.Sites.Get(_outpost).OwnerPlayerId = 2;
            SetRoute(_fleet, _outpost, Batch);
            Assert.IsFalse(_sim.ConvoyRoutes.Has(_fleet), "outpost not owned");
        }

        [Test]
        public void Route_Ends_WhenTheOutpostIsLost_OrOnAZeroBatch()
        {
            SetRoute(_fleet, _outpost, Batch);
            _sim.Sites.Get(_outpost).OwnerPlayerId = 2;
            Run(_sim.Balance.Convoys.CheckIntervalTicks);
            Assert.IsFalse(_sim.ConvoyRoutes.Has(_fleet), "outpost lost");

            _sim.Sites.Get(_outpost).OwnerPlayerId = Player;
            SetRoute(_fleet, _outpost, Batch);
            Assert.IsTrue(_sim.ConvoyRoutes.Has(_fleet));
            SetRoute(_fleet, _outpost, 0);
            Assert.IsFalse(_sim.ConvoyRoutes.Has(_fleet), "cleared");
        }
    }
}
