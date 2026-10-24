using System;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Persistence
{
    /// <summary>
    /// Saves and loads a whole <see cref="SimulationWorld"/> as MessagePack: [schema version, world seed, tick,
    /// allocator [generations, free indices], queued commands, stores [[entity, component]...] in a fixed order].
    /// Balance comes from config, not the save; the starlane graph is rebuilt. Keys and stores are append-only.
    /// </summary>
    public static class SaveGame
    {
        /// <summary>Bump when a saved shape changes, and add a migration test for the older version.</summary>
        public const int SchemaVersion = 7;

        private const int TopLevelKeys = 6;
        private const int StoreCount = 41;

        private delegate void Writer<T>(MsgPackWriter w, in T value);
        private delegate T Reader<T>(MsgPackReader r);

        public static byte[] Write(SimulationWorld sim) => Write(sim, SchemaVersion, StoreCount);

        /// <summary>Writes the first <paramref name="storeCount"/> stores under <paramref name="schemaVersion"/>: the
        /// shape an older build saved, for migration tests.</summary>
        internal static byte[] Write(SimulationWorld sim, int schemaVersion, int storeCount)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (storeCount < 0 || storeCount > StoreCount) throw new ArgumentOutOfRangeException(nameof(storeCount));
            var w = new MsgPackWriter(64 * 1024);
            w.WriteArrayHeader(TopLevelKeys);
            w.Write(schemaVersion);
            w.Write(sim.WorldSeed);
            w.Write(sim.Clock.CurrentTick);
            WriteAllocator(w, sim.World);

            w.WriteArrayHeader(sim.Clock.PendingCommandCount);
            for (int i = 0; i < sim.Clock.PendingCommandCount; i++)
                ComponentFormatters.Write(w, sim.Clock.PendingAt(i));

            w.WriteArrayHeader(storeCount);
            if (storeCount > 0) WriteStore(w, sim.Sites, ComponentFormatters.Write);
            if (storeCount > 1) WriteStore(w, sim.Starlanes, ComponentFormatters.Write);
            if (storeCount > 2) WriteStore(w, sim.LaneWeather, ComponentFormatters.Write);
            if (storeCount > 3) WriteStore(w, sim.StormFronts, ComponentFormatters.Write);
            if (storeCount > 4) WriteStore(w, sim.Weather, ComponentFormatters.Write);
            if (storeCount > 5) WriteStore(w, sim.Stockpiles, ComponentFormatters.Write);
            if (storeCount > 6) WriteStore(w, sim.Deposits, ComponentFormatters.Write);
            if (storeCount > 7) WriteStore(w, sim.Refineries, ComponentFormatters.Write);
            if (storeCount > 8) WriteStore(w, sim.Quarantines, ComponentFormatters.Write);
            if (storeCount > 9) WriteStore(w, sim.Players, ComponentFormatters.Write);
            if (storeCount > 10) WriteStore(w, sim.Relays, ComponentFormatters.Write);
            if (storeCount > 11) WriteStore(w, sim.Fleets, ComponentFormatters.Write);
            if (storeCount > 12) WriteStore(w, sim.FleetMotions, ComponentFormatters.Write);
            if (storeCount > 13) WriteStore(w, sim.FleetRoutes, ComponentFormatters.Write);
            if (storeCount > 14) WriteStore(w, sim.FleetSensors, ComponentFormatters.Write);
            if (storeCount > 15) WriteStore(w, sim.FleetReadiness, ComponentFormatters.Write);
            if (storeCount > 16) WriteStore(w, sim.FleetCombat, ComponentFormatters.Write);
            if (storeCount > 17) WriteStore(w, sim.Relocations, ComponentFormatters.Write);
            if (storeCount > 18) WriteStore(w, sim.RadarScans, ComponentFormatters.Write);
            if (storeCount > 19) WriteStore(w, sim.Wallets, ComponentFormatters.Write);
            if (storeCount > 20) WriteStore(w, sim.Standings, ComponentFormatters.Write);
            if (storeCount > 21) WriteStore(w, sim.WtoOrders, ComponentFormatters.Write);
            if (storeCount > 22) WriteStore(w, sim.ItemMarkets, ComponentFormatters.Write);
            if (storeCount > 23) WriteStore(w, sim.Contracts, ComponentFormatters.Write);
            if (storeCount > 24) WriteStore(w, sim.ContractCargo, ComponentFormatters.Write);
            if (storeCount > 25) WriteStore(w, sim.StolenGoods, ComponentFormatters.Write);
            if (storeCount > 26) WriteStore(w, sim.Sanctions, ComponentFormatters.Write);
            if (storeCount > 27) WriteStore(w, sim.Bulletins, ComponentFormatters.Write);
            if (storeCount > 28) WriteStore(w, sim.AiControllers, ComponentFormatters.Write);
            if (storeCount > 29) WriteStore(w, sim.TechProgress, ComponentFormatters.Write);
            if (storeCount > 30) WriteStore(w, sim.ShipOrders, ComponentFormatters.Write);
            if (storeCount > 31) WriteStore(w, sim.JumpGates, ComponentFormatters.Write);
            if (storeCount > 32) WriteStore(w, sim.ConvoyRoutes, ComponentFormatters.Write);
            if (storeCount > 33) WriteStore(w, sim.Citadels, ComponentFormatters.Write);
            if (storeCount > 34) WriteStore(w, sim.CitadelAssaults, ComponentFormatters.Write);
            if (storeCount > 35) WriteStore(w, sim.VictoryRecords, ComponentFormatters.Write);
            if (storeCount > 36) WriteStore(w, sim.DirectorateSeats, ComponentFormatters.Write);
            if (storeCount > 37) WriteStore(w, sim.Loans, ComponentFormatters.Write);
            if (storeCount > 38) WriteStore(w, sim.StarSystems, ComponentFormatters.Write);
            if (storeCount > 39) WriteStore(w, sim.SystemNames, ComponentFormatters.Write);
            if (storeCount > 40) WriteStore(w, sim.SystemCharts, ComponentFormatters.Write);
            return w.ToArray();
        }

        /// <summary>Builds a world from <paramref name="data"/>. Throws <see cref="FormatException"/> on bad data or
        /// a schema newer than <see cref="SchemaVersion"/>.</summary>
        public static SimulationWorld Load(GameBalanceConfig balance, ReadOnlyMemory<byte> data)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            var r = new MsgPackReader(data);
            int keys = r.ReadArrayHeader();
            if (keys < TopLevelKeys) throw new FormatException($"Save has {keys} top-level keys; expected {TopLevelKeys}.");
            int version = r.ReadInt32();
            if (version < 1 || version > SchemaVersion)
                throw new FormatException($"Save schema {version} is not supported (this build reads 1..{SchemaVersion}).");
            ulong seed = r.ReadUInt64();
            long tick = r.ReadInt64();

            var sim = new SimulationWorld(balance, seed, tick);
            ReadAllocator(r, sim.World);

            int commands = r.ReadArrayHeader();
            for (int i = 0; i < commands; i++)
                sim.Clock.Enqueue(ComponentFormatters.ReadCommand(r));

            int stores = r.ReadArrayHeader();
            if (stores > 0) ReadStore(r, sim.World, sim.Sites, ComponentFormatters.ReadSiteInfo);
            if (stores > 1) ReadStore(r, sim.World, sim.Starlanes, ComponentFormatters.ReadStarlaneEdge);
            if (stores > 2) ReadStore(r, sim.World, sim.LaneWeather, ComponentFormatters.ReadLaneWeather);
            if (stores > 3) ReadStore(r, sim.World, sim.StormFronts, ComponentFormatters.ReadStormFront);
            if (stores > 4) ReadStore(r, sim.World, sim.Weather, ComponentFormatters.ReadSiteWeather);
            if (stores > 5) ReadStore(r, sim.World, sim.Stockpiles, ComponentFormatters.ReadStockpile);
            if (stores > 6) ReadStore(r, sim.World, sim.Deposits, ComponentFormatters.ReadMineralDeposit);
            if (stores > 7) ReadStore(r, sim.World, sim.Refineries, ComponentFormatters.ReadRefinery);
            if (stores > 8) ReadStore(r, sim.World, sim.Quarantines, ComponentFormatters.ReadSiteQuarantine);
            if (stores > 9) ReadStore(r, sim.World, sim.Players, ComponentFormatters.ReadPlayerHq);
            if (stores > 10) ReadStore(r, sim.World, sim.Relays, ComponentFormatters.ReadRelayStation);
            if (stores > 11) ReadStore(r, sim.World, sim.Fleets, ComponentFormatters.ReadFleetUnit);
            if (stores > 12) ReadStore(r, sim.World, sim.FleetMotions, ComponentFormatters.ReadFleetMotion);
            if (stores > 13) ReadStore(r, sim.World, sim.FleetRoutes, ComponentFormatters.ReadFleetRoute);
            if (stores > 14) ReadStore(r, sim.World, sim.FleetSensors, ComponentFormatters.ReadFleetSensors);
            if (stores > 15) ReadStore(r, sim.World, sim.FleetReadiness, ComponentFormatters.ReadFleetReadiness);
            if (stores > 16) ReadStore(r, sim.World, sim.FleetCombat, ComponentFormatters.ReadFleetCombatState);
            if (stores > 17) ReadStore(r, sim.World, sim.Relocations, ComponentFormatters.ReadCapitalRelocation);
            if (stores > 18) ReadStore(r, sim.World, sim.RadarScans, ComponentFormatters.ReadRadarScan);
            if (stores > 19) ReadStore(r, sim.World, sim.Wallets, ComponentFormatters.ReadWallet);
            if (stores > 20) ReadStore(r, sim.World, sim.Standings, ComponentFormatters.ReadPlayerStanding);
            if (stores > 21) ReadStore(r, sim.World, sim.WtoOrders, ComponentFormatters.ReadWtoOrder);
            if (stores > 22) ReadStore(r, sim.World, sim.ItemMarkets, ComponentFormatters.ReadItemMarket);
            if (stores > 23) ReadStore(r, sim.World, sim.Contracts, ComponentFormatters.ReadWtoContract);
            if (stores > 24) ReadStore(r, sim.World, sim.ContractCargo, ComponentFormatters.ReadContractCargo);
            if (stores > 25) ReadStore(r, sim.World, sim.StolenGoods, ComponentFormatters.ReadStolenGoods);
            if (stores > 26) ReadStore(r, sim.World, sim.Sanctions, ComponentFormatters.ReadSystemSanction);
            if (stores > 27) ReadStore(r, sim.World, sim.Bulletins, ComponentFormatters.ReadSystemBulletin);
            if (stores > 28) ReadStore(r, sim.World, sim.AiControllers, ComponentFormatters.ReadAiController);
            if (stores > 29) ReadStore(r, sim.World, sim.TechProgress, ComponentFormatters.ReadTechProgress);
            if (stores > 30) ReadStore(r, sim.World, sim.ShipOrders, ComponentFormatters.ReadShipBuildOrder);
            if (stores > 31) ReadStore(r, sim.World, sim.JumpGates, ComponentFormatters.ReadJumpGate);
            if (stores > 32) ReadStore(r, sim.World, sim.ConvoyRoutes, ComponentFormatters.ReadConvoyRoute);
            if (stores > 33) ReadStore(r, sim.World, sim.Citadels, ComponentFormatters.ReadSystemCitadel);
            if (stores > 34) ReadStore(r, sim.World, sim.CitadelAssaults, ComponentFormatters.ReadCitadelAssault);
            if (stores > 35) ReadStore(r, sim.World, sim.VictoryRecords, ComponentFormatters.ReadVictoryRecord);
            if (stores > 36) ReadStore(r, sim.World, sim.DirectorateSeats, ComponentFormatters.ReadDirectorateSeat);
            if (stores > 37) ReadStore(r, sim.World, sim.Loans, ComponentFormatters.ReadLoan);
            else
                AddMissingLoans(sim); // schema 1: no loans yet, so every player owes nothing
            if (stores > 38) ReadStore(r, sim.World, sim.StarSystems, ComponentFormatters.ReadStarSystem);
            if (stores > 39) ReadStore(r, sim.World, sim.SystemNames, ComponentFormatters.ReadSystemName);
            if (stores > 40) ReadStore(r, sim.World, sim.SystemCharts, ComponentFormatters.ReadSystemChart);
            if (stores > StoreCount) r.Skip(stores - StoreCount);
            r.Skip(keys - TopLevelKeys);
            if (!r.End) throw new FormatException("Save has bytes after its end.");

            if (!sim.Stockpiles.Has(sim.WtoFeePool))
                sim.Stockpiles.Add(sim.WtoFeePool, default); // schema 3 and older: THE SYSTEM held no stock yet
            sim.Graph.Rebuild(sim.Starlanes);
            sim.Galaxy.EnsureSystemsForAllClusters(tick); // schema 5 and older: systems from before the catalog
            if (stores <= 40)
                ChartWhatPlayersHold(sim, tick); // schema 6 and older: no charts yet
            return sim;
        }

        // Before charts every system was known to all; a migrated player keeps the systems it holds a site in, has a
        // fleet in, or reaches through its jump gate, plus every hub (a Citadel's system).
        private static void ChartWhatPlayersHold(SimulationWorld sim, long tick)
        {
            var galaxy = sim.Galaxy;
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                ref var site = ref sim.Sites.At(i);
                if (site.OwnerPlayerId > 0)
                    galaxy.Learn(site.OwnerPlayerId, site.ClusterId, Unruled.Core.Galaxy.ChartSource.Migration, tick);
            }
            for (int i = 0; i < sim.Fleets.Count; i++)
                LearnSite(sim, sim.Fleets.At(i).OwnerPlayerId, Docked(sim, sim.Fleets.EntityAt(i)), tick);
            for (int i = 0; i < sim.JumpGates.Count; i++)
            {
                ref var gate = ref sim.JumpGates.At(i);
                LearnSite(sim, gate.OwnerPlayerId, gate.HomeSite, tick);
                LearnSite(sim, gate.OwnerPlayerId, gate.FarSite, tick);
            }
            for (int c = 0; c < sim.Citadels.Count; c++)
                for (int p = 0; p < sim.Players.Count; p++)
                    LearnSite(sim, sim.Players.At(p).PlayerId, sim.Citadels.EntityAt(c), tick);
        }

        private static void LearnSite(SimulationWorld sim, int playerId, EntityId site, long tick)
        {
            if (sim.Sites.Has(site))
                sim.Galaxy.Learn(playerId, sim.Sites.Get(site).ClusterId, Unruled.Core.Galaxy.ChartSource.Migration, tick);
        }

        // Where a fleet is: its site, or the site it left when on a lane.
        private static EntityId Docked(SimulationWorld sim, EntityId fleet)
        {
            if (!sim.FleetMotions.Has(fleet))
                return default;
            ref var motion = ref sim.FleetMotions.Get(fleet);
            return motion.Site.IsNull ? motion.FromSite : motion.Site;
        }

        private static void AddMissingLoans(SimulationWorld sim)
        {
            for (int i = 0; i < sim.Players.Count; i++)
                if (!sim.Loans.Has(sim.Players.EntityAt(i)))
                    sim.Loans.Add(sim.Players.EntityAt(i), default);
        }

        private static void WriteAllocator(MsgPackWriter w, World world)
        {
            w.WriteArrayHeader(2);
            w.WriteArrayHeader(world.IssuedCount);
            for (int i = 0; i < world.IssuedCount; i++)
                w.Write(world.GenerationAt(i));
            w.WriteArrayHeader(world.FreeCount);
            for (int i = 0; i < world.FreeCount; i++)
                w.Write(world.FreeIndexAt(i));
        }

        private static void ReadAllocator(MsgPackReader r, World world)
        {
            int keys = r.ReadArrayHeader();
            if (keys < 2) throw new FormatException("Allocator needs generations and free indices.");
            var generations = new int[r.ReadArrayHeader()];
            for (int i = 0; i < generations.Length; i++)
                generations[i] = r.ReadInt32();
            var free = new int[r.ReadArrayHeader()];
            for (int i = 0; i < free.Length; i++)
                free[i] = r.ReadInt32();
            if (keys > 2) r.Skip(keys - 2);
            world.Restore(generations, free);
        }

        private static void WriteStore<T>(MsgPackWriter w, ComponentStore<T> store, Writer<T> write) where T : struct
        {
            w.WriteArrayHeader(store.Count);
            for (int i = 0; i < store.Count; i++)
            {
                w.WriteArrayHeader(2);
                ComponentFormatters.WriteEntity(w, store.EntityAt(i));
                write(w, in store.At(i));
            }
        }

        private static void ReadStore<T>(MsgPackReader r, World world, ComponentStore<T> store, Reader<T> read)
            where T : struct
        {
            int count = r.ReadArrayHeader();
            for (int i = 0; i < count; i++)
            {
                int keys = r.ReadArrayHeader();
                if (keys < 2) throw new FormatException("Store entry needs an entity and a component.");
                var entity = ComponentFormatters.ReadEntity(r);
                if (!world.IsAlive(entity)) throw new FormatException($"Store entry for dead entity {entity}.");
                store.Add(entity, read(r));
                if (keys > 2) r.Skip(keys - 2);
            }
        }
    }
}
