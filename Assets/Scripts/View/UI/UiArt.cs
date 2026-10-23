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
        }

        public Texture2D Agency(SystemAgency agency) =>
            (int)agency < _agencies.Length ? _agencies[(int)agency] : null;

        public Texture2D Planet(PlanetLifecycleState state) =>
            (int)state < _planets.Length ? _planets[(int)state] : null;

        private static Texture2D Load(string name) => Resources.Load<Texture2D>("UI/" + name);
    }
}
