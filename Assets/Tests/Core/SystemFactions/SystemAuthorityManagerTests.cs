using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.SystemFactions
{
    public class SystemAuthorityManagerTests
    {
        private const int Pirate = 1;
        private const int Trader = 2;

        private SimulationWorld _sim;
        private EntityId _pirateSite, _traderSite;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 6);
            _pirateSite = Site(Pirate);
            _traderSite = Site(Trader);
            _sim.CreatePlayer(Pirate, _pirateSite);
            _sim.CreatePlayer(Trader, _traderSite);
        }

        private EntityId Site(int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private void Buy(int player, EntityId site) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                ExecuteAtTick = _sim.Clock.CurrentTick + 1, PlayerId = player, CommandType = CommandType.PlaceBuyOrder,
                Target = site, ParamA = (int)ItemId.ItemSteel, ParamB = 1, ParamC = 5_000
            });

        private int OrdersOf(int player)
        {
            int count = 0;
            for (int i = 0; i < _sim.WtoOrders.Count; i++)
                if (_sim.WtoOrders.At(i).OwnerPlayerId == player)
                    count++;
            return count;
        }

        private EntityId DefenceFleet()
        {
            for (int i = 0; i < _sim.Fleets.Count; i++)
                if (_sim.Fleets.At(i).OwnerPlayerId == SystemAgencies.OwnerId(SystemAgency.Wto))
                    return _sim.Fleets.EntityAt(i);
            return default;
        }

        private int BulletinsOf(BulletinKind kind)
        {
            int count = 0;
            for (int i = 0; i < _sim.Bulletins.Count; i++)
                if (_sim.Bulletins.At(i).Kind == kind)
                    count++;
            return count;
        }

        [Test]
        public void FirstOffence_BarsTheWto_ForTheBarTime()
        {
            _sim.Authority.ReportPiracy(Pirate, _sim.Clock.CurrentTick);
            Buy(Pirate, _pirateSite);
            Buy(Trader, _traderSite);
            _sim.Clock.Advance(1);
            Assert.AreEqual(0, OrdersOf(Pirate), "the offender is barred");
            Assert.AreEqual(1, OrdersOf(Trader));
            Assert.IsTrue(DefenceFleet().IsNull, "one offence is not a Call-to-Arms");

            _sim.Clock.Advance(_sim.Balance.Authority.WtoBarTicks);
            Buy(Pirate, _pirateSite);
            _sim.Clock.Advance(1);
            Assert.AreEqual(1, OrdersOf(Pirate), "the bar has run out");
        }

        [Test]
        public void ThirdOffence_CallsTheWtoDefenceFleet_ToTheCoreBase_ForItsDuration()
        {
            var authority = _sim.Balance.Authority;
            for (int i = 0; i < authority.LayerTwoOffences; i++)
                _sim.Authority.ReportPiracy(Pirate, _sim.Clock.CurrentTick);
            _sim.Clock.Advance(1);

            var fleet = DefenceFleet();
            Assert.IsFalse(fleet.IsNull, "a defence fleet formed");
            Assert.AreEqual(_pirateSite, _sim.FleetMotions.Get(fleet).Site);
            Assert.AreEqual(authority.DefenceLineShips, _sim.Fleets.Get(fleet).LineShips);
            Assert.AreEqual(1, BulletinsOf(BulletinKind.CallToArms));
            Assert.AreEqual(Pirate, _sim.Bulletins.At(0).PlayerId);

            _sim.Clock.Advance(authority.DefenceTicks);
            Assert.IsFalse(_sim.World.IsAlive(fleet), "the defence fleet stands down");
            Assert.IsTrue(DefenceFleet().IsNull);
            Assert.AreEqual(0, BulletinsOf(BulletinKind.CallToArms), "the bulletin expired");
        }

        [Test]
        public void NuclearStrike_PostsAnIaeaBulletin_UntilTheIrradiationEnds()
        {
            _sim.Lifecycle.ApplyNuclearStrike(_traderSite, 0);
            long until = _sim.Sites.Get(_traderSite).IrradiatedUntilTick;
            _sim.Clock.Advance(1);
            _sim.Clock.Advance(1);

            Assert.AreEqual(1, BulletinsOf(BulletinKind.NuclearStrike), "posted once");
            var bulletin = _sim.Bulletins.At(0);
            Assert.AreEqual(SystemAgency.Iaea, bulletin.Agency);
            Assert.AreEqual(_traderSite, bulletin.Site);
            Assert.AreEqual(until, bulletin.ExpiresTick);

            _sim.Clock.Advance(until - _sim.Clock.CurrentTick);
            Assert.AreEqual(0, BulletinsOf(BulletinKind.NuclearStrike));
        }

        [Test]
        public void SystemFleets_LeaveNoLoot()
        {
            var raider = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Pirate, TechTier = 1, LineShips = 20 }, _traderSite);
            ref var hold = ref _sim.Stockpiles.Get(raider);
            for (int i = 0; i < ItemIds.Count; i++)
                hold.Add(ItemIds.FromSlot(i), 1_000);
            var system = _sim.CreateFleet(
                new FleetUnit { OwnerPlayerId = SystemAgencies.OwnerId(SystemAgency.Wto), TechTier = 1, LineShips = 1 },
                _traderSite);
            _sim.Stockpiles.Get(system).Add(ItemId.RawWater, 50);

            for (int i = 0; i < 600 && _sim.World.IsAlive(system); i++)
                _sim.Clock.Advance(1);

            Assert.IsFalse(_sim.World.IsAlive(system), "the system fleet was sunk");
            Assert.AreEqual(1_000, _sim.Stockpiles.Get(raider).Get(ItemId.RawWater), "nothing looted");
        }

        [Test]
        public void Forecast_ShowsTheFrontsThatForm_WithinTheBracketLead()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 3);
            sim.GenerateSystem(41, clusterId: 0);
            sim.GenerateSystem(42, clusterId: 1);
            var hq = sim.CreatePlayer(Pirate, sim.Sites.EntityAt(0));
            sim.CreatePlayer(Trader, sim.Sites.EntityAt(1));
            sim.Standings.Get(hq).Bracket = 6;

            int lead = sim.Forecast.LeadTicks(Pirate);
            Assert.AreEqual(sim.Balance.Leaderboard.Bracket(6).NoaaLeadTicks, lead);
            var forecast = new ForecastFront[256];
            Assert.AreEqual(0, sim.Forecast.Forecast(Trader, 0, forecast), "unrated players get no NOAA service");
            int count = sim.Forecast.Forecast(Pirate, 0, forecast);
            Assert.Less(count, forecast.Length);

            var formed = new List<StormFront>();
            var seen = new HashSet<EntityId>();
            for (int t = 1; t <= lead; t++)
            {
                sim.Clock.Advance(1);
                for (int i = 0; i < sim.StormFronts.Count; i++)
                    if (seen.Add(sim.StormFronts.EntityAt(i)))
                        formed.Add(sim.StormFronts.At(i));
            }

            Assert.Greater(formed.Count, 0, "fronts formed within the lead");
            Assert.AreEqual(formed.Count, count);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(formed[i].ClusterId, forecast[i].Front.ClusterId, $"front {i}");
                Assert.AreEqual(formed[i].Scale, forecast[i].Front.Scale, $"front {i}");
                Assert.AreEqual(formed[i].Level, forecast[i].Front.Level, $"front {i}");
                Assert.AreEqual(formed[i].CenterX, forecast[i].Front.CenterX, $"front {i}");
                Assert.AreEqual(formed[i].CenterY, forecast[i].Front.CenterY, $"front {i}");
                Assert.AreEqual(formed[i].EndTick, forecast[i].Front.EndTick, $"front {i}");
            }
        }
    }
}
