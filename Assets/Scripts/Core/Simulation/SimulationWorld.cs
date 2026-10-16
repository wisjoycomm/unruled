using System;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Simulation
{
    /// <summary>
    /// Composition root of the simulation: creates the world, every component store and every system, and
    /// registers the systems on <see cref="Clock"/> in the one fixed tick order. Stores are exposed for reads and
    /// for world setup (generators, tests); during play, state changes only through queued commands and systems.
    /// </summary>
    public sealed class SimulationWorld
    {
        public readonly GameBalanceConfig Balance;
        public readonly ulong WorldSeed;
        public readonly World World;
        public readonly CommandRouter Commands;
        public readonly SimClock Clock;

        public readonly ComponentStore<SiteInfo> Sites;
        public readonly ComponentStore<StarlaneEdge> Starlanes;
        public readonly ComponentStore<LaneWeather> LaneWeather;
        public readonly ComponentStore<StormFront> StormFronts;
        public readonly ComponentStore<SiteWeather> Weather;
        public readonly ComponentStore<Stockpile> Stockpiles;
        public readonly ComponentStore<MineralDeposit> Deposits;
        public readonly ComponentStore<Refinery> Refineries;
        public readonly ComponentStore<SiteQuarantine> Quarantines;
        public readonly ComponentStore<PlayerHq> Players;
        public readonly ComponentStore<RelayStation> Relays;
        public readonly ComponentStore<FleetUnit> Fleets;
        public readonly ComponentStore<FleetMotion> FleetMotions;
        public readonly ComponentStore<FleetRoute> FleetRoutes;
        public readonly ComponentStore<FleetSensors> FleetSensors;
        public readonly ComponentStore<FleetReadiness> FleetReadiness;
        public readonly ComponentStore<FleetCombatState> FleetCombat;
        public readonly ComponentStore<CapitalRelocation> Relocations;
        public readonly ComponentStore<RadarScan> RadarScans;
        public readonly ComponentStore<Wallet> Wallets;
        public readonly ComponentStore<PlayerStanding> Standings;
        public readonly ComponentStore<WtoOrder> WtoOrders;
        public readonly ComponentStore<ItemMarket> ItemMarkets;
        public readonly ComponentStore<WtoContract> Contracts;
        public readonly ComponentStore<ContractCargo> ContractCargo;
        public readonly ComponentStore<StolenGoods> StolenGoods;
        public readonly ComponentStore<SystemSanction> Sanctions;
        public readonly ComponentStore<SystemBulletin> Bulletins;
        public readonly ComponentStore<AiController> AiControllers;
        public readonly ComponentStore<TechProgress> TechProgress;
        public readonly ComponentStore<ShipBuildOrder> ShipOrders;

        public readonly StarlaneGraph Graph = new StarlaneGraph();
        public readonly Ledger Ledger;
        public readonly EntityId WtoFeePool;
        public readonly EntityId StaTollPool;
        public readonly EntityId WtoEscrow;
        public readonly BracketRates Rates;
        public readonly WtoMarketEngine Market;
        public readonly WtoContractSystem ContractSystem;
        public readonly LeaderboardAuditSystem Audit;
        public readonly SystemAuthorityManager Authority;
        public readonly NoaaForecast Forecast;
        public readonly UtilityAiEngine Ai; // not on the clock: the host calls Think (Server 2)
        public readonly NoaaWeatherEngine WeatherEngine;
        public readonly PlanetLifecycleSystem Lifecycle;
        public readonly FleetFactory FleetFactory;
        public readonly RoutePlanner Routes;
        public readonly StaTollGate Tolls;
        public readonly FleetMovementSystem Movement;
        public readonly CapitalRelocationSystem Relocation;
        public readonly SignalLatencySolver Latency;
        public readonly CommandIssuer Issuer;
        public readonly ResearchSystem Research;
        public readonly TechGates TechGates;

        public SimulationWorld(GameBalanceConfig balance, ulong worldSeed, long startTick = 0)
        {
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            WorldSeed = worldSeed;
            World = new World();
            Commands = new CommandRouter();
            Clock = new SimClock(Commands, startTick);

            Sites = World.CreateStore<SiteInfo>();
            Starlanes = World.CreateStore<StarlaneEdge>();
            LaneWeather = World.CreateStore<LaneWeather>();
            StormFronts = World.CreateStore<StormFront>();
            Weather = World.CreateStore<SiteWeather>();
            Stockpiles = World.CreateStore<Stockpile>();
            Deposits = World.CreateStore<MineralDeposit>();
            Refineries = World.CreateStore<Refinery>();
            Quarantines = World.CreateStore<SiteQuarantine>();
            Players = World.CreateStore<PlayerHq>();
            Relays = World.CreateStore<RelayStation>();
            Fleets = World.CreateStore<FleetUnit>();
            FleetMotions = World.CreateStore<FleetMotion>();
            FleetRoutes = World.CreateStore<FleetRoute>();
            FleetSensors = World.CreateStore<FleetSensors>();
            FleetReadiness = World.CreateStore<FleetReadiness>();
            FleetCombat = World.CreateStore<FleetCombatState>();
            Relocations = World.CreateStore<CapitalRelocation>();
            RadarScans = World.CreateStore<RadarScan>();
            Wallets = World.CreateStore<Wallet>();
            Standings = World.CreateStore<PlayerStanding>();
            WtoOrders = World.CreateStore<WtoOrder>();
            ItemMarkets = World.CreateStore<ItemMarket>();
            Contracts = World.CreateStore<WtoContract>();
            ContractCargo = World.CreateStore<ContractCargo>();
            StolenGoods = World.CreateStore<StolenGoods>();
            Sanctions = World.CreateStore<SystemSanction>();
            Bulletins = World.CreateStore<SystemBulletin>();
            AiControllers = World.CreateStore<AiController>();
            TechProgress = World.CreateStore<TechProgress>();
            ShipOrders = World.CreateStore<ShipBuildOrder>();

            Ledger = new Ledger(Wallets);
            WtoFeePool = World.Create();
            Wallets.Add(WtoFeePool, new Wallet { Pool = TaxPoolKind.WtoMarketFees });
            StaTollPool = World.Create();
            Wallets.Add(StaTollPool, new Wallet { Pool = TaxPoolKind.StaTollGates });
            WtoEscrow = World.Create();
            Wallets.Add(WtoEscrow, new Wallet { Pool = TaxPoolKind.WtoEscrow });
            Rates = new BracketRates(Ledger, Standings, balance.Leaderboard);
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                ItemMarkets.Add(World.Create(), new ItemMarket
                {
                    Item = item, LastVwapCents = balance.Recipes.GetItem(item).BaseWtoPrice * Wallet.CentsPerCredit
                });
            }

            TechGates = new TechGates(TechProgress, Players, balance.Research);
            Latency = new SignalLatencySolver(Graph, Starlanes, Weather, LaneWeather, Players, Relays, Fleets, FleetMotions,
                balance.Latency, TechGates);
            Issuer = new CommandIssuer(Clock, Latency, Sites, Starlanes, FleetMotions, Deposits, Players, Relocations);
            FleetFactory = new FleetFactory(
                World, Sites, Fleets, FleetMotions, FleetRoutes, FleetSensors, FleetReadiness, FleetCombat, Stockpiles, balance.Combat);
            Routes = new RoutePlanner(Graph, Starlanes, balance.Starlanes, balance.Fleets);
            Authority = new SystemAuthorityManager(
                World, Sanctions, Bulletins, Sites, Players, Stockpiles, Ledger, FleetFactory, balance.Fleets, balance.Authority);
            ContractSystem = new WtoContractSystem(
                World, Contracts, ContractCargo, FleetMotions, Starlanes, Stockpiles, Ledger, WtoEscrow, WtoFeePool, Rates,
                FleetFactory, Routes, Clock, balance.Fleets, balance.Contracts);

            // Fixed tick order: weather first, then producers before consumers, watchers last.
            WeatherEngine = new NoaaWeatherEngine(
                World, StormFronts, Sites, Weather, Starlanes, LaneWeather, balance.Weather, balance.Starlanes,
                balance.Lifecycle, worldSeed);
            Clock.Register(WeatherEngine);
            Forecast = new NoaaForecast(WeatherEngine, Rates);
            var mining = new MiningSystem(Deposits, Sites, Stockpiles, balance.Mining, TechGates);
            Clock.Register(mining);
            Commands.Register(CommandType.RetractDrills, mining);
            Lifecycle = new PlanetLifecycleSystem(Sites, Deposits, Stockpiles, balance.Mining, balance.Lifecycle, TechGates);
            Clock.Register(Lifecycle);
            Commands.Register(CommandType.Terraform, Lifecycle);
            Clock.Register(new RefinerySystem(Refineries, Stockpiles, Weather, Sites, Ledger, balance.Recipes, balance.Refinery));
            Market = new WtoMarketEngine(
                World, WtoOrders, ItemMarkets, Sites, Stockpiles, StolenGoods, Ledger, WtoEscrow, Rates, ContractSystem,
                Authority, balance.Wto);
            Clock.Register(Market);
            Commands.Register(CommandType.PlaceBuyOrder, Market);
            Commands.Register(CommandType.PlaceSellOrder, Market);
            Commands.Register(CommandType.CancelOrder, Market);
            Clock.Register(new WhoQuarantineSystem(Quarantines, Stockpiles, balance.SystemFactions));

            var upkeep = new FleetUpkeepSystem(Fleets, FleetReadiness, FleetMotions, Stockpiles, balance.Fleets);
            Clock.Register(upkeep);
            Commands.Register(CommandType.Mothball, upkeep);
            Commands.Register(CommandType.Activate, upkeep);

            Tolls = new StaTollGate(Ledger, Standings, StaTollPool, balance.Starlanes, balance.Leaderboard, balance.Sta);
            Movement = new FleetMovementSystem(
                Fleets, FleetMotions, FleetRoutes, FleetReadiness, FleetCombat, Sites, Starlanes, Stockpiles, Routes,
                Tolls, balance.Starlanes, balance.Fleets);
            Clock.Register(Movement);
            Commands.Register(CommandType.Move, Movement);
            Commands.Register(CommandType.Hold, Movement);
            Commands.Register(CommandType.Reverse, Movement);

            var combat = new SalvoCombatEngine(
                World, Fleets, FleetMotions, FleetReadiness, FleetCombat, Sites, Starlanes, Stockpiles, Players, Relocations,
                ContractCargo, StolenGoods, Authority, balance.Combat, balance.Fleets, balance.Relocation);
            Clock.Register(combat);
            Commands.Register(CommandType.SetRof, combat);
            Commands.Register(CommandType.SetFocus, combat);
            Commands.Register(CommandType.VentCoolant, combat);

            Relocation = new CapitalRelocationSystem(
                World, Players, Relocations, Fleets, FleetMotions, Sites, Starlanes, Stockpiles, FleetFactory,
                balance.Relocation, worldSeed);
            Clock.Register(Relocation);
            Commands.Register(CommandType.RelocateBase, Relocation);

            var radar = new FocusedRadarSystem(
                World, RadarScans, Fleets, FleetMotions, FleetSensors, Starlanes, Players, Stockpiles, balance.Radar,
                balance.Fleets);
            Clock.Register(radar);
            Commands.Register(CommandType.ScanBelt, radar);
            Commands.Register(CommandType.CancelScan, radar);
            Commands.Register(CommandType.EcmJam, radar);
            Research = new ResearchSystem(
                TechProgress, Players, Fleets, FleetMotions, Stockpiles, Sites, Weather, LaneWeather, Authority,
                balance.Research, worldSeed);
            Clock.Register(Research);
            Commands.Register(CommandType.StressTest, Research);
            var shipyard = new ShipyardSystem(
                World, ShipOrders, Fleets, FleetMotions, FleetReadiness, FleetCombat, Players, Sites, Stockpiles,
                FleetFactory, TechGates, balance.Shipyard);
            Clock.Register(shipyard);
            Commands.Register(CommandType.BuildShips, shipyard);
            Commands.Register(CommandType.Retrofit, shipyard);
            Clock.Register(ContractSystem);
            Audit = new LeaderboardAuditSystem(
                Standings, Wallets, Sites, Fleets, Stockpiles, Ledger, WtoFeePool, StaTollPool, Market, balance.Leaderboard);
            Clock.Register(Audit);
            Commands.Register(CommandType.AuditOptIn, Audit);
            Clock.Register(Authority);

            Ai = new UtilityAiEngine(
                AiControllers, Players, Fleets, FleetMotions, FleetCombat, Stockpiles, Sites, Weather, Deposits, Starlanes,
                RadarScans, Issuer, balance.Ai, balance.Combat, balance.Radar);
        }

        /// <summary>
        /// World setup: creates a player's HQ with its core base, an unrated standing and a wallet holding the starting
        /// credits.
        /// </summary>
        public EntityId CreatePlayer(int playerId, EntityId coreBase)
        {
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (Ledger.TryFindPlayerWallet(playerId, out _))
                throw new InvalidOperationException($"Player {playerId} already exists.");
            var hq = World.Create();
            Players.Add(hq, new PlayerHq { PlayerId = playerId, CoreBase = coreBase });
            Wallets.Add(hq, new Wallet { OwnerPlayerId = playerId });
            Standings.Add(hq, new PlayerStanding { Bracket = LeaderboardBalance.Unrated });
            TechProgress.Add(hq, new TechProgress { TechTier = 1 });
            Ledger.Grant(hq, Balance.Ledger.StartingCents);
            return hq;
        }

        /// <summary>World setup: creates a fleet at a site with every fleet component and an empty cargo hold.</summary>
        public EntityId CreateFleet(in FleetUnit unit, EntityId site) => FleetFactory.Create(unit, site);

        /// <summary>
        /// World setup: generates one star system's sites and lanes from its seed, gives them weather components,
        /// then reindexes the graph.
        /// </summary>
        public void GenerateSystem(ulong systemSeed, int clusterId)
        {
            SystemSeedGenerator.GenerateSystem(World, Sites, Balance.Universe, systemSeed, clusterId);
            StarlaneGenerator.LinkSystem(World, Sites, Starlanes, Balance.Starlanes, systemSeed, clusterId);

            for (int i = 0; i < Sites.Count; i++)
                if (!Weather.Has(Sites.EntityAt(i)))
                    Weather.Add(Sites.EntityAt(i), default);
            for (int i = 0; i < Starlanes.Count; i++)
                if (!LaneWeather.Has(Starlanes.EntityAt(i)))
                    LaneWeather.Add(Starlanes.EntityAt(i), default);
            Graph.Rebuild(Starlanes);
        }
    }
}
