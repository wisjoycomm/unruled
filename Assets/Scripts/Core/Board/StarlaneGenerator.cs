using System;
using System.Collections.Generic;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Links the sites of one generated system with lanes (PROVISIONAL topology, see the roadmap):
    /// an STA highway between neighbouring orbits (always connected), an outer-rim bypass between orbits two apart
    /// by chance, and a storm shortcut between orbits <see cref="StarlaneBalance.ShortcutSpan"/> apart by chance.
    /// The RNG draw order is part of the save contract.
    /// </summary>
    public static class StarlaneGenerator
    {
        private const ulong StreamSalt = 0x5354_4152_4C41_4E45UL; // "STARLANE": separate stream from site layout

        public static void LinkSystem(
            World world, ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes,
            StarlaneBalance balance, ulong systemSeed, int clusterId)
        {
            if (sites == null) throw new ArgumentNullException(nameof(sites));
            LinkSystem(world, sites, lanes, balance, systemSeed, CollectOrbits(sites, clusterId), null);
        }

        /// <summary>
        /// Links a system whose sites the caller already holds in orbit order (no scan of the site store); each new
        /// lane is appended to <paramref name="created"/> when given.
        /// </summary>
        internal static void LinkSystem(
            World world, ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes,
            StarlaneBalance balance, ulong systemSeed, EntityId[] orbits, List<EntityId> created)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (sites == null) throw new ArgumentNullException(nameof(sites));
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (orbits == null) throw new ArgumentNullException(nameof(orbits));

            var rng = new ChaCha8(systemSeed ^ StreamSalt);

            for (int i = 0; i + 1 < orbits.Length; i++)
                AddLane(world, sites, lanes, orbits[i], orbits[i + 1], StarlaneTier.StaHighway, created);

            for (int i = 0; i + 2 < orbits.Length; i++)
            {
                if (rng.NextInt(0, 99) < balance.BypassChancePercent)
                    AddLane(world, sites, lanes, orbits[i], orbits[i + 2], StarlaneTier.OuterRimBypass, created);
            }

            for (int i = 0; i + balance.ShortcutSpan < orbits.Length; i++)
            {
                if (rng.NextInt(0, 99) < balance.ShortcutChancePercent)
                    AddLane(world, sites, lanes, orbits[i], orbits[i + balance.ShortcutSpan], StarlaneTier.HazardStormShortcut,
                        created);
            }
        }

        private static EntityId[] CollectOrbits(ComponentStore<SiteInfo> sites, int clusterId)
        {
            int count = 0;
            for (int i = 0; i < sites.Count; i++)
                if (sites.At(i).ClusterId == clusterId)
                    count++;

            var orbits = new EntityId[count];
            for (int i = 0; i < sites.Count; i++)
            {
                ref var site = ref sites.At(i);
                if (site.ClusterId != clusterId)
                    continue;
                if (site.OrbitIndex < 0 || site.OrbitIndex >= count || !orbits[site.OrbitIndex].IsNull)
                    throw new InvalidOperationException($"Cluster {clusterId} orbits are not 0..{count - 1} without gaps.");
                orbits[site.OrbitIndex] = sites.EntityAt(i);
            }
            return orbits;
        }

        private static void AddLane(
            World world, ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes,
            EntityId a, EntityId b, StarlaneTier tier, List<EntityId> created)
        {
            ref var siteA = ref sites.Get(a);
            ref var siteB = ref sites.Get(b);
            long dx = siteA.PosX - siteB.PosX;
            long dy = siteA.PosY - siteB.PosY;

            var lane = world.Create();
            lanes.Add(lane, new StarlaneEdge
            {
                SiteA = a, SiteB = b, Tier = tier, Length = (int)FixedMath.Sqrt(dx * dx + dy * dy)
            });
            created?.Add(lane);
        }
    }
}
