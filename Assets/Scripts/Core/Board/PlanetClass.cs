namespace Unruled.Core.Board
{
    /// <summary>
    /// NASA's four exoplanet types (science.nasa.gov/exoplanets/planet-types), as they fall in one generated system:
    /// rocky worlds inside and across the habitable zone, gas giants past it, and Neptune-like ice giants furthest out.
    /// </summary>
    public enum PlanetClass : byte
    {
        Terrestrial = 0, // Earth-sized or smaller, rock and metal
        SuperEarth = 1, // rocky, larger than Earth, thick atmosphere
        NeptuneLike = 2, // ice giant: hydrogen/helium over water, ammonia and methane ices
        GasGiant = 3 // Jupiter/Saturn-like hydrogen and helium
    }

    /// <summary>
    /// A planet's class, read from its orbit and terrain seed (both fixed at generation, so nothing extra is saved):
    /// inside the habitable zone's inner edge a world is a hot Terrestrial; from there out to the outer edge it is
    /// Terrestrial or, by the seed, a Super-Earth; past the outer edge a Gas Giant; past the ice line a Neptune-like.
    /// </summary>
    public static class PlanetClasses
    {
        public const int Count = 4;

        public static PlanetClass Of(in SiteInfo site, UniverseGenBalance balance)
        {
            if (balance == null) throw new System.ArgumentNullException(nameof(balance));
            long r = Simulation.FixedMath.Sqrt((long)site.PosX * site.PosX + (long)site.PosY * site.PosY);
            int half = balance.HabitableWidth / 2;
            if (r >= balance.IceLineRadius)
                return PlanetClass.NeptuneLike;
            if (r > balance.HabitableRadius + half)
                return PlanetClass.GasGiant;
            if (r < balance.HabitableRadius - half)
                return PlanetClass.Terrestrial;
            return (int)(site.TerrainSeed % 1000) < balance.SuperEarthPermille ? PlanetClass.SuperEarth : PlanetClass.Terrestrial;
        }
    }
}
