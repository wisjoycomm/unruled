using UnityEngine;
using UnityEngine.UIElements;

namespace Unruled.View.UI.Theme
{
    /// <summary>
    /// A container with a dashed 1 px border: the style guide's mark for things not yet certain (orders still in
    /// signal latency, stale radar). USS borders can't dash, so it draws its own. Colour: the <c>--u-dash-color</c>
    /// custom property.
    /// </summary>
    [UxmlElement]
    public partial class DashedFrame : VisualElement
    {
        private const float Dash = 4f;
        private const float Gap = 3f;
        private const float Width = 1f;

        private static readonly CustomStyleProperty<Color> DashColorProperty = new CustomStyleProperty<Color>("--u-dash-color");

        private Color _color = Color.white;

        public DashedFrame()
        {
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(DashColorProperty, out var color) && color != _color)
            {
                _color = color;
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            float w = layout.width, h = layout.height;
            if (w <= 0f || h <= 0f)
                return;
            var painter = context.painter2D;
            painter.strokeColor = _color;
            painter.lineWidth = Width;
            painter.lineCap = LineCap.Butt;
            float inset = Width * 0.5f;
            DashedLine(painter, new Vector2(inset, inset), new Vector2(w - inset, inset));
            DashedLine(painter, new Vector2(w - inset, inset), new Vector2(w - inset, h - inset));
            DashedLine(painter, new Vector2(w - inset, h - inset), new Vector2(inset, h - inset));
            DashedLine(painter, new Vector2(inset, h - inset), new Vector2(inset, inset));
        }

        private static void DashedLine(Painter2D painter, Vector2 from, Vector2 to)
        {
            float length = Vector2.Distance(from, to);
            if (length <= 0f)
                return;
            var step = (to - from) / length;
            painter.BeginPath();
            for (float d = 0f; d < length; d += Dash + Gap)
            {
                painter.MoveTo(from + step * d);
                painter.LineTo(from + step * Mathf.Min(d + Dash, length));
            }
            painter.Stroke();
        }
    }
}
