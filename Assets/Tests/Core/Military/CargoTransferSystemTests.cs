using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    public class CargoTransferSystemTests
    {
        private const int Player = 1;

        private SimulationWorld _sim;
        private EntityId _core;
        private EntityId _other;
        private EntityId _fleet;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 4);
            _sim.GenerateSystem(31, clusterId: 0);
            _core = _sim.Sites.EntityAt(0);
            _other = _sim.Sites.EntityAt(1);
            for (int i = 0; i < _sim.Sites.Count; i++)
                _sim.Stockpiles.Add(_sim.Sites.EntityAt(i), default);
            _sim.Sites.Get(_core).OwnerPlayerId = Player;
            _sim.CreatePlayer(Player, _core);
            _fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 2 }, _core);
            Site.Add(ItemId.ItemAmmo, 50);
        }

        private ref Stockpile Site => ref _sim.Stockpiles.Get(_core);
        private ref Stockpile Hold => ref _sim.Stockpiles.Get(_fleet);

        private void Transfer(EntityId fleet, ItemId item, int quantity, int fill = 0)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = Player, CommandType = CommandType.TransferCargo, Target = fleet, ParamA = (int)item, ParamB = quantity,
                ParamC = fill
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            for (long t = _sim.Clock.CurrentTick; t < at; t++)
                _sim.Clock.Advance(1);
        }

        [Test]
        public void Load_ThenUnload_MovesItemsBetweenSiteAndHold()
        {
            Transfer(_fleet, ItemId.ItemAmmo, 30);
            Assert.AreEqual(20, Site.Get(ItemId.ItemAmmo));
            Assert.AreEqual(30, Hold.Get(ItemId.ItemAmmo));

            Transfer(_fleet, ItemId.ItemAmmo, -10);
            Assert.AreEqual(30, Site.Get(ItemId.ItemAmmo));
            Assert.AreEqual(20, Hold.Get(ItemId.ItemAmmo));
        }

        [Test]
        public void FillTo_TopsUpTheHold_FromWhatTheSiteHas_AndRepeatsChangeNothing()
        {
            Hold.Add(ItemId.ItemAmmo, 10);
            Transfer(_fleet, ItemId.ItemAmmo, 30, fill: 1);
            Assert.AreEqual(30, Hold.Get(ItemId.ItemAmmo));
            Assert.AreEqual(30, Site.Get(ItemId.ItemAmmo));

            Transfer(_fleet, ItemId.ItemAmmo, 30, fill: 1);
            Assert.AreEqual(30, Hold.Get(ItemId.ItemAmmo), "already full");

            Transfer(_fleet, ItemId.ItemAmmo, 100, fill: 1);
            Assert.AreEqual(60, Hold.Get(ItemId.ItemAmmo), "takes what the site has");
            Assert.AreEqual(0, Site.Get(ItemId.ItemAmmo));
        }

        [Test]
        public void Transfer_MoreThanTheSourceHolds_OrAtASiteNotOwned_DoesNothing()
        {
            Transfer(_fleet, ItemId.ItemAmmo, 51);
            Assert.AreEqual(50, Site.Get(ItemId.ItemAmmo));

            var away = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, LineShips = 2 }, _other);
            _sim.Stockpiles.Get(_other).Add(ItemId.ItemAmmo, 50);
            Transfer(away, ItemId.ItemAmmo, 10);
            Assert.AreEqual(0, _sim.Stockpiles.Get(away).Get(ItemId.ItemAmmo));
        }

        [Test]
        public void Unload_StolenUnitsGoFirst_AndKeepTheirTag()
        {
            Hold.Add(ItemId.ItemSteel, 10);
            _sim.StolenGoods.Add(_fleet, default);
            _sim.StolenGoods.Get(_fleet).Add(ItemId.ItemSteel, 4);

            Transfer(_fleet, ItemId.ItemSteel, -6);

            Assert.AreEqual(0, _sim.StolenGoods.Get(_fleet).Get(ItemId.ItemSteel));
            Assert.AreEqual(4, _sim.StolenGoods.Get(_core).Get(ItemId.ItemSteel));
            Assert.AreEqual(6, Site.Get(ItemId.ItemSteel));
        }

        [Test]
        public void ConvoyHold_TakesAtMostItsCapacity_PlainLoadsOverItAreRefused_FillsStopAtIt()
        {
            int capacity = _sim.Balance.Convoys.CapacityPerConvoy;
            var convoy = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Player, TechTier = 1, CargoConvoys = 2 }, _core);
            Site.Add(ItemId.ItemFuel, 10 * capacity);

            Transfer(convoy, ItemId.ItemFuel, 2 * capacity + 1);
            Assert.AreEqual(0, _sim.Stockpiles.Get(convoy).Get(ItemId.ItemFuel), "over capacity: refused");

            Transfer(convoy, ItemId.ItemFuel, capacity);
            Transfer(convoy, ItemId.ItemFuel, 5 * capacity, fill: 1);
            Assert.AreEqual(2 * capacity, _sim.Stockpiles.Get(convoy).Get(ItemId.ItemFuel), "a fill stops at capacity");

            Site.Add(ItemId.ItemSteel, capacity);
            Transfer(convoy, ItemId.ItemSteel, capacity);
            Assert.AreEqual(capacity, _sim.Stockpiles.Get(convoy).Get(ItemId.ItemSteel), "capacity is per item");
        }

        [Test]
        public void ContractConvoy_CannotTransfer()
        {
            Hold.Add(ItemId.ItemSteel, 10);
            _sim.ContractCargo.Add(_fleet, new ContractCargo { Item = ItemId.ItemSteel, Quantity = 10 });
            Transfer(_fleet, ItemId.ItemSteel, -10);
            Assert.AreEqual(10, Hold.Get(ItemId.ItemSteel));
        }
    }
}
