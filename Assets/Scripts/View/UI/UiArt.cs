using Unruled.Core.Board;
using Unruled.Core.SystemFactions;
using UnityEngine;

namespace Unruled.View.UI
{
    /// <summary>
    /// The UI's images from <c>Resources/UI</c>, loaded once by <see cref="MainScreenView"/> and handed to the panels
    /// that draw them. A missing file stays null and the element simply shows no image.
    /// </summary>
    public sealed class UiArt
    {
        private readonly Texture2D[] _agencies = new Texture2D[6];
        private readonly Texture2D[] _planets = new Texture2D[6];

        /// <summary>Tech tiers drawn per ship class (T1..T5): size and the class's key feature count grow with tier.</summary>
        public const int Tiers = 5;
        public const int ShipClasses = 4; // Line, EW, Supply, Cargo
        private static readonly string[] ShipNames = { "line", "ew", "supply", "cargo" };
        private static readonly string[] TokenNames = { "warfleet", "convoy", "patrol", "inspector" };
        private readonly Texture2D[] _ships = new Texture2D[ShipClasses * Tiers];
        private readonly Texture2D[] _tokens = new Texture2D[TokenNames.Length * Tiers];

        public readonly Texture2D Warning, Market, Scan, Ecm, Mothball, Activate, Focus, Relocate, Terraform, Pause;
        public readonly Texture2D Citadel, JumpGate;
        public readonly Texture2D VictoryScreen, DefeatScreen, MenuScreen;
        public readonly Texture2D CapitalPlanet;

        public UiArt()
        {
            _agencies[(int)SystemAgency.Sta] = Load("agency_sta");
            _agencies[(int)SystemAgency.Wto] = Load("agency_wto");
            _agencies[(int)SystemAgency.Who] = Load("agency_who");
            _agencies[(int)SystemAgency.Iaea] = Load("agency_iaea");
            _agencies[(int)SystemAgency.Noaa] = Load("agency_noaa");
            Warning = Load("alert_warning");
            Market = Load("action_market");
            Scan = Load("action_scan");
            Ecm = Load("action_ecm");
            Mothball = Load("action_mothball");
            Activate = Load("action_activate");
            Focus = Load("action_focus");
            Relocate = Load("action_relocate");
            Terraform = Load("action_terraform");
            Pause = Load("action_pause");
            Citadel = Load("system_citadel_emblem");
            JumpGate = Load("jump_gate_emblem");
            VictoryScreen = Load("screen_victory");
            DefeatScreen = Load("screen_defeat");
            MenuScreen = Load("screen_menu");
            _planets[(int)PlanetLifecycleState.PristineWorld] = Load("node_pristine");
            _planets[(int)PlanetLifecycleState.DeepCoreStripping] = Load("node_deep_core");
            _planets[(int)PlanetLifecycleState.DepletedBarrenHusk] = Load("node_barren_husk");
            _planets[(int)PlanetLifecycleState.IrradiatedDeadWorld] = Load("node_irradiated");
            _planets[(int)PlanetLifecycleState.TerraformedRecovery] = Load("node_terraformed");
            CapitalPlanet = Load("node_core_base");
            for (int c = 0; c < ShipClasses; c++)
            {
                var fallback = Load("ship_class_" + ShipNames[c]);
                for (int t = 0; t < Tiers; t++)
                    _ships[c * Tiers + t] = Load("ship_" + ShipNames[c] + "_t" + (t + 1)) ?? fallback;
            }
            for (int k = 0; k < TokenNames.Length; k++)
            {
                var fallback = Load("token_" + TokenNames[k]);
                for (int t = 0; t < Tiers; t++)
                    _tokens[k * Tiers + t] = Load("token_" + TokenNames[k] + "_t" + (t + 1)) ?? fallback;
            }
        }

        public Texture2D Agency(SystemAgency agency) =>
            (int)agency < _agencies.Length ? _agencies[(int)agency] : null;

        /// <summary>Ship-class plate (0 Line, 1 EW, 2 Supply, 3 Cargo) at a tech tier 1..5.</summary>
        public Texture2D Ship(int shipClass, int tier) => _ships[shipClass * Tiers + TierIndex(tier)];

        /// <summary>Board token (0 warfleet, 1 convoy, 2 SYSTEM patrol, 3 IAEA inspector) at a tech tier 1..5.</summary>
        public Texture2D Token(int kind, int tier) => _tokens[kind * Tiers + TierIndex(tier)];

        private static int TierIndex(int tier) => tier < 1 ? 0 : tier > Tiers ? Tiers - 1 : tier - 1;

        public Texture2D Planet(PlanetLifecycleState state) =>
            (int)state < _planets.Length ? _planets[(int)state] : null;

        private static Texture2D Load(string name) => Resources.Load<Texture2D>("UI/" + name);
    }
}
