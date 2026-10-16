using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.AI
{
    /// <summary>Map: my core base H - highway - target site T (neutral unless a test says otherwise).</summary>
    public class AiExpansionTests
    {
        private const int Me = 1;
        private const int Rival = 2;
        private const byte Sentinel = 1; // Stealth_Raid 0, Siege_Conquest 0
        private const byte Raider = 3; // Stealth_Raid 1.9
        private const byte Warlord = 4; // Siege_Conquest 1.8

        private SimulationWorld _sim;
        private EntityId _home, _target, _hq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 2);
            _home = Site(0, Me);
            _target = Site(200, 0);
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = _home, SiteB = _target, Tier = StarlaneTier.StaHighway, Length = 200 });
            _sim.LaneWeather.Add(lane, default);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _hq = _sim.CreatePlayer(Me, _home);
        }

        private EntityId Site(int x, int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner, PosX = x });
            _sim.Stockpiles.Add(site, default);
            _sim.Weather.Add(site, default);
            return site;
        }

        private EntityId Fleet(int owner, EntityId site, int lineShips, bool loaded, byte tier = 1)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = owner, TechTier = tier, LineShips = lineShips }, site);
            if (!loaded)
                return fleet;
            ref var hold = ref _sim.Stockpiles.Get(fleet);
            hold.Add(ItemId.ItemAmmo, lineShips * _sim.Balance.Ai.LoadAmmoPerLineShip);
            hold.Add(ItemId.ItemFuel, _sim.Balance.Ai.LoadFuel);
            hold.Add(ItemId.ItemSteel, 10);
            hold.Add(ItemId.ItemBattery, 5);
            return fleet;
        }

        private AiOrder? Find(byte archetype, AiAction action)
        {
            _sim.Ai.Assign(_hq, archetype);
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

        [Test]
        public void LoadsAmmo_AtTheCoreBase_BeforeGoingOut()
        {
            var fleet = Fleet(Me, _home, 4, loaded: false);
            _sim.Stockpiles.Get(_home).Add(ItemId.ItemAmmo, 500);

            var order = Find(Warlord, AiAction.LoadCargo);
            Assert.IsTrue(order.HasValue);
            Assert.AreEqual(fleet, order.Value.Subject);
            RunPendingOrders();
            Assert.AreEqual(4 * _sim.Balance.Ai.LoadAmmoPerLineShip, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemAmmo));
        }

        [Test]
        public void Warlord_BesiegesANeutralSite_ThenClaimsIt()
        {
            var fleet = Fleet(Me, _home, 4, loaded: true);

            var order = Find(Warlord, AiAction.Siege);
            Assert.IsTrue(order.HasValue);
            Assert.AreEqual(IssueResult.Issued, order.Value.Result);
            for (int i = 0; i < 2_000 && _sim.FleetMotions.Get(fleet).Site != _target; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(_target, _sim.FleetMotions.Get(fleet).Site, "arrived");

            Assert.IsTrue(Find(Warlord, AiAction.ClaimSite).HasValue);
            RunPendingOrders();
            Assert.AreEqual(Me, _sim.Sites.Get(_target).OwnerPlayerId);
        }

        [Test]
        public void Raider_HuntsWeakerRivalFleets_AtARivalSite()
        {
            _sim.Sites.Get(_target).OwnerPlayerId = Rival;
            Fleet(Me, _home, 6, loaded: true);
            Fleet(Rival, _target, 2, loaded: false);

            var order = Find(Raider, AiAction.StealthRaid);
            Assert.IsTrue(order.HasValue);
            Assert.AreEqual(IssueResult.Issued, order.Value.Result);
        }

        [Test]
        public void NobodyAttacksAStrongerDefender_AndTheSentinelNeverLeaves()
        {
            _sim.Sites.Get(_target).OwnerPlayerId = Rival;
            Fleet(Me, _home, 2, loaded: true);
            Fleet(Rival, _target, 6, loaded: false);
            Assert.IsFalse(Find(Raider, AiAction.StealthRaid).HasValue, "outgunned");
            Assert.IsFalse(Find(Warlord, AiAction.Siege).HasValue, "outgunned");

            _sim.Sites.Get(_target).OwnerPlayerId = 0;
            Assert.IsFalse(Find(Sentinel, AiAction.Siege).HasValue);
            Assert.IsFalse(Find(Sentinel, AiAction.StealthRaid).HasValue);
        }

        [Test]
        public void StressTests_WhenTelemetryIsDoneAndTheBaseCanPay()
        {
            _sim.TechProgress.Get(_hq).Stage = ResearchStage.StressTest;
            Assert.IsFalse(Find(Sentinel, AiAction.StressTest).HasValue, "can't pay");

            var cost = _sim.Balance.Research.Tier(2).StressTestCost;
            for (int i = 0; i < cost.Length; i++)
                _sim.Stockpiles.Get(_home).Add(cost[i].Item, cost[i].Quantity);
            Assert.IsTrue(Find(Sentinel, AiAction.StressTest).HasValue);
        }

        [Test]
        public void OpensTheGate_AtTier2_UnlessItStaysHome()
        {
            _sim.JumpGates.Add(_sim.World.Create(), new JumpGate { OwnerPlayerId = Me, HomeSite = _home, FarSite = _target });
            ref var stock = ref _sim.Stockpiles.Get(_home);
            stock.Add(ItemId.ItemBattery, 100);
            stock.Add(ItemId.ItemFuel, 100);
            Assert.IsFalse(Find(Warlord, AiAction.OpenGate).HasValue, "tier 1");

            _sim.TechProgress.Get(_hq).TechTier = 2;
            Assert.IsFalse(Find(Sentinel, AiAction.OpenGate).HasValue, "the Sentinel stays sealed");
            Assert.IsTrue(Find(Warlord, AiAction.OpenGate).HasValue);
        }

        // The target becomes THE SYSTEM's Citadel; its garrison is struck down and not due back during the test.
        private void MakeTargetAnUnguardedCitadel()
        {
            _sim.Defense.CreateCitadel(_target);
            ref var citadel = ref _sim.Citadels.Get(_target);
            _sim.World.Destroy(citadel.Garrison);
            citadel.Garrison = default;
            citadel.GarrisonDueTick = long.MaxValue;
        }

        [Test]
        public void Warlord_AssaultsAnUnguardedCitadel_ThenHoldsItAndBombards()
        {
            MakeTargetAnUnguardedCitadel();
            var fleet = Fleet(Me, _home, 4, loaded: true, tier: 3);

            var order = Find(Warlord, AiAction.AssaultCitadel);
            Assert.IsTrue(order.HasValue);
            Assert.AreEqual(IssueResult.Issued, order.Value.Result);
            for (int i = 0; i < 2_000 && _sim.FleetMotions.Get(fleet).Site != _target; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(_target, _sim.FleetMotions.Get(fleet).Site, "arrived");

            Assert.IsFalse(Find(Warlord, AiAction.ReturnHome).HasValue, "holds the Citadel while it has ammo");
            int shield = _sim.Citadels.Get(_target).Integrity;
            _sim.Clock.Advance(_sim.Balance.Citadel.CheckIntervalTicks);
            Assert.Less(_sim.Citadels.Get(_target).Integrity, shield, "bombarded");
        }

        [Test]
        public void NobodyAssaultsTheCitadel_BelowTheTier_OrAgainstAStrongerGarrison()
        {
            _sim.Defense.CreateCitadel(_target);
            Fleet(Me, _home, 4, loaded: true, tier: 3);
            Assert.IsFalse(Find(Warlord, AiAction.AssaultCitadel).HasValue, "the garrison outguns 4 ships");

            SetUp();
            MakeTargetAnUnguardedCitadel();
            var fleet = Fleet(Me, _home, 4, loaded: true, tier: 2);
            Assert.IsFalse(Find(Warlord, AiAction.AssaultCitadel).HasValue, "tier 2");

            _sim.Fleets.Get(fleet).TechTier = 3;
            Assert.IsFalse(Find(Sentinel, AiAction.AssaultCitadel).HasValue, "the Sentinel never besieges");
            Assert.IsTrue(Find(Warlord, AiAction.AssaultCitadel).HasValue);
        }
    }
}
