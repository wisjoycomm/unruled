using System;

namespace Unruled.Core.Board
{
    /// <summary>Star-system layout numbers (GDD §2.1 A). Distances in map units, ESI in milli-units, angles in binary units.</summary>
    public sealed class UniverseGenBalance
    {
        public const int PositionRoundingMargin = 4;

        public readonly int SitesPerSystem; // the most planets a system can have
        public readonly int MinSitesPerSystem; // the fewest
        public readonly int SettledMinSites; // a home or hub system has at least this many (gates, Citadel, core base)
        public readonly int MinRadius;
        public readonly int RadiusStep;
        public readonly int RadiusNoise;
        public readonly int MinSiteSpacing;
        public readonly int AngleStep;
        public readonly int AngleJitter;
        public readonly int HabitableRadius;
        public readonly int HabitableWidth;
        public readonly int AtmoNoiseMilli;
        public readonly int IceLineRadius; // past this a giant is Neptune-like (see PlanetClasses)
        public readonly int SuperEarthPermille; // share of habitable-zone rocky worlds that are Super-Earths

        public UniverseGenBalance(
            int sitesPerSystem, int minRadius, int radiusStep, int radiusNoise, int minSiteSpacing,
            int angleStep, int angleJitter, int habitableRadius, int habitableWidth, int atmoNoiseMilli,
            int iceLineRadius, int superEarthPermille, int minSitesPerSystem, int settledMinSites)
        {
            if (minSitesPerSystem < 1 || minSitesPerSystem > sitesPerSystem) throw new ArgumentOutOfRangeException(nameof(minSitesPerSystem));
            if (settledMinSites < minSitesPerSystem || settledMinSites > sitesPerSystem) throw new ArgumentOutOfRangeException(nameof(settledMinSites));
            if (iceLineRadius <= habitableRadius + habitableWidth / 2) throw new ArgumentOutOfRangeException(nameof(iceLineRadius));
            if (superEarthPermille < 0 || superEarthPermille > 1000) throw new ArgumentOutOfRangeException(nameof(superEarthPermille));
            if (sitesPerSystem < 1) throw new ArgumentOutOfRangeException(nameof(sitesPerSystem));
            if (minRadius < minSiteSpacing) throw new ArgumentOutOfRangeException(nameof(minRadius));
            if (radiusNoise < 0) throw new ArgumentOutOfRangeException(nameof(radiusNoise));
            // Sites sit on distinct orbits, so a radial gap >= spacing guarantees the GDD minimum distance.
            // The extra margin covers integer truncation of positions and fixed-point trig error.
            if (radiusStep - 2 * radiusNoise < minSiteSpacing + PositionRoundingMargin)
                throw new ArgumentException(
                    "radiusStep - 2 * radiusNoise must exceed minSiteSpacing by the rounding margin.", nameof(radiusStep));
            if (angleJitter < 0) throw new ArgumentOutOfRangeException(nameof(angleJitter));
            if (habitableWidth <= 0) throw new ArgumentOutOfRangeException(nameof(habitableWidth));
            if (atmoNoiseMilli < 0) throw new ArgumentOutOfRangeException(nameof(atmoNoiseMilli));

            SitesPerSystem = sitesPerSystem;
            MinSitesPerSystem = minSitesPerSystem;
            SettledMinSites = settledMinSites;
            MinRadius = minRadius;
            RadiusStep = radiusStep;
            RadiusNoise = radiusNoise;
            MinSiteSpacing = minSiteSpacing;
            AngleStep = angleStep;
            AngleJitter = angleJitter;
            HabitableRadius = habitableRadius;
            HabitableWidth = habitableWidth;
            AtmoNoiseMilli = atmoNoiseMilli;
            IceLineRadius = iceLineRadius;
            SuperEarthPermille = superEarthPermille;
        }
    }
}
