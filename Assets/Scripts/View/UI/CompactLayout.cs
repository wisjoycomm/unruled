using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// Phone layout. When the panel is narrower than the desktop columns need, it adds <c>main--compact</c>: the board
    /// fills the body and a bottom bar swaps in the stockpile dock or the fleet/operations column as an opaque pane
    /// over it. Wider panels get the 3-column layout back. Layout only; reads and writes no simulation state.
    /// </summary>
    public sealed class CompactLayout
    {
        /// <summary>Dock (260) + side column (360) + the narrowest useful board, in panel pixels.</summary>
        public const float MinDesktopWidth = 1100f;

        private const string CompactClass = "main--compact";
        private const string OnClass = "seg__item--on";
        private static readonly string[] PaneClasses = { "main--show-dock", null, "main--show-side" };

        private readonly VisualElement _main;
        private readonly Button[] _nav = new Button[3];
        private int _pane = 1;

        public bool IsCompact { get; private set; }

        public CompactLayout(VisualElement root)
        {
            _main = TopTelemetryHud.Require<VisualElement>(root, "main");
            for (int i = 0; i < _nav.Length; i++)
            {
                int pane = i;
                _nav[i] = TopTelemetryHud.Require<Button>(root, "nav-" + UiFormat.Number(i));
                _nav[i].clicked += () => ShowPane(pane);
            }
            _main.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            ShowPane(_pane);
        }

        /// <summary>0 = stockpile dock, 1 = board, 2 = fleet card, market and operations.</summary>
        public void ShowPane(int pane)
        {
            _pane = pane;
            for (int i = 0; i < _nav.Length; i++)
            {
                _nav[i].EnableInClassList(OnClass, i == pane);
                if (PaneClasses[i] != null)
                    _main.EnableInClassList(PaneClasses[i], i == pane);
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            bool compact = evt.newRect.width < MinDesktopWidth;
            if (compact == IsCompact)
                return;
            IsCompact = compact;
            _main.EnableInClassList(CompactClass, compact);
            ShowPane(1);
        }
    }
}
