using System;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Deterministic star-system layout from one 64-bit seed (GDD §2.1 A), in integer math only:
    /// r_i = MinRadius + i * RadiusStep + noise; phi_i = phi_(i-1) + golden angle + jitter;
    /// ESI_0 = clamp(1 - ((r_i - R_HZ) / W_HZ)^2 + AtmoNoise).
    /// </summary>
    public static class SystemSeedGenerator
    {
        public const int EsiMaxMilli = 1000;

        private const ulong CountSalt = 0x4F52_4249_5453_4E4FUL; // "ORBITSNO": separate stream, layout draws unchanged

        /// <summary>
        /// How many planets a system seed gives: MinSitesPerSystem..SitesPerSystem, at least
        /// <paramref name="atLeast"/>. Drawn from its own stream, so the orbits it keeps are laid out as before.
        /// </summary>
        public static int PlanetCount(UniverseGenBalance balance, ulong systemSeed, int atLeast)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            var rng = new SplitMix64(systemSeed ^ CountSalt);
            int count = rng.NextInt(balance.MinSitesPerSystem, balance.SitesPerSystem);
            return Math.Max(count, Math.Min(atLeast, balance.SitesPerSystem));
        }

        /// <summary>Creates one site entity per orbit, in orbit order. The RNG draw order is part of the save contract.</summary>
        public static void GenerateSystem(
            World world, ComponentStore<SiteInfo> sites, UniverseGenBalance balance, ulong systemSeed, int clusterId) =>
            GenerateSystem(world, sites, balance, systemSeed, clusterId, null, PlanetCount(balance, systemSeed, 0));

        /// <summary>
        /// As above with <paramref name="count"/> planets (orbits 0..count-1), also writing each orbit's site into
        /// <paramref name="orbits"/> when given.
        /// </summary>
        public static void GenerateSystem(
            World world, ComponentStore<SiteInfo> sites, UniverseGenBalance balance, ulong systemSeed, int clusterId,
            EntityId[] orbits, int count)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (sites == null) throw new ArgumentNullException(nameof(sites));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (count < 1 || count > balance.SitesPerSystem) throw new ArgumentOutOfRangeException(nameof(count));
            if (orbits != null && orbits.Length < count) throw new ArgumentException("Too short.", nameof(orbits));

            var rng = new ChaCha8(systemSeed);
            int angle = rng.NextInt(0, FixedMath.AngleMask);
            long widthSquared = (long)balance.HabitableWidth * balance.HabitableWidth;

            for (int i = 0; i < count; i++)
            {
                int radius = balance.MinRadius + i * balance.RadiusStep +
                             rng.NextInt(-balance.RadiusNoise, balance.RadiusNoise);
                if (i > 0)
                    angle = (angle + balance.AngleStep + rng.NextInt(-balance.AngleJitter, balance.AngleJitter)) &
                            FixedMath.AngleMask;

                long offset = radius - balance.HabitableRadius;
                long esi = EsiMaxMilli - offset * offset * EsiMaxMilli / widthSquared +
                           rng.NextInt(-balance.AtmoNoiseMilli, balance.AtmoNoiseMilli);
                int esiMilli = (int)Math.Max(0, Math.Min(EsiMaxMilli, esi));

                var entity = world.Create();
                sites.Add(entity, new SiteInfo
                {
                    ClusterId = clusterId,
                    OrbitIndex = i,
                    PosX = (int)(((long)radius * FixedMath.Cos(angle)) >> 16),
                    PosY = (int)(((long)radius * FixedMath.Sin(angle)) >> 16),
                    TerrainSeed = rng.NextULong(),
                    BaseEsiMilli = esiMilli,
                    CurrentEsiMilli = esiMilli,
                    LifecycleState = PlanetLifecycleState.PristineWorld,
                    OwnerPlayerId = 0
                });
                if (orbits != null)
                    orbits[i] = entity;
            }
        }
    }
}
