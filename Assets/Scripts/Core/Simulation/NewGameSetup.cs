using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;

namespace Unruled.Core.Simulation
{
    /// <summary>The entities a new game hands to the local player.</summary>
    public readonly struct NewGame
    {
        public readonly EntityId PlayerHq;
        public readonly EntityId CoreBase;
        public readonly EntityId Fleet;

        public NewGame(EntityId playerHq, EntityId coreBase, EntityId fleet)
        {
            PlayerHq = playerHq;
            CoreBase = coreBase;
            Fleet = fleet;
        }
    }

    /// <summary>
    /// World setup for a new game (GDD §1, §2.1 A). Every participant gets a home system (cluster = participant
    /// index) from a seed drawn from the world seed. Each site gets a stockpile and deposits of the raw items whose
    /// preferred ESI window holds its ESI; home systems hold no tungsten or uranium but every other raw item (one missing
    /// after the roll goes to the non-core site nearest its ESI window). The core base is the home's
    /// highest-ESI site: it is owned, rigged, given refineries and stock, and a fleet starts there. Rivals are AI-run.
    /// Then the neutral contested cluster (cluster = participant count) is generated with every raw item (filled in the
    /// same way), each home gets a dormant jump gate from its outermost orbit to its own contested orbit (GDD §1.2), and
    /// uninhabited star systems (clusters = participant count + 1 ..) are generated with every raw item and no native life,
    /// linked from the contested entry orbits and in an outer ring. THE SYSTEM holds the contested cluster's outermost
    /// orbit, which no gate lands on, as its Citadel.
    /// Runs once, before the clock starts.
    /// </summary>
    public static class NewGameSetup
    {
        public static NewGame Create(SimulationWorld sim, int playerId)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (sim.Sites.Count != 0) throw new InvalidOperationException("The world already has sites.");
            var rules = sim.Balance.NewGame;
            if (playerId <= 0 || playerId > int.MaxValue - rules.RivalCount)
                throw new ArgumentOutOfRangeException(nameof(playerId));

            var rng = new SplitMix64(sim.WorldSeed);
            var player = SetUpParticipant(sim, ref rng, playerId, 0);
            for (int i = 1; i <= rules.RivalCount; i++)
            {
                var rival = SetUpParticipant(sim, ref rng, playerId + i, i);
                byte archetype = (byte)((i - 1) % sim.Balance.Ai.Count + 1);
                sim.Ai.Assign(rival.PlayerHq, archetype);
            }
            int participants = 1 + rules.RivalCount;
            SetUpContestedCluster(sim, ref rng, playerId, participants);
            SetUpUninhabitedClusters(sim, ref rng, participants);
            sim.Graph.Rebuild(sim.Starlanes);
            return player;
        }

        /// <summary>The contested cluster's id in a game with this many participants.</summary>
        public static int ContestedClusterId(int participants) => participants;

        /// <summary>The first uninhabited cluster's id in a game with this many participants.</summary>
        public static int FirstUninhabitedClusterId(int participants) => ContestedClusterId(participants) + 1;

        private static void SetUpContestedCluster(SimulationWorld sim, ref SplitMix64 rng, int firstPlayerId, int participants)
        {
            ulong systemSeed = rng.NextULong();
            int firstSite = sim.Sites.Count;
            int firstDeposit = sim.Deposits.Count;
            int contested = ContestedClusterId(participants);
            sim.GenerateSystem(systemSeed, contested, rebuildGraph: false);
            var siteRng = new SplitMix64(systemSeed);
            for (int i = firstSite; i < sim.Sites.Count; i++)
            {
                var site = sim.Sites.EntityAt(i);
                if (!sim.Stockpiles.Has(site))
                    sim.Stockpiles.Add(site, default);
                AddDeposits(sim, ref siteRng, site, sim.Sites.At(i).BaseEsiMilli, home: false);
            }
            var citadel = OuterOrbit(sim, contested, firstSite);
            AddMissingRaws(sim, firstSite, firstDeposit, contested, default, citadel, home: false);

            int orbits = sim.Sites.Count - firstSite;
            int perSys = sim.Balance.Universe.SitesPerSystem;
            for (int p = 0; p < participants; p++)
            {
                var home = OuterOrbit(sim, p, p * perSys);
                var far = SiteAtOrbit(sim, contested, participants < orbits ? (int)((long)p * orbits / participants) : p % Math.Max(1, orbits - 1), firstSite);
                sim.JumpGates.Add(sim.World.Create(), new JumpGate { OwnerPlayerId = firstPlayerId + p, HomeSite = home, FarSite = far });
            }
            for (int g = 0; g < sim.JumpGates.Count; g++)
                if (sim.JumpGates.At(g).FarSite == citadel)
                    throw new InvalidOperationException($"{participants} participants leave no free orbit for the Citadel.");
            sim.Defense.CreateCitadel(citadel);
        }

        private static void SetUpUninhabitedClusters(SimulationWorld sim, ref SplitMix64 rng, int participants)
        {
            int perParticipant = sim.Balance.NewGame.UninhabitedClustersPerParticipant;
            if (perParticipant <= 0)
                return;
            int firstCluster = FirstUninhabitedClusterId(participants);
            int count = participants * perParticipant;
            var clusterFirstSite = new int[count];
            for (int u = 0; u < count; u++)
            {
                ulong systemSeed = rng.NextULong();
                int firstSite = sim.Sites.Count;
                int firstDeposit = sim.Deposits.Count;
                clusterFirstSite[u] = firstSite;
                int clusterId = firstCluster + u;
                sim.GenerateSystem(systemSeed, clusterId, rebuildGraph: false);
                var siteRng = new SplitMix64(systemSeed);
                for (int i = firstSite; i < sim.Sites.Count; i++)
                {
                    var site = sim.Sites.EntityAt(i);
                    if (!sim.Stockpiles.Has(site))
                        sim.Stockpiles.Add(site, default);
                    AddDeposits(sim, ref siteRng, site, sim.Sites.At(i).BaseEsiMilli, home: false);
                }
                AddMissingRaws(sim, firstSite, firstDeposit, clusterId, default, default, home: false);
            }

            int length = sim.Balance.JumpGates.LaneLength;
            int firstInterLane = sim.Starlanes.Count;
            for (int p = 0; p < participants; p++)
            {
                var far = sim.JumpGates.At(p).FarSite;
                int baseOffset = p * perParticipant;
                int baseCluster = firstCluster + baseOffset;
                AddInterSystemLane(sim, far, SiteAtOrbit(sim, baseCluster, 0, clusterFirstSite[baseOffset]), StarlaneTier.StaHighway, length, firstInterLane);
                for (int k = 1; k < perParticipant; k++)
                    AddInterSystemLane(sim,
                        OuterOrbit(sim, baseCluster + k - 1, clusterFirstSite[baseOffset + k - 1]),
                        SiteAtOrbit(sim, baseCluster + k, 0, clusterFirstSite[baseOffset + k]),
                        StarlaneTier.StaHighway, length, firstInterLane);
            }
            if (count > 1)
                for (int u = 0; u < count; u++)
                    AddInterSystemLane(sim,
                        OuterOrbit(sim, firstCluster + u, clusterFirstSite[u]),
                        OuterOrbit(sim, firstCluster + (u + 1) % count, clusterFirstSite[(u + 1) % count]),
                        StarlaneTier.OuterRimBypass, length, firstInterLane);
        }

        private static void AddInterSystemLane(SimulationWorld sim, EntityId siteA, EntityId siteB, StarlaneTier tier, int length, int searchFrom)
        {
            if (siteA == siteB)
                return;
            for (int i = searchFrom; i < sim.Starlanes.Count; i++)
            {
                ref var existing = ref sim.Starlanes.At(i);
                if ((existing.SiteA == siteA && existing.SiteB == siteB) ||
                    (existing.SiteA == siteB && existing.SiteB == siteA))
                    return;
            }
            var lane = sim.World.Create();
            sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = siteA, SiteB = siteB, Tier = tier, Length = length });
            sim.LaneWeather.Add(lane, default);
        }

        private static EntityId OuterOrbit(SimulationWorld sim, int clusterId, int startIndex)
        {
            EntityId best = default;
            int bestOrbit = -1;
            for (int i = Math.Max(0, startIndex); i < sim.Sites.Count; i++)
            {
                ref var site = ref sim.Sites.At(i);
                if (site.ClusterId != clusterId)
                {
                    if (!best.IsNull)
                        break;
                    continue;
                }
                if (site.OrbitIndex > bestOrbit)
                {
                    bestOrbit = site.OrbitIndex;
                    best = sim.Sites.EntityAt(i);
                }
            }
            return best;
        }

        private static EntityId SiteAtOrbit(SimulationWorld sim, int clusterId, int orbit, int startIndex)
        {
            for (int i = Math.Max(0, startIndex); i < sim.Sites.Count; i++)
                if (sim.Sites.At(i).ClusterId == clusterId && sim.Sites.At(i).OrbitIndex == orbit)
                    return sim.Sites.EntityAt(i);
            throw new InvalidOperationException($"Cluster {clusterId} has no orbit {orbit}.");
        }

        private static NewGame SetUpParticipant(SimulationWorld sim, ref SplitMix64 rng, int playerId, int clusterId)
        {
            var rules = sim.Balance.NewGame;
            ulong systemSeed = rng.NextULong();
            int firstSite = sim.Sites.Count;
            int firstDeposit = sim.Deposits.Count;
            sim.GenerateSystem(systemSeed, clusterId, rebuildGraph: false);

            var siteRng = new SplitMix64(systemSeed);
            EntityId coreBase = default;
            int bestEsi = -1;
            for (int i = firstSite; i < sim.Sites.Count; i++)
            {
                var site = sim.Sites.EntityAt(i);
                if (!sim.Stockpiles.Has(site))
                    sim.Stockpiles.Add(site, default);
                AddDeposits(sim, ref siteRng, site, sim.Sites.At(i).BaseEsiMilli, home: true);
                if (sim.Sites.At(i).BaseEsiMilli > bestEsi)
                {
                    bestEsi = sim.Sites.At(i).BaseEsiMilli;
                    coreBase = site;
                }
            }
            if (bestEsi < 0) throw new InvalidOperationException($"System {clusterId} generated no sites.");
            AddMissingRaws(sim, firstSite, firstDeposit, clusterId, coreBase, default, home: true);

            sim.Sites.Get(coreBase).OwnerPlayerId = playerId;
            var hq = sim.CreatePlayer(playerId, coreBase);
            for (int d = 0; d < rules.CoreBaseDepositCount; d++)
            {
                var spec = rules.CoreBaseDeposit(d);
                bool found = false;
                for (int i = firstDeposit; i < sim.Deposits.Count && !found; i++)
                {
                    ref var deposit = ref sim.Deposits.At(i);
                    if (deposit.Site != coreBase || deposit.RawItem != spec.Item)
                        continue;
                    deposit.RigCount = spec.Quantity;
                    deposit.SurfaceReserve = rules.SurfaceReserve * rules.CoreBaseReserveMultiplier;
                    deposit.DeepCrustReserve = rules.DeepCrustReserve * rules.CoreBaseReserveMultiplier;
                    deposit.CoreFractureReserve = rules.CoreFractureReserve * rules.CoreBaseReserveMultiplier;
                    found = true;
                }
                if (!found)
                    sim.Deposits.Add(sim.World.Create(), new MineralDeposit
                    {
                        Site = coreBase,
                        RawItem = spec.Item,
                        SurfaceReserve = rules.SurfaceReserve * rules.CoreBaseReserveMultiplier,
                        DeepCrustReserve = rules.DeepCrustReserve * rules.CoreBaseReserveMultiplier,
                        CoreFractureReserve = rules.CoreFractureReserve * rules.CoreBaseReserveMultiplier,
                        RigCount = spec.Quantity
                    });
            }
            for (int i = 0; i < rules.StartingRefineryCount; i++)
                sim.Refineries.Add(sim.World.Create(), new Refinery
                {
                    Site = coreBase, Output = rules.StartingRefinery(i), StockCap = sim.Balance.Refinery.DefaultStockCap
                });
            ref var stock = ref sim.Stockpiles.Get(coreBase);
            for (int i = 0; i < rules.CoreBaseStockCount; i++)
                stock.Add(rules.CoreBaseStock(i).Item, rules.CoreBaseStock(i).Quantity);

            var fleet = sim.CreateFleet(new FleetUnit
            {
                OwnerPlayerId = playerId,
                TechTier = rules.FleetTechTier,
                LineShips = rules.FleetLineShips,
                EwShips = rules.FleetEwShips,
                SupplyTenders = rules.FleetSupplyTenders,
                CargoConvoys = rules.FleetCargoConvoys
            }, coreBase);
            ref var hold = ref sim.Stockpiles.Get(fleet);
            for (int i = 0; i < rules.FleetStockCount; i++)
                hold.Add(rules.FleetStock(i).Item, rules.FleetStock(i).Quantity);
            return new NewGame(hq, coreBase, fleet);
        }

        // GDD §1.2: a home system lacks only the raw items excluded at home; the contested cluster lacks none outside the
        // Citadel (GDD §0: uranium is mined before THE SYSTEM falls). Each raw item with an ESI window that the roll left
        // out gets a deposit on the site nearest that window (lowest orbit on a tie), never the core base unless it is
        // the only site, and never the Citadel.
        private static void AddMissingRaws(SimulationWorld sim, int firstSite, int firstDeposit, int clusterId,
            EntityId coreBase, EntityId citadel, bool home)
        {
            var rules = sim.Balance.NewGame;
            for (int slot = 0; slot < ItemIds.RawCount; slot++)
            {
                var raw = ItemIds.FromSlot(slot);
                ref readonly var item = ref sim.Balance.Recipes.GetItem(raw);
                if ((home && rules.IsExcludedAtHome(raw)) || !item.HasPreferredEsi || HasDeposit(sim, firstDeposit, clusterId, raw, citadel))
                    continue;
                EntityId best = default;
                int bestDistance = int.MaxValue;
                for (int i = firstSite; i < sim.Sites.Count; i++)
                {
                    var site = sim.Sites.EntityAt(i);
                    if ((site == coreBase && sim.Sites.Count - firstSite > 1) || site == citadel)
                        continue;
                    int esi = sim.Sites.At(i).BaseEsiMilli;
                    int distance = esi < item.PreferredEsiMinMilli ? item.PreferredEsiMinMilli - esi
                        : esi > item.PreferredEsiMaxMilli ? esi - item.PreferredEsiMaxMilli : 0;
                    if (distance < bestDistance ||
                        (distance == bestDistance && sim.Sites.At(i).OrbitIndex < sim.Sites.Get(best).OrbitIndex))
                    {
                        best = site;
                        bestDistance = distance;
                    }
                }
                sim.Deposits.Add(sim.World.Create(), new MineralDeposit
                {
                    Site = best,
                    RawItem = raw,
                    SurfaceReserve = rules.SurfaceReserve,
                    DeepCrustReserve = rules.DeepCrustReserve,
                    CoreFractureReserve = rules.CoreFractureReserve
                });
            }
        }

        private static bool HasDeposit(SimulationWorld sim, int firstDeposit, int clusterId, ItemId raw, EntityId except)
        {
            for (int d = firstDeposit; d < sim.Deposits.Count; d++)
            {
                ref var deposit = ref sim.Deposits.At(d);
                if (deposit.RawItem == raw && deposit.Site != except && sim.Sites.Get(deposit.Site).ClusterId == clusterId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Picks up to <see cref="NewGameBalance.DepositsPerSite"/> distinct raw items whose preferred ESI window holds
        /// the site's ESI, in a seeded order. A site no window holds gets one deposit of the nearest window's item.
        /// Home systems skip the raw items excluded at home.
        /// </summary>
        private static void AddDeposits(SimulationWorld sim, ref SplitMix64 rng, EntityId site, int esiMilli, bool home)
        {
            var rules = sim.Balance.NewGame;
            Span<ItemId> candidates = stackalloc ItemId[ItemIds.RawCount];
            int count = 0;
            ItemId nearest = ItemId.None;
            int nearestDistance = int.MaxValue;
            for (int slot = 0; slot < ItemIds.RawCount; slot++)
            {
                var raw = ItemIds.FromSlot(slot);
                if (home && rules.IsExcludedAtHome(raw))
                    continue;
                ref readonly var item = ref sim.Balance.Recipes.GetItem(raw);
                if (!item.HasPreferredEsi)
                    continue;
                int distance = esiMilli < item.PreferredEsiMinMilli ? item.PreferredEsiMinMilli - esiMilli
                    : esiMilli > item.PreferredEsiMaxMilli ? esiMilli - item.PreferredEsiMaxMilli : 0;
                if (distance == 0)
                    candidates[count++] = raw;
                else if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = raw;
                }
            }
            if (count == 0)
            {
                if (nearest == ItemId.None)
                    return;
                candidates[count++] = nearest;
            }

            int picks = Math.Min(rules.DepositsPerSite, count);
            for (int i = 0; i < picks; i++)
            {
                int j = rng.NextInt(i, count - 1);
                var raw = candidates[j];
                candidates[j] = candidates[i];
                candidates[i] = raw;
                sim.Deposits.Add(sim.World.Create(), new MineralDeposit
                {
                    Site = site,
                    RawItem = raw,
                    SurfaceReserve = rules.SurfaceReserve,
                    DeepCrustReserve = rules.DeepCrustReserve,
                    CoreFractureReserve = rules.CoreFractureReserve
                });
            }
        }
    }
}
