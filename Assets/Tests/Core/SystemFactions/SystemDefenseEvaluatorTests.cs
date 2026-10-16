using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.SystemFactions
{
    public class SystemDefenseEvaluatorTests
    {
        private const int Me = 1;

        private SimulationWorld _sim;
        private EntityId _citadel;

        private CitadelBalance Rules => _sim.Balance.Citadel;
        private ref SystemCitadel Citadel => ref _sim.Citadels.Get(_citadel);

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 9);
            var home = Site(Me);
            _sim.CreatePlayer(Me, home);
            _citadel = Site(0);
            _sim.Defense.CreateCitadel(_citadel);
        }

        private EntityId Site(int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner });
            _sim.Stockpiles.Add(site, default);
            return site;
        }

        private EntityId Fleet(byte tier, int lineShips, int ammo)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = tier, LineShips = lineShips }, _citadel);
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemAmmo, ammo);
            return fleet;
        }

        private void DestroyGarrison() => _sim.World.Destroy(Citadel.Garrison);

        private void NextCheck() => _sim.Clock.Advance(Rules.CheckIntervalTicks - _sim.Clock.CurrentTick % Rules.CheckIntervalTicks);

        [Test]
        public void CreateCitadel_HandsTheSiteToTheSystem_AtFullShield_WithAGarrison()
        {
            Assert.AreEqual(SystemDefenseEvaluator.OwnerId, _sim.Sites.Get(_citadel).OwnerPlayerId);
            Assert.AreEqual(Rules.MaxIntegrity, Citadel.Integrity);
            Assert.AreEqual(Rules.StartTier, Citadel.DefenceTier);
            ref var garrison = ref _sim.Fleets.Get(Citadel.Garrison);
            Assert.AreEqual(SystemDefenseEvaluator.OwnerId, garrison.OwnerPlayerId);
            Assert.AreEqual(Rules.GarrisonLineShips, garrison.LineShips);
            Assert.AreEqual(_citadel, _sim.FleetMotions.Get(Citadel.Garrison).Site);
        }

        [Test]
        public void WhileTheGarrisonHolds_NobodyBombards()
        {
            Fleet(tier: 5, lineShips: 10, ammo: 100);
            NextCheck();
            Assert.AreEqual(Rules.MaxIntegrity, Citadel.Integrity);
            Assert.AreEqual(0, _sim.CitadelAssaults.Count, "no assault recorded");
        }

        [Test]
        public void WithoutAGarrison_LineShipsBombardByTier_ForAmmo()
        {
            DestroyGarrison();
            var fleet = Fleet(tier: 5, lineShips: 10, ammo: 100);
            NextCheck();

            int damage = 10 * Rules.BombardPerLineShip(5);
            Assert.AreEqual(Rules.MaxIntegrity - damage, Citadel.Integrity);
            Assert.AreEqual(100 - 10 * Rules.AmmoPerLineShip, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemAmmo));
            Assert.AreEqual(damage, _sim.CitadelAssaults.At(0).Damage);
            Assert.AreEqual(_sim.Clock.CurrentTick, _sim.CitadelAssaults.At(0).LastHitTick);
        }

        [Test]
        public void WithoutAmmo_NoBombardment_AndTheShieldRegenerates()
        {
            DestroyGarrison();
            Citadel.Integrity = Rules.MaxIntegrity / 2;
            Fleet(tier: 5, lineShips: 10, ammo: 0);
            NextCheck();
            Assert.AreEqual(Rules.MaxIntegrity / 2 + Rules.RegenPerCheck, Citadel.Integrity);
        }

        [Test]
        public void ADestroyedGarrison_ReformsAfterTheRespawnTime_AtTheTierOfTheShield()
        {
            Citadel.Integrity = Rules.MaxIntegrity * 30 / 100;
            DestroyGarrison();
            NextCheck();
            Assert.IsTrue(Citadel.Garrison.IsNull, "re-forming");
            Assert.AreEqual(5, Citadel.DefenceTier, "below 40% the defence is Tier 5");

            _sim.Clock.Advance(Rules.GarrisonRespawnTicks);
            NextCheck();
            Assert.IsFalse(Citadel.Garrison.IsNull);
            Assert.AreEqual(5, _sim.Fleets.Get(Citadel.Garrison).TechTier);
        }

        [Test]
        public void AtZero_TheCitadelFalls_ForGood()
        {
            DestroyGarrison();
            Citadel.Integrity = 5;
            Fleet(tier: 1, lineShips: 10, ammo: 100);
            NextCheck();
            Assert.AreEqual(0, Citadel.Integrity);
            Assert.AreEqual(_sim.Clock.CurrentTick, Citadel.FallenTick);

            _sim.Clock.Advance(Rules.GarrisonRespawnTicks + Rules.CheckIntervalTicks);
            Assert.AreEqual(0, Citadel.Integrity, "no regeneration");
            Assert.IsTrue(Citadel.Garrison.IsNull, "no new garrison");
        }

        [Test]
        public void NewGame_PutsTheCitadelOnAContestedOrbitNoGateLandsOn()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 21);
            NewGameSetup.Create(sim, playerId: 1);
            Assert.AreEqual(1, sim.Citadels.Count);
            var site = sim.Citadels.EntityAt(0);
            int participants = 1 + sim.Balance.NewGame.RivalCount;
            Assert.AreEqual(NewGameSetup.ContestedClusterId(participants), sim.Sites.Get(site).ClusterId);
            for (int g = 0; g < sim.JumpGates.Count; g++)
                Assert.AreNotEqual(site, sim.JumpGates.At(g).FarSite);
        }
    }
}
