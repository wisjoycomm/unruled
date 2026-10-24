using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Galaxy;
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
    /// shortcut zigzag; R3+ radio blackout overlaid; the player's focused scans as a sweep on the lane), site glyphs
    /// (own, hostile, unclaimed, THE SYSTEM, core base, WHO lock, irradiated, terraformed; rigs, refineries, relays,
    /// the Citadel's shield arc), fleets as tier chevrons with state marks (engaged, mothballed, warming up,
    /// unsupplied, guns locked, RWR alarm, WTO contract convoy), the selected fleet's route and convoy run, and NOAA
    /// storm discs the player's bracket can see; a jump gate or inter-system lane is a ring on its site.
    /// The board is one continuous zoom over the galaxy catalog (wheel or pinch, drag to pan), drawn at the level of
    /// detail the zoom calls for: the whole galaxy as star-density dust with the players' systems as dots; a star region
    /// with every catalog star in view, hyperlanes when close, and the charted systems with their links, owners and own
    /// fleets; zoomed onto a charted system, that system as above; closer still onto a planet, its preview card. Clicking
    /// a charted system in a region previews it, a second click flies into it. The SYSTEM and GALAXY tabs fly to the
    /// last system and to the whole galaxy. One mesh, rebuilt only when the sim tick, the camera or the board's screen
    /// area changes. Names are pooled UI Toolkit labels over the board view. Clicks select through
    /// <see cref="MainScreenView"/>; this class never writes simulation state.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MapBoardRenderer : MonoBehaviour
    {
        private const float FitMargin = 1.12f;
        private const float SitePickPx = 18f;
        private const float FleetPickPx = 20f;
        private const float FleetOffsetPx = 30f;
        private const float ClusterPickPx = 40f;

        // Zoom levels, in galaxy units of the camera's fit radius (half the shorter side of the board).
        private const float SystemFootprint = 120f; // a charted system's outer orbit spans this on the galaxy map
        private const float EnterSystemHalf = SystemFootprint * 1.6f; // closer than this, the nearest system opens
        private const float PlanetZoom = 0.3f; // a system zoomed under this share of its extent previews a planet
        private const float RegionMaxHalf = 12_000f; // further out than this, stars give way to density dust
        private const float HyperlaneMaxHalf = 2_500f;
        private const int RegionStarLimit = 6_000;
        private const int HyperlaneStarLimit = 400;
        private const int DustCellsAcross = 90;
        private const float WheelStep = 1.12f;
        private const float DragThresholdPx = 6f;

        private static readonly string[] StormLevelNames = { "", "MINOR", "MODERATE", "STRONG", "SEVERE", "EXTREME" };
        private static readonly char[] StormScaleLetters = { ' ', 'R', 'S', 'G' };

        [SerializeField] private Camera _camera;

        private BoardMesh _mesh; // created in Bind: Unity objects can't be made in a MonoBehaviour's field initialisers
        private readonly List<Label> _labels = new List<Label>();
        private readonly List<VisualElement> _glyphs = new List<VisualElement>();
        private Texture2D _coreGlyph;
        private Texture2D _citadelGlyph;
        private Texture2D _starHome, _starContested, _starFrontier, _beacon, _hazard;
        private UiArt _art;
        private int _glyphsUsed;
        private readonly List<float> _orbits = new List<float>();
        private readonly List<Vector2Int> _systemPos = new List<Vector2Int>(); // per cluster, galaxy units
        private readonly List<float> _systemExtent = new List<float>(); // per cluster, outer orbit radius x margin
        private readonly List<int> _clusterFleets = new List<int>();
        private readonly Dictionary<int, int> _portalStack = new Dictionary<int, int>();
        private readonly List<StarRecord> _stars = new List<StarRecord>(RegionStarLimit);
        private readonly List<Vector2Int> _hyperlanes = new List<Vector2Int>(); // pairs of galaxy positions
        private readonly List<StarId> _neighbours = new List<StarId>();
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
        private int _labelsUsed;

        private SimulationWorld _sim;
        private MainScreenView _screen;
        private VisualElement _boardView;
        private VisualElement _labelLayer;
        private VisualElement _backdropLayer;
        private VisualElement _sectorBadge;
        private Label _sectorBadgeLabel;
        private int _playerId;
        private int _clusterId; // the system the SYSTEM tab returns to: the last one shown
        private int _systemCluster = -1; // the system on the board, -1 when the board shows a region or the galaxy
        private int _shownLevel = -1;
        private float _extent = 1f; // the shown system's extent, local units
        private float _systemScale = 1f; // galaxy units per local unit of the shown system
        private double _camX, _camY; // camera centre, galaxy units
        private float _camHalf = 1f; // camera fit radius, galaxy units
        private bool _camDirty = true;
        private Vector2 _viewCentre; // centre of the drawn view, in the units the mesh is built in
        private float _halfWidth = 1f, _halfHeight = 1f;
        private int _systemCount = -1;
        private bool _starsValid;
        private bool _dragging;
        private Vector2 _pressPos;
        private float _pinchDistance;
        private int _previewCluster = -1;
        private EntityId _planetPreview;
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
            _boardView.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _boardView.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _boardView.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            _boardView.RegisterCallback<WheelEvent>(OnWheel);
            if (_labelLayer == null)
            {
                _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                _labelLayer.style.position = Position.Absolute;
                _labelLayer.style.left = 0;
                _labelLayer.style.top = 0;
                _labelLayer.style.right = 0;
                _labelLayer.style.bottom = 0;
                _labelLayer.style.overflow = Overflow.Hidden; // zoomed in, names past the board's edge stay off the panels
            }
            _boardView.Insert(0, _labelLayer);
            if (_coreGlyph == null)
            {
                _coreGlyph = Resources.Load<Texture2D>("UI/node_core_base");
                _citadelGlyph = Resources.Load<Texture2D>("UI/node_citadel");
                _starHome = Resources.Load<Texture2D>("UI/star_home");
                _starContested = Resources.Load<Texture2D>("UI/star_contested");
                _starFrontier = Resources.Load<Texture2D>("UI/star_frontier");
                _beacon = Resources.Load<Texture2D>("UI/beacon_toll");
                _hazard = Resources.Load<Texture2D>("UI/marker_hazard");
                _art = new UiArt();
            }
            EnsureTelemetryOverlays();
            screen.SystemTab.clicked -= OnSystemTab;
            screen.SystemTab.clicked += OnSystemTab;
            screen.GalaxyTab.clicked -= ShowGalaxy;
            screen.GalaxyTab.clicked += ShowGalaxy;

            _clusterId = 0;
            if (HudQueries.TryFindHq(sim, playerId, out var hq) && sim.Sites.Has(sim.Players.Get(hq).CoreBase))
                _clusterId = sim.Sites.Get(sim.Players.Get(hq).CoreBase).ClusterId;
            _systemCount = -1;
            ShowCluster(_clusterId);
        }

        /// <summary>Flies the camera onto a charted star system, framing its outer orbit.</summary>
        public void ShowCluster(int clusterId)
        {
            EnsureSystems();
            if (clusterId < 0 || clusterId >= _systemPos.Count)
                return;
            _clusterId = clusterId;
            _camX = _systemPos[clusterId].x;
            _camY = _systemPos[clusterId].y;
            _camHalf = SystemFootprint;
            _camDirty = true;
        }

        /// <summary>Pulls the camera out to the whole galaxy.</summary>
        public void ShowGalaxy()
        {
            _camX = 0;
            _camY = 0;
            _camHalf = _sim.Balance.Galaxy.RadiusUnits * 1.05f;
            _camDirty = true;
        }

        private void OnSystemTab() => ShowCluster(_clusterId);

        // Where each charted system sits on the galaxy map, and how big its orbits are: rebuilt when a system is charted.
        private void EnsureSystems()
        {
            int clusters = _sim.Galaxy.NextClusterId();
            if (clusters == _systemCount)
                return;
            _systemCount = clusters;
            _systemPos.Clear();
            _systemExtent.Clear();
            for (int c = 0; c < clusters; c++)
            {
                var system = _sim.Galaxy.SystemOf(c);
                _systemPos.Add(!system.IsNull && _sim.StarSystems.Has(system)
                    ? new Vector2Int(_sim.StarSystems.Get(system).X, _sim.StarSystems.Get(system).Y)
                    : Vector2Int.zero);
                _systemExtent.Add(1f);
            }
            for (int i = 0; i < _sim.Sites.Count; i++)
            {
                ref var site = ref _sim.Sites.At(i);
                if (site.ClusterId < 0 || site.ClusterId >= clusters)
                    continue;
                float r = new Vector2(site.PosX, site.PosY).magnitude * FitMargin;
                if (r > _systemExtent[site.ClusterId])
                    _systemExtent[site.ClusterId] = r;
            }
        }

        // The zoom level the camera calls for: 0 galaxy dust, 1 star region, 2 a system, 3 a planet of it.
        private int Level(out int cluster)
        {
            cluster = -1;
            if (_camHalf > RegionMaxHalf)
                return 0;
            if (_camHalf > EnterSystemHalf)
                return 1;
            double best = double.MaxValue;
            for (int c = 0; c < _systemPos.Count; c++)
            {
                double dx = _systemPos[c].x - _camX, dy = _systemPos[c].y - _camY;
                double d = dx * dx + dy * dy;
                if (d < best)
                {
                    best = d;
                    cluster = c;
                }
            }
            if (cluster < 0 || best > (double)EnterSystemHalf * EnterSystemHalf)
            {
                cluster = -1;
                return 1;
            }
            return _camHalf < SystemFootprint * PlanetZoom ? 3 : 2;
        }

        // Sets what the drawing code reads for the current level: the shown system (local units), or the galaxy view.
        private void ApplyLevel(int level, int cluster)
        {
            bool system = level >= 2;
            if (system && cluster != _systemCluster)
            {
                _systemCluster = cluster;
                _clusterId = cluster;
                _orbits.Clear();
                for (int i = 0; i < _sim.Sites.Count; i++)
                {
                    ref var site = ref _sim.Sites.At(i);
                    if (site.ClusterId == cluster)
                        _orbits.Add(new Vector2(site.PosX, site.PosY).magnitude);
                }
                _extent = _systemExtent[cluster];
                _systemScale = SystemFootprint / _extent;
                _screen.Preview.Hide();
                _previewCluster = -1;
            }
            else if (!system)
                _systemCluster = -1;
            if (level == _shownLevel && (!system || cluster == _clusterId))
                return;
            if (level < 2 && _shownLevel >= 2 || level == 2 && _shownLevel == 3)
            {
                _screen.Preview.Hide();
                _planetPreview = default;
            }
            _shownLevel = level;
            _backdropLayer.style.display = system ? DisplayStyle.Flex : DisplayStyle.None;
            _screen.ShowBoardTab(!system, _clusterId);
            UpdateSectorBadge(!system, _clusterId);
        }


        private void LateUpdate()
        {
            if (_sim == null)
                return;
            var rect = _boardView.worldBound;
            var panelRoot = _boardView.panel?.visualTree;
            if (panelRoot == null || rect.width <= 0f || rect.height <= 0f)
                return;
            var selection = _screen.Fleet.Fleet;
            EnsureSystems();
            if (_sim.Clock.CurrentTick == _drawnTick && rect == _drawnRect && selection == _drawnSelection && !_camDirty)
                return;
            if (_camDirty)
                _starsValid = false;
            _drawnTick = _sim.Clock.CurrentTick;
            _drawnRect = rect;
            _drawnSelection = selection;
            _camDirty = false;

            int level = Level(out int cluster);
            ApplyLevel(level, cluster);

            // The mesh is built around the view centre: in the shown system's local units, or in galaxy units relative
            // to the camera (so huge galaxy coordinates never lose float precision).
            float half;
            if (level >= 2)
            {
                _viewCentre = new Vector2((float)((_camX - _systemPos[cluster].x) / _systemScale),
                    (float)((_camY - _systemPos[cluster].y) / _systemScale));
                half = _camHalf / _systemScale;
            }
            else
            {
                _viewCentre = Vector2.zero;
                half = _camHalf;
            }
            var root = panelRoot.worldBound;
            _camera.rect = new Rect(rect.xMin / root.width, 1f - rect.yMax / root.height, rect.width / root.width,
                rect.height / root.height);
            float aspect = rect.width / rect.height;
            _halfHeight = aspect >= 1f ? half : half / aspect;
            _halfWidth = _halfHeight * aspect;
            _camera.orthographic = true;
            _camera.orthographicSize = _halfHeight;
            _camera.transform.localPosition = new Vector3(_viewCentre.x, _viewCentre.y, -10f);
            _mesh.UnitsPerPixel = 2f * _halfHeight / rect.height;

            Rebuild(selection, level);
            if (level == 3)
                PreviewNearestPlanet();
        }

        // Zoomed in on a planet: the preview card follows the planet nearest the view centre.
        private void PreviewNearestPlanet()
        {
            var nearest = default(EntityId);
            float best = float.MaxValue;
            for (int i = 0; i < _sim.Sites.Count; i++)
            {
                ref var site = ref _sim.Sites.At(i);
                if (site.ClusterId != _systemCluster)
                    continue;
                float d = (new Vector2(site.PosX, site.PosY) - _viewCentre).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = _sim.Sites.EntityAt(i);
                }
            }
            if (nearest.IsNull || nearest == _planetPreview)
                return;
            _planetPreview = nearest;
            _screen.Preview.ShowSite(nearest);
        }

        private void Rebuild(EntityId selection, int level)
        {
            long tick = _sim.Clock.CurrentTick;
            _mesh.Clear();
            _labelsUsed = 0;
            _glyphsUsed = 0;
            if (level < 2)
            {
                if (level == 0)
                    DrawDust();
                else
                    DrawRegion();
                DrawGalaxy(level);
                _mesh.Upload();
                for (int i = _labelsUsed; i < _labels.Count; i++)
                    _labels[i].style.display = DisplayStyle.None;
                for (int i = _glyphsUsed; i < _glyphs.Count; i++)
                    _glyphs[i].style.display = DisplayStyle.None;
                return;
            }

            var orbitColor = (Color32)UnruledPalette.Raised;
            for (int i = 0; i < _orbits.Count; i++)
                _mesh.RingWorld(Vector2.zero, _orbits[i], 1f, orbitColor);

            DrawStorms(tick);
            var star = StarTexture(_clusterId);
            if (star != null)
                PlaceGlyph(Vector2.zero, 56f, star, Color.white);
            else
                _mesh.Disc(Vector2.zero, 14f, UnruledPalette.Sta);
            DrawLanes();
            DrawTraffic(tick);
            DrawBlackouts();
            DrawScans(tick);
            DrawGates();
            DrawSelectedRoute(selection);
            DrawSites(tick);
            DrawCitadels(tick);
            DrawFleets(tick, selection);

            _mesh.Upload();
            for (int i = _labelsUsed; i < _labels.Count; i++)
                _labels[i].style.display = DisplayStyle.None;
            for (int i = _glyphsUsed; i < _glyphs.Count; i++)
                _glyphs[i].style.display = DisplayStyle.None;
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
                        // Twin rails with an STA toll beacon at the midpoint.
                        var gold = UnruledPalette.Sta;
                        gold.a = 0.7f;
                        var side = Perpendicular(a, b) * (2.5f * _mesh.UnitsPerPixel);
                        _mesh.Line(a + side, b + side, 1.5f, gold);
                        _mesh.Line(a - side, b - side, 1.5f, gold);
                        if (_beacon != null)
                            PlaceGlyph((a + b) * 0.5f, 18f, _beacon, Color.white);
                        break;
                    case StarlaneTier.OuterRimBypass:
                        _mesh.DashedLine(a, b, 2f, 2f, 6f, UnruledPalette.Unclaimed);
                        if (_hazard != null)
                            PlaceGlyph((a + b) * 0.5f, 18f, _hazard, Color.white);
                        break;
                    default:
                        _mesh.Zigzag(a, b, 2f, 6f, 30f, UnruledPalette.Danger);
                        break;
                }
            }
        }

        // From a gate or warp point, a route runs out past the edge of the system towards the far system, with
        // arrowheads; its direction is the far system's bearing on the galaxy chart.
        private void DrawExitVector(Vector2 from, EntityId there, Color colour)
        {
            if (!_sim.Sites.Has(there))
                return;
            int far = _sim.Sites.Get(there).ClusterId;
            Vector2 dir;
            if (_systemCluster >= 0 && far >= 0 && far < _systemPos.Count && _systemPos[far] != _systemPos[_systemCluster])
                dir = ((Vector2)(_systemPos[far] - _systemPos[_systemCluster])).normalized;
            else
                dir = from.sqrMagnitude > 1f ? from.normalized : Vector2.up;
            float u = _mesh.UnitsPerPixel;
            var start = from + dir * (26f * u);
            var end = from + dir * (_extent * 0.55f);
            var faint = colour;
            faint.a = 0.5f;
            _mesh.DashedLine(start, end, 1.5f, 10f, 6f, faint);
            var side = new Vector2(-dir.y, dir.x);
            for (int k = 0; k < 3; k++)
            {
                var tip = Vector2.Lerp(start, end, 0.35f + k * 0.25f);
                _mesh.Line(tip - dir * (7f * u) + side * (6f * u), tip, 1.5f, colour);
                _mesh.Line(tip - dir * (7f * u) - side * (6f * u), tip, 1.5f, colour);
            }
        }

        private static Vector2 Perpendicular(Vector2 a, Vector2 b)
        {
            var d = b - a;
            return d.sqrMagnitude > 1e-6f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;
        }

        // Moving traffic: a short trail of fading dots behind every visible fleet in transit, in its side colour.
        private void DrawTraffic(long tick)
        {
            float u = _mesh.UnitsPerPixel;
            for (int i = 0; i < _sim.Fleets.Count; i++)
            {
                ref var unit = ref _sim.Fleets.At(i);
                var fleet = _sim.Fleets.EntityAt(i);
                if (!TryFleetHeading(fleet, out var p, out var dir) || !IsVisible(fleet, in unit, tick))
                    continue;
                var colour = FleetColour(in unit);
                for (int k = 1; k <= 4; k++)
                {
                    var dot = colour;
                    dot.a = 0.55f - k * 0.11f;
                    _mesh.Disc(p - dir * ((10f + k * 7f) * u), 2.2f, dot);
                }
            }
        }

        private Color FleetColour(in FleetUnit unit) =>
            unit.OwnerPlayerId == _playerId ? UnruledPalette.You
            : unit.OwnerPlayerId < 0 ? AgencyColour(unit.OwnerPlayerId)
            : UnruledPalette.Hostile;

        // A fleet on a lane inside this system: its position and the unit direction it travels in.
        private bool TryFleetHeading(EntityId fleet, out Vector2 position, out Vector2 direction)
        {
            direction = Vector2.up;
            if (!TryFleetPosition(fleet, out position))
                return false;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            if (motion.Lane.IsNull || !_sim.Starlanes.Has(motion.Lane))
                return false;
            var to = _sim.Starlanes.Get(motion.Lane).OtherEnd(motion.FromSite);
            if (!InCluster(motion.FromSite) || !InCluster(to))
                return false;
            var d = Pos(to) - Pos(motion.FromSite);
            if (d.sqrMagnitude < 1e-6f)
                return false;
            direction = d.normalized;
            return true;
        }

        private Texture2D StarTexture(int clusterId)
        {
            if (IsHub(clusterId))
                return _starContested;
            return ClusterCoreOwner(clusterId) != 0 ? _starHome : _starFrontier;
        }

        // Token kind by role, drawn at the fleet's tech tier: 0 warfleet, 1 convoy, 2 SYSTEM patrol, 3 IAEA inspector.
        private Texture2D FleetToken(EntityId fleet, in FleetUnit unit)
        {
            int kind = unit.OwnerPlayerId == SystemAgencies.OwnerId(SystemAgency.Iaea) ? 3
                : unit.OwnerPlayerId < 0 ? 2
                : _sim.ConvoyRoutes.Has(fleet) || _sim.ContractCargo.Has(fleet) ? 1
                : 0;
            return _art.Token(kind, unit.TechTier);
        }

        // R3+ radio blackout: orders across the lane are refused, so it is marked over the lane in the NOAA colour.
        private void DrawBlackouts()
        {
            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                var id = _sim.Starlanes.EntityAt(i);
                if (!_sim.LaneWeather.Has(id))
                    continue;
                byte level = _sim.LaneWeather.Get(id).RadioBlackoutLevel;
                ref var lane = ref _sim.Starlanes.At(i);
                if (level < 3 || !InCluster(lane.SiteA) || !InCluster(lane.SiteB))
                    continue;
                var colour = UnruledPalette.NoaaLevel(level);
                colour.a = 0.55f;
                _mesh.DashedLine(Pos(lane.SiteA), Pos(lane.SiteB), 7f, 3f, 5f, colour);
                PlaceLabel((Pos(lane.SiteA) + Pos(lane.SiteB)) * 0.5f, "R" + UiFormat.Number(level) + " BLACKOUT",
                    UnruledPalette.NoaaLevel(level), true, false);
            }
        }

        // The player's focused scans: a bright band growing along the lane as the scan integrates.
        private void DrawScans(long tick)
        {
            for (int i = 0; i < _sim.RadarScans.Count; i++)
            {
                ref var scan = ref _sim.RadarScans.At(i);
                if (scan.OwnerPlayerId != _playerId || !_sim.Starlanes.Has(scan.Lane))
                    continue;
                ref var lane = ref _sim.Starlanes.Get(scan.Lane);
                if (!InCluster(lane.SiteA) || !InCluster(lane.SiteB))
                    continue;
                var a = Pos(lane.SiteA);
                var b = Pos(lane.SiteB);
                float t = scan.IntegrationTicks > 0 ? Mathf.Clamp01((tick - scan.StartTick) / (float)scan.IntegrationTicks) : 1f;
                var glow = UnruledPalette.You;
                glow.a = 0.25f;
                _mesh.Line(a, b, 9f, glow);
                _mesh.Line(a, Vector2.Lerp(a, b, t), 3f, UnruledPalette.You);
                PlaceLabel((a + b) * 0.5f + new Vector2(0f, 14f * _mesh.UnitsPerPixel),
                    "SCAN " + UiFormat.Number((long)(t * 100f)) + "%", UnruledPalette.You, true, true);
            }
        }

        // The selected own fleet's remaining route (lanes still ahead) and its convoy run, if it has one.
        private void DrawSelectedRoute(EntityId fleet)
        {
            if (!_sim.Fleets.Has(fleet) || _sim.Fleets.Get(fleet).OwnerPlayerId != _playerId)
                return;
            var ink = UnruledPalette.You;
            ink.a = 0.8f;
            if (_sim.FleetRoutes.Has(fleet))
            {
                ref var route = ref _sim.FleetRoutes.Get(fleet);
                ref var motion = ref _sim.FleetMotions.Get(fleet);
                var at = motion.Lane.IsNull ? motion.Site : _sim.Starlanes.Get(motion.Lane).OtherEnd(motion.FromSite);
                if (!motion.Lane.IsNull && TryFleetPosition(fleet, out var p) && InCluster(at))
                    _mesh.DashedLine(p, Pos(at), 3f, 6f, 4f, ink);
                for (int i = route.Next; i < route.Count && _sim.Sites.Has(at); i++)
                {
                    var laneId = route.LaneAt(i);
                    if (!_sim.Starlanes.Has(laneId))
                        break;
                    var next = _sim.Starlanes.Get(laneId).OtherEnd(at);
                    if (InCluster(at) && InCluster(next))
                        _mesh.DashedLine(Pos(at), Pos(next), 3f, 6f, 4f, ink);
                    at = next;
                }
                if (InCluster(at) && route.Next < route.Count)
                    _mesh.Ring(Pos(at), 15f, 2f, UnruledPalette.You);
            }
            if (_sim.ConvoyRoutes.Has(fleet))
            {
                ref var convoy = ref _sim.ConvoyRoutes.Get(fleet);
                if (InCluster(convoy.Hub) && InCluster(convoy.Outpost))
                {
                    var teal = UnruledPalette.Wto;
                    teal.a = 0.7f;
                    _mesh.DashedLine(Pos(convoy.Hub), Pos(convoy.Outpost), 2f, 10f, 6f, teal);
                    PlaceLabel((Pos(convoy.Hub) + Pos(convoy.Outpost)) * 0.5f - new Vector2(0f, 12f * _mesh.UnitsPerPixel),
                        "CONVOY RUN", UnruledPalette.Wto, true, false);
                }
            }
        }

        private void DrawGates()
        {
            _portalStack.Clear();
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
                DrawExitVector(p, there, gate.IsOpen ? UnruledPalette.Sta : UnruledPalette.Unclaimed);
                string star = _sim.Sites.Has(there) ? UiFormat.StarName(_sim.Sites.Get(there).ClusterId) : "";
                int stack = _portalStack.TryGetValue(here.Index, out int n) ? n : 0;
                _portalStack[here.Index] = stack + 1;
                PlaceLabel(p + new Vector2(0f, (34f + 16f * stack) * _mesh.UnitsPerPixel),
                    (gate.IsOpen ? "GATE → " : "GATE DORMANT → ") + star,
                    gate.IsOpen ? UnruledPalette.Sta : UnruledPalette.InkSoft, true, false);
            }

            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                var laneId = _sim.Starlanes.EntityAt(i);
                if (IsJumpGateLane(laneId))
                    continue;
                ref var lane = ref _sim.Starlanes.At(i);
                bool aIn = InCluster(lane.SiteA);
                bool bIn = InCluster(lane.SiteB);
                if (aIn == bIn)
                    continue;
                var here = aIn ? lane.SiteA : lane.SiteB;
                var there = lane.OtherEnd(here);
                var p = Pos(here);
                var col = lane.Tier == StarlaneTier.StaHighway ? UnruledPalette.Sta : UnruledPalette.Unclaimed;
                _mesh.DashedRing(p, 24f, 2f, 5f, 3f, col);
                DrawExitVector(p, there, col);
                string star = _sim.Sites.Has(there) ? UiFormat.StarName(_sim.Sites.Get(there).ClusterId) : "";
                int stack = _portalStack.TryGetValue(here.Index, out int k) ? k : 0;
                _portalStack[here.Index] = stack + 1;
                PlaceLabel(p + new Vector2(0f, (34f + 16f * stack) * _mesh.UnitsPerPixel), "WARP → " + star, col, true, false);
            }
        }

        // A hub: a system holding one of THE SYSTEM's Citadels (works for systems from before the catalog too).
        private bool IsHub(int clusterId)
        {
            for (int i = 0; i < _sim.Citadels.Count; i++)
            {
                var site = _sim.Citadels.EntityAt(i);
                if (_sim.Sites.Has(site) && _sim.Sites.Get(site).ClusterId == clusterId)
                    return true;
            }
            return false;
        }

        // A charted system's place in the drawn galaxy view (relative to the camera).
        private Vector2 SystemAt(int clusterId) =>
            new Vector2((float)(_systemPos[clusterId].x - _camX), (float)(_systemPos[clusterId].y - _camY));

        private Vector2 GalaxyAt(int x, int y) => new Vector2((float)(x - _camX), (float)(y - _camY));

        // Whole galaxy: star density sampled from the catalog on a coarse grid over the view, faint where the field is
        // thin, bright along the arms and the core. Cached until the camera moves.
        private readonly List<Vector3> _dust = new List<Vector3>(); // x, y (galaxy units), density 0..1
        private float _dustCell;

        private void DrawDust()
        {
            var catalog = _sim.Galaxy.Catalog;
            if (!_starsValid)
            {
                _starsValid = true;
                _dust.Clear();
                int size = catalog.Balance.SectorSize;
                float max = catalog.Balance.MaxStarsPerSector;
                _dustCell = 2f * Mathf.Max(_halfWidth, _halfHeight) / DustCellsAcross;
                int across = Mathf.CeilToInt(_halfWidth * 2f / _dustCell) + 1;
                int down = Mathf.CeilToInt(_halfHeight * 2f / _dustCell) + 1;
                for (int j = 0; j < down; j++)
                for (int i = 0; i < across; i++)
                {
                    double gx = _camX - _halfWidth + (i + 0.5) * _dustCell;
                    double gy = _camY - _halfHeight + (j + 0.5) * _dustCell;
                    // Four sectors per cell: one alone reads as noise.
                    float sum = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        double sx = gx + ((k & 1) - 0.5) * _dustCell * 0.5;
                        double sy = gy + ((k >> 1) - 0.5) * _dustCell * 0.5;
                        sum += catalog.StarCount((int)System.Math.Round(sx / size), (int)System.Math.Round(sy / size));
                    }
                    float density = sum / (4f * max);
                    if (density > 0f)
                        _dust.Add(new Vector3((float)(gx - _camX), (float)(gy - _camY), density));
                }
            }
            // Soft overlapping blobs: the thin field between the arms barely shows, the arms glow, the core burns warm.
            float cellPx = _dustCell / _mesh.UnitsPerPixel;
            var ink = (Color32)UnruledPalette.InkSoft;
            var warm = (Color32)UnruledPalette.Sta;
            for (int i = 0; i < _dust.Count; i++)
            {
                var d = _dust[i];
                var c = d.z > 0.75f ? warm : ink;
                c.a = (byte)Mathf.Clamp(d.z * d.z * 120f, 0f, 120f);
                if (c.a < 3)
                    continue;
                _mesh.Disc(new Vector2(d.x, d.y), cellPx * 0.85f, c);
            }
        }

        // A star region: every catalog star in view (capped), hub stars red, home-class soft, frontier dim; close in,
        // the hyperlanes between them. Cached until the camera moves.
        private void DrawRegion()
        {
            var catalog = _sim.Galaxy.Catalog;
            if (!_starsValid)
            {
                _starsValid = true;
                catalog.StarsInRect((int)(_camX - _halfWidth), (int)(_camY - _halfHeight), (int)(_camX + _halfWidth),
                    (int)(_camY + _halfHeight), _stars, RegionStarLimit);
                _hyperlanes.Clear();
                if (_camHalf <= HyperlaneMaxHalf && _stars.Count <= HyperlaneStarLimit)
                    for (int i = 0; i < _stars.Count; i++)
                    {
                        catalog.Neighbours(_stars[i].Id, _neighbours);
                        for (int n = 0; n < _neighbours.Count; n++)
                        {
                            if (_neighbours[n].Packed < _stars[i].Id.Packed || !catalog.TryGetStar(_neighbours[n], out var other))
                                continue;
                            _hyperlanes.Add(new Vector2Int(_stars[i].X, _stars[i].Y));
                            _hyperlanes.Add(new Vector2Int(other.X, other.Y));
                        }
                    }
            }
            var lane = (Color32)UnruledPalette.Rule;
            for (int i = 0; i + 1 < _hyperlanes.Count; i += 2)
                _mesh.Line(GalaxyAt(_hyperlanes[i].x, _hyperlanes[i].y), GalaxyAt(_hyperlanes[i + 1].x, _hyperlanes[i + 1].y),
                    1f, lane);
            float sizePx = _camHalf < HyperlaneMaxHalf ? 3f : 1.6f;
            for (int i = 0; i < _stars.Count; i++)
            {
                var star = _stars[i];
                var c = star.Class == StarClass.Hub ? UnruledPalette.Danger
                    : star.Class == StarClass.Home ? UnruledPalette.InkSoft
                    : UnruledPalette.InkDim;
                _mesh.Disc(GalaxyAt(star.X, star.Y), star.Class == StarClass.Hub ? sizePx * 1.5f : sizePx, c);
            }
        }

        // Charted systems over the dust or region: links between them, owner colours, and (in a region) names and fleets.
        private void DrawGalaxy(int level)
        {
            bool detailed = level == 1;
            int count = _systemPos.Count;
            _clusterFleets.Clear();
            for (int c = 0; c < count; c++)
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
                int gA = _sim.Sites.Get(gate.HomeSite).ClusterId, gB = _sim.Sites.Get(gate.FarSite).ClusterId;
                if (gA < 0 || gB < 0 || gA >= count || gB >= count)
                    continue;
                DrawRoute(SystemAt(gA), SystemAt(gB), gate.IsOpen ? UnruledPalette.Sta : UnruledPalette.Unclaimed,
                    gate.IsOpen ? 3f : 2f, !gate.IsOpen);
            }

            for (int i = 0; i < _sim.Starlanes.Count; i++)
            {
                var laneId = _sim.Starlanes.EntityAt(i);
                if (IsJumpGateLane(laneId))
                    continue;
                ref var lane = ref _sim.Starlanes.At(i);
                if (!_sim.Sites.Has(lane.SiteA) || !_sim.Sites.Has(lane.SiteB))
                    continue;
                int cA = _sim.Sites.Get(lane.SiteA).ClusterId;
                int cB = _sim.Sites.Get(lane.SiteB).ClusterId;
                if (cA == cB || cA < 0 || cB < 0 || cA >= count || cB >= count)
                    continue;
                var a = SystemAt(cA);
                var b = SystemAt(cB);
                if (lane.Tier == StarlaneTier.StaHighway)
                {
                    var gold = UnruledPalette.Sta;
                    gold.a = 0.55f;
                    DrawRoute(a, b, gold, 2f, false);
                }
                else
                    DrawRoute(a, b, UnruledPalette.Unclaimed, 1.8f, true);
            }

            for (int c = 0; c < count; c++)
            {
                var p = SystemAt(c);
                int owner = ClusterCoreOwner(c);
                bool contested = IsHub(c);
                Color colour = contested ? UnruledPalette.Danger : owner != 0 ? SideColour(owner) : UnruledPalette.Unclaimed;
                if (!detailed)
                {
                    _mesh.Disc(p, owner == _playerId ? 5f : 3.5f, colour);
                    if (owner == _playerId)
                        _mesh.Ring(p, 10f, 1.5f, UnruledPalette.You);
                    continue;
                }
                if (c == _clusterId)
                    _mesh.SquareOutline(p, 64f, 2f, UnruledPalette.Ink);
                var starTex = StarTexture(c);
                if (starTex != null)
                {
                    _mesh.Ring(p, 24f, 2f, colour);
                    PlaceGlyph(p, 40f, starTex, Color.white);
                }
                else
                {
                    _mesh.Disc(p, 22f, colour);
                    _mesh.Disc(p, 18f, UnruledPalette.Void);
                    _mesh.Disc(p, 8f, colour);
                }
                string name = UiFormat.StarName(c) +
                              (contested ? " · CONTESTED" : owner == _playerId ? " · HOME" : owner == 0 ? " · FRONTIER" : "");
                bool strong = contested || owner == _playerId;
                PlaceLabel(p - new Vector2(0f, 40f * _mesh.UnitsPerPixel), name, strong ? UnruledPalette.Ink : UnruledPalette.InkSoft,
                    true, true);
                if (_clusterFleets[c] > 0)
                    PlaceLabel(p + new Vector2(0f, 40f * _mesh.UnitsPerPixel),
                        UiFormat.Number(_clusterFleets[c]) + (_clusterFleets[c] == 1 ? " FLEET" : " FLEETS"), UnruledPalette.You,
                        true, false);
            }
        }


        // A route between two systems on the chart, bowed gently so crossing routes stay readable.
        private void DrawRoute(Vector2 a, Vector2 b, Color colour, float widthPx, bool dashed)
        {
            const int Segments = 14;
            var mid = (a + b) * 0.5f;
            var d = b - a;
            var bow = new Vector2(-d.y, d.x) * 0.12f;
            var prev = a;
            for (int i = 1; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                var q = Vector2.Lerp(Vector2.Lerp(a, mid + bow, t), Vector2.Lerp(mid + bow, b, t), t);
                if (!dashed || (i & 1) == 1)
                    _mesh.Line(prev, q, widthPx, colour);
                prev = q;
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

        private bool IsJumpGateLane(EntityId lane)
        {
            for (int i = 0; i < _sim.JumpGates.Count; i++)
                if (_sim.JumpGates.At(i).Lane == lane)
                    return true;
            return false;
        }

        private void EnsureTelemetryOverlays()
        {
            if (_backdropLayer == null)
            {
                _backdropLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                _backdropLayer.style.position = Position.Absolute;
                _backdropLayer.style.left = 0;
                _backdropLayer.style.top = 0;
                _backdropLayer.style.right = 0;
                _backdropLayer.style.bottom = 0;
                _backdropLayer.style.opacity = 0.16f;
                var backdropTex = Resources.Load<Texture2D>("UI/galaxy_sector_backdrop");
                if (backdropTex != null)
                    _backdropLayer.style.backgroundImage = new StyleBackground(backdropTex);
                _boardView.Insert(0, _backdropLayer);
            }
            if (_sectorBadge == null)
            {
                _sectorBadge = new VisualElement { pickingMode = PickingMode.Ignore };
                _sectorBadge.style.position = Position.Absolute;
                _sectorBadge.style.left = 12;
                _sectorBadge.style.top = 56; // below the SYSTEM / GALAXY tabs
                _sectorBadge.style.flexDirection = FlexDirection.Row;
                _sectorBadge.style.alignItems = Align.Center;
                _sectorBadge.style.paddingLeft = 8;
                _sectorBadge.style.paddingRight = 10;
                _sectorBadge.style.paddingTop = 4;
                _sectorBadge.style.paddingBottom = 4;
                _sectorBadge.style.backgroundColor = new Color(0.067f, 0.106f, 0.18f, 0.85f);
                _sectorBadge.style.borderTopWidth = 1;
                _sectorBadge.style.borderBottomWidth = 1;
                _sectorBadge.style.borderLeftWidth = 1;
                _sectorBadge.style.borderRightWidth = 1;
                _sectorBadge.style.borderTopColor = UnruledPalette.Raised;
                _sectorBadge.style.borderBottomColor = UnruledPalette.Raised;
                _sectorBadge.style.borderLeftColor = UnruledPalette.Raised;
                _sectorBadge.style.borderRightColor = UnruledPalette.Raised;
                var emblemTex = Resources.Load<Texture2D>("UI/frontier_sector_emblem");
                if (emblemTex != null)
                {
                    var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                    icon.style.width = 20;
                    icon.style.height = 20;
                    icon.style.marginRight = 8;
                    icon.style.backgroundImage = new StyleBackground(emblemTex);
                    _sectorBadge.Add(icon);
                }
                _sectorBadgeLabel = new Label { pickingMode = PickingMode.Ignore };
                _sectorBadgeLabel.AddToClassList("board-label");
                _sectorBadgeLabel.AddToClassList("board-label--strong");
                _sectorBadge.Add(_sectorBadgeLabel);
                _boardView.Add(_sectorBadge);
            }
        }

        private void UpdateSectorBadge(bool galaxy, int clusterId)
        {
            if (_sectorBadgeLabel == null)
                return;
            if (galaxy)
            {
                _sectorBadgeLabel.style.color = UnruledPalette.You;
                UiFormat.Set(_sectorBadgeLabel, _shownLevel == 0 ? "ASTROMETRIC GALAXY CHART" : "STAR REGION · SCROLL TO ZOOM");
                return;
            }
            int owner = ClusterCoreOwner(clusterId);
            bool contested = IsHub(clusterId);
            string tag = contested
                ? " · CONTESTED CORE SECTOR"
                : owner == _playerId
                    ? " · HOME SECTOR"
                    : owner != 0
                        ? " · RIVAL HOME SECTOR"
                        : " · UNINHABITED FRONTIER SECTOR";
            _sectorBadgeLabel.style.color = contested
                ? UnruledPalette.Danger
                : owner == 0
                    ? UnruledPalette.Unclaimed
                    : SideColour(owner);
            UiFormat.Set(_sectorBadgeLabel, UiFormat.StarName(clusterId) + tag);
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
                    _mesh.SquareOutline(p, 68f, 2f, SideColour(coreOwner));
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
                bool citadel = _sim.Citadels.Has(entity) && _sim.Citadels.Get(entity).FallenTick == 0;
                var planet = PlanetClasses.Of(in site, _sim.Balance.Universe);
                var glyph = citadel ? _citadelGlyph
                    : coreOwner != 0 ? _coreGlyph
                    : _art.Planets.Get(planet, site.TerrainSeed,
                        irradiated ? PlanetLifecycleState.IrradiatedDeadWorld : site.LifecycleState);
                if (glyph != null)
                {
                    // The planet's own painted sprite, sized by NASA type; the owner reads from the ring around it, the
                    // lanes stop at the dark disc.
                    _mesh.Disc(p, 16f, UnruledPalette.Board);
                    float size = citadel || coreOwner != 0 ? 54f : PlanetSizePx(planet);
                    if (!citadel)
                        _mesh.Ring(p, size * 0.5f + 4f, 2f, stroke);
                    PlaceGlyph(p, size, glyph, Husked(Color.white, site.LifecycleState));
                }
                else
                {
                    _mesh.Disc(p, 10f, stroke);
                    _mesh.Disc(p, 8f, fill);
                }
                if (site.OwnerPlayerId == _playerId)
                    DrawSiteWorks(entity, p);
                if (_sim.Relays.Has(entity))
                    _mesh.Triangle(p + new Vector2(-18f, 14f) * _mesh.UnitsPerPixel, 10f, 1.5f,
                        SideColour(_sim.Relays.Get(entity).OwnerPlayerId));
                if (irradiated && glyph == null)
                    _mesh.Line(p + new Vector2(-6f, -6f) * _mesh.UnitsPerPixel, p + new Vector2(6f, 6f) * _mesh.UnitsPerPixel, 2f,
                        UnruledPalette.Iaea);
                PlaceLabel(p - new Vector2(0f, (coreOwner != 0 ? 40f : 32f) * _mesh.UnitsPerPixel), UiFormat.SiteName(site), UnruledPalette.InkSoft, true, false);
            }
        }

        // Sprite box per NASA type: painted sprites leave padding (and room for a giant's ring), so boxes run large.
        private static float PlanetSizePx(PlanetClass planet) =>
            planet == PlanetClass.GasGiant ? 64f : planet == PlanetClass.NeptuneLike ? 54f : planet == PlanetClass.SuperEarth ? 52f : 46f;

        // Own site works under the glyph: one tick per rig (dim when paused or retracting), one square per refinery.
        private void DrawSiteWorks(EntityId site, Vector2 p)
        {
            float u = _mesh.UnitsPerPixel;
            int rigs = 0;
            bool idle = false;
            long tick = _sim.Clock.CurrentTick;
            for (int d = 0; d < _sim.Deposits.Count; d++)
            {
                ref var deposit = ref _sim.Deposits.At(d);
                if (deposit.Site != site)
                    continue;
                rigs += deposit.RigCount;
                idle |= deposit.IsPaused || tick < deposit.RetractUntilTick;
            }
            int shown = Mathf.Min(rigs, 10);
            var rigColour = idle ? UnruledPalette.InkDim : UnruledPalette.InkSoft;
            for (int i = 0; i < shown; i++)
            {
                var x = p + new Vector2((i - (shown - 1) * 0.5f) * 4f, -15f) * u;
                _mesh.Line(x, x - new Vector2(0f, 5f) * u, 2f, rigColour);
            }
            int refineries = 0;
            for (int r = 0; r < _sim.Refineries.Count; r++)
                if (_sim.Refineries.At(r).Site == site)
                    refineries++;
            for (int i = 0; i < Mathf.Min(refineries, 8); i++)
                _mesh.SquareFill(p + new Vector2(14f + i * 6f, 8f) * u, 4f, UnruledPalette.Wto);
        }

        // THE SYSTEM's Citadel: a shield arc around the site (integrity), the garrison tier, or the fallen mark.
        private void DrawCitadels(long tick)
        {
            int max = _sim.Balance.Citadel.MaxIntegrity;
            for (int i = 0; i < _sim.Citadels.Count; i++)
            {
                var site = _sim.Citadels.EntityAt(i);
                if (!InCluster(site))
                    continue;
                ref var citadel = ref _sim.Citadels.At(i);
                var p = Pos(site);
                if (citadel.FallenTick != 0)
                {
                    _mesh.DashedRing(p, 36f, 2f, 3f, 5f, UnruledPalette.InkDim);
                    PlaceLabel(p + new Vector2(0f, 50f * _mesh.UnitsPerPixel), "CITADEL FALLEN", UnruledPalette.InkDim, true, true);
                    continue;
                }
                var track = UnruledPalette.Sta;
                track.a = 0.25f;
                _mesh.Ring(p, 36f, 4f, track);
                bool hit = tick - citadel.LastHitTick < 60 && citadel.LastHitTick > 0;
                _mesh.Arc(p, 36f, 4f, max > 0 ? citadel.Integrity / (float)max : 0f, hit ? UnruledPalette.Danger : UnruledPalette.Sta);
                PlaceLabel(p + new Vector2(0f, 50f * _mesh.UnitsPerPixel),
                    "CITADEL · " + UiFormat.Number(max > 0 ? (long)citadel.Integrity * 100 / max : 0) + "% · T" +
                    UiFormat.Number(citadel.DefenceTier), hit ? UnruledPalette.Danger : UnruledPalette.Sta, true, true);
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
                var colour = FleetColour(in unit);
                if (fleet == selection)
                {
                    _mesh.SquareFill(p, 40f, UnruledPalette.Raised);
                    _mesh.SquareOutline(p, 40f, 2f, UnruledPalette.Ink);
                }
                if (unit.OwnerPlayerId == SystemAgencies.OwnerId(SystemAgency.Iaea))
                    _mesh.DashedRing(p, 24f, 2f, 4f, 3f, UnruledPalette.Iaea);
                bool contract = _sim.ContractCargo.Has(fleet);
                if (contract)
                    _mesh.SquareOutline(p, 30f, 1.5f, UnruledPalette.Wto);
                ref var combat = ref _sim.FleetCombat.Get(fleet);
                ref var readiness = ref _sim.FleetReadiness.Get(fleet);
                if (tick < combat.EngagedUntilTick)
                {
                    // engaged: a ring that pulses with the salvo clock
                    float pulse = 18f + (tick % 4) * 3f;
                    var flash = UnruledPalette.Danger;
                    flash.a = 0.8f;
                    _mesh.Ring(p, pulse, 2f, flash);
                }
                if (readiness.IsMothballed)
                {
                    var dim = colour;
                    dim.a = 0.4f;
                    colour = dim;
                    _mesh.DashedRing(p, 16f, 1.5f, 2f, 3f, UnruledPalette.InkDim);
                }
                else if (tick < readiness.WarmupUntilTick)
                    _mesh.DashedRing(p, 16f, 1.5f, 4f, 3f, UnruledPalette.You);
                var token = FleetToken(fleet, in unit);
                if (token != null)
                {
                    // Ship token turned along its lane; the tier chevrons shrink to a mark under it.
                    float angle = TryFleetHeading(fleet, out _, out var heading)
                        ? Mathf.Atan2(heading.x, heading.y) * Mathf.Rad2Deg
                        : 0f;
                    PlaceGlyph(p, 44f, token, colour, angle);
                    _mesh.Chevrons(p + new Vector2(0f, -20f) * _mesh.UnitsPerPixel, Mathf.Clamp(unit.TechTier, 1, 5), own, colour, 0.45f);
                }
                else
                    _mesh.Chevrons(p, Mathf.Clamp(unit.TechTier, 1, 5), own, colour);
                bool rwr = own && DrawFleetAlerts(fleet, p, in combat, in readiness, tick);

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
                if (contract)
                    text += " · WTO CARGO";
                if (rwr)
                    text += " · RWR!";
                PlaceLabel(p + new Vector2(26f * _mesh.UnitsPerPixel, 0f), text, own ? UnruledPalette.Ink : colour, false, false);
            }
        }

        // Small marks above an own fleet: unsupplied (red bar), guns locked (red square), venting (blue bar), RWR alarm.
        // Returns whether the RWR alarm is up (the caller adds it to the fleet's label).
        private bool DrawFleetAlerts(EntityId fleet, Vector2 p, in FleetCombatState combat, in FleetReadiness readiness, long tick)
        {
            float u = _mesh.UnitsPerPixel;
            var above = p + new Vector2(0f, 27f) * u;
            if (readiness.IsUnsupplied)
                _mesh.Line(above - new Vector2(10f, 0f) * u, above + new Vector2(10f, 0f) * u, 3f, UnruledPalette.Danger);
            if (combat.IsOverheated)
                _mesh.SquareFill(above + new Vector2(-14f, 4f) * u, 6f, UnruledPalette.ThermalLocked);
            else if (combat.IsVenting)
                _mesh.Line(above + new Vector2(-17f, 4f) * u, above + new Vector2(-11f, 4f) * u, 3f, UnruledPalette.ThermalNominal);
            long rwr = _sim.FleetSensors.Get(fleet).RwrWarningTick;
            if (rwr > 0 && tick - rwr < _sim.Balance.Radar.IntegrationTicks)
            {
                var warn = UnruledPalette.Danger;
                warn.a = (tick & 1) == 0 ? 1f : 0.45f;
                _mesh.DashedRing(p, 30f, 2f, 3f, 3f, warn);
                return true;
            }
            return false;
        }

        private bool TryFleetPosition(EntityId fleet, out Vector2 position)
        {
            position = default;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            if (!motion.Lane.IsNull && _sim.Starlanes.Has(motion.Lane))
            {
                ref var lane = ref _sim.Starlanes.Get(motion.Lane);
                var from = motion.FromSite;
                var to = lane.OtherEnd(from);
                bool fromIn = InCluster(from);
                bool toIn = InCluster(to);
                if (!fromIn && !toIn)
                    return false;
                float t = lane.Length > 0 ? Mathf.Clamp01(motion.TravelledMilli / (lane.Length * 1000f)) : 0f;
                if (fromIn && toIn)
                {
                    position = Vector2.Lerp(Pos(from), Pos(to), t);
                    return true;
                }
                if (fromIn && t < 0.5f)
                {
                    var p = Pos(from);
                    var dir = p.sqrMagnitude > 1f ? p.normalized : Vector2.up;
                    position = p + dir * (t * 80f * _mesh.UnitsPerPixel);
                    return true;
                }
                if (toIn && t >= 0.5f)
                {
                    var p = Pos(to);
                    var dir = p.sqrMagnitude > 1f ? p.normalized : Vector2.up;
                    position = p + dir * ((1f - t) * 80f * _mesh.UnitsPerPixel);
                    return true;
                }
                return false;
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

        // Board pixels to the units the mesh is drawn in.
        private Vector2 ToWorld(Vector2 local)
        {
            var size = _boardView.layout.size;
            return _viewCentre + new Vector2((local.x / size.x * 2f - 1f) * _halfWidth, (1f - local.y / size.y * 2f) * _halfHeight);
        }

        // Galaxy units per drawn unit at the current level.
        private float GalaxyPerWorld => _shownLevel >= 2 ? _systemScale : 1f;

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_sim == null)
                return;
            _pointers[evt.pointerId] = evt.localPosition;
            _boardView.CapturePointer(evt.pointerId);
            if (_pointers.Count == 1)
            {
                _pressPos = evt.localPosition;
                _dragging = false;
            }
            else
                _pinchDistance = PinchDistance();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_pointers.TryGetValue(evt.pointerId, out var last))
                return;
            Vector2 now = evt.localPosition;
            _pointers[evt.pointerId] = now;
            if (_pointers.Count >= 2)
            {
                float d = PinchDistance();
                if (_pinchDistance > 1f && d > 1f)
                    ZoomAt(PinchCentre(), _pinchDistance / d);
                _pinchDistance = d;
                _dragging = true;
                return;
            }
            if (!_dragging && (now - _pressPos).magnitude < DragThresholdPx)
                return;
            _dragging = true;
            var size = _boardView.layout.size;
            float galaxyPerPx = 2f * _halfHeight / size.y * GalaxyPerWorld;
            _camX -= (now.x - last.x) * galaxyPerPx;
            _camY += (now.y - last.y) * galaxyPerPx;
            _camDirty = true;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_pointers.Remove(evt.pointerId))
                return;
            if (_boardView.HasPointerCapture(evt.pointerId))
                _boardView.ReleasePointer(evt.pointerId);
            if (_pointers.Count == 0 && !_dragging)
                Click(evt.localPosition);
            if (_pointers.Count == 0)
                _dragging = false;
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            _pointers.Remove(evt.pointerId);
            if (_pointers.Count == 0)
                _dragging = false;
        }

        private void OnWheel(WheelEvent evt)
        {
            if (_sim == null)
                return;
            ZoomAt(evt.localMousePosition, evt.delta.y > 0f ? WheelStep : 1f / WheelStep);
            evt.StopPropagation();
        }

        // Zooms by `factor` (above 1 = out) keeping the galaxy point under `local` fixed on screen.
        private void ZoomAt(Vector2 local, float factor)
        {
            float min = SystemFootprint * PlanetZoom * 0.25f;
            float max = _sim.Balance.Galaxy.RadiusUnits * 1.2f;
            float next = Mathf.Clamp(_camHalf * factor, min, max);
            var size = _boardView.layout.size;
            if (size.y <= 0f)
                return;
            float aspect = size.x / size.y;
            float halfH = aspect >= 1f ? _camHalf : _camHalf / aspect;
            double ox = (local.x / size.x * 2f - 1f) * halfH * aspect;
            double oy = (1f - local.y / size.y * 2f) * halfH;
            double k = 1.0 - next / _camHalf;
            _camX += ox * k;
            _camY += oy * k;
            _camHalf = next;
            _camDirty = true;
        }

        private float PinchDistance()
        {
            Vector2 a = default, b = default;
            int n = 0;
            foreach (var p in _pointers.Values)
            {
                if (n == 0) a = p;
                else if (n == 1) b = p;
                n++;
            }
            return n >= 2 ? Vector2.Distance(a, b) : 0f;
        }

        private Vector2 PinchCentre()
        {
            Vector2 sum = default;
            int n = 0;
            foreach (var p in _pointers.Values)
            {
                sum += p;
                n++;
            }
            return n > 0 ? sum / n : sum;
        }

        private void Click(Vector2 local)
        {
            var world = ToWorld(local);
            if (_shownLevel < 2)
            {
                float pick = ClusterPickPx * _mesh.UnitsPerPixel;
                for (int c = 0; c < _systemPos.Count; c++)
                {
                    if (Vector2.Distance(SystemAt(c), world) > pick)
                        continue;
                    // First click previews the system; a second click on it flies into it.
                    if (c == _previewCluster)
                        ShowCluster(c);
                    else
                    {
                        _previewCluster = c;
                        _screen.Preview.ShowCluster(c, StarTexture(c));
                    }
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

        // A depleted husk is drawn fainter, so live worlds stand out at a glance.
        private static Color Husked(Color tint, PlanetLifecycleState state)
        {
            if (state == PlanetLifecycleState.DepletedBarrenHusk)
                tint.a *= 0.6f;
            return tint;
        }

        private void PlaceGlyph(Vector2 world, float sizePx, Texture2D texture, Color tint, float degrees = 0f)
        {
            VisualElement glyph;
            if (_glyphsUsed < _glyphs.Count)
            {
                glyph = _glyphs[_glyphsUsed];
            }
            else
            {
                glyph = new VisualElement { pickingMode = PickingMode.Ignore };
                glyph.style.position = Position.Absolute;
                glyph.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                _glyphs.Add(glyph);
                _labelLayer.Insert(0, glyph);
            }
            _glyphsUsed++;
            var size = _boardView.layout.size;
            glyph.style.display = DisplayStyle.Flex;
            glyph.style.width = sizePx;
            glyph.style.height = sizePx;
            glyph.style.left = ((world.x - _viewCentre.x) / _halfWidth + 1f) * 0.5f * size.x - sizePx * 0.5f;
            glyph.style.top = (1f - (world.y - _viewCentre.y) / _halfHeight) * 0.5f * size.y - sizePx * 0.5f;
            glyph.style.backgroundImage = new StyleBackground(texture);
            glyph.style.unityBackgroundImageTintColor = tint;
            glyph.style.rotate = new Rotate(degrees);
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
            float x = ((world.x - _viewCentre.x) / _halfWidth + 1f) * 0.5f * size.x;
            float y = (1f - (world.y - _viewCentre.y) / _halfHeight) * 0.5f * size.y;
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
            {
                _boardView.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                _boardView.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                _boardView.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                _boardView.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
                _boardView.UnregisterCallback<WheelEvent>(OnWheel);
            }
            if (_mesh != null)
                Destroy(_mesh.Mesh);
            _art?.Planets.Clear();
        }
    }
}
