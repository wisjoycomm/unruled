using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>Map: my core base H - highway - my outpost O.</summary>
    public class RelayStationSystemTests
    {
        private const int Me = 1;

        private SimulationWorld _sim;
        private LatencyBalance _b;
        private EntityId _home, _outpost;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 3);
            _b = _sim.Balance.Latency;
            _home = Site(0);
            _outpost = Site(200);
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = _home, SiteB = _outpost, Tier = StarlaneTier.StaHighway, Length = 200 });
            _sim.LaneWeather.Add(lane, default);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _sim.CreatePlayer(Me, _home);
            Stock(_b.RelayBuildSteel, _b.RelayBuildBatteries + 2 * _b.RelayUpkeepBatteries);
        }

        private EntityId Site(int x)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = Me, PosX = x });
            _sim.Stockpiles.Add(site, default);
            _sim.Weather.Add(site, default);
            return site;
        }

        private void Stock(int steel, int batteries)
        {
            ref var stock = ref _sim.Stockpiles.Get(_outpost);
            stock.Add(ItemId.ItemSteel, steel);
            stock.Add(ItemId.ItemBattery, batteries);
        }

        private int Latency()
        {
            Assert.AreEqual(LatencyStatus.Ok, _sim.Latency.TryComputeToSite(Me, _outpost, out int latency));
            return latency;
        }

        private void Build()
        {
            var command = new DelayedPlayerCommand { PlayerId = Me, CommandType = CommandType.BuildRelay, Target = _outpost };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            while (_sim.Clock.CurrentTick < at)
                _sim.Clock.Advance(1);
        }

        private void ToNextUpkeep()
        {
            do _sim.Clock.Advance(1);
            while (_sim.Clock.CurrentTick % _b.RelayUpkeepIntervalTicks != 0);
        }

        [Test]
        public void ARelayBuiltOnAnOwnSite_PaysItsCostFromTheSite_AndRelaysTheHopToIt()
        {
            Assert.AreEqual(_b.BaseTicks + _b.HopTicks, Latency());

            Build();

            Assert.IsTrue(_sim.Relays.Has(_outpost));
            Assert.AreEqual(Me, _sim.Relays.Get(_outpost).OwnerPlayerId);
            Assert.AreEqual(0, _sim.Stockpiles.Get(_outpost).Get(ItemId.ItemSteel));
            Assert.AreEqual(2 * _b.RelayUpkeepBatteries, _sim.Stockpiles.Get(_outpost).Get(ItemId.ItemBattery));
            Assert.AreEqual(_b.BaseTicks + _b.RelayedHopTicks, Latency());
        }

        [Test]
        public void NoRelay_OnARivalSite_OrWithoutTheCost_OrOnTopOfAnother()
        {
            _sim.Stockpiles.Get(_outpost).Add(ItemId.ItemSteel, -1);
            Build();
            Assert.IsFalse(_sim.Relays.Has(_outpost), "a steel short");

            Stock(1, 0);
            _sim.Sites.Get(_outpost).OwnerPlayerId = 2;
            Build();
            Assert.IsFalse(_sim.Relays.Has(_outpost), "not my site");

            _sim.Sites.Get(_outpost).OwnerPlayerId = Me;
            Build();
            Assert.IsTrue(_sim.Relays.Has(_outpost));
            Stock(_b.RelayBuildSteel, _b.RelayBuildBatteries);
            Build();
            Assert.AreEqual(_b.RelayBuildSteel, _sim.Stockpiles.Get(_outpost).Get(ItemId.ItemSteel), "one relay per site");
        }

        [Test]
        public void ARelay_BurnsItsUpkeep_AndGoesDarkWhenTheSiteCannotPay()
        {
            Build();

            ToNextUpkeep();
            Assert.AreEqual(_b.RelayUpkeepBatteries, _sim.Stockpiles.Get(_outpost).Get(ItemId.ItemBattery));
            ToNextUpkeep();
            Assert.IsTrue(_sim.Relays.Has(_outpost));
            Assert.AreEqual(0, _sim.Stockpiles.Get(_outpost).Get(ItemId.ItemBattery));

            ToNextUpkeep();
            Assert.IsFalse(_sim.Relays.Has(_outpost));
            Assert.AreEqual(_b.BaseTicks + _b.HopTicks, Latency());
        }

        [Test]
        public void ARelay_IsLost_WhenItsSiteChangesHands()
        {
            Build();
            _sim.Sites.Get(_outpost).OwnerPlayerId = 2;

            ToNextUpkeep();

            Assert.IsFalse(_sim.Relays.Has(_outpost));
            Assert.AreEqual(2 * _b.RelayUpkeepBatteries, _sim.Stockpiles.Get(_outpost).Get(ItemId.ItemBattery));
        }
    }
}
