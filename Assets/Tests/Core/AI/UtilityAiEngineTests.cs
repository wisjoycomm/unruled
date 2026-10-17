using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.AI
{
    /// <summary>Map: my core base H - bypass - my field site F. Rival fleets show up at F.</summary>
    public class UtilityAiEngineTests
    {
        private const int Me = 1;
        private const int Rival = 2;
        private const byte Sentinel = 1; // vents at 75 C, retreats below 0.65, scans above 30%, evacuates at G3/S3
        private const byte Trader = 2; // vents at 70 C
        private const byte Warlord = 4; // retreats below 0.35, evacuates at G5/S5

        private SimulationWorld _sim;
        private EntityId _home, _field, _lane, _hq;

        [SetUp]
        public void SetUp() => _sim = Build(out _home, out _field, out _lane, out _hq);

        private static SimulationWorld Build(out EntityId home, out EntityId field, out EntityId lane, out EntityId hq)
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 2);
            home = Site(sim, 0);
            field = Site(sim, 200);
            lane = sim.World.Create();
            sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = home, SiteB = field, Tier = StarlaneTier.OuterRimBypass, Length = 200 });
            sim.Graph.Rebuild(sim.Starlanes);
            hq = sim.CreatePlayer(Me, home);
            sim.Standings.Get(hq).IsAudited = true; // so the audit opt-in doesn't add an order to every think
            sim.Ledger.TryFindPlayerWallet(Me, out var wallet);
            sim.Ledger.Burn(wallet, sim.Ledger.Balance(wallet)); // and no WTO bids for the stress test
            sim.Loans.Get(hq) = new Loan { PrincipalCents = sim.Balance.Ledger.CreditLimitCents, DueTick = long.MaxValue }; // nor loans
            return sim;
        }

        private static EntityId Site(SimulationWorld sim, int x)
        {
            var site = sim.World.Create();
            sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = Me, PosX = x });
            sim.Stockpiles.Add(site, default);
            sim.Weather.Add(site, default);
            return site;
        }

        private static EntityId Fleet(SimulationWorld sim, int owner, EntityId site, int lineShips, int ammo = 1_000, int fuel = 100)
        {
            var fleet = sim.CreateFleet(new FleetUnit { OwnerPlayerId = owner, TechTier = 1, LineShips = lineShips }, site);
            ref var hold = ref sim.Stockpiles.Get(fleet);
            hold.Add(ItemId.ItemAmmo, ammo);
            hold.Add(ItemId.ItemFuel, fuel);
            hold.Add(ItemId.ItemCoolant, 100);
            return fleet;
        }

        private EntityId Fleet(int owner, EntityId site, int lineShips, int ammo = 1_000, int fuel = 100) =>
            Fleet(_sim, owner, site, lineShips, ammo, fuel);

        private int Think(byte archetype)
        {
            _sim.Ai.Assign(_hq, archetype);
            return _sim.Ai.Think(_sim.Clock.CurrentTick);
        }

        private AiOrder OnlyOrder(byte archetype)
        {
            Assert.AreEqual(1, Think(archetype), "one order");
            var order = _sim.Ai.OrderAt(0);
            Assert.AreEqual(IssueResult.Issued, order.Result);
            Assert.AreEqual(Me, order.PlayerId);
            return order;
        }

        private void RunPendingOrders()
        {
            for (int i = 0; i < 1_000 && _sim.Clock.PendingCommandCount > 0; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(0, _sim.Clock.PendingCommandCount, "orders arrived");
        }

        [Test]
        public void Score_IsWeightTimesGeometricMean()
        {
            Assert.AreEqual(1_800, UtilityAiEngine.Score(1_800, 1_000));
            Assert.AreEqual(900, UtilityAiEngine.Score(1_800, 250, 1_000), "sqrt(0.25 x 1) = 0.5");
            Assert.AreEqual(0, UtilityAiEngine.Score(1_800, 0, 1_000), "a zero consideration vetoes");
        }

        [Test]
        public void ThinksOnlyOnTheInterval_AndOnlyForAssignedPlayers()
        {
            Fleet(Me, _field, 10, ammo: 0);
            Assert.AreEqual(0, _sim.Ai.Think(0), "no AI-run players");
            _sim.Ai.Assign(_hq, Sentinel);
            Assert.AreEqual(0, _sim.Ai.Think(5));
            Assert.AreEqual(1, _sim.Ai.Think(10));
            _sim.Ai.Release(_hq);
            Assert.AreEqual(0, _sim.Ai.Think(20));
        }

        [Test]
        public void VentsCoolant_AtThePresetTemperature()
        {
            var fleet = Fleet(Me, _home, 10);
            _sim.FleetCombat.Get(fleet).TemperatureMilli = 72_000;
            Assert.AreEqual(0, Think(Sentinel), "72 C is below the Sentinel's 75 C");

            var order = OnlyOrder(Trader);
            Assert.AreEqual(AiAction.VentCoolant, order.Action);
            Assert.AreEqual(fleet, order.Subject);
            RunPendingOrders();
            Assert.Less(_sim.FleetCombat.Get(fleet).TemperatureMilli, 72_000, "venting cooled the hull");

            _sim.FleetCombat.Get(fleet).TemperatureMilli = 72_000;
            _sim.FleetCombat.Get(fleet).IsVenting = false;
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemCoolant, -_sim.Stockpiles.Get(fleet).Get(ItemId.ItemCoolant));
            Assert.AreEqual(0, Think(Trader), "no coolant, no vent");
        }

        [Test]
        public void Retreats_WhenOutgunned_BelowThePresetRatio()
        {
            var fleet = Fleet(Me, _field, 6);
            Fleet(Rival, _field, 10, ammo: 0); // unarmed, so my fleet survives until the order arrives
            Assert.AreEqual(0, Think(Warlord), "0.6 is above the Warlord's 0.35");

            var order = OnlyOrder(Sentinel);
            Assert.AreEqual(AiAction.Retreat, order.Action, "0.6 is below the Sentinel's 0.65");
            Assert.AreEqual(fleet, order.Subject);
            RunPendingOrders();
            Assert.IsTrue(_sim.FleetMotions.Get(fleet).Site != _field, "the fleet left the field");
        }

        [Test]
        public void Resupplies_AtTheCoreBase_WhenAmmoRunsLow()
        {
            var fleet = Fleet(Me, _field, 10, ammo: 10); // below 5 per line ship
            var order = OnlyOrder(Sentinel);
            Assert.AreEqual(AiAction.Resupply, order.Action);
            Assert.AreEqual(fleet, order.Subject);
            RunPendingOrders();
            Assert.AreEqual(_lane, _sim.FleetMotions.Get(fleet).Lane, "heading home");

            Fleet(Me, _home, 10, ammo: 0);
            Fleet(Me, _field, 10, ammo: 0, fuel: 0);
            _sim.Clock.Advance(10 - _sim.Clock.CurrentTick % 10);
            Assert.AreEqual(0, Think(Sentinel), "at home already; no fuel to leave");
        }

        [Test]
        public void PausesDrills_AtThePresetStormLevel_AndResumesAfter()
        {
            var deposit = _sim.World.Create();
            _sim.Deposits.Add(deposit, new MineralDeposit { Site = _field, RawItem = ItemId.RawWater, SurfaceReserve = 1_000, RigCount = 1 });
            _sim.Weather.Get(_field).GeomagneticStormLevel = 3;
            Assert.AreEqual(0, Think(Warlord), "G3 is below the Warlord's G5");

            var order = OnlyOrder(Sentinel);
            Assert.AreEqual(AiAction.PauseDrills, order.Action);
            Assert.AreEqual(deposit, order.Subject);
            RunPendingOrders();
            Assert.IsTrue(_sim.Deposits.Get(deposit).IsPaused, "the crews are out, the rigs stay");
            Assert.AreEqual(1, _sim.Deposits.Get(deposit).RigCount);

            _sim.Weather.Get(_field).GeomagneticStormLevel = 0;
            Assert.AreEqual(AiAction.PauseDrills, OnlyOrder(Sentinel).Action);
            RunPendingOrders();
            Assert.IsFalse(_sim.Deposits.Get(deposit).IsPaused);
        }

        [Test]
        public void RetractsDrills_WhenOutgunnedAtTheSite()
        {
            var deposit = _sim.World.Create();
            _sim.Deposits.Add(deposit, new MineralDeposit { Site = _field, RawItem = ItemId.RawWater, SurfaceReserve = 1_000, RigCount = 1 });
            Fleet(Rival, _field, lineShips: 5);

            var order = OnlyOrder(Sentinel);
            Assert.AreEqual(AiAction.RetractDrills, order.Action);
            RunPendingOrders();
            Assert.Greater(_sim.Deposits.Get(deposit).RetractUntilTick, 0, "the drills are coming out");
        }

        [Test]
        public void ScansTheBelt_AboveThePresetBattery()
        {
            int startup = _sim.Balance.Radar.StartupBatteries;
            ref var core = ref _sim.Stockpiles.Get(_home);
            core.Add(ItemId.ItemBattery, _sim.Balance.Ai.ScanBatteryReserve - 1); // kept back for the fleets
            Assert.AreEqual(0, Think(Sentinel), "nothing over the fleets' reserve");
            core.Add(ItemId.ItemBattery, 1);
            var testCost = _sim.Balance.Research.Tier(2).StressTestCost;
            for (int i = 0; i < testCost.Length; i++)
                if (testCost[i].Item == ItemId.ItemBattery)
                    core.Add(ItemId.ItemBattery, testCost[i].Quantity); // kept back for the next stress test
            for (int i = 0; i < _sim.Balance.SiteControl.ClaimCostCount; i++)
                if (_sim.Balance.SiteControl.ClaimCost(i).Item == ItemId.ItemBattery)
                    core.Add(ItemId.ItemBattery, _sim.Balance.SiteControl.ClaimCost(i).Quantity); // and the next claim
            core.Add(ItemId.ItemBattery, 2 * startup); // 20%
            Assert.AreEqual(0, Think(Sentinel), "20% is below the Sentinel's 30%");

            core.Add(ItemId.ItemBattery, 3 * startup); // 50%
            var order = OnlyOrder(Sentinel);
            Assert.AreEqual(AiAction.RadarScan, order.Action);
            Assert.AreEqual(_lane, order.Subject);
            RunPendingOrders();
            Assert.AreEqual(1, _sim.RadarScans.Count);
            Assert.AreEqual(0, _sim.Ai.Think(_sim.Clock.CurrentTick - _sim.Clock.CurrentTick % 10), "already scanning");
        }

        [Test]
        public void ScansStart_OnlyAtTheTopOfTheHour()
        {
            _sim.Stockpiles.Get(_home).Add(ItemId.ItemBattery, 5_000);
            _sim.Clock.Advance(_sim.Balance.Ai.ScanWindowTicks);
            Assert.AreEqual(0, CountOrders(Sentinel, AiAction.RadarScan), "past the window");

            _sim.Clock.Advance(SimTick.TicksPerHour - _sim.Balance.Ai.ScanWindowTicks);
            Assert.AreEqual(1, CountOrders(Sentinel, AiAction.RadarScan), "the next hour");
        }

        [Test]
        public void ScansKeepBack_WhatOpeningAnUnlockedGateNeeds()
        {
            _sim.TechProgress.Get(_hq).TechTier = 2; // the gate unlocks; the next stress test takes no batteries
            var gate = _sim.World.Create();
            _sim.JumpGates.Add(gate, new JumpGate { OwnerPlayerId = Me, HomeSite = _home, FarSite = _field });
            ref var core = ref _sim.Stockpiles.Get(_home);
            core.Add(ItemId.ItemBattery, _sim.Balance.Ai.ScanBatteryReserve);
            for (int i = 0; i < _sim.Balance.SiteControl.ClaimCostCount; i++)
                if (_sim.Balance.SiteControl.ClaimCost(i).Item == ItemId.ItemBattery)
                    core.Add(ItemId.ItemBattery, _sim.Balance.SiteControl.ClaimCost(i).Quantity);
            core.Add(ItemId.ItemBattery, 5 * _sim.Balance.Radar.StartupBatteries); // 50% over the claim
            Assert.AreEqual(0, CountOrders(Sentinel, AiAction.RadarScan), "the gate opening comes first");

            _sim.JumpGates.Get(gate).Lane = _lane; // opened
            Assert.AreEqual(1, CountOrders(Sentinel, AiAction.RadarScan));
        }

        private int CountOrders(byte archetype, AiAction action)
        {
            int n = Think(archetype), count = 0;
            for (int i = 0; i < n; i++)
                if (_sim.Ai.OrderAt(i).Action == action)
                    count++;
            return count;
        }

        [Test]
        public void SameWorld_SameOrders()
        {
            List<string> Run()
            {
                var sim = Build(out var home, out var field, out _, out var hq);
                sim.Stockpiles.Get(home).Add(ItemId.ItemBattery, 10 * sim.Balance.Radar.StartupBatteries);
                Fleet(sim, Me, field, 6, ammo: 20);
                Fleet(sim, Rival, field, 10);
                sim.Ai.Assign(hq, Sentinel);
                var orders = new List<string>();
                for (int t = 0; t < 300; t++)
                {
                    sim.Clock.Advance(1);
                    int count = sim.Ai.Think(sim.Clock.CurrentTick);
                    for (int i = 0; i < count; i++)
                    {
                        var o = sim.Ai.OrderAt(i);
                        orders.Add($"{sim.Clock.CurrentTick}:{o.Action}:{o.Subject.Index}:{o.ScoreMilli}:{o.Result}");
                    }
                }
                return orders;
            }

            var first = Run();
            Assert.Greater(first.Count, 0);
            CollectionAssert.AreEqual(first, Run());
        }
    }
}
