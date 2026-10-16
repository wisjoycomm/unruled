using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;
using Unruled.View.UI;
using Unruled.View.UI.Theme;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View
{
    /// <summary>
    /// Draws one star system on the single-layer board: orbits, lanes (STA highway solid, rim bypass dotted, storm
    /// shortcut zigzag), site glyphs (own, hostile, unclaimed, THE SYSTEM, core base, WHO lock, irradiated), fleets as
    /// tier chevrons, and NOAA storm discs the player's bracket can see; a jump gate is a ring on its site.
    /// The galaxy tab instead shows every star system: the contested cluster in the middle, homes around it, gate links
    /// (open solid, dormant dashed) and own fleet counts; clicking a system opens it. One mesh, rebuilt only when the sim tick or
    /// the board's screen area changes. Names are pooled UI Toolkit labels over the board view. Clicks select through
    /// <see cref="MainScreenView"/>; this class never writes simulation state.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MapBoardRenderer : MonoBehaviour
    {
        private const float FitMargin = 1.12f;
        private const float SitePickPx = 18f;
        private const float FleetPickPx = 20f;
        private const float FleetOffsetPx = 30f;
        private const float GalaxyRadius = 1000f;
        private const float ClusterPickPx = 40f;

        private static readonly string[] StormLevelNames = { "", "MINOR", "MODERATE", "STRONG", "SEVERE", "EXTREME" };
        private static readonly char[] StormScaleLetters = { ' ', 'R', 'S', 'G' };

        [SerializeField] private Camera _camera;

        private BoardMesh _mesh; // created in Bind: Unity objects can't be made in a MonoBehaviour's field initialisers
        private readonly List<Label> _labels = new List<Label>();
        private readonly List<float> _orbits = new List<float>();
        private readonly List<Vector2> _clusterPos = new List<Vector2>();
        private readonly List<int> _clusterFleets = new List<int>();
        private int _labelsUsed;

        private SimulationWorld _sim;
        private MainScreenView _screen;
        private VisualElement _boardView;
        private VisualElement _labelLayer;
        private int _playerId;
        private int _clusterId;
        private bool _galaxy;
        private int _contestedId = -1;
        private float _extent = 1f;
        private float _halfWidth = 1f, _halfHeight = 1f;
        private long _drawnTick = -1;
        private Rect _drawnRect;
        private EntityId _drawnSelection;

        public void Bind(SimulationWorld sim, int playerId, MainScreenView screen)
        {
            if (_camera == null)
                throw new MissingReferenceException("MapBoardRenderer needs its board camera.");
            _sim = sim;
            _playerId = playerId;
            _screen = screen;
            if (_mesh == null)
                _mesh = new BoardMesh();
            GetComponent<MeshFilter>().sharedMesh = _mesh.Mesh;

            _boardView = screen.BoardView;
            _boardView.pickingMode = PickingMode.Position;
            _boardView.RegisterCallback<PointerDownEvent>(OnPointerDown);
            if (_labelLayer == null)
            {
                _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                _labelLayer.style.position = Position.Absolute;
                _labelLayer.style.left = 0;
                _labelLayer.style.top = 0;
                _labelLayer.style.right = 0;
                _labelLayer.style.bottom = 0;
            }
            _boardView.Insert(0, _labelLayer);
            screen.SystemTab.clicked -= OnSystemTab;
            screen.SystemTab.clicked += OnSystemTab;
            screen.GalaxyTab.clicked -= ShowGalaxy;
            screen.GalaxyTab.clicked += ShowGalaxy;

            _clusterId = 0;
            if (HudQueries.TryFindHq(sim, playerId, out var hq) && sim.Sites.Has(sim.Players.Get(hq).CoreBase))
                _clusterId = sim.Sites.Get(sim.Players.Get(hq).CoreBase).ClusterId;
            ShowCluster(_clusterId);
        }

        /// <summary>Switches the board to another star system.</summary>
        public void ShowCluster(int clusterId)
        {
            _galaxy = false;
            _clusterId = clusterId;
            _screen.ShowBoardTab(false, clusterId);
            _orbits.Clear();
            float max = 1f;
            for (int i = 0; i < _sim.Sites.Count; i++)
            {
                ref var site = ref _sim.Sites.At(i);
                if (site.ClusterId != clusterId)
                    continue;
                float r = new Vector2(site.PosX, site.PosY).magnitude;
                _orbits.Add(r);
                if (r > max)
                    max = r;
            }
            _extent = max * FitMargin;
            _drawnTick = -1;
        }

        /// <summary>Switches the board to the galaxy overview.</summary>
        public void ShowGalaxy()
        {
            _galaxy = true;
            _contestedId = _sim.JumpGates.Count > 0 && _sim.Sites.Has(_sim.JumpGates.At(0).FarSite)
                ? _sim.Sites.Get(_sim.JumpGates.At(0).FarSite).ClusterId
                : -1;
            int clusters = 0;
            for (int i = 0; i < _sim.Sites.Count; i++)
                clusters = Mathf.Max(clusters, _sim.Sites.At(i).ClusterId + 1);
            _clusterPos.Clear();
            int ring = _contestedId >= 0 ? clusters - 1 : clusters;
            int slot = 0;
            for (int c = 0; c < clusters; c++)
            {
                if (c == _contestedId)
                {
                    _clusterPos.Add(Vector2.zero);
                    continue;
                }
                float angle = Mathf.PI * 0.5f - 2f * Mathf.PI * slot++ / Mathf.Max(1, ring);
                _clusterPos.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * GalaxyRadius);
            }
            _extent = GalaxyRadius * 1.35f;
            _drawnTick = -1;
            _screen.ShowBoardTab(true, _clusterId);
        }

        private void OnSystemTab() => ShowCluster(_clusterId);

        private void LateUpdate()
        {
            if (_sim == null)
                return;
            var rect = _boardView.worldBound;
            var panelRoot = _boardView.panel?.visualTree;
            if (panelRoot == null || rect.width <= 0f || rect.height <= 0f)
                return;
            var selection = _screen.Fleet.Fleet;
            if (_sim.Clock.CurrentTick == _drawnTick && rect == _drawnRect && selection == _drawnSelection)
                return;
            _drawnTick = _sim.Clock.CurrentTick;
            _drawnRect = rect;
            _drawnSelection = selection;

            var root = panelRoot.worldBound;
            _camera.rect = new Rect(rect.xMin / root.width, 1f - rect.yMax / root.height, rect.width / root.width,
                rect.height / root.height);
            float aspect = rect.width / rect.height;
            _halfHeight = aspect >= 1f ? _extent : _extent / aspect;
            _halfWidth = _halfHeight * aspect;
            _camera.orthographic = true;
            _camera.orthographicSize = _halfHeight;
            _mesh.UnitsPerPixel = 2f * _halfHeight / rect.height;

            Rebuild(selection);
        }

        private void Rebuild(EntityId selection)
        {
            long tick = _sim.Clock.CurrentTick;
            _mesh.Clear();
            _labelsUsed = 0;
            if (_galaxy)
            {
                DrawGalaxy();
                _mesh.Upload();
                for (int i = _labelsUsed; i < _labels.Count; i++)
                    _labels[i].style.display = DisplayStyle.None;
                return;
            }

            var orbitColor = (Color32)UnruledPalette.Raised;
            for (int i = 0; i < _orbits.Count; i++)
                _mesh.RingWorld(Vector2.zero, _orbits[i], 1f, orbitColor);

            DrawStorms(tick);
            _mesh.Disc(Vector2.zero, 14f, UnruledPalette.Sta);
            DrawLanes();
            DrawGates();
            DrawSites(tick);
            DrawFleets(tick, selection);

            _mesh.Upload();
            for (int i = _labelsUsed; i < _labels.Count; i++)
                _labels[i].style.display = DisplayStyle.None;
        }

        private void DrawStorms(long tick)
        {
            if (!_sim.Forecast.SeesCurrentStorms(_playerId))
                return;
            for (int i = 0; i < _sim.StormFronts.Count; i++)
            {
                ref var storm = ref _sim.StormFronts.At(i);
                if (storm.ClusterId != _clusterId || tick >= storm.EndTick)
                    continue;
                var colour = UnruledPalette.NoaaLevel(storm.Level);
                var centre = new Vector2(storm.CenterX, storm.CenterY);
                var fill = colour;
                fill.a = 0.16f;
                _mesh.DiscWorld(centre, storm.Radius, fill);
                _mesh.RingWorld(centre, storm.Radius, 1.5f, colour);
                long left = storm.EndTick - tick;
                string text = StormScaleLetters[(int)storm.Scale] + UiFormat.Number(storm.Level) + " · " +
                              StormLevelNames[storm.Level] + " · " + UiFormat.Duration(left);
                PlaceLabel(centre + new Vector2(0f, storm.Radius + 14f * _mesh.UnitsPerPixel), text, colour, true, true);
            }
        }

        private void DrawLanes()
        {
            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                ref var lane = ref _sim.Starlanes.At(i);
                if (!InCluster(lane.SiteA) || !InCluster(lane.SiteB))
                    continue;
                var a = Pos(lane.SiteA);
                var b = Pos(lane.SiteB);
                switch (lane.Tier)
                {
                    case StarlaneTier.StaHighway:
                        var gold = UnruledPalette.Sta;
                        gold.a = 0.7f;
                        _mesh.Line(a, b, 2.5f, gold);
                        break;
                    case StarlaneTier.OuterRimBypass:
                        _mesh.DashedLine(a, b, 2f, 2f, 6f, UnruledPalette.Unclaimed);
                        break;
                    default:
                        _mesh.Zigzag(a, b, 2f, 6f, 30f, UnruledPalette.Danger);
                        break;
                }
            }
        }

        private void DrawGates()
        {
            for (int i = 0; i < _sim.JumpGates.Count; i++)
            {
                ref var gate = ref _sim.JumpGates.At(i);
                EntityId here, there;
                if (InCluster(gate.HomeSite))
                {
                    here = gate.HomeSite;
                    there = gate.FarSite;
                }
                else if (gate.IsOpen && InCluster(gate.FarSite))
                {
                    here = gate.FarSite;
                    there = gate.HomeSite;
                }
                else
                    continue;
                var p = Pos(here);
                if (gate.IsOpen)
                    _mesh.Ring(p, 20f, 2.5f, UnruledPalette.Sta);
                else
                    _mesh.DashedRing(p, 20f, 2f, 4f, 4f, UnruledPalette.Unclaimed);
                string star = _sim.Sites.Has(there) ? UiFormat.StarName(_sim.Sites.Get(there).ClusterId) : "";
                PlaceLabel(p + new Vector2(0f, 34f * _mesh.UnitsPerPixel), (gate.IsOpen ? "GATE → " : "GATE DORMANT → ") + star,
                    gate.IsOpen ? UnruledPalette.Sta : UnruledPalette.InkSoft, true, false);
            }
        }

        private void DrawGalaxy()
        {
            _clusterFleets.Clear();
            for (int c = 0; c < _clusterPos.Count; c++)
                _clusterFleets.Add(0);
            for (int i = 0; i < _sim.Fleets.Count; i++)
            {
                if (_sim.Fleets.At(i).OwnerPlayerId != _playerId)
                    continue;
                ref var motion = ref _sim.FleetMotions.Get(_sim.Fleets.EntityAt(i));
                var at = motion.Lane.IsNull ? motion.Site : motion.FromSite;
                if (_sim.Sites.Has(at) && _sim.Sites.Get(at).ClusterId < _clusterFleets.Count)
                    _clusterFleets[_sim.Sites.Get(at).ClusterId]++;
            }

            for (int i = 0; i < _sim.JumpGates.Count; i++)
            {
                ref var gate = ref _sim.JumpGates.At(i);
                if (!_sim.Sites.Has(gate.HomeSite) || !_sim.Sites.Has(gate.FarSite))
                    continue;
                var a = _clusterPos[_sim.Sites.Get(gate.HomeSite).ClusterId];
                var b = _clusterPos[_sim.Sites.Get(gate.FarSite).ClusterId];
                if (gate.IsOpen)
                    _mesh.Line(a, b, 3f, UnruledPalette.Sta);
                else
                    _mesh.DashedLine(a, b, 2f, 4f, 8f, UnruledPalette.Unclaimed);
            }

            for (int c = 0; c < _clusterPos.Count; c++)
            {
                var p = _clusterPos[c];
                int owner = ClusterCoreOwner(c);
                bool contested = c == _contestedId;
                Color colour = contested ? UnruledPalette.Danger : owner != 0 ? SideColour(owner) : UnruledPalette.Unclaimed;
                if (c == _clusterId)
                    _mesh.SquareOutline(p, 64f, 2f, UnruledPalette.Ink);
                _mesh.Disc(p, 22f, colour);
                _mesh.Disc(p, 18f, UnruledPalette.Void);
                _mesh.Disc(p, 8f, colour);
                string name = UiFormat.StarName(c) + (contested ? " · CONTESTED" : owner == _playerId ? " · HOME" : "");
                bool strong = contested || owner == _playerId;
                PlaceLabel(p - new Vector2(0f, 40f * _mesh.UnitsPerPixel), name, strong ? UnruledPalette.Ink : UnruledPalette.InkSoft,
                    true, true);
                if (_clusterFleets[c] > 0)
                    PlaceLabel(p + new Vector2(0f, 40f * _mesh.UnitsPerPixel),
                        UiFormat.Number(_clusterFleets[c]) + (_clusterFleets[c] == 1 ? " FLEET" : " FLEETS"), UnruledPalette.You,
                        true, false);
            }
        }

        private int ClusterCoreOwner(int clusterId)
        {
            for (int i = 0; i < _sim.Players.Count; i++)
            {
                var core = _sim.Players.At(i).CoreBase;
                if (_sim.Sites.Has(core) && _sim.Sites.Get(core).ClusterId == clusterId)
                    return _sim.Players.At(i).PlayerId;
            }
            return 0;
        }

        private void DrawSites(long tick)
        {
            for (int i = 0; i < _sim.Sites.Count; i++)
            {
                ref var site = ref _sim.Sites.At(i);
                if (site.ClusterId != _clusterId)
                    continue;
                var entity = _sim.Sites.EntityAt(i);
                var p = new Vector2(site.PosX, site.PosY);
                int coreOwner = CoreOwner(entity);
                if (coreOwner != 0)
                    _mesh.SquareOutline(p, 30f, 2f, SideColour(coreOwner));
                if (_sim.Quarantines.Has(entity) && _sim.Quarantines.Get(entity).IsExportLocked(tick))
                    _mesh.DashedRing(p, 16f, 2f, 3f, 3f, UnruledPalette.Who);

                Color fill, stroke;
                bool irradiated = site.IsIrradiated(tick);
                if (irradiated)
                {
                    fill = UnruledPalette.IrradiatedGround;
                    stroke = UnruledPalette.Iaea;
                }
                else if (site.OwnerPlayerId == 0)
                {
                    fill = UnruledPalette.Void;
                    stroke = UnruledPalette.Unclaimed;
                }
                else if (site.OwnerPlayerId < 0)
                {
                    fill = UnruledPalette.Void;
                    stroke = AgencyColour(site.OwnerPlayerId);
                }
                else
                {
                    fill = SideColour(site.OwnerPlayerId);
                    stroke = fill;
                }
                _mesh.Disc(p, 10f, stroke);
                _mesh.Disc(p, 8f, fill);
                if (irradiated)
                    _mesh.Line(p + new Vector2(-6f, -6f) * _mesh.UnitsPerPixel, p + new Vector2(6f, 6f) * _mesh.UnitsPerPixel, 2f,
                        UnruledPalette.Iaea);
                PlaceLabel(p - new Vector2(0f, 26f * _mesh.UnitsPerPixel), UiFormat.SiteName(site), UnruledPalette.InkSoft, true, false);
            }
        }

        private void DrawFleets(long tick, EntityId selection)
        {
            for (int i = 0; i < _sim.Fleets.Count; i++)
            {
                ref var unit = ref _sim.Fleets.At(i);
                var fleet = _sim.Fleets.EntityAt(i);
                if (!TryFleetPosition(fleet, out var p) || !IsVisible(fleet, in unit, tick))
                    continue;
                bool own = unit.OwnerPlayerId == _playerId;
                var colour = own ? UnruledPalette.You : unit.OwnerPlayerId < 0 ? AgencyColour(unit.OwnerPlayerId) : UnruledPalette.Hostile;
                if (fleet == selection)
                {
                    _mesh.SquareFill(p, 40f, UnruledPalette.Raised);
                    _mesh.SquareOutline(p, 40f, 2f, UnruledPalette.Ink);
                }
                if (unit.OwnerPlayerId == SystemAgencies.OwnerId(SystemAgency.Iaea))
                    _mesh.DashedRing(p, 24f, 2f, 4f, 3f, UnruledPalette.Iaea);
                _mesh.Chevrons(p, Mathf.Clamp(unit.TechTier, 1, 5), own, colour);

                string text;
                if (own)
                {
                    text = UiFormat.FleetName(HudQueries.FleetOrdinal(_sim, fleet));
                    if (fleet == selection)
                        text += " · " + UiFormat.Number(_sim.FleetCombat.Get(fleet).TemperatureMilli / 1000) + " °C";
                }
                else if (unit.OwnerPlayerId < 0)
                    text = AgencyName(unit.OwnerPlayerId) + " · T" + UiFormat.Number(unit.TechTier);
                else
                    text = "HOSTILE · T" + UiFormat.Number(unit.TechTier);
                PlaceLabel(p + new Vector2(26f * _mesh.UnitsPerPixel, 0f), text, own ? UnruledPalette.Ink : colour, false, false);
            }
        }

        private bool TryFleetPosition(EntityId fleet, out Vector2 position)
        {
            position = default;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            if (!motion.Lane.IsNull && _sim.Starlanes.Has(motion.Lane))
            {
                ref var lane = ref _sim.Starlanes.Get(motion.Lane);
                var from = motion.FromSite;
                if (!InCluster(from) || !InCluster(lane.OtherEnd(from)))
                    return false;
                float t = lane.Length > 0 ? Mathf.Clamp01(motion.TravelledMilli / (lane.Length * 1000f)) : 0f;
                position = Vector2.Lerp(Pos(from), Pos(lane.OtherEnd(from)), t);
                return true;
            }
            if (!InCluster(motion.Site))
                return false;
            // Docked fleets sit beside their site, side by side.
            int slot = 0;
            for (int i = 0; i < _sim.FleetMotions.Count; i++)
            {
                var other = _sim.FleetMotions.EntityAt(i);
                if (other.Index >= fleet.Index)
                    continue;
                ref var m = ref _sim.FleetMotions.At(i);
                if (m.Lane.IsNull && m.Site == motion.Site)
                    slot++;
            }
            position = Pos(motion.Site) + new Vector2(FleetOffsetPx * (slot + 1) * _mesh.UnitsPerPixel, 0f);
            return true;
        }

        // Own fleets always; others when a focused scan revealed them or they sit at one of the player's sites.
        private bool IsVisible(EntityId fleet, in FleetUnit unit, long tick)
        {
            if (unit.OwnerPlayerId == _playerId)
                return true;
            if (_sim.FleetSensors.Has(fleet) && tick < _sim.FleetSensors.Get(fleet).RevealedUntilTick)
                return true;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            return motion.Lane.IsNull && _sim.Sites.Has(motion.Site) && _sim.Sites.Get(motion.Site).OwnerPlayerId == _playerId;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_sim == null)
                return;
            var local = (Vector2)evt.localPosition;
            var size = _boardView.layout.size;
            var world = new Vector2((local.x / size.x * 2f - 1f) * _halfWidth, (1f - local.y / size.y * 2f) * _halfHeight);
            long tick = _sim.Clock.CurrentTick;
            if (_galaxy)
            {
                float pick = ClusterPickPx * _mesh.UnitsPerPixel;
                for (int c = 0; c < _clusterPos.Count; c++)
                {
                    if (Vector2.Distance(_clusterPos[c], world) > pick)
                        continue;
                    ShowCluster(c);
                    return;
                }
                return;
            }

            float best = FleetPickPx * _mesh.UnitsPerPixel;
            var picked = default(EntityId);
            for (int i = 0; i < _sim.Fleets.Count; i++)
            {
                var fleet = _sim.Fleets.EntityAt(i);
                if (_sim.Fleets.At(i).OwnerPlayerId != _playerId || !TryFleetPosition(fleet, out var p))
                    continue;
                float d = Vector2.Distance(p, world);
                if (d < best)
                {
                    best = d;
                    picked = fleet;
                }
            }
            if (!picked.IsNull)
            {
                _screen.SelectFleet(picked);
                return;
            }

            best = SitePickPx * _mesh.UnitsPerPixel;
            for (int i = 0; i < _sim.Sites.Count; i++)
            {
                ref var site = ref _sim.Sites.At(i);
                if (site.ClusterId != _clusterId)
                    continue;
                float d = Vector2.Distance(new Vector2(site.PosX, site.PosY), world);
                if (d < best)
                {
                    best = d;
                    picked = _sim.Sites.EntityAt(i);
                }
            }
            _screen.SelectTarget(picked);
            _drawnTick = -1;
        }

        private void PlaceLabel(Vector2 world, string text, Color colour, bool centred, bool strong)
        {
            Label label;
            if (_labelsUsed < _labels.Count)
            {
                label = _labels[_labelsUsed];
            }
            else
            {
                label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("board-label");
                label.style.position = Position.Absolute;
                _labels.Add(label);
                _labelLayer.Add(label);
            }
            _labelsUsed++;
            var size = _boardView.layout.size;
            float x = (world.x / _halfWidth + 1f) * 0.5f * size.x;
            float y = (1f - world.y / _halfHeight) * 0.5f * size.y;
            label.style.display = DisplayStyle.Flex;
            label.style.left = x;
            label.style.top = y;
            label.style.translate = new Translate(Length.Percent(centred ? -50f : 0f), Length.Percent(-50f));
            label.style.color = colour;
            label.EnableInClassList("board-label--strong", strong);
            UiFormat.Set(label, text);
        }

        private bool InCluster(EntityId site) => _sim.Sites.Has(site) && _sim.Sites.Get(site).ClusterId == _clusterId;

        private Vector2 Pos(EntityId site)
        {
            ref var info = ref _sim.Sites.Get(site);
            return new Vector2(info.PosX, info.PosY);
        }

        private int CoreOwner(EntityId site)
        {
            for (int i = 0; i < _sim.Players.Count; i++)
                if (_sim.Players.At(i).CoreBase == site)
                    return _sim.Players.At(i).PlayerId;
            return 0;
        }

        private Color SideColour(int ownerId) =>
            ownerId == _playerId ? UnruledPalette.You : ownerId < 0 ? AgencyColour(ownerId) : UnruledPalette.Hostile;

        private static Color AgencyColour(int ownerId)
        {
            switch ((SystemAgency)(-ownerId))
            {
                case SystemAgency.Sta: return UnruledPalette.Sta;
                case SystemAgency.Wto: return UnruledPalette.Wto;
                case SystemAgency.Who: return UnruledPalette.Who;
                case SystemAgency.Iaea: return UnruledPalette.Iaea;
                default: return UnruledPalette.Noaa;
            }
        }

        private static string AgencyName(int ownerId)
        {
            switch ((SystemAgency)(-ownerId))
            {
                case SystemAgency.Sta: return "STA";
                case SystemAgency.Wto: return "WTO";
                case SystemAgency.Who: return "WHO";
                case SystemAgency.Iaea: return "IAEA";
                default: return "NOAA";
            }
        }

        private void OnDestroy()
        {
            if (_boardView != null)
                _boardView.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            if (_mesh != null)
                Destroy(_mesh.Mesh);
        }
    }
}
