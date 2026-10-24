using System.Globalization;
using Unruled.Core.Board;
using Unruled.Core.Galaxy;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// Text helpers for the HUD controllers. Labels are written only when their value changes, so a steady screen
    /// builds no strings; a changing one builds at most one per label per tick.
    /// </summary>
    public static class UiFormat
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // Display names for star clusters; sites read as STAR-ORBIT (e.g. KEPLER-3). Presentation only.
        private static readonly string[] StarNames =
        {
            "KEPLER", "VEGA", "ALTAIR", "DENEB", "RIGEL", "SIRIUS", "CASTOR", "POLLUX", "ANTARES", "ARCTURUS",
            "CAPELLA", "PROCYON", "MIRA", "SPICA", "REGULUS", "BELLATRIX"
        };

        public static void Set(Label label, ref long cache, long value, string text)
        {
            if (cache == value && label.text.Length > 0)
                return;
            cache = value;
            label.text = text;
        }

        /// <summary>Writes <paramref name="text"/> only when it differs (for texts built from several values).</summary>
        public static void Set(Label label, string text)
        {
            if (!string.Equals(label.text, text, System.StringComparison.Ordinal))
                label.text = text;
        }

        public static string Thousands(long value) => value.ToString("N0", Invariant);

        public static string Number(long value) => value.ToString(Invariant);

        /// <summary>Cents as a price with two decimals, e.g. 1180 -> "11.80".</summary>
        public static string Price(long cents) => (cents / 100m).ToString("0.00", Invariant);

        /// <summary>Signed per-mille change as a percentage with one decimal, e.g. 31 -> "+3.1%".</summary>
        public static string PercentDelta(long perMille)
        {
            if (perMille == 0)
                return "0.0%";
            string body = (System.Math.Abs(perMille) / 10m).ToString("0.0", Invariant);
            return (perMille > 0 ? "+" : "−") + body + "%";
        }

        /// <summary>Signed count; zero shows as a middle dot.</summary>
        public static string SignedCount(long value)
        {
            if (value == 0)
                return "·";
            return value > 0 ? "+" + Number(value) : "−" + Number(-value);
        }

        /// <summary>Sim time as "DAY d · hh:mm:ss" (day 1 starts at tick 0).</summary>
        public static string Clock(long tick)
        {
            long day = tick / SimTick.TicksPerDay + 1;
            long inDay = tick % SimTick.TicksPerDay;
            long h = inDay / SimTick.TicksPerHour;
            long m = inDay % SimTick.TicksPerHour / SimTick.TicksPerMinute;
            long s = inDay % SimTick.TicksPerMinute;
            return "DAY " + Number(day) + " · " + h.ToString("00", Invariant) + ":" + m.ToString("00", Invariant) + ":" +
                   s.ToString("00", Invariant);
        }

        /// <summary>A span of ticks as "h:mm:ss".</summary>
        public static string Duration(long ticks)
        {
            long h = ticks / SimTick.TicksPerHour;
            long m = ticks % SimTick.TicksPerHour / SimTick.TicksPerMinute;
            long s = ticks % SimTick.TicksPerMinute;
            return Number(h) + ":" + m.ToString("00", Invariant) + ":" + s.ToString("00", Invariant);
        }

        /// <summary>A long span for prose: "30 days", "2 h", "45 min" (whole units, rounded down).</summary>
        public static string Span(long ticks)
        {
            if (ticks >= SimTick.TicksPerDay)
                return Number(ticks / SimTick.TicksPerDay) + (ticks / SimTick.TicksPerDay == 1 ? " day" : " days");
            if (ticks >= SimTick.TicksPerHour)
                return Number(ticks / SimTick.TicksPerHour) + " h";
            if (ticks >= SimTick.TicksPerMinute)
                return Number(ticks / SimTick.TicksPerMinute) + " min";
            return Number(ticks) + " s";
        }

        public static string SiteName(in SiteInfo site) => StarName(site.ClusterId) + "-" + Number(site.OrbitIndex + 1);

        /// <summary>NASA planet type, as the planet card names it.</summary>
        public static string PlanetClassName(PlanetClass planet)
        {
            switch (planet)
            {
                case PlanetClass.SuperEarth: return "SUPER-EARTH";
                case PlanetClass.NeptuneLike: return "NEPTUNE-LIKE";
                case PlanetClass.GasGiant: return "GAS GIANT";
                default: return "TERRESTRIAL";
            }
        }

        public static string StarName(int clusterId)
        {
            int wrap = clusterId / StarNames.Length;
            string name = StarNames[clusterId % StarNames.Length];
            return wrap == 0 ? name : name + " " + Number(wrap + 1);
        }

        /// <summary>A catalog star's code: UNR-(sector x).(sector y)-(index), e.g. UNR-41.-7-3.</summary>
        public static string CatalogCode(StarId star) =>
            "UNR-" + Number(star.SectorX) + "." + Number(star.SectorY) + "-" + Number(star.Index);

        /// <summary>Fleets read as FLEET A, B, ... in creation order among the owner's fleets.</summary>
        public static string FleetName(int ordinal) => "FLEET " + FleetLetter(ordinal);

        /// <summary>The same name in running text, e.g. "Fleet B".</summary>
        public static string FleetLabel(int ordinal) => "Fleet " + FleetLetter(ordinal);

        private static string FleetLetter(int ordinal) =>
            ordinal < 26 ? ((char)('A' + ordinal)).ToString() : Number(ordinal + 1);
    }
}
