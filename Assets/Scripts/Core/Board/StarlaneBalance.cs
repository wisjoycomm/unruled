using System;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Travel rules of one lane tier (data sheet tab 04). <see cref="MaxStormLevel"/> is the highest NOAA level the
    /// lane is exposed to (0 = sheltered). Fuel is a multiplier in milli-units (4.5x = 4500).
    /// </summary>
    public readonly struct StarlaneTierRules
    {
        public readonly int SpeedPercent;
        public readonly int FuelMultiplierMilli;
        public readonly byte MaxStormLevel;
        public readonly byte MinTechTier;
        public readonly int AutoRadarHiddenPercent;
        public readonly bool ChargesStaToll;

        public StarlaneTierRules(
            int speedPercent, int fuelMultiplierMilli, byte maxStormLevel, byte minTechTier,
            int autoRadarHiddenPercent, bool chargesStaToll)
        {
            if (speedPercent < 1) throw new ArgumentOutOfRangeException(nameof(speedPercent));
            if (fuelMultiplierMilli < 0) throw new ArgumentOutOfRangeException(nameof(fuelMultiplierMilli));
            if (maxStormLevel > 5) throw new ArgumentOutOfRangeException(nameof(maxStormLevel));
            if (minTechTier > 5) throw new ArgumentOutOfRangeException(nameof(minTechTier));
            if (autoRadarHiddenPercent < 0 || autoRadarHiddenPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(autoRadarHiddenPercent));

            SpeedPercent = speedPercent;
            FuelMultiplierMilli = fuelMultiplierMilli;
            MaxStormLevel = maxStormLevel;
            MinTechTier = minTechTier;
            AutoRadarHiddenPercent = autoRadarHiddenPercent;
            ChargesStaToll = chargesStaToll;
        }
    }

    /// <summary>Lane tier rules plus the generator's topology chances (percent per candidate pair).</summary>
    public sealed class StarlaneBalance
    {
        public readonly StarlaneTierRules Highway;
        public readonly StarlaneTierRules Bypass;
        public readonly StarlaneTierRules Shortcut;
        public readonly int BypassChancePercent;
        public readonly int ShortcutChancePercent;
        public readonly int ShortcutSpan;

        public StarlaneBalance(
            StarlaneTierRules highway, StarlaneTierRules bypass, StarlaneTierRules shortcut,
            int bypassChancePercent, int shortcutChancePercent, int shortcutSpan)
        {
            if (bypassChancePercent < 0 || bypassChancePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(bypassChancePercent));
            if (shortcutChancePercent < 0 || shortcutChancePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(shortcutChancePercent));
            if (shortcutSpan < 3) throw new ArgumentOutOfRangeException(nameof(shortcutSpan), "Span 1 and 2 are highways and bypasses.");

            Highway = highway;
            Bypass = bypass;
            Shortcut = shortcut;
            BypassChancePercent = bypassChancePercent;
            ShortcutChancePercent = shortcutChancePercent;
            ShortcutSpan = shortcutSpan;
        }

        public ref readonly StarlaneTierRules Get(StarlaneTier tier)
        {
            switch (tier)
            {
                case StarlaneTier.StaHighway: return ref Highway;
                case StarlaneTier.OuterRimBypass: return ref Bypass;
                case StarlaneTier.HazardStormShortcut: return ref Shortcut;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, null);
            }
        }
    }
}
