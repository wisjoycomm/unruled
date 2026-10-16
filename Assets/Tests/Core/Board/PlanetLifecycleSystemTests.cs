using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Board
{
    public class PlanetLifecycleSystemTests
    {
        private const int Owner = 7;
        private const long StrikeTick = 1000;

        private World _world;
        private ComponentStore<SiteInfo> _sites;
        private ComponentStore<MineralDeposit> _deposits;
        private ComponentStore<Stockpile> _stockpiles;
        private LifecycleBalance _balance;
        private PlanetLifecycleSystem _system;
        private EntityId _site;
        private EntityId _biomass;
        private EntityId _tungsten;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _sites = _world.CreateStore<SiteInfo>();
            _deposits = _world.CreateStore<MineralDeposit>();
            _stockpiles = _world.CreateStore<Stockpile>();
            _balance = DefaultGameBalance.CreateLifecycleBalance();
            _system = new PlanetLifecycleSystem(
                _sites, _deposits, _stockpiles, DefaultGameBalance.CreateMiningBalance(), _balance);

            _site = _world.Create();
            _sites.Add(_site, new SiteInfo
            {
                TerrainSeed = 0xABCDEFUL, BaseEsiMilli = 800, CurrentEsiMilli = 800, OwnerPlayerId = Owner,
                LifecycleState = PlanetLifecycleState.PristineWorld
            });
            _stockpiles.Add(_site, default);
            _biomass = AddDeposit(ItemId.RawBiomass, 0);
            _tungsten = AddDeposit(ItemId.RawTungsten, 500);
        }

        private ref SiteInfo Site => ref _sites.Get(_site);
        private ref Stockpile Stock => ref _stockpiles.Get(_site);

        private EntityId AddDeposit(ItemId raw, int core)
        {
            var deposit = _world.Create();
            _deposits.Add(deposit, new MineralDeposit
            {
                Site = _site, RawItem = raw, SurfaceReserve = 100, DeepCrustReserve = 100, CoreFractureReserve = core
            });
            return deposit;
        }

        private DelayedPlayerCommand Terraform(int player) =>
            new DelayedPlayerCommand { PlayerId = player, CommandType = CommandType.Terraform, Target = _site };

        private void PayTerraformCost()
        {
            Stock.Add(ItemId.RawWater, _balance.TerraformWater);
            Stock.Add(ItemId.RawNitrogen, _balance.TerraformNitrogen);
            Stock.Add(ItemId.RawBiomass, _balance.TerraformBiomass);
            Stock.Add(ItemId.ItemNuclearCore, _balance.TerraformNuclearCores);
        }

        [Test]
        public void NuclearStrike_KillsWorld_WipesBiomass_UnlocksCore_IrradiatesForSeededWindow()
        {
            Stock.Add(ItemId.RawBiomass, 40);

            _system.ApplyNuclearStrike(_site, StrikeTick);

            Assert.AreEqual(PlanetLifecycleState.IrradiatedDeadWorld, Site.LifecycleState);
            Assert.AreEqual(_balance.IrradiatedEsiMilli, Site.CurrentEsiMilli);
            Assert.AreEqual(0, Stock.Get(ItemId.RawBiomass));
            Assert.AreEqual(0, _deposits.Get(_biomass).SurfaceReserve + _deposits.Get(_biomass).DeepCrustReserve);
            Assert.IsTrue(_deposits.Get(_tungsten).IsCoreFractureUnlocked);
            Assert.AreEqual(100, _deposits.Get(_tungsten).SurfaceReserve, "only biomass is wiped");

            long duration = Site.IrradiatedUntilTick - StrikeTick;
            Assert.That(duration, Is.InRange(_balance.IrradiationMinTicks, _balance.IrradiationMaxTicks));
            Assert.IsTrue(Site.IsIrradiated(Site.IrradiatedUntilTick - 1));
            Assert.IsFalse(Site.IsIrradiated(Site.IrradiatedUntilTick));
        }

        [Test]
        public void NuclearStrike_DurationIsDeterministic()
        {
            _system.ApplyNuclearStrike(_site, StrikeTick);
            long first = Site.IrradiatedUntilTick;
            _system.ApplyNuclearStrike(_site, StrikeTick);
            Assert.AreEqual(first, Site.IrradiatedUntilTick);
        }

        [Test]
        public void DeadWorld_IsNotPushedDownTheDepletionPath()
        {
            _system.ApplyNuclearStrike(_site, StrikeTick);
            for (int i = 0; i < _deposits.Count; i++)
            {
                _deposits.At(i).SurfaceReserve = 0;
                _deposits.At(i).DeepCrustReserve = 0;
            }

            _system.OnTick(StrikeTick + 10 - StrikeTick % 10);

            Assert.AreEqual(PlanetLifecycleState.IrradiatedDeadWorld, Site.LifecycleState);
            Assert.AreEqual(_balance.IrradiatedEsiMilli, Site.CurrentEsiMilli);
        }

        [Test]
        public void Terraform_PaysCost_RaisesEsi_ClearsRadiation_KeepsDepletedReserves()
        {
            _system.ApplyNuclearStrike(_site, StrikeTick);
            PayTerraformCost();
            var command = Terraform(Owner);

            _system.Handle(in command, StrikeTick + 50);

            Assert.AreEqual(PlanetLifecycleState.TerraformedRecovery, Site.LifecycleState);
            Assert.AreEqual(_balance.TerraformedEsiMilli, Site.CurrentEsiMilli);
            Assert.IsFalse(Site.IsIrradiated(StrikeTick + 50));
            Assert.AreEqual(0, Stock.Get(ItemId.ItemNuclearCore));
            Assert.AreEqual(0, Stock.Get(ItemId.RawWater));
            Assert.AreEqual(0, _deposits.Get(_biomass).SurfaceReserve);
        }

        [Test]
        public void Terraform_RejectedForNonOwner_LivingWorld_MissingItems_OrStaleTarget()
        {
            PayTerraformCost();
            var command = Terraform(Owner);
            _system.Handle(in command, StrikeTick);
            Assert.AreEqual(PlanetLifecycleState.PristineWorld, Site.LifecycleState, "living world");

            _system.ApplyNuclearStrike(_site, StrikeTick);
            var stranger = Terraform(Owner + 1);
            _system.Handle(in stranger, StrikeTick + 1);
            Assert.AreEqual(PlanetLifecycleState.IrradiatedDeadWorld, Site.LifecycleState, "non-owner");

            var stale = Terraform(Owner);
            stale.Target = new EntityId(_site.Index, _site.Generation + 1);
            _system.Handle(in stale, StrikeTick + 1);
            Assert.AreEqual(PlanetLifecycleState.IrradiatedDeadWorld, Site.LifecycleState, "stale target");

            Stock.Add(ItemId.ItemNuclearCore, -_balance.TerraformNuclearCores);
            _system.Handle(in command, StrikeTick + 1);
            Assert.AreEqual(PlanetLifecycleState.IrradiatedDeadWorld, Site.LifecycleState, "missing core");
            Assert.AreEqual(_balance.TerraformWater, Stock.Get(ItemId.RawWater), "nothing charged on rejection");
        }

        [Test]
        public void TerraformCommand_IsRoutedThroughSimulationWorld()
        {
            var sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            var site = sim.World.Create();
            sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = Owner, LifecycleState = PlanetLifecycleState.PristineWorld });
            sim.Stockpiles.Add(site, default);
            sim.Lifecycle.ApplyNuclearStrike(site, 0);
            ref var stock = ref sim.Stockpiles.Get(site);
            stock.Add(ItemId.RawWater, _balance.TerraformWater);
            stock.Add(ItemId.RawNitrogen, _balance.TerraformNitrogen);
            stock.Add(ItemId.RawBiomass, _balance.TerraformBiomass);
            stock.Add(ItemId.ItemNuclearCore, _balance.TerraformNuclearCores);

            sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                ExecuteAtTick = 3, PlayerId = Owner, CommandType = CommandType.Terraform, Target = site
            });
            sim.Clock.Advance(3);

            Assert.AreEqual(PlanetLifecycleState.TerraformedRecovery, sim.Sites.Get(site).LifecycleState);
        }
    }
}
