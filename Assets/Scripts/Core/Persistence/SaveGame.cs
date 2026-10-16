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
        public const int SchemaVersion = 1;

        private const int TopLevelKeys = 6;
        private const int StoreCount = 37;

        private delegate void Writer<T>(MsgPackWriter w, in T value);
        private delegate T Reader<T>(MsgPackReader r);

        public static byte[] Write(SimulationWorld sim)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            var w = new MsgPackWriter(64 * 1024);
            w.WriteArrayHeader(TopLevelKeys);
            w.Write(SchemaVersion);
            w.Write(sim.WorldSeed);
            w.Write(sim.Clock.CurrentTick);
            WriteAllocator(w, sim.World);

            w.WriteArrayHeader(sim.Clock.PendingCommandCount);
            for (int i = 0; i < sim.Clock.PendingCommandCount; i++)
                ComponentFormatters.Write(w, sim.Clock.PendingAt(i));

            w.WriteArrayHeader(StoreCount);
            WriteStore(w, sim.Sites, ComponentFormatters.Write);
            WriteStore(w, sim.Starlanes, ComponentFormatters.Write);
            WriteStore(w, sim.LaneWeather, ComponentFormatters.Write);
            WriteStore(w, sim.StormFronts, ComponentFormatters.Write);
            WriteStore(w, sim.Weather, ComponentFormatters.Write);
            WriteStore(w, sim.Stockpiles, ComponentFormatters.Write);
            WriteStore(w, sim.Deposits, ComponentFormatters.Write);
            WriteStore(w, sim.Refineries, ComponentFormatters.Write);
            WriteStore(w, sim.Quarantines, ComponentFormatters.Write);
            WriteStore(w, sim.Players, ComponentFormatters.Write);
            WriteStore(w, sim.Relays, ComponentFormatters.Write);
            WriteStore(w, sim.Fleets, ComponentFormatters.Write);
            WriteStore(w, sim.FleetMotions, ComponentFormatters.Write);
            WriteStore(w, sim.FleetRoutes, ComponentFormatters.Write);
            WriteStore(w, sim.FleetSensors, ComponentFormatters.Write);
            WriteStore(w, sim.FleetReadiness, ComponentFormatters.Write);
            WriteStore(w, sim.FleetCombat, ComponentFormatters.Write);
            WriteStore(w, sim.Relocations, ComponentFormatters.Write);
            WriteStore(w, sim.RadarScans, ComponentFormatters.Write);
            WriteStore(w, sim.Wallets, ComponentFormatters.Write);
            WriteStore(w, sim.Standings, ComponentFormatters.Write);
            WriteStore(w, sim.WtoOrders, ComponentFormatters.Write);
            WriteStore(w, sim.ItemMarkets, ComponentFormatters.Write);
            WriteStore(w, sim.Contracts, ComponentFormatters.Write);
            WriteStore(w, sim.ContractCargo, ComponentFormatters.Write);
            WriteStore(w, sim.StolenGoods, ComponentFormatters.Write);
            WriteStore(w, sim.Sanctions, ComponentFormatters.Write);
            WriteStore(w, sim.Bulletins, ComponentFormatters.Write);
            WriteStore(w, sim.AiControllers, ComponentFormatters.Write);
            WriteStore(w, sim.TechProgress, ComponentFormatters.Write);
            WriteStore(w, sim.ShipOrders, ComponentFormatters.Write);
            WriteStore(w, sim.JumpGates, ComponentFormatters.Write);
            WriteStore(w, sim.ConvoyRoutes, ComponentFormatters.Write);
            WriteStore(w, sim.Citadels, ComponentFormatters.Write);
            WriteStore(w, sim.CitadelAssaults, ComponentFormatters.Write);
            WriteStore(w, sim.VictoryRecords, ComponentFormatters.Write);
            WriteStore(w, sim.DirectorateSeats, ComponentFormatters.Write);
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
            if (stores > StoreCount) r.Skip(stores - StoreCount);
            r.Skip(keys - TopLevelKeys);
            if (!r.End) throw new FormatException("Save has bytes after its end.");

            sim.Graph.Rebuild(sim.Starlanes);
            return sim;
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
