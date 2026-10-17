using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;

namespace Unruled.Tests.Core.AI
{
    /// <summary>One AI player with a core base; its WTO orders.</summary>
    public class AiTradeTests
    {
        private const int Me = 1;
        private const byte Trader = 2; // Industrialist_Trader

        private SimulationWorld _sim;
        private EntityId _home, _hq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 3);
            _home = _sim.World.Create();
            _sim.Sites.Add(_home, new SiteInfo { OwnerPlayerId = Me });
            _sim.Stockpiles.Add(_home, default);
            _sim.Weather.Add(_home, default);
            _hq = _sim.CreatePlayer(Me, _home);
            _sim.Standings.Get(_hq).IsAudited = true;
            _sim.TechProgress.Get(_hq).Stage = ResearchStage.Retooling; // no stress test to bid for unless a test says so
        }

        private AiOrder? Find(AiAction action)
        {
            _sim.Ai.Assign(_hq, Trader);
            while (_sim.Clock.CurrentTick % _sim.Balance.Ai.ThinkIntervalTicks != 0)
                _sim.Clock.Advance(1);
            int n = _sim.Ai.Think(_sim.Clock.CurrentTick);
            for (int i = 0; i < n; i++)
                if (_sim.Ai.OrderAt(i).Action == action)
                    return _sim.Ai.OrderAt(i);
            return null;
        }

        private void RunPendingOrders()
        {
            for (int i = 0; i < 1_000 && _sim.Clock.PendingCommandCount > 0; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(0, _sim.Clock.PendingCommandCount, "orders arrived");
        }

        private int Orders(OrderSide side)
        {
            int n = 0;
            for (int i = 0; i < _sim.WtoOrders.Count; i++)
                if (_sim.WtoOrders.At(i).Side == side)
                    n++;
            return n;
        }

        private long Price(ItemId item, int percent) =>
            _sim.Market.ReferencePriceCents(item, _sim.Clock.CurrentTick) * (100 + percent) / 100;

        [Test]
        public void SellsABatchOfSurplus_UnderTheReferencePrice_ThenCancelsItWhenStale()
        {
            var ai = _sim.Balance.Ai;
            _sim.Stockpiles.Get(_home).Add(ItemId.RawBiomass, ai.SellAbove + ai.SellBatch);

            var order = Find(AiAction.SellSurplus);
            Assert.IsTrue(order.HasValue);
            Assert.AreEqual(IssueResult.Issued, order.Value.Result);
            Assert.IsFalse(Find(AiAction.SellSurplus).HasValue, "one sell order in flight at a time");
            RunPendingOrders();

            Assert.AreEqual(1, _sim.WtoOrders.Count);
            ref var placed = ref _sim.WtoOrders.At(0);
            Assert.AreEqual(OrderSide.Sell, placed.Side);
            Assert.AreEqual(ai.SellBatch, placed.Remaining);
            Assert.AreEqual(Price(ItemId.RawBiomass, -ai.TradePricePercent), placed.PriceCents);
            Assert.AreEqual(ai.SellAbove, _sim.Stockpiles.Get(_home).Get(ItemId.RawBiomass));
            Assert.IsFalse(Find(AiAction.SellSurplus).HasValue, "stock is back at the threshold");

            _sim.Clock.Advance(ai.TradeOrderTicks);
            Assert.IsTrue(Find(AiAction.CancelOrder).HasValue);
            RunPendingOrders();
            Assert.AreEqual(0, Orders(OrderSide.Sell), "cancelled");
            Assert.AreEqual(ai.SellAbove + ai.SellBatch, _sim.Stockpiles.Get(_home).Get(ItemId.RawBiomass), "goods back");
        }

        [Test]
        public void KeepsTheRawsItsCoreRefineriesTakeIn_Unsold()
        {
            var ai = _sim.Balance.Ai;
            _sim.Stockpiles.Get(_home).Add(ItemId.RawBiomass, ai.SellAbove + ai.SellBatch);
            _sim.Refineries.Add(_sim.World.Create(), new Refinery { Site = _home, Output = ItemId.ItemRations });

            Assert.IsFalse(Find(AiAction.SellSurplus).HasValue, "rations are made from biomass");
        }

        [Test]
        public void SellsIntoARivalBid_AtItsPrice_OnlyWhenAFuelledConvoyCanReachIt()
        {
            var ai = _sim.Balance.Ai;
            var rivalSite = _sim.World.Create();
            _sim.Sites.Add(rivalSite, new SiteInfo { OwnerPlayerId = 2, PosX = 200 });
            long bidPrice = Price(ItemId.ItemSteel, 10);
            _sim.WtoOrders.Add(_sim.World.Create(), new WtoOrder
            {
                OwnerPlayerId = 2, Item = ItemId.ItemSteel, Side = OrderSide.Buy, PriceCents = bidPrice, Remaining = 40,
                Site = rivalSite
            });
            _sim.Stockpiles.Get(_home).Add(ItemId.ItemSteel, ai.FillAbove + 25);

            Assert.IsFalse(Find(AiAction.SellSurplus).HasValue, "no starlane, so the contract could not be delivered");

            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = _home, SiteB = rivalSite, Tier = StarlaneTier.StaHighway, Length = 200 });
            _sim.LaneWeather.Add(lane, default);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _sim.Clock.Advance(1);
            Assert.IsFalse(Find(AiAction.SellSurplus).HasValue, "no fuel above the core reserve for the convoy");

            _sim.Stockpiles.Get(_home).Add(ItemId.ItemFuel, ai.CoreSupplyReserve + 1_000);
            _sim.Clock.Advance(1);
            Assert.IsTrue(Find(AiAction.SellSurplus).HasValue);
            DelayedPlayerCommand sell = default;
            for (int i = 0; i < _sim.Clock.PendingCommandCount; i++)
                if (_sim.Clock.PendingAt(i).CommandType == CommandType.PlaceSellOrder)
                    sell = _sim.Clock.PendingAt(i);
            Assert.AreEqual((int)ItemId.ItemSteel, sell.ParamA);
            Assert.AreEqual(25, sell.ParamB, "all of the stock above the fill floor");
            Assert.AreEqual(bidPrice, sell.ParamC, "at the bid's price");
        }

        [Test]
        public void BidsOverTheReferencePrice_ForWhatTheStressTestLacks()
        {
            _sim.TechProgress.Get(_hq).Stage = ResearchStage.StressTest;
            var cost = _sim.Balance.Research.Tier(2).StressTestCost;
            for (int i = 1; i < cost.Length; i++)
                _sim.Stockpiles.Get(_home).Add(cost[i].Item, cost[i].Quantity);

            var order = Find(AiAction.BuyShortfall);
            Assert.IsTrue(order.HasValue);
            RunPendingOrders();

            Assert.AreEqual(1, _sim.WtoOrders.Count);
            ref var bid = ref _sim.WtoOrders.At(0);
            Assert.AreEqual(OrderSide.Buy, bid.Side);
            Assert.AreEqual(cost[0].Item, bid.Item);
            Assert.AreEqual(cost[0].Quantity, bid.Remaining);
            Assert.AreEqual(Price(cost[0].Item, _sim.Balance.Ai.TradePricePercent), bid.PriceCents);
            Assert.IsFalse(Find(AiAction.BuyShortfall).HasValue, "already bidding");
        }

        [Test]
        public void UnderTheTravelCash_ItSellsABatchToTheSystem_InsteadOfListingIt()
        {
            var ai = _sim.Balance.Ai;
            _sim.Ledger.TryFindPlayerWallet(Me, out var wallet);
            _sim.Ledger.Burn(wallet, _sim.Ledger.Balance(wallet));
            _sim.Ledger.Grant(wallet, ai.TravelCashCredits * Wallet.CentsPerCredit / 2);
            _sim.Ledger.Grant(_sim.StaTollPool, 1_000_000 * Wallet.CentsPerCredit);
            _sim.Stockpiles.Get(_home).Add(ItemId.RawBiomass, ai.SellAbove + ai.SellBatch);

            Assert.IsTrue(Find(AiAction.SellToSystem).HasValue);
            RunPendingOrders();
            Assert.AreEqual(0, Orders(OrderSide.Sell), "no listing while short of the travel cash");
            Assert.AreEqual(ai.SellAbove, _sim.Stockpiles.Get(_home).Get(ItemId.RawBiomass), "one batch sold");
            Assert.Greater(_sim.Ledger.Balance(wallet), ai.TravelCashCredits * Wallet.CentsPerCredit / 2);
        }

        [Test]
        public void AListing_NeverLocksTheTravelCash()
        {
            var ai = _sim.Balance.Ai;
            long travel = ai.TravelCashCredits * Wallet.CentsPerCredit;
            _sim.Ledger.TryFindPlayerWallet(Me, out var wallet);
            _sim.Ledger.Burn(wallet, _sim.Ledger.Balance(wallet));
            _sim.Ledger.Grant(wallet, travel);
            _sim.Stockpiles.Get(_home).Add(ItemId.RawBiomass, ai.SellAbove + ai.SellBatch);
            Assert.IsFalse(Find(AiAction.SellSurplus).HasValue, "no cash above the travel cash for a deposit");

            _sim.Ledger.Grant(wallet, 100 * Wallet.CentsPerCredit);
            _sim.Clock.Advance(1);
            Assert.IsTrue(Find(AiAction.SellSurplus).HasValue);
            RunPendingOrders();
            Assert.AreEqual(1, Orders(OrderSide.Sell));
            Assert.GreaterOrEqual(_sim.Ledger.Balance(wallet), travel);
        }

        [Test]
        public void IdleCash_BuysTheCheapestRivalListing_OfARawItFeeds_OnlyDebtFree()
        {
            var ai = _sim.Balance.Ai;
            _sim.TechProgress.Get(_hq).Stage = ResearchStage.Complete; // retooling would finish and start the next research
            _sim.Refineries.Add(_sim.World.Create(), new Refinery { Site = _home, Output = ItemId.ItemRations });
            _sim.Ledger.TryFindPlayerWallet(Me, out var wallet);
            _sim.Ledger.Burn(wallet, _sim.Ledger.Balance(wallet));
            _sim.Ledger.Grant(wallet, 3 * ai.CashReserveCredits * Wallet.CentsPerCredit);
            long price = Price(ItemId.RawBiomass, -5);
            foreach (long ask in new[] { price + 10, price })
                _sim.WtoOrders.Add(_sim.World.Create(), new WtoOrder
                {
                    OwnerPlayerId = 2, Item = ItemId.RawBiomass, Side = OrderSide.Sell, PriceCents = ask, Remaining = 10,
                    Site = _sim.World.Create()
                });
            _sim.Loans.Get(_hq).PrincipalCents = 1;
            Assert.IsFalse(Find(AiAction.BuyFeedstock).HasValue, "idle cash repays debt first");

            _sim.Loans.Get(_hq).PrincipalCents = 0;
            _sim.Clock.Advance(1);
            Assert.IsTrue(Find(AiAction.BuyFeedstock).HasValue);
        }

        [Test]
        public void IdleCash_BuysNoFeedstock_ItAlreadyHolds()
        {
            var ai = _sim.Balance.Ai;
            _sim.TechProgress.Get(_hq).Stage = ResearchStage.Complete;
            _sim.Refineries.Add(_sim.World.Create(), new Refinery { Site = _home, Output = ItemId.ItemRations });
            _sim.Stockpiles.Get(_home).Add(ItemId.RawBiomass, ai.SellAbove);
            _sim.WtoOrders.Add(_sim.World.Create(), new WtoOrder
            {
                OwnerPlayerId = 2, Item = ItemId.RawBiomass, Side = OrderSide.Sell, PriceCents = Price(ItemId.RawBiomass, 0),
                Remaining = 10, Site = _sim.World.Create()
            });
            Assert.IsFalse(Find(AiAction.BuyFeedstock).HasValue);
        }

        [Test]
        public void DoesNotBid_WithoutTheCredits()
        {
            _sim.TechProgress.Get(_hq).Stage = ResearchStage.StressTest;
            _sim.Ledger.TryFindPlayerWallet(Me, out var wallet);
            _sim.Ledger.Burn(wallet, _sim.Ledger.Balance(wallet));
            _sim.Ledger.Grant(wallet, _sim.Balance.Ai.TravelCashCredits * Wallet.CentsPerCredit);
            Assert.IsFalse(Find(AiAction.BuyShortfall).HasValue, "the travel cash is kept for tolls");
        }
    }
}
