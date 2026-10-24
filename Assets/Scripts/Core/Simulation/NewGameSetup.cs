using System;
using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
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
    /// Every system is a star from the seeded <see cref="GalaxyCatalog"/>: the hub is a hub-class star on the spawn
    /// ring, homes are the home-class stars nearest it and frontiers the frontier stars nearest it, each materialised
    /// with its catalog system seed (so the same world seed always lays out the same galaxy).
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

            int participants = 1 + rules.RivalCount;
            var picks = PickStars(sim.Galaxy.Catalog, participants, participants * rules.UninhabitedClustersPerParticipant);
            var player = SetUpParticipant(sim, picks[0], playerId, 0);
            for (int i = 1; i <= rules.RivalCount; i++)
            {
                var rival = SetUpParticipant(sim, picks[i], playerId + i, i);
                byte archetype = (byte)((i - 1) % sim.Balance.Ai.Count + 1);
                sim.Ai.Assign(rival.PlayerHq, archetype);
            }
            SetUpContestedCluster(sim, picks[participants], playerId, participants);
            SetUpUninhabitedClusters(sim, picks, participants);
            LinkHyperlanes(sim);
            ChartSpawnGroup(sim, playerId, participants);
            sim.Graph.Rebuild(sim.Starlanes);
            return player;
        }

        /// <summary>The contested cluster's id in a game with this many participants.</summary>
        public static int ContestedClusterId(int participants) => participants;

        /// <summary>The first uninhabited cluster's id in a game with this many participants.</summary>
        public static int FirstUninhabitedClusterId(int participants) => ContestedClusterId(participants) + 1;

        /// <summary>
        /// The stars of one spawn group, in cluster order: <paramref name="homes"/> home-class stars, then the hub, then
        /// <paramref name="frontiers"/> frontier stars. The hub is the hub-class star nearest a point on the spawn ring
        /// (at the angle the catalog's seed puts it); homes and frontiers are the stars of their class nearest the hub,
        /// searched in widening sector rings so a sparse patch still fills. Deterministic for a world seed.
        /// </summary>
        public static StarRecord[] PickStars(GalaxyCatalog catalog, int homes, int frontiers)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var b = catalog.Balance;
            var picks = new StarRecord[homes + 1 + frontiers];
            var rng = new SplitMix64(catalog.Seed ^ 0xA5A5A5A5UL);
            int angle = rng.NextInt(0, FixedMath.AngleMask);
            int ringX = (int)(((long)b.SpawnRingSectors * FixedMath.Cos(angle)) >> 16);
            int ringY = (int)(((long)b.SpawnRingSectors * FixedMath.Sin(angle)) >> 16);
            var used = new HashSet<long>();
            var hub = Nearest(catalog, ringX, ringY, StarClass.Hub, used, b.RadiusSectors);
            picks[homes] = hub;
            used.Add(hub.Id.Packed);
            catalog.SectorOf(hub.X, hub.Y, out int hx, out int hy);
            for (int i = 0; i < homes; i++)
            {
                picks[i] = Nearest(catalog, hx, hy, StarClass.Home, used, b.RadiusSectors);
                used.Add(picks[i].Id.Packed);
            }
            for (int i = 0; i < frontiers; i++)
            {
                picks[homes + 1 + i] = Nearest(catalog, hx, hy, StarClass.Frontier, used, b.RadiusSectors);
                used.Add(picks[homes + 1 + i].Id.Packed);
            }
            return picks;
        }

        // The unused star of `cls` nearest the centre of sector (sx, sy), searching square rings of sectors outward and
        // stopping one ring after the first hit (a closer star can only sit in the next ring).
        private static StarRecord Nearest(GalaxyCatalog catalog, int sx, int sy, StarClass cls, HashSet<long> used, int maxRing)
        {
            var stars = new List<StarRecord>();
            int size = catalog.Balance.SectorSize;
            long cx = (long)sx * size, cy = (long)sy * size;
            bool found = false;
            StarRecord best = default;
            long bestD = long.MaxValue;
            int stopAt = maxRing;
            for (int ring = 0; ring <= stopAt; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        continue;
                    catalog.GetSector(sx + dx, sy + dy, stars);
                    for (int i = 0; i < stars.Count; i++)
                    {
                        var s = stars[i];
                        if (s.Class != cls || used.Contains(s.Id.Packed))
                            continue;
                        long ddx = s.X - cx, ddy = s.Y - cy;
                        long d = ddx * ddx + ddy * ddy;
                        if (d < bestD || (d == bestD && s.Id.Packed < best.Id.Packed))
                        {
                            best = s;
                            bestD = d;
                            found = true;
                        }
                    }
                }
                if (found && stopAt == maxRing)
                    stopAt = Math.Min(maxRing, ring + 1);
            }
            if (!found) throw new InvalidOperationException($"The galaxy has no free {cls} star near sector ({sx}, {sy}).");
            return best;
        }

        // Generates a catalog star's system under `clusterId` and registers it with the galaxy; returns its orbits.
        private static EntityId[] Materialise(SimulationWorld sim, in StarRecord star, int clusterId)
        {
            var orbits = sim.GenerateSystem(star.SystemSeed, clusterId, rebuildGraph: false,
                star.Class == StarClass.Frontier ? 0 : sim.Balance.Universe.SettledMinSites);
            sim.Galaxy.Register(star, clusterId, sim.Clock.CurrentTick);
            return orbits;
        }

        private static void SetUpContestedCluster(SimulationWorld sim, in StarRecord star, int firstPlayerId, int participants)
        {
            ulong systemSeed = star.SystemSeed;
            int contested = ContestedClusterId(participants);
            var sites = Materialise(sim, star, contested);
            var deposits = new List<EntityId>();
            var siteRng = new SplitMix64(systemSeed);
            for (int i = 0; i < sites.Length; i++)
            {
                if (!sim.Stockpiles.Has(sites[i]))
                    sim.Stockpiles.Add(sites[i], default);
                AddDeposits(sim, ref siteRng, sites[i], sim.Sites.Get(sites[i]).BaseEsiMilli, home: false, deposits);
            }
            var citadel = sites[sites.Length - 1];
            AddMissingRaws(sim, sites, deposits, default, citadel, home: false);

            int orbits = sites.Length;
            for (int p = 0; p < participants; p++)
            {
                var home = sim.Galaxy.OuterOrbit(p);
                var far = sites[participants < orbits ? (int)((long)p * orbits / participants) : p % Math.Max(1, orbits - 1)];
                sim.JumpGates.Add(sim.World.Create(), new JumpGate { OwnerPlayerId = firstPlayerId + p, HomeSite = home, FarSite = far });
            }
            for (int g = 0; g < sim.JumpGates.Count; g++)
                if (sim.JumpGates.At(g).FarSite == citadel)
                    throw new InvalidOperationException($"{participants} participants leave no free orbit for the Citadel.");
            sim.Defense.CreateCitadel(citadel);
        }

        private static void SetUpUninhabitedClusters(SimulationWorld sim, StarRecord[] picks, int participants)
        {
            int perParticipant = sim.Balance.NewGame.UninhabitedClustersPerParticipant;
            if (perParticipant <= 0)
                return;
            int firstCluster = FirstUninhabitedClusterId(participants);
            int count = participants * perParticipant;
            var clusterSites = new EntityId[count][];
            for (int u = 0; u < count; u++)
            {
                clusterSites[u] = Materialise(sim, picks[participants + 1 + u], firstCluster + u);
                StockFrontier(sim, picks[participants + 1 + u].SystemSeed, clusterSites[u]);
            }

            int length = sim.Balance.JumpGates.LaneLength;
            int firstInterLane = sim.Starlanes.Count;
            for (int p = 0; p < participants; p++)
            {
                var far = sim.JumpGates.At(p).FarSite;
                int baseOffset = p * perParticipant;
                int baseCluster = firstCluster + baseOffset;
                AddInterSystemLane(sim, far, clusterSites[baseOffset][0], StarlaneTier.StaHighway, length, firstInterLane);
                for (int k = 1; k < perParticipant; k++)
                    AddInterSystemLane(sim, Outer(clusterSites[baseOffset + k - 1]), clusterSites[baseOffset + k][0],
                        StarlaneTier.StaHighway, length, firstInterLane);
            }
            if (count > 1)
                for (int u = 0; u < count; u++)
                    AddInterSystemLane(sim, Outer(clusterSites[u]), Outer(clusterSites[(u + 1) % count]),
                        StarlaneTier.OuterRimBypass, length, firstInterLane);
        }

        private static EntityId Outer(EntityId[] orbits) => orbits[orbits.Length - 1];

        // Each participant starts knowing its home, the hub and the uninhabited systems wired to its gate's far side;
        // everything else in the group is hidden until surveyed, reached, shared or bought.
        private static void ChartSpawnGroup(SimulationWorld sim, int firstPlayerId, int participants)
        {
            int perParticipant = sim.Balance.NewGame.UninhabitedClustersPerParticipant;
            int firstFrontier = FirstUninhabitedClusterId(participants);
            long tick = sim.Clock.CurrentTick;
            for (int p = 0; p < participants; p++)
            {
                int player = firstPlayerId + p;
                sim.Galaxy.Learn(player, p, ChartSource.Spawn, tick);
                sim.Galaxy.Learn(player, ContestedClusterId(participants), ChartSource.Spawn, tick);
                for (int k = 0; k < perParticipant; k++)
                    sim.Galaxy.Learn(player, firstFrontier + p * perParticipant + k, ChartSource.Spawn, tick);
            }
        }

        /// <summary>
        /// Every catalog hyperlane between two materialised systems becomes an STA highway (lower cluster's outermost
        /// orbit to the higher one's orbit 0, the way a survey links), so no system is sealed: homes keep their
        /// dormant gate to the hub but also reach their catalog neighbours. Hyperlanes to uncharted stars stay exits a
        /// fleet can survey.
        /// </summary>
        private static void LinkHyperlanes(SimulationWorld sim)
        {
            var galaxy = sim.Galaxy;
            var neighbours = new List<int>();
            int length = sim.Balance.Galaxy.SurveyLaneLength;
            int firstInterLane = sim.Starlanes.Count;
            int clusters = galaxy.NextClusterId();
            for (int c = 0; c < clusters; c++)
            {
                if (!galaxy.TryGetStar(c, out var star))
                    continue;
                galaxy.MaterialisedNeighbours(star, neighbours);
                for (int i = 0; i < neighbours.Count; i++)
                {
                    int n = neighbours[i];
                    if (n == c)
                        continue;
                    int low = Math.Min(c, n), high = Math.Max(c, n);
                    AddInterSystemLane(sim, galaxy.OuterOrbit(low), galaxy.SiteAtOrbit(high, 0), StarlaneTier.StaHighway,
                        length, firstInterLane);
                }
            }
        }

        /// <summary>
        /// Gives a just-generated uninhabited system (its sites in orbit order) stockpiles and deposits: the raws its
        /// ESI rolls, then every raw item the roll left out. Shared by new games and surveys.
        /// </summary>
        internal static void StockFrontier(SimulationWorld sim, ulong systemSeed, EntityId[] orbits)
        {
            var deposits = new List<EntityId>();
            var siteRng = new SplitMix64(systemSeed);
            for (int i = 0; i < orbits.Length; i++)
            {
                if (!sim.Stockpiles.Has(orbits[i]))
                    sim.Stockpiles.Add(orbits[i], default);
                AddDeposits(sim, ref siteRng, orbits[i], sim.Sites.Get(orbits[i]).BaseEsiMilli, home: false, deposits);
            }
            AddMissingRaws(sim, orbits, deposits, default, default, home: false);
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


        private static NewGame SetUpParticipant(SimulationWorld sim, in StarRecord star, int playerId, int clusterId)
        {
            var rules = sim.Balance.NewGame;
            ulong systemSeed = star.SystemSeed;
            var sites = Materialise(sim, star, clusterId);
            var deposits = new List<EntityId>();

            var siteRng = new SplitMix64(systemSeed);
            EntityId coreBase = default;
            int bestEsi = -1;
            for (int i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                if (!sim.Stockpiles.Has(site))
                    sim.Stockpiles.Add(site, default);
                int esi = sim.Sites.Get(site).BaseEsiMilli;
                AddDeposits(sim, ref siteRng, site, esi, home: true, deposits);
                if (esi > bestEsi)
                {
                    bestEsi = esi;
                    coreBase = site;
                }
            }
            if (bestEsi < 0) throw new InvalidOperationException($"System {clusterId} generated no sites.");
            AddMissingRaws(sim, sites, deposits, coreBase, default, home: true);

            sim.Sites.Get(coreBase).OwnerPlayerId = playerId;
            var hq = sim.CreatePlayer(playerId, coreBase);
            for (int d = 0; d < rules.CoreBaseDepositCount; d++)
            {
                var spec = rules.CoreBaseDeposit(d);
                bool found = false;
                for (int i = 0; i < deposits.Count && !found; i++)
                {
                    ref var deposit = ref sim.Deposits.Get(deposits[i]);
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
        private static void AddMissingRaws(SimulationWorld sim, EntityId[] sites, List<EntityId> deposits,
            EntityId coreBase, EntityId citadel, bool home)
        {
            var rules = sim.Balance.NewGame;
            for (int slot = 0; slot < ItemIds.RawCount; slot++)
            {
                var raw = ItemIds.FromSlot(slot);
                ref readonly var item = ref sim.Balance.Recipes.GetItem(raw);
                if ((home && rules.IsExcludedAtHome(raw)) || !item.HasPreferredEsi || HasDeposit(sim, deposits, raw, citadel))
                    continue;
                EntityId best = default;
                int bestDistance = int.MaxValue;
                bool bestHolds = false;
                for (int i = 0; i < sites.Length; i++)
                {
                    var site = sites[i];
                    if ((site == coreBase && sites.Length > 1) || site == citadel)
                        continue;
                    ref var info = ref sim.Sites.Get(site);
                    bool holds = rules.PlanetHolds(PlanetClasses.Of(in info, sim.Balance.Universe), raw);
                    int esi = info.BaseEsiMilli;
                    int distance = esi < item.PreferredEsiMinMilli ? item.PreferredEsiMinMilli - esi
                        : esi > item.PreferredEsiMaxMilli ? esi - item.PreferredEsiMaxMilli : 0;
                    // A planet whose type holds the raw beats any that doesn't; then the nearest ESI, then the lowest orbit.
                    bool better = best.IsNull || (holds && !bestHolds) ||
                                  (holds == bestHolds && (distance < bestDistance ||
                                                          (distance == bestDistance && info.OrbitIndex < sim.Sites.Get(best).OrbitIndex)));
                    if (better)
                    {
                        best = site;
                        bestDistance = distance;
                        bestHolds = holds;
                    }
                }
                var added = sim.World.Create();
                deposits.Add(added);
                sim.Deposits.Add(added, RollDeposit(sim, best, raw));
            }
        }

        // Whether one of the system's new deposits (all on its own sites) is of `raw`, ignoring those on `except`.
        private static bool HasDeposit(SimulationWorld sim, List<EntityId> deposits, ItemId raw, EntityId except)
        {
            for (int d = 0; d < deposits.Count; d++)
            {
                ref var deposit = ref sim.Deposits.Get(deposits[d]);
                if (deposit.RawItem == raw && deposit.Site != except)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Picks up to <see cref="NewGameBalance.DepositsPerSite"/> distinct raw items whose preferred ESI window holds
        /// the site's ESI, in a seeded order. A site no window holds gets one deposit of the nearest window's item.
        /// Home systems skip the raw items excluded at home, and every planet skips the raws its NASA class can't hold.
        /// </summary>
        private static void AddDeposits(SimulationWorld sim, ref SplitMix64 rng, EntityId site, int esiMilli, bool home,
            List<EntityId> added)
        {
            var rules = sim.Balance.NewGame;
            var planet = PlanetClasses.Of(in sim.Sites.Get(site), sim.Balance.Universe);
            Span<ItemId> candidates = stackalloc ItemId[ItemIds.RawCount];
            int count = 0;
            ItemId nearest = ItemId.None;
            int nearestDistance = int.MaxValue;
            for (int slot = 0; slot < ItemIds.RawCount; slot++)
            {
                var raw = ItemIds.FromSlot(slot);
                if ((home && rules.IsExcludedAtHome(raw)) || !rules.PlanetHolds(planet, raw))
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
                var deposit = sim.World.Create();
                added.Add(deposit);
                sim.Deposits.Add(deposit, RollDeposit(sim, site, raw));
            }
        }

        /// <summary>
        /// A generated deposit: the base reserves scaled by the planet's class and the ore's richness, each layer rolled
        /// on its own from the planet's terrain seed and the ore (so the roll never shifts any other generation).
        /// </summary>
        private static MineralDeposit RollDeposit(SimulationWorld sim, EntityId site, ItemId raw)
        {
            var rules = sim.Balance.NewGame;
            ref var info = ref sim.Sites.Get(site);
            long richness = (long)rules.RichnessPercent(PlanetClasses.Of(in info, sim.Balance.Universe)) *
                            rules.RichnessPercent(raw);
            var rng = new SplitMix64(info.TerrainSeed ^ (0xD1B54A32D192ED03UL * (ulong)(ItemIds.ToSlot(raw) + 1)));
            return new MineralDeposit
            {
                Site = site,
                RawItem = raw,
                SurfaceReserve = Roll(ref rng, rules, rules.SurfaceReserve, richness),
                DeepCrustReserve = Roll(ref rng, rules, rules.DeepCrustReserve, richness),
                CoreFractureReserve = Roll(ref rng, rules, rules.CoreFractureReserve, richness)
            };
        }

        // One layer: base reserve x class% x ore% x a roll in the configured percent band.
        private static int Roll(ref SplitMix64 rng, NewGameBalance rules, int baseReserve, long richnessPercentSquared)
        {
            int roll = rng.NextInt(rules.ReserveRollMinPercent, rules.ReserveRollMaxPercent);
            return (int)Math.Min(int.MaxValue, baseReserve * richnessPercentSquared * roll / 1_000_000L);
        }
    }
}
