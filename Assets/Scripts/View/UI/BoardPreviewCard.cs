using Unruled.Core.Board;
using Unruled.Core.Simulation;
using Unruled.View.UI.Theme;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// The board's preview plate, top-right over the board: a planet picked on the system view (portrait, crust
    /// cross-section with the reserve left in each layer, owner, ESI, rigs and refineries) or a star system picked on
    /// the galaxy view (its star, a row of planet thumbnails, owners, own fleets and the jump gate). Reads state only;
    /// it refreshes once per tick and only rewrites what changed.
    /// </summary>
    public sealed class BoardPreviewCard
    {
        private const int Layers = 3;
        private const int MaxThumbs = 10;
        private static readonly string[] LayerNames = { "SURFACE", "DEEP CRUST", "CORE" };

        private readonly VisualElement _card, _portrait, _section, _thumbRow;
        private readonly Label _title, _meta, _line1, _line2;
        private readonly VisualElement[] _layerRows = new VisualElement[Layers];
        private readonly VisualElement[] _layerFills = new VisualElement[Layers];
        private readonly Label[] _layerValues = new Label[Layers];
        private readonly VisualElement[] _thumbs = new VisualElement[MaxThumbs];
        private readonly UiArt _art;
        private readonly Texture2D _crust;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _site;
        private int _cluster = -1;
        private long _shownKey = long.MinValue;

        public BoardPreviewCard(VisualElement board, UiArt art)
        {
            _art = art;
            _crust = Resources.Load<Texture2D>("UI/crust_section");
            _card = new VisualElement { pickingMode = PickingMode.Ignore };
            _card.AddToClassList("preview");
            _card.style.display = DisplayStyle.None;

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("preview__head");
            _portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            _portrait.AddToClassList("preview__portrait");
            head.Add(_portrait);
            var titles = new VisualElement { pickingMode = PickingMode.Ignore };
            titles.AddToClassList("preview__titles");
            _title = NewLabel("preview__title");
            _meta = NewLabel("section-meta");
            titles.Add(_title);
            titles.Add(_meta);
            head.Add(titles);
            _card.Add(head);

            _section = new VisualElement { pickingMode = PickingMode.Ignore };
            _section.AddToClassList("preview__section");
            if (_crust != null)
                _section.style.backgroundImage = new StyleBackground(_crust);
            _card.Add(_section);

            for (int i = 0; i < Layers; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("preview__layer");
                var name = NewLabel("gauge__label");
                name.text = LayerNames[i];
                name.AddToClassList("preview__layer-name");
                var track = new VisualElement { pickingMode = PickingMode.Ignore };
                track.AddToClassList("gauge__track");
                track.AddToClassList("preview__layer-track");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("preview__layer-fill");
                fill.AddToClassList("preview__layer-fill--" + UiFormat.Number(i));
                track.Add(fill);
                _layerValues[i] = NewLabel("preview__layer-value");
                row.Add(name);
                row.Add(track);
                row.Add(_layerValues[i]);
                _layerRows[i] = row;
                _layerFills[i] = fill;
                _card.Add(row);
            }

            _thumbRow = new VisualElement { pickingMode = PickingMode.Ignore };
            _thumbRow.AddToClassList("preview__thumbs");
            for (int i = 0; i < MaxThumbs; i++)
            {
                _thumbs[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                _thumbs[i].AddToClassList("preview__thumb");
                _thumbRow.Add(_thumbs[i]);
            }
            _card.Add(_thumbRow);

            _line1 = NewLabel("u-caption");
            _line1.AddToClassList("preview__line");
            _line2 = NewLabel("u-caption");
            _line2.AddToClassList("preview__line");
            _card.Add(_line1);
            _card.Add(_line2);
            var frame = new PlateFrame();
            frame.AddToClassList("plate-frame");
            _card.Add(frame);
            board.Add(_card);
        }

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            Hide();
        }

        public void ShowSite(EntityId site)
        {
            _site = site;
            _cluster = -1;
            _shownKey = long.MinValue;
            Refresh();
        }

        public void ShowCluster(int clusterId, Texture2D star)
        {
            _site = default;
            _cluster = clusterId;
            _shownKey = long.MinValue;
            if (star != null)
                _portrait.style.backgroundImage = new StyleBackground(star);
            Refresh();
        }

        public void Hide()
        {
            _site = default;
            _cluster = -1;
            _card.style.display = DisplayStyle.None;
        }

        public void Refresh()
        {
            if (_sim == null)
                return;
            if (!_site.IsNull && _sim.Sites.Has(_site))
                RefreshSite();
            else if (_cluster >= 0)
                RefreshCluster();
            else
                _card.style.display = DisplayStyle.None;
        }

        private void RefreshSite()
        {
            ref var site = ref _sim.Sites.Get(_site);
            long surface = 0, crust = 0, core = 0;
            int rigs = 0, deposits = 0;
            for (int d = 0; d < _sim.Deposits.Count; d++)
            {
                ref var deposit = ref _sim.Deposits.At(d);
                if (deposit.Site != _site)
                    continue;
                deposits++;
                surface += deposit.SurfaceReserve;
                crust += deposit.DeepCrustReserve;
                core += deposit.CoreFractureReserve;
                rigs += deposit.RigCount;
            }
            int refineries = 0;
            for (int r = 0; r < _sim.Refineries.Count; r++)
                if (_sim.Refineries.At(r).Site == _site)
                    refineries++;
            long key = (long)_site.Index << 40 ^ (surface + crust * 3 + core * 7) ^ (long)site.LifecycleState << 56 ^
                       (long)site.CurrentEsiMilli << 20 ^ (long)(site.OwnerPlayerId + 64) << 48 ^ rigs << 8 ^ refineries;
            if (key == _shownKey)
                return;
            _shownKey = key;

            _card.style.display = DisplayStyle.Flex;
            SetSiteMode(true);
            bool capital = IsCore(_site);
            var portrait = capital ? _art.CapitalPlanet : _art.Planet(site.LifecycleState);
            if (portrait != null)
                _portrait.style.backgroundImage = new StyleBackground(portrait);
            _title.text = UiFormat.SiteName(in site) + (capital ? " · CAPITAL" : "");
            _meta.text = OwnerText(site.OwnerPlayerId) + " · " + LifecycleText(site.LifecycleState);

            long max = System.Math.Max(1L, System.Math.Max(surface, System.Math.Max(crust, core)));
            SetLayer(0, surface, max);
            SetLayer(1, crust, max);
            SetLayer(2, core, max);
            _line1.text = "ESI " + Esi(site.CurrentEsiMilli) + " · " + UiFormat.Number(deposits) + " DEPOSITS";
            _line2.text = UiFormat.Number(rigs) + " RIGS · " + UiFormat.Number(refineries) + " REFINERIES";
        }

        private void RefreshCluster()
        {
            int planets = 0, own = 0, rival = 0, neutral = 0, fleets = 0;
            for (int i = 0; i < _sim.Sites.Count; i++)
            {
                ref var site = ref _sim.Sites.At(i);
                if (site.ClusterId != _cluster)
                    continue;
                if (planets < MaxThumbs)
                {
                    var entity = _sim.Sites.EntityAt(i);
                    var tex = IsCore(entity) ? _art.CapitalPlanet : _art.Planet(site.LifecycleState);
                    _thumbs[planets].style.display = DisplayStyle.Flex;
                    if (tex != null)
                        _thumbs[planets].style.backgroundImage = new StyleBackground(tex);
                    _thumbs[planets].style.borderBottomColor = OwnerColour(site.OwnerPlayerId);
                }
                planets++;
                if (site.OwnerPlayerId == _playerId) own++;
                else if (site.OwnerPlayerId == 0) neutral++;
                else rival++;
            }
            for (int i = 0; i < _sim.Fleets.Count; i++)
            {
                if (_sim.Fleets.At(i).OwnerPlayerId != _playerId)
                    continue;
                ref var motion = ref _sim.FleetMotions.Get(_sim.Fleets.EntityAt(i));
                var at = motion.Lane.IsNull ? motion.Site : motion.FromSite;
                if (_sim.Sites.Has(at) && _sim.Sites.Get(at).ClusterId == _cluster)
                    fleets++;
            }
            int gate = GateState();
            long key = (long)_cluster << 40 | (long)own << 30 | (long)rival << 20 | (long)neutral << 10 | (long)fleets << 2 | (uint)gate;
            if (key == _shownKey)
                return;
            _shownKey = key;

            _card.style.display = DisplayStyle.Flex;
            SetSiteMode(false);
            for (int i = planets; i < MaxThumbs; i++)
                _thumbs[i].style.display = DisplayStyle.None;
            _title.text = "SYSTEM " + UiFormat.StarName(_cluster);
            _meta.text = UiFormat.Number(planets) + " PLANETS";
            _line1.text = "YOURS " + UiFormat.Number(own) + " · RIVAL " + UiFormat.Number(rival) + " · OPEN " + UiFormat.Number(neutral);
            _line2.text = UiFormat.Number(fleets) + (fleets == 1 ? " FLEET HERE" : " FLEETS HERE") +
                          (gate == 2 ? " · GATE OPEN" : gate == 1 ? " · GATE SEALED" : "");
        }

        // 0 = no jump gate touches this system, 1 = sealed, 2 = open.
        private int GateState()
        {
            for (int i = 0; i < _sim.JumpGates.Count; i++)
            {
                ref var gate = ref _sim.JumpGates.At(i);
                bool here = (_sim.Sites.Has(gate.HomeSite) && _sim.Sites.Get(gate.HomeSite).ClusterId == _cluster) ||
                            (_sim.Sites.Has(gate.FarSite) && _sim.Sites.Get(gate.FarSite).ClusterId == _cluster);
                if (here)
                    return gate.IsOpen ? 2 : 1;
            }
            return 0;
        }

        private void SetSiteMode(bool site)
        {
            var on = site ? DisplayStyle.Flex : DisplayStyle.None;
            _section.style.display = _crust != null ? on : DisplayStyle.None;
            for (int i = 0; i < Layers; i++)
                _layerRows[i].style.display = on;
            _thumbRow.style.display = site ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetLayer(int index, long reserve, long max)
        {
            _layerFills[index].style.width = Length.Percent(reserve * 100f / max);
            _layerValues[index].text = UiFormat.Thousands(reserve);
        }

        private bool IsCore(EntityId site)
        {
            for (int i = 0; i < _sim.Players.Count; i++)
                if (_sim.Players.At(i).CoreBase == site)
                    return true;
            return false;
        }

        private Color OwnerColour(int owner)
        {
            if (owner == _playerId) return UnruledPalette.You;
            if (owner == 0) return UnruledPalette.Unclaimed;
            return owner < 0 ? UnruledPalette.Sta : UnruledPalette.Hostile;
        }

        private string OwnerText(int owner)
        {
            if (owner == 0) return "NEUTRAL";
            if (owner == _playerId) return "YOURS";
            if (owner < 0) return _sim.Citadels.Has(_site) ? "CITADEL" : "THE SYSTEM";
            return "RIVAL P" + UiFormat.Number(owner);
        }

        private static string LifecycleText(PlanetLifecycleState state)
        {
            switch (state)
            {
                case PlanetLifecycleState.PristineWorld: return "PRISTINE";
                case PlanetLifecycleState.DeepCoreStripping: return "DEEP-CORE STRIPPING";
                case PlanetLifecycleState.DepletedBarrenHusk: return "BARREN HUSK";
                case PlanetLifecycleState.IrradiatedDeadWorld: return "IRRADIATED";
                case PlanetLifecycleState.TerraformedRecovery: return "TERRAFORMED";
                default: return "UNKNOWN";
            }
        }

        private static string Esi(int milli) =>
            (milli / 1000m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        private static Label NewLabel(string cls)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }
    }
}
