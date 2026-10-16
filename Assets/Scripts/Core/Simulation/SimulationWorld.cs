using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
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

        public readonly StarlaneGraph Graph = new StarlaneGraph();
        public readonly PlanetLifecycleSystem Lifecycle;

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

            // Fixed tick order: weather first, then producers before consumers, watchers last.
            Clock.Register(new NoaaWeatherEngine(
                World, StormFronts, Sites, Weather, Starlanes, LaneWeather, balance.Weather, balance.Starlanes,
                balance.Lifecycle, worldSeed));
            Clock.Register(new MiningSystem(Deposits, Stockpiles, balance.Mining));
            Lifecycle = new PlanetLifecycleSystem(Sites, Deposits, Stockpiles, balance.Mining, balance.Lifecycle);
            Clock.Register(Lifecycle);
            Commands.Register(CommandType.Terraform, Lifecycle);
            Clock.Register(new RefinerySystem(Refineries, Stockpiles, Weather, balance.Recipes, balance.Refinery));
            Clock.Register(new WhoQuarantineSystem(Quarantines, Stockpiles, balance.SystemFactions));
        }

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
