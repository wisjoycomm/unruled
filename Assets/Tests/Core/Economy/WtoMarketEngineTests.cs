using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Economy
{
    public class WtoMarketEngineTests
    {
        private const int Seller = 1;
        private const int Buyer = 2;
        private const int Seller2 = 3;
        private const ItemId Steel = ItemId.ItemSteel; // base price 50 credits

        private SimulationWorld _sim;
        private EntityId _sellerSite, _seller2Site, _buyerSite;
        private EntityId _sellerHq, _seller2Hq, _buyerHq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 5);
            _sellerSite = Site(Seller);
            _seller2Site = Site(Seller2);
            _buyerSite = Site(Buyer);
            _sellerHq = _sim.CreatePlayer(Seller, _sellerSite);
            _seller2Hq = _sim.CreatePlayer(Seller2, _seller2Site);
            _buyerHq = _sim.CreatePlayer(Buyer, _buyerSite);
            _sim.Stockpiles.Get(_sellerSite).Add(Steel, 100);
            _sim.Stockpiles.Get(_seller2Site).Add(Steel, 100);
        }

        private EntityId Site(int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private void Order(CommandType type, int player, EntityId site, int quantity, long priceCents) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                ExecuteAtTick = _sim.Clock.CurrentTick + 1, PlayerId = player, CommandType = type, Target = site,
                ParamA = (int)Steel, ParamB = quantity, ParamC = (int)priceCents
            });

        private void Step() => _sim.Clock.Advance(1);

        private int Held(EntityId site) => _sim.Stockpiles.Get(site).Get(Steel);
        private long Cash(EntityId wallet) => _sim.Ledger.Balance(wallet);
        private long Start => _sim.Balance.Ledger.StartingCents;

        [Test]
        public void Orders_ReserveGoodsAndMoney_AndCancelReturnsThem()
        {
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 30, 5_000);
            Order(CommandType.PlaceBuyOrder, Buyer, _buyerSite, 10, 4_500);
            Step();
            Assert.AreEqual(70, Held(_sellerSite));
            Assert.AreEqual(Start - 45_000, Cash(_buyerHq));
            Assert.AreEqual(45_000, Cash(_sim.WtoEscrow));
            Assert.AreEqual(2, _sim.WtoOrders.Count, "no cross at 45 vs 50");

            for (int i = _sim.WtoOrders.Count - 1; i >= 0; i--)
            {
                var o = _sim.WtoOrders.At(i);
                var cmd = new DelayedPlayerCommand
                {
                    ExecuteAtTick = _sim.Clock.CurrentTick + 1, PlayerId = o.OwnerPlayerId,
                    CommandType = CommandType.CancelOrder, Target = o.Site
                };
                cmd.ParamEntity = _sim.WtoOrders.EntityAt(i);
                _sim.Clock.Enqueue(cmd);
            }
            Step();
            Assert.AreEqual(0, _sim.WtoOrders.Count);
            Assert.AreEqual(100, Held(_sellerSite));
            Assert.AreEqual(Start, Cash(_buyerHq));
            Assert.AreEqual(0, Cash(_sim.WtoEscrow));
        }

        [Test]
        public void Orders_Refused_OutsideTheBand_OffOwnSite_OrUnaffordable()
        {
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 1, 3_999); // below 0.8 x 5000
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 1, 6_001); // above 1.2 x 5000
            Order(CommandType.PlaceSellOrder, Seller, _buyerSite, 1, 5_000);  // not the seller's site
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 101, 5_000); // more than held
            Order(CommandType.PlaceBuyOrder, Buyer, _buyerSite, 1_000_000, 5_000); // can't pay
            Step();
            Assert.AreEqual(0, _sim.WtoOrders.Count);
            Assert.AreEqual(100, Held(_sellerSite));
            Assert.AreEqual(Start, Cash(_buyerHq));

            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 1, 4_000);
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 1, 6_000);
            Step();
            Assert.AreEqual(2, _sim.WtoOrders.Count, "band edges are allowed");
        }

        [Test]
        public void Match_PriceThenTime_AtTheRestingPrice_FeeToThePool()
        {
            Order(CommandType.PlaceSellOrder, Seller2, _seller2Site, 10, 5_200);
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 10, 5_000);
            Step();
            Order(CommandType.PlaceSellOrder, Seller2, _seller2Site, 10, 5_000); // same price, later
            Order(CommandType.PlaceBuyOrder, Buyer, _buyerSite, 15, 5_500);
            Step();

            // 10 from Seller at 50.00 (best price, earliest), then 5 from Seller2's later 50.00 order.
            Assert.AreEqual(15, Held(_buyerSite));
            Assert.AreEqual(Start - 15 * 5_000, Cash(_buyerHq), "refunded down to the resting price");
            long fee1 = 10 * 5_000 * 500 / 10_000, fee2 = 5 * 5_000 * 500 / 10_000;
            Assert.AreEqual(Start + 50_000 - fee1, Cash(_sellerHq));
            Assert.AreEqual(Start + 25_000 - fee2, Cash(_seller2Hq));
            Assert.AreEqual(fee1 + fee2, Cash(_sim.WtoFeePool));
            Assert.AreEqual(0, Cash(_sim.WtoEscrow));
            Assert.AreEqual(2, _sim.WtoOrders.Count, "the 52.00 order and 5 left at 50.00 rest");
            Assert.AreEqual(80, Held(_seller2Site));
        }

        [Test]
        public void BracketFee_LowersTheSellersFee()
        {
            _sim.Standings.Get(_sellerHq).Bracket = 6; // Apex: 2.0%
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 10, 5_000);
            Order(CommandType.PlaceBuyOrder, Buyer, _buyerSite, 10, 5_000);
            Step();
            Assert.AreEqual(10 * 5_000 * 200 / 10_000, Cash(_sim.WtoFeePool));
        }

        [Test]
        public void Vwap_WeighsTradesInTheWindow_AndHoldsAfterIt()
        {
            long window = _sim.Balance.Wto.VwapWindowTicks;
            Assert.AreEqual(5_000, _sim.Market.ReferencePriceCents(Steel, _sim.Clock.CurrentTick), "seeded base price");

            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 10, 4_000);
            Order(CommandType.PlaceBuyOrder, Buyer, _buyerSite, 10, 4_000);
            Step();
            Order(CommandType.PlaceSellOrder, Seller, _sellerSite, 30, 4_800);
            Order(CommandType.PlaceBuyOrder, Buyer, _buyerSite, 30, 4_800);
            Step();
            long vwap = (10 * 4_000 + 30 * 4_800) / 40;
            Assert.AreEqual(vwap, _sim.Market.ReferencePriceCents(Steel, _sim.Clock.CurrentTick));

            _sim.Clock.Advance(window);
            Assert.AreEqual(vwap, _sim.Market.ReferencePriceCents(Steel, _sim.Clock.CurrentTick), "empty window keeps it");
            Assert.IsFalse(_sim.Market.IsInBand(Steel, 6_000, _sim.Clock.CurrentTick), "band follows the VWAP");
            Assert.IsTrue(_sim.Market.IsInBand(Steel, vwap * 120 / 100, _sim.Clock.CurrentTick));
        }
    }
}
