using UnityEngine;

namespace Unruled.View.UI.Theme
{
    /// <summary>
    /// The style guide's colours for code that draws outside USS (the board renderer, Painter2D icons).
    /// Mirrors the variables in <c>Unruled.uss</c>; change both together.
    /// </summary>
    public static class UnruledPalette
    {
        // Ground & text
        public static readonly Color Void = Hex(0x0B1220);
        public static readonly Color Board = Hex(0x0E1626);
        public static readonly Color Panel = Hex(0x111B2E);
        public static readonly Color Raised = Hex(0x172440);
        public static readonly Color Rule = Hex(0x2A3A5C);
        public static readonly Color RuleStrong = Hex(0x4A5D82);
        public static readonly Color Ink = Hex(0xE6EDF7);
        public static readonly Color InkSoft = Hex(0xC4CEDD);
        public static readonly Color InkDim = Hex(0x9AA8C0);

        // Sides & THE SYSTEM's agencies
        public static readonly Color You = Hex(0x4DA3FF);
        public static readonly Color Hostile = Hex(0xFF8A3D);
        public static readonly Color Unclaimed = Hex(0x8796B0);
        public static readonly Color Sta = Hex(0xF2B634);
        public static readonly Color Wto = Hex(0x2EC4B6);
        public static readonly Color Who = Hex(0x5BD47A);
        public static readonly Color Iaea = Hex(0xFFE14D);
        public static readonly Color Noaa = Hex(0xF39237);
        public static readonly Color Danger = Hex(0xE5543B);
        public static readonly Color IrradiatedGround = Hex(0x3A3A1A);

        // Thermal
        public static readonly Color ThermalNominal = Hex(0x1E4F7A);
        public static readonly Color ThermalHot = Hex(0xF39237);
        public static readonly Color ThermalLocked = Hex(0xE5543B);

        private static readonly Color[] NoaaLevels =
        {
            Hex(0xF5E663), Hex(0xF7C548), Hex(0xF39237), Hex(0xE5543B), Hex(0x9E1F35)
        };

        /// <summary>Colour of a NOAA R/S/G level 1-5.</summary>
        public static Color NoaaLevel(int level) => NoaaLevels[Mathf.Clamp(level, 1, 5) - 1];

        /// <summary>Text colour on a NOAA level swatch (white only on level 5).</summary>
        public static Color NoaaInk(int level) => level >= 5 ? Color.white : Void;

        private static Color Hex(int rgb) =>
            new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);
    }
}
