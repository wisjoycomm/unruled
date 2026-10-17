using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;

namespace Unruled.Tests.Core.SystemFactions
{
    public class WtoContractSystemTests
    {
        private const int Seller = 1;
        private const int Buyer = 2;
        private const int Pirate = 3;
        private const ItemId Ammo = ItemId.ItemAmmo;
        private const int Qty = 20;
        private const long Price = 9_500;
        private const long Value = Qty * Price;
        private const long Deposit = Value * 30 / 100;
        private const long Fee = Value * 500 / 10_000;

        private SimulationWorld _sim;
        private EntityId _sellerSite, _buyerSite, _island;
        private EntityId _sellerHq, _buyerHq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 9);
            _sellerSite = Site(Seller);
            _buyerSite = Site(Buyer);
            _island = Site(Buyer);
            _sim.Starlanes.Add(_sim.World.Create(),
                new StarlaneEdge { SiteA = _sellerSite, SiteB = _buyerSite, Tier = StarlaneTier.OuterRimBypass, Length = 300 });
            _sim.Graph.Rebuild(_sim.Starlanes);
            _sellerHq = _sim.CreatePlayer(Seller, _sellerSite);
            _buyerHq = _sim.CreatePlayer(Buyer, _buyerSite);
            _sim.CreatePlayer(Pirate, _sellerSite);
        }

        private EntityId Site(int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        // What the order book hands over: value and deposit already in escrow, goods already out of the seller's site.
        private void Open(EntityId buyerSite)
        {
            Assert.IsTrue(_sim.Ledger.Transfer(_buyerHq, _sim.WtoEscrow, Value));
            Assert.IsTrue(_sim.Ledger.Transfer(_sellerHq, _sim.WtoEscrow, Deposit));
            _sim.ContractSystem.Open(new TradeMatch
            {
                BuyerPlayerId = Buyer, SellerPlayerId = Seller, Item = Ammo, Quantity = Qty, PriceCents = Price,
                DepositCents = Deposit, SellerSite = _sellerSite, BuyerSite = buyerSite
            }, _sim.Clock.CurrentTick);
        }

        private EntityId Convoy() => _sim.Contracts.At(0).Convoy;
        private long Cash(EntityId wallet) => _sim.Ledger.Balance(wallet);
        private long Start => _sim.Balance.Ledger.StartingCents;

        private void AssertBuyerMadeWhole()
        {
            Assert.AreEqual(0, _sim.Contracts.Count);
            Assert.AreEqual(Start + Deposit, Cash(_buyerHq), "refund plus the seller's deposit");
            Assert.AreEqual(Start - Deposit, Cash(_sellerHq));
            Assert.AreEqual(0, Cash(_sim.WtoEscrow));
            Assert.AreEqual(0, Cash(_sim.WtoFeePool));
        }

        [Test]
        public void Delivery_PaysTheSellerValueLessFeePlusDeposit()
        {
            _sim.Stockpiles.Get(_sellerSite).Add(ItemId.ItemFuel, 500);
            Open(_buyerSite);
            var convoy = Convoy();
            Assert.AreEqual(Seller, _sim.Fleets.Get(convoy).OwnerPlayerId);
            Assert.AreEqual(1, _sim.Fleets.Get(convoy).CargoConvoys);
            int fuel = _sim.Routes.FuelFor(_sim.Fleets.Get(convoy), _sim.Starlanes.At(0));
            Assert.AreEqual(fuel, _sim.Stockpiles.Get(convoy).Get(ItemId.ItemFuel), "route fuel loaded from the seller");
            Assert.AreEqual(Qty, _sim.Stockpiles.Get(convoy).Get(Ammo));

            for (int i = 0; i < 2_000 && _sim.Contracts.Count > 0; i++)
                _sim.Clock.Advance(1);

            Assert.AreEqual(0, _sim.Contracts.Count);
            Assert.IsFalse(_sim.World.IsAlive(convoy));
            Assert.AreEqual(Qty, _sim.Stockpiles.Get(_buyerSite).Get(Ammo));
            Assert.AreEqual(Start + Value - Fee, Cash(_sellerHq));
            Assert.AreEqual(Start - Value, Cash(_buyerHq));
            Assert.AreEqual(Fee, Cash(_sim.WtoFeePool));
            Assert.AreEqual(0, Cash(_sim.WtoEscrow));
        }

        [Test]
        public void ConvoyLost_RefundsTheBuyer_FromTheDeposit()
        {
            _sim.Stockpiles.Get(_sellerSite).Add(ItemId.ItemFuel, 500);
            Open(_buyerSite);
            _sim.World.Destroy(Convoy());
            _sim.Clock.Advance(1);
            AssertBuyerMadeWhole();
        }

        [Test]
        public void MissedDeadline_CompensatesTheBuyer_AndTheConvoyStaysTheSellers()
        {
            Open(_buyerSite); // no fuel at the seller's site: the convoy can't leave
            var convoy = Convoy();
            long deadline = _sim.Contracts.At(0).DeadlineTick;
            _sim.Clock.Advance(deadline - _sim.Clock.CurrentTick - 1);
            Assert.AreEqual(1, _sim.Contracts.Count, "still open before the deadline");
            _sim.Clock.Advance(1);

            AssertBuyerMadeWhole();
            Assert.IsTrue(_sim.World.IsAlive(convoy));
            Assert.AreEqual(Qty, _sim.Stockpiles.Get(convoy).Get(Ammo));
            Assert.IsFalse(_sim.ContractCargo.Has(convoy));
        }

        [Test]
        public void Piracy_TakesTheCargo_FlaggedStolen()
        {
            Open(_buyerSite); // stuck at the seller's site without fuel
            var raider = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Pirate, TechTier = 1, LineShips = 10 }, _sellerSite);
            _sim.FleetCombat.Get(raider).RaidsConvoys = true;
            ref var hold = ref _sim.Stockpiles.Get(raider);
            for (int i = 0; i < ItemIds.Count; i++)
                hold.Add(ItemIds.FromSlot(i), 1_000);

            var convoy = Convoy();
            for (int i = 0; i < 600 && _sim.World.IsAlive(convoy); i++)
                _sim.Clock.Advance(1);

            Assert.IsFalse(_sim.World.IsAlive(convoy), "the convoy was sunk");
            AssertBuyerMadeWhole();
            int ammo = _sim.Stockpiles.Get(raider).Get(Ammo);
            Assert.Greater(ammo, 1_000 - 100, "the cargo was taken");
            Assert.AreEqual(Qty, _sim.StolenGoods.Get(raider).Get(Ammo), "only the contract goods are stolen");
            Assert.AreEqual(ammo - Qty, _sim.StolenGoods.Get(raider).Clean(_sim.Stockpiles.Get(raider), Ammo));
            Assert.IsFalse(_sim.Authority.MayTrade(Pirate, _sim.Clock.CurrentTick), "piracy bars the WTO");
            Assert.IsTrue(_sim.Authority.MayTrade(Seller, _sim.Clock.CurrentTick));
        }

        [Test]
        public void NoRoute_VoidsTheTrade_AndSameSiteDeliversAtOnce()
        {
            Open(_island);
            Assert.AreEqual(0, _sim.Contracts.Count);
            Assert.AreEqual(Qty, _sim.Stockpiles.Get(_sellerSite).Get(Ammo), "goods back to the seller");
            Assert.AreEqual(Start, Cash(_sellerHq));
            Assert.AreEqual(Start, Cash(_buyerHq));
            Assert.AreEqual(0, Cash(_sim.WtoEscrow));

            Open(_sellerSite);
            Assert.AreEqual(0, _sim.Contracts.Count);
            Assert.AreEqual(2 * Qty, _sim.Stockpiles.Get(_sellerSite).Get(Ammo));
            Assert.AreEqual(Start + Value - Fee, Cash(_sellerHq));
            Assert.AreEqual(Fee, Cash(_sim.WtoFeePool));
        }
    }
}
