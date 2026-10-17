using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;

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
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 3);
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
        public void Route_SetsOutToSupply_OnlyWithHalfABatchAboveItsReserve_SoARunNeverBurnsMoreFuelThanItHauls()
        {
            int hubReserve = _sim.Balance.Convoys.HubSupplyReserve;
            int roundTrip = 2 * _sim.Movement.FuelFor(_sim.Fleets.Get(_fleet), _sim.Starlanes.At(0));
            Hub.Add(ItemId.ItemCoolant, hubReserve - Hub.Get(ItemId.ItemCoolant));
            Hub.Add(ItemId.ItemFuel, hubReserve + roundTrip + Batch / 2 - 1 - Hub.Get(ItemId.ItemFuel));
            SetRoute(_fleet, _outpost, Batch);

            Run(1_000);
            Assert.AreEqual(_hub, _sim.FleetMotions.Get(_fleet).Site, "a dribble of fuel isn't worth the trip");
            Assert.AreEqual(0, Outpost.Get(ItemId.ItemFuel));

            Hub.Add(ItemId.ItemFuel, 1);
            Run(1_000);
            Assert.GreaterOrEqual(Outpost.Get(ItemId.ItemFuel), Batch / 2);
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

        // A second way to the outpost: hub - bypass - M - bypass - outpost (toll-free, slower).
        private EntityId AddBypass()
        {
            var middle = Site(100);
            foreach (var end in new[] { _hub, _outpost })
            {
                var lane = _sim.World.Create();
                _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = end, SiteB = middle, Tier = StarlaneTier.OuterRimBypass, Length = 150 });
                _sim.LaneWeather.Add(lane, default);
            }
            _sim.Graph.Rebuild(_sim.Starlanes);
            return middle;
        }

        [Test]
        public void Route_OfAnOwnerWhoCannotPayTheTolls_TakesTheBypass()
        {
            AddBypass();
            _sim.Ledger.TryFindPlayerWallet(Player, out var wallet);
            _sim.Ledger.Burn(wallet, _sim.Ledger.Balance(wallet));
            Outpost.Add(ItemId.RawIron, Batch);
            SetRoute(_fleet, _outpost, Batch);

            bool bypass = false, highway = false;
            for (int i = 0; i < 3_000 && Hub.Get(ItemId.RawIron) == 0; i++)
            {
                Run(1);
                var lane = _sim.FleetMotions.Get(_fleet).Lane;
                if (lane.IsNull)
                    continue;
                bypass |= _sim.Starlanes.Get(lane).Tier == StarlaneTier.OuterRimBypass;
                highway |= _sim.Starlanes.Get(lane).Tier == StarlaneTier.StaHighway;
            }
            Assert.IsTrue(bypass && !highway, "around the highway");
            Assert.AreEqual(Batch, Hub.Get(ItemId.RawIron), "the goods still came home");
        }

        [Test]
        public void Route_SetsOut_WithTheFuelToComeHomeByTheBypass_InCaseTheTollsCanNoLongerBePaid()
        {
            AddBypass();
            long highway = 0, bypass = 0;
            var unit = _sim.Fleets.Get(_fleet);
            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                int fuel = _sim.Routes.FuelFor(unit, _sim.Starlanes.At(i));
                if (_sim.Starlanes.At(i).Tier == StarlaneTier.StaHighway)
                    highway += fuel;
                else
                    bypass += fuel;
            }
            Assert.Greater(bypass, highway + 1, "the bypass home takes more than a highway leg's fuel");
            Outpost.Add(ItemId.RawIron, 1);
            SetRoute(_fleet, _outpost, 1);
            for (int i = 0; i < 3_000 && _sim.FleetMotions.Get(_fleet).Lane.IsNull; i++)
                Run(1);
            Assert.AreEqual(StarlaneTier.StaHighway, _sim.Starlanes.Get(_sim.FleetMotions.Get(_fleet).Lane).Tier, "out on the highway");
            Assert.GreaterOrEqual(_sim.Stockpiles.Get(_fleet).Get(ItemId.ItemFuel), bypass, "the bypass fuel home is aboard");
        }

        [Test]
        public void AtTheHub_ItUnloads_EvenWithNoRouteOutItCanPay()
        {
            SetRoute(_fleet, _outpost, Batch);
            _sim.Ledger.TryFindPlayerWallet(Player, out var wallet);
            _sim.Ledger.Burn(wallet, _sim.Ledger.Balance(wallet));
            _sim.Stockpiles.Get(_fleet).Add(ItemId.RawTungsten, 500);

            Run(_sim.Balance.Convoys.CheckIntervalTicks);

            Assert.AreEqual(500, Hub.Get(ItemId.RawTungsten), "the haul comes off though the highway toll is unpaid");
            Assert.AreEqual(_hub, _sim.FleetMotions.Get(_fleet).Site);
        }

        [Test]
        public void Route_CutShortAtAnotherSite_HeadsBackToTheHub()
        {
            var middle = AddBypass();
            SetRoute(_fleet, _outpost, Batch);
            _sim.FleetMotions.Get(_fleet).Site = middle;
            _sim.Stockpiles.Get(_fleet).Add(ItemId.ItemFuel, 100);

            Run(1_000);
            Assert.AreEqual(_hub, _sim.FleetMotions.Get(_fleet).Site);
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
