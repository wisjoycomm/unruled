using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Tests.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>Map: my core base H - highway - a rival site T with biomass and tungsten.</summary>
    public class NuclearStrikeSystemTests
    {
        private const int Me = 1;

        private SimulationWorld _sim;
        private EntityId _home, _target, _biomass, _tungsten;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(FundedBalance.Create(), worldSeed: 5);
            _home = Site(0, Me);
            _target = Site(200, 2);
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = _home, SiteB = _target, Tier = StarlaneTier.StaHighway, Length = 200 });
            _sim.LaneWeather.Add(lane, default);
            _sim.Graph.Rebuild(_sim.Starlanes);
            _sim.CreatePlayer(Me, _home);
            _biomass = Deposit(ItemId.RawBiomass);
            _tungsten = Deposit(ItemId.RawTungsten);
            _sim.Stockpiles.Get(_target).Add(ItemId.RawBiomass, 50);
        }

        private EntityId Site(int x, int owner)
        {
            var site = _sim.World.Create();
            _sim.Sites.Add(site, new SiteInfo { OwnerPlayerId = owner, PosX = x, CurrentEsiMilli = 800, TerrainSeed = 9 });
            _sim.Stockpiles.Add(site, default);
            _sim.Weather.Add(site, default);
            return site;
        }

        private EntityId Deposit(ItemId raw)
        {
            var deposit = _sim.World.Create();
            _sim.Deposits.Add(deposit, new MineralDeposit
            {
                Site = _target, RawItem = raw, SurfaceReserve = 1_000, DeepCrustReserve = 1_000, CoreFractureReserve = 1_000
            });
            return deposit;
        }

        private EntityId Fleet(byte tier, int cores)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = Me, TechTier = tier, LineShips = 4 }, _target);
            _sim.Stockpiles.Get(fleet).Add(ItemId.ItemNuclearCore, cores);
            return fleet;
        }

        private void Strike(EntityId fleet)
        {
            var command = new DelayedPlayerCommand { PlayerId = Me, CommandType = CommandType.NuclearStrike, Target = fleet };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(command, out long at));
            while (_sim.Clock.CurrentTick < at)
                _sim.Clock.Advance(1);
        }

        private bool Irradiated => _sim.Sites.Get(_target).LifecycleState == PlanetLifecycleState.IrradiatedDeadWorld;

        [Test]
        public void ATier5Fleet_FiresANuclearCore_AndTheSiteBecomesAnIrradiatedDeadWorld()
        {
            int cores = _sim.Balance.Lifecycle.StrikeNuclearCores;
            var fleet = Fleet(_sim.Balance.Lifecycle.StrikeMinFleetTier, cores + 1);

            Strike(fleet);

            Assert.IsTrue(Irradiated);
            Assert.IsTrue(_sim.Sites.Get(_target).IsIrradiated(_sim.Clock.CurrentTick));
            Assert.AreEqual(1, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemNuclearCore), "the cores leave the hold");
            Assert.AreEqual(0, _sim.Deposits.Get(_biomass).SurfaceReserve, "biomass wiped out");
            Assert.AreEqual(0, _sim.Stockpiles.Get(_target).Get(ItemId.RawBiomass));
            Assert.IsTrue(_sim.Deposits.Get(_tungsten).IsCoreFractureUnlocked, "the core fracture opens");
        }

        [Test]
        public void ALowerTierFleet_CannotFire()
        {
            var fleet = Fleet((byte)(_sim.Balance.Lifecycle.StrikeMinFleetTier - 1), 5);
            Strike(fleet);
            Assert.IsFalse(Irradiated);
            Assert.AreEqual(5, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemNuclearCore));
        }

        [Test]
        public void NoStrike_WithoutTheCores()
        {
            var fleet = Fleet(_sim.Balance.Lifecycle.StrikeMinFleetTier, _sim.Balance.Lifecycle.StrikeNuclearCores - 1);
            Strike(fleet);
            Assert.IsFalse(Irradiated);
        }

        [Test]
        public void NoStrike_OnAnOwnSite_OrOneOfTheSystems()
        {
            var fleet = Fleet(_sim.Balance.Lifecycle.StrikeMinFleetTier, 5);
            _sim.Sites.Get(_target).OwnerPlayerId = Me;
            Strike(fleet);
            Assert.IsFalse(Irradiated, "own site");

            _sim.Sites.Get(_target).OwnerPlayerId = -1;
            Strike(fleet);
            Assert.IsFalse(Irradiated, "THE SYSTEM's site");
            Assert.AreEqual(5, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemNuclearCore));
        }

        [Test]
        public void AnIrradiatedSite_IsNotStruckAgain()
        {
            var fleet = Fleet(_sim.Balance.Lifecycle.StrikeMinFleetTier, 5);
            Strike(fleet);
            int left = _sim.Stockpiles.Get(fleet).Get(ItemId.ItemNuclearCore);

            Strike(fleet);

            Assert.AreEqual(left, _sim.Stockpiles.Get(fleet).Get(ItemId.ItemNuclearCore));
        }
    }
}
