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
    /// preferred ESI window holds its ESI; home systems hold no tungsten or uranium. The core base is the home's
    /// highest-ESI site: it is owned, rigged, given refineries and stock, and a fleet starts there. Rivals are AI-run.
    /// Then the neutral contested cluster (cluster = participant count) is generated with every raw item, and each home
    /// gets a dormant jump gate from its outermost orbit to its own contested orbit (GDD §1.2). THE SYSTEM holds the
    /// contested cluster's outermost orbit, which no gate lands on, as its Citadel.
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
            SetUpContestedCluster(sim, ref rng, playerId, 1 + rules.RivalCount);
            return player;
        }

        /// <summary>The contested cluster's id in a game with this many participants.</summary>
        public static int ContestedClusterId(int participants) => participants;

        private static void SetUpContestedCluster(SimulationWorld sim, ref SplitMix64 rng, int firstPlayerId, int participants)
        {
            ulong systemSeed = rng.NextULong();
            int firstSite = sim.Sites.Count;
            int contested = ContestedClusterId(participants);
            sim.GenerateSystem(systemSeed, contested);
            var siteRng = new SplitMix64(systemSeed);
            for (int i = firstSite; i < sim.Sites.Count; i++)
            {
                var site = sim.Sites.EntityAt(i);
                if (!sim.Stockpiles.Has(site))
                    sim.Stockpiles.Add(site, default);
                AddDeposits(sim, ref siteRng, site, sim.Sites.At(i).BaseEsiMilli, home: false);
            }

            int orbits = sim.Sites.Count - firstSite;
            for (int p = 0; p < participants; p++)
            {
                var home = OuterOrbit(sim, p);
                var far = SiteAtOrbit(sim, contested, (int)((long)p * orbits / participants));
                sim.JumpGates.Add(sim.World.Create(), new JumpGate { OwnerPlayerId = firstPlayerId + p, HomeSite = home, FarSite = far });
            }
            var citadel = OuterOrbit(sim, contested);
            for (int g = 0; g < sim.JumpGates.Count; g++)
                if (sim.JumpGates.At(g).FarSite == citadel)
                    throw new InvalidOperationException($"{participants} participants leave no free orbit for the Citadel.");
            sim.Defense.CreateCitadel(citadel);
        }

        private static EntityId OuterOrbit(SimulationWorld sim, int clusterId)
        {
            EntityId best = default;
            int bestOrbit = -1;
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                ref var site = ref sim.Sites.At(i);
                if (site.ClusterId == clusterId && site.OrbitIndex > bestOrbit)
                {
                    bestOrbit = site.OrbitIndex;
                    best = sim.Sites.EntityAt(i);
                }
            }
            return best;
        }

        private static EntityId SiteAtOrbit(SimulationWorld sim, int clusterId, int orbit)
        {
            for (int i = 0; i < sim.Sites.Count; i++)
                if (sim.Sites.At(i).ClusterId == clusterId && sim.Sites.At(i).OrbitIndex == orbit)
                    return sim.Sites.EntityAt(i);
            throw new InvalidOperationException($"Cluster {clusterId} has no orbit {orbit}.");
        }

        private static NewGame SetUpParticipant(SimulationWorld sim, ref SplitMix64 rng, int playerId, int clusterId)
        {
            var rules = sim.Balance.NewGame;
            ulong systemSeed = rng.NextULong();
            int firstSite = sim.Sites.Count;
            sim.GenerateSystem(systemSeed, clusterId);

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

            sim.Sites.Get(coreBase).OwnerPlayerId = playerId;
            var hq = sim.CreatePlayer(playerId, coreBase);
            for (int d = 0; d < rules.CoreBaseDepositCount; d++)
            {
                var spec = rules.CoreBaseDeposit(d);
                bool found = false;
                for (int i = 0; i < sim.Deposits.Count && !found; i++)
                {
                    ref var deposit = ref sim.Deposits.At(i);
                    if (deposit.Site != coreBase || deposit.RawItem != spec.Item)
                        continue;
                    deposit.RigCount = spec.Quantity;
                    found = true;
                }
                if (!found)
                    sim.Deposits.Add(sim.World.Create(), new MineralDeposit
                    {
                        Site = coreBase,
                        RawItem = spec.Item,
                        SurfaceReserve = rules.SurfaceReserve,
                        DeepCrustReserve = rules.DeepCrustReserve,
                        CoreFractureReserve = rules.CoreFractureReserve,
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
