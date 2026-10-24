using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    /// <summary>Me (1) holds core base C1; the rival (2) holds core base C2 and an outpost O; lanes join them all.</summary>
    public class PresenceSystemTests
    {
        private const int Me = 1;
        private const int Rival = 2;

        private SimulationWorld _sim;
        private PresenceBalance _b;
        private EntityId _c1, _c2, _outpost, _meHq, _rivalHq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 3);
            _b = _sim.Balance.Presence;
            _c1 = Site(0, Me);
            _c2 = Site(400, Rival);
            _outpost = Site(200, Rival);
            Lane(_c1, _outpost);
            Lane(_outpost, _c2);
            _meHq = _sim.CreatePlayer(Me, _c1);
            _rivalHq = _sim.CreatePlayer(Rival, _c2);
            Send(CommandType.LogIn, Me);
            Send(CommandType.LogIn, Rival);
        }

        private EntityId Site(int x, int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { PosX = x, OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private void Lane(EntityId a, EntityId b)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = a, SiteB = b, Tier = StarlaneTier.StaHighway, Length = 200 });
        }

        private void Send(CommandType type, int player, EntityId target = default)
        {
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = _sim.Clock.CurrentTick, ExecuteAtTick = _sim.Clock.CurrentTick + 1, PlayerId = player,
                CommandType = type, Target = target
            });
            _sim.Clock.Advance(1);
        }

        private void Run(long ticks) => _sim.Clock.Advance(ticks);

        private long Now => _sim.Clock.CurrentTick;

        private bool Protected(EntityId site) => _sim.Presence.IsProtectedAt(Rival, site, Now);

        private EntityId Fleet(int owner, EntityId site, int line = 10)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = owner, TechTier = 1, LineShips = line, EwShips = 2 }, site);
            ref var hold = ref _sim.Stockpiles.Get(fleet);
            hold.Add(ItemId.ItemAmmo, 500);
            hold.Add(ItemId.ItemBattery, 500);
            hold.Add(ItemId.ItemCoolant, 500);
            hold.Add(ItemId.ItemFuel, 500);
            hold.Add(ItemId.ItemOxygen, 500);
            hold.Add(ItemId.ItemRations, 500);
            hold.Add(ItemId.ItemSteel, 50);
            return fleet;
        }

        [Test]
        public void Logout_RaisesTheShieldAfterTheDelay_OverOwnSitesOnly_ForAtMostOneLogoutsLimit()
        {
            Send(CommandType.LogOut, Rival);
            Assert.IsFalse(Protected(_outpost), "not up yet");

            Run(_b.ShieldDelayTicks);
            Assert.IsTrue(Protected(_outpost));
            Assert.IsTrue(Protected(_c2));
            Assert.IsFalse(Protected(_c1), "a site it doesn't own");
            Assert.IsFalse(Protected(default), "out in space");

            Run(_b.ShieldMaxTicks);
            Assert.IsFalse(Protected(_outpost), "the per-logout limit ran out");
        }

        [Test]
        public void TheWeeklyAllowance_RunsOut_AndAnEarlyLoginGivesBackWhatWasNotUsed()
        {
            long logouts = _b.ShieldWeeklyTicks / _b.ShieldMaxTicks;
            Send(CommandType.LogOut, Rival);
            Run(_b.ShieldDelayTicks + _b.ShieldMaxTicks / 2);
            Send(CommandType.LogIn, Rival);
            Assert.AreEqual(_b.ShieldMaxTicks / 2, _sim.Presences.Get(_rivalHq).WeekShieldTicks, 1, "about half given back");

            for (int i = 0; i < logouts; i++)
            {
                Send(CommandType.LogOut, Rival);
                Run(_b.ShieldDelayTicks + _b.ShieldMaxTicks);
                Send(CommandType.LogIn, Rival);
            }
            Assert.AreEqual(_b.ShieldWeeklyTicks, _sim.Presences.Get(_rivalHq).WeekShieldTicks, "the week is spent");
            Send(CommandType.LogOut, Rival);
            Run(_b.ShieldDelayTicks);
            Assert.IsFalse(Protected(_outpost), "no allowance left this week");

            Run(PresenceBalance.TicksPerWeek);
            Send(CommandType.LogIn, Rival);
            Send(CommandType.LogOut, Rival);
            Run(_b.ShieldDelayTicks);
            Assert.IsTrue(Protected(_outpost), "a new week, a new allowance");
        }

        [Test]
        public void AShieldedSite_IsNotFought_NorClaimed()
        {
            var defender = Fleet(Rival, _c2);
            Send(CommandType.LogOut, Rival);
            Run(_b.ShieldDelayTicks);
            var attacker = Fleet(Me, _c2);

            Run(10 * _sim.Balance.Combat.SalvoCycleTicks);
            Assert.AreEqual(10, _sim.Fleets.Get(defender).LineShips);
            Assert.AreEqual(10, _sim.Fleets.Get(attacker).LineShips, "the shielded side fights nobody either");

            Fleet(Me, _outpost);
            Send(CommandType.ClaimSite, Me, _outpost);
            Assert.AreEqual(Rival, _sim.Sites.Get(_outpost).OwnerPlayerId);
        }

        [Test]
        public void AFightBeforeTheShieldRises_PushesItBack()
        {
            Fleet(Rival, _outpost);
            Fleet(Me, _outpost);
            Send(CommandType.LogOut, Rival);
            Run(_b.ShieldDelayTicks);
            Assert.IsFalse(Protected(_outpost), "still fighting: the shield waits");
        }

        [Test]
        public void ANewcomer_IsProtectedEverywhere_UntilItAttacksOrGivesItUp()
        {
            _sim.Presence.GrantNewcomerProtection(_rivalHq);
            Assert.IsTrue(Protected(_outpost));
            Assert.IsTrue(Protected(default), "even out in space");

            Send(CommandType.EndProtection, Rival);
            Assert.IsFalse(Protected(_outpost));

            _sim.Presence.GrantNewcomerProtection(_meHq);
            Fleet(Me, _outpost);
            Send(CommandType.ClaimSite, Me, _outpost);
            Assert.AreEqual(Me, _sim.Sites.Get(_outpost).OwnerPlayerId);
            Assert.IsFalse(_sim.Presence.IsProtectedAt(Me, _c1, Now), "taking a rival's site ends it");
        }

        [Test]
        public void Presence_SurvivesASave_AndSchema7SavesLoadEveryoneUnprotected()
        {
            _sim.Presence.GrantNewcomerProtection(_meHq);
            Send(CommandType.LogOut, Rival);
            var balance = _sim.Balance;

            var loaded = SaveGame.Load(balance, SaveGame.Write(_sim));
            Assert.AreEqual(_sim.Presences.Get(_rivalHq).ShieldFromTick, loaded.Presences.Get(_rivalHq).ShieldFromTick);
            Assert.IsTrue(loaded.Presences.Get(_meHq).IsNewcomer);
            Assert.IsTrue(loaded.Presences.Get(_meHq).IsOnline);

            var old = SaveGame.Load(balance, SaveGame.Write(_sim, schemaVersion: 7, storeCount: 42));
            Assert.AreEqual(2, old.Presences.Count);
            Assert.IsFalse(old.Presence.IsProtectedAt(Me, _c1, old.Clock.CurrentTick));
            CollectionAssert.AreEqual(SaveGame.Write(old), SaveGame.Write(SaveGame.Load(balance, SaveGame.Write(old))),
                "resaved as the current schema");
        }
    }
}
