using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.Galaxy
{
    public class ChartMarketTests
    {
        private SimulationWorld _sim;
        private NewGame _game;
        private int _charted; // a system player 1 surveyed first

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 31);
            _game = NewGameSetup.Create(_sim, 1);
            _sim.Stockpiles.Get(_game.Fleet).Add(ItemId.ItemFuel, _sim.Balance.Galaxy.SurveyFuel);
            int home = _sim.Sites.Get(_game.CoreBase).ClusterId;
            Assert.IsTrue(_sim.Galaxy.TryPickSurvey(1, home, out var star));
            var survey = new DelayedPlayerCommand
            {
                PlayerId = 1, CommandType = CommandType.Survey, Target = _game.Fleet, ExecuteAtTick = 1
            };
            StarId.Split(star.Packed, out survey.ParamA, out survey.ParamB);
            _sim.Clock.Enqueue(survey);
            _sim.Clock.Advance(1);
            Assert.IsTrue(_sim.Galaxy.TryGetCluster(star, out _charted));
            for (int p = 1; p <= 4; p++)
                _sim.Ledger.Grant(Wallet(p), 500 * global::Unruled.Core.Economy.Wallet.CentsPerCredit); // players start broke
        }

        private EntityId SiteOf(int cluster) => _sim.Galaxy.SiteAtOrbit(cluster, 0);
        private EntityId CoreOf(int playerId) => _sim.Players.At(playerId - 1).CoreBase;

        private EntityId Wallet(int playerId)
        {
            Assert.IsTrue(_sim.Ledger.TryFindPlayerWallet(playerId, out var wallet));
            return wallet;
        }

        // Runs a chart order (from the player's own base, about a system) straight through the handler; latency is the
        // issuer's business, tested elsewhere.
        private void Run(int playerId, CommandType type, int cluster, int a = 0, int b = 0) =>
            Run(playerId, type, CoreOf(playerId), a, b, default, cluster);

        private void Run(int playerId, CommandType type, EntityId target, int a = 0, int b = 0, EntityId param = default,
            int cluster = 0)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = type, Target = target, ParamA = a, ParamB = b, ParamC = cluster
            };
            if (!param.IsNull)
                command.ParamEntity = param;
            _sim.Charts.Handle(command, _sim.Clock.CurrentTick);
        }

        [Test]
        public void Share_GivesTheChartForFree_OnlyFromSomeoneWhoKnowsIt()
        {
            Assert.IsFalse(_sim.Galaxy.Knows(2, _charted));
            Run(3, CommandType.ShareChart, _charted, a: 2);
            Assert.IsFalse(_sim.Galaxy.Knows(2, _charted), "player 3 doesn't know it, so can't share it");

            long cash = _sim.Ledger.Balance(Wallet(2));
            Run(1, CommandType.ShareChart, _charted, a: 2);
            Assert.IsTrue(_sim.Galaxy.Knows(2, _charted));
            Assert.AreEqual(cash, _sim.Ledger.Balance(Wallet(2)));
        }

        [Test]
        public void PlayerShop_AnyKnowerSells_EachBuyerPaysOnce_AndTheListingStays()
        {
            Run(1, CommandType.ShareChart, _charted, a: 2);
            Run(2, CommandType.ListChart, _charted, a: (int)ChartShop.Player, b: 30);
            var listing = _sim.Charts.FindListing(2, _charted, ChartShop.Player);
            Assert.IsFalse(listing.IsNull, "a second-hand knower may sell on its own shop");

            long seller = _sim.Ledger.Balance(Wallet(2)), buyer = _sim.Ledger.Balance(Wallet(3)), sta = _sim.Ledger.Balance(_sim.StaTollPool);
            Run(3, CommandType.BuyChart, CoreOf(3), param: listing);
            Assert.IsTrue(_sim.Galaxy.Knows(3, _charted));
            Assert.AreEqual(buyer - 3000, _sim.Ledger.Balance(Wallet(3)));
            Assert.AreEqual(seller + 3000, _sim.Ledger.Balance(Wallet(2)));
            Assert.AreEqual(sta, _sim.Ledger.Balance(_sim.StaTollPool), "no STA cut on a player shop");

            Run(3, CommandType.BuyChart, CoreOf(3), param: listing);
            Assert.AreEqual(buyer - 3000, _sim.Ledger.Balance(Wallet(3)), "a chart is bought once");
            Run(4, CommandType.BuyChart, CoreOf(4), param: listing);
            Assert.IsTrue(_sim.Galaxy.Knows(4, _charted), "the listing serves the next buyer");
            Assert.AreEqual(2, _sim.ChartListings.Get(listing).Sales);
        }

        [Test]
        public void WorldShop_OnlyTheFirstSurveyor_PaysTheFee_StaTakesItsCut_AndTheRegistryPosts()
        {
            Run(1, CommandType.ShareChart, _charted, a: 2);
            Run(2, CommandType.ListChart, _charted, a: (int)ChartShop.World, b: 100);
            Assert.IsTrue(_sim.Charts.FindListing(2, _charted, ChartShop.World).IsNull, "not the first surveyor");

            var galaxy = _sim.Balance.Galaxy;
            long seller = _sim.Ledger.Balance(Wallet(1)), sta = _sim.Ledger.Balance(_sim.StaTollPool);
            Run(1, CommandType.ListChart, _charted, a: (int)ChartShop.World, b: 100);
            var listing = _sim.Charts.FindListing(1, _charted, ChartShop.World);
            Assert.IsFalse(listing.IsNull);
            Assert.AreEqual(seller - galaxy.ChartListingFeeCents, _sim.Ledger.Balance(Wallet(1)));
            Assert.AreEqual(sta + galaxy.ChartListingFeeCents, _sim.Ledger.Balance(_sim.StaTollPool));
            bool posted = false;
            for (int i = 0; i < _sim.Bulletins.Count; i++)
                posted |= _sim.Bulletins.At(i).Kind == BulletinKind.ChartRegistered && _sim.Bulletins.At(i).PlayerId == 1;
            Assert.IsTrue(posted, "the STA registry publishes the listing");

            Run(1, CommandType.ListChart, _charted, a: (int)ChartShop.World, b: 100);
            Assert.AreEqual(seller - galaxy.ChartListingFeeCents, _sim.Ledger.Balance(Wallet(1)), "registered once");

            seller = _sim.Ledger.Balance(Wallet(1));
            sta = _sim.Ledger.Balance(_sim.StaTollPool);
            Run(3, CommandType.BuyChart, CoreOf(3), param: listing);
            long cut = 100 * global::Unruled.Core.Economy.Wallet.CentsPerCredit * galaxy.ChartSaleCutPermille / 1000;
            Assert.IsTrue(_sim.Galaxy.Knows(3, _charted));
            Assert.AreEqual(sta + cut, _sim.Ledger.Balance(_sim.StaTollPool));
            Assert.AreEqual(seller + 10_000 - cut, _sim.Ledger.Balance(Wallet(1)));
        }

        [Test]
        public void Buy_Refused_WhenBroke_OrFromOwnListing_OrAtSomeoneElsesSite_AndCancelWithdraws()
        {
            Run(1, CommandType.ListChart, _charted, a: (int)ChartShop.Player, b: 1_000_000);
            var listing = _sim.Charts.FindListing(1, _charted, ChartShop.Player);
            Run(3, CommandType.BuyChart, CoreOf(3), param: listing);
            Assert.IsFalse(_sim.Galaxy.Knows(3, _charted), "can't afford it");
            Run(3, CommandType.BuyChart, CoreOf(1), param: listing);
            Assert.IsFalse(_sim.Galaxy.Knows(3, _charted), "the order must travel to the buyer's own base");

            Run(2, CommandType.CancelChartListing, _charted, a: (int)ChartShop.Player);
            Assert.IsTrue(_sim.ChartListings.Has(listing), "only the seller withdraws it");
            Run(1, CommandType.CancelChartListing, _charted, a: (int)ChartShop.Player);
            Assert.IsFalse(_sim.ChartListings.Has(listing));
        }

        [Test]
        public void ChartOrders_FromSomeoneElsesSite_ChangeNothing()
        {
            Run(1, CommandType.ListChart, CoreOf(2), a: (int)ChartShop.Player, b: 10, cluster: _charted);
            Assert.IsTrue(_sim.Charts.FindListing(1, _charted, ChartShop.Player).IsNull);
            Run(1, CommandType.ShareChart, CoreOf(2), a: 2, cluster: _charted);
            Assert.IsFalse(_sim.Galaxy.Knows(2, _charted));
        }

        [Test]
        public void Ai_ListsAChartItSurveyedFirst_OnTheWorldShop_AndARivalAiBuysIt()
        {
            var seller = _sim.Players.EntityAt(0);
            var buyer = _sim.Players.EntityAt(1);
            _sim.Ai.Assign(seller, 2);
            _sim.Ai.Assign(buyer, 2);
            _sim.Ledger.Grant(Wallet(2), 10_000 * global::Unruled.Core.Economy.Wallet.CentsPerCredit); // idle cash
            int home2 = _sim.Sites.Get(CoreOf(2)).ClusterId;
            _sim.Galaxy.Learn(2, _sim.Sites.Get(_game.CoreBase).ClusterId, ChartSource.Share, 0); // knows space next to it

            // Chart trades are thought over once an hour; orders land after their signal latency.
            for (int hour = 0; hour < 6 && !_sim.Galaxy.Knows(2, _charted); hour++)
            {
                _sim.Clock.Advance(SimTick.TicksPerHour - _sim.Clock.CurrentTick % SimTick.TicksPerHour);
                _sim.Ai.Think(_sim.Clock.CurrentTick);
                _sim.Clock.Advance(_sim.Balance.Ai.ThinkIntervalTicks);
                _sim.Ai.Think(_sim.Clock.CurrentTick);
            }
            Assert.IsFalse(_sim.Charts.FindListing(1, _charted, ChartShop.World).IsNull, "the first surveyor lists it on the world shop");
            Assert.IsTrue(_sim.Galaxy.Knows(2, _charted), "a rival next to it buys it");
            Assert.AreNotEqual(home2, _charted);
        }

        [Test]
        public void Listings_RoundTripThroughASave()
        {
            Run(1, CommandType.ListChart, _charted, a: (int)ChartShop.World, b: 40);
            var loaded = SaveGame.Load(_sim.Balance, SaveGame.Write(_sim));
            Assert.AreEqual(1, loaded.ChartListings.Count);
            Assert.AreEqual(4000, loaded.ChartListings.At(0).PriceCents);
            Assert.AreEqual(1, loaded.StarSystems.Get(loaded.Galaxy.SystemOf(_charted)).FirstSurveyorPlayerId);
            CollectionAssert.AreEqual(SaveGame.Write(_sim), SaveGame.Write(loaded));
        }
    }
}
