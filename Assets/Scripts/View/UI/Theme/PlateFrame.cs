using UnityEngine;
using UnityEngine.UIElements;

namespace Unruled.View.UI.Theme
{
    /// <summary>
    /// The Style A "technical plate" frame: chamfered corner brackets and small registration crosses inside the
    /// element's edge, drawn over its own background. Decoration only; it never takes clicks. Colour: the
    /// <c>--u-plate-color</c> custom property.
    /// </summary>
    [UxmlElement]
    public partial class PlateFrame : VisualElement
    {
        private const float Inset = 6f;
        private const float Arm = 18f;
        private const float Chamfer = 6f;
        private const float Cross = 4f;
        private const float Width = 1.5f;

        private static readonly CustomStyleProperty<Color> PlateColorProperty = new CustomStyleProperty<Color>("--u-plate-color");

        private Color _color = new Color(0.29f, 0.36f, 0.51f);

        public PlateFrame()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(PlateColorProperty, out var color) && color != _color)
            {
                _color = color;
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            float w = layout.width, h = layout.height;
            if (w < Arm * 3f || h < Arm * 3f)
                return;
            var painter = context.painter2D;
            painter.strokeColor = _color;
            painter.lineWidth = Width;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            float l = Inset, t = Inset, r = w - Inset, b = h - Inset;
            Corner(painter, new Vector2(l, t), 1f, 1f);
            Corner(painter, new Vector2(r, t), -1f, 1f);
            Corner(painter, new Vector2(r, b), -1f, -1f);
            Corner(painter, new Vector2(l, b), 1f, -1f);
            float midX = w * 0.5f, midY = h * 0.5f;
            Tick(painter, new Vector2(midX, t), true);
            Tick(painter, new Vector2(midX, b), true);
            Tick(painter, new Vector2(l, midY), false);
            Tick(painter, new Vector2(r, midY), false);
        }

        // An L bracket whose corner is cut at 45 degrees; dx/dy point into the element.
        private static void Corner(Painter2D painter, Vector2 corner, float dx, float dy)
        {
            painter.BeginPath();
            painter.MoveTo(corner + new Vector2(0f, Arm * dy));
            painter.LineTo(corner + new Vector2(0f, Chamfer * dy));
            painter.LineTo(corner + new Vector2(Chamfer * dx, 0f));
            painter.LineTo(corner + new Vector2(Arm * dx, 0f));
            painter.Stroke();
        }

        // A short registration cross on the edge midpoint.
        private static void Tick(Painter2D painter, Vector2 centre, bool horizontalEdge)
        {
            painter.BeginPath();
            if (horizontalEdge)
            {
                painter.MoveTo(centre + new Vector2(-Cross, 0f));
                painter.LineTo(centre + new Vector2(Cross, 0f));
            }
            else
            {
                painter.MoveTo(centre + new Vector2(0f, -Cross));
                painter.LineTo(centre + new Vector2(0f, Cross));
            }
            painter.Stroke();
        }
    }
}
