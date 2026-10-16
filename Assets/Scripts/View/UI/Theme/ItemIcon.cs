using System;
using System.Collections.Generic;
using System.Globalization;
using Unruled.Core.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unruled.View.UI.Theme
{
    /// <summary>
    /// One of the style guide's 20 item pictograms, drawn with Painter2D from its 24x24 SVG path:
    /// RAW_* ore as an outline in ink-soft, refined ITEM_* goods filled in "you" blue. Sized by the
    /// <c>.u-icon</c> class (16 px); any other size scales the path.
    /// </summary>
    [UxmlElement]
    public partial class ItemIcon : VisualElement
    {
        private const float ViewBox = 24f;
        private const float StrokeWidth = 1.6f;

        // Style guide section 05, in ItemId order.
        private static readonly string[] Paths =
        {
            "M12 3 C12 3 5 11 5 15 a7 7 0 0 0 14 0 C19 11 12 3 12 3 Z",
            "M4 12 a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0 M12 12 a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0",
            "M12 3 L20 18 L4 18 Z M12 3 L12 13 M4 18 L12 13 L20 18",
            "M5 19 C5 9 11 4 19 5 C20 13 15 19 5 19 Z M5 19 L14 10",
            "M12 3 L20 7.5 L20 16.5 L12 21 L4 16.5 L4 7.5 Z M4 7.5 L12 12 L20 7.5 M12 12 L12 21",
            "M7 4 L17 4 L22 12 L17 20 L7 20 L2 12 Z",
            "M12 2 L21 12 L12 22 L3 12 Z M3 12 L21 12",
            "M12 3 L21 20 L3 20 Z M12 9 L16.5 17 L7.5 17 Z",
            "M4 4 L20 4 L20 20 L4 20 Z M8 8 L16 8 L16 16 L8 16 Z",
            "M12 2 a10 10 0 1 0 0.01 0 Z M12 9.5 a2.5 2.5 0 1 0 0.01 0 Z M12 9.5 L12 4 M9.8 13.2 L5 16 M14.2 13.2 L19 16",
            "M12 3 a9 9 0 1 0 0.01 0 Z M12 8 a4 4 0 1 1 -0.01 0 Z",
            "M3 7 L12 3 L21 7 L21 17 L12 21 L3 17 Z",
            "M11 2 L13 2 L13 22 L11 22 Z M2 11 L22 11 L22 13 L2 13 Z M4.2 5.6 L5.6 4.2 L19.8 18.4 L18.4 19.8 Z M18.4 4.2 L19.8 5.6 L5.6 19.8 L4.2 18.4 Z",
            "M12 2 C14 7 19 9 19 15 a7 7 0 0 1 -14 0 C5 11 8 9 9 6 C10 9 11 10 12 10 C12 7 12 5 12 2 Z",
            "M4 3 L20 3 L20 7 L14 7 L14 17 L20 17 L20 21 L4 21 L4 17 L10 17 L10 7 L4 7 Z",
            "M3 15 L21 15 L21 20 L3 20 Z M5 9 L19 9 L19 13.5 L5 13.5 Z M7 3 L17 3 L17 7.5 L7 7.5 Z",
            "M7 7 L17 7 L17 17 L7 17 Z M9 3 L10.5 3 L10.5 6 L9 6 Z M13.5 3 L15 3 L15 6 L13.5 6 Z M9 18 L10.5 18 L10.5 21 L9 21 Z M13.5 18 L15 18 L15 21 L13.5 21 Z M3 9 L6 9 L6 10.5 L3 10.5 Z M3 13.5 L6 13.5 L6 15 L3 15 Z M18 9 L21 9 L21 10.5 L18 10.5 Z M18 13.5 L21 13.5 L21 15 L18 15 Z",
            "M9 2 L15 2 L15 4 L18 4 L18 22 L6 22 L6 4 L9 4 Z",
            "M9 21 L9 9 C9 5 12 2 12 2 C12 2 15 5 15 9 L15 21 Z",
            "M12 2 a10 10 0 1 0 0.01 0 Z M12 7 a5 5 0 1 1 -0.01 0 Z M12 10 a2 2 0 1 0 0.01 0 Z"
        };

        private enum Op : byte { Move, Line, Cubic, Arc, Close }

        private struct Segment
        {
            public Op Op;
            public Vector2 A, B, C; // Line/Move: A; Cubic: A, B controls, C end; Arc: A centre, B.x radius, C start/end angle (deg)
            public bool Clockwise;
        }

        private static readonly Segment[][] Parsed = ParseAll();

        private ItemId _item = ItemId.RawWater;

        [UxmlAttribute]
        public ItemId Item
        {
            get => _item;
            set
            {
                if (_item == value)
                    return;
                _item = value;
                MarkDirtyRepaint();
            }
        }

        public ItemIcon()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("u-icon");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!ItemIds.IsValid(_item))
                return;
            float size = Mathf.Min(layout.width, layout.height);
            if (size <= 0f)
                return;
            float scale = size / ViewBox;
            var painter = context.painter2D;
            bool raw = ItemIds.IsRaw(_item);
            painter.lineWidth = Mathf.Max(1f, StrokeWidth * scale);
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = raw ? UnruledPalette.InkSoft : UnruledPalette.You;
            painter.fillColor = UnruledPalette.You;

            painter.BeginPath();
            var segments = Parsed[ItemIds.ToSlot(_item)];
            for (int i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                switch (s.Op)
                {
                    case Op.Move: painter.MoveTo(s.A * scale); break;
                    case Op.Line: painter.LineTo(s.A * scale); break;
                    case Op.Cubic: painter.BezierCurveTo(s.A * scale, s.B * scale, s.C * scale); break;
                    case Op.Arc:
                        painter.Arc(s.A * scale, s.B.x * scale, s.C.x, s.C.y,
                            s.Clockwise ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                        break;
                    case Op.Close: painter.ClosePath(); break;
                }
            }
            if (!raw)
                painter.Fill(FillRule.OddEven);
            painter.Stroke();
        }

        private static Segment[][] ParseAll()
        {
            var all = new Segment[Paths.Length][];
            for (int i = 0; i < Paths.Length; i++)
                all[i] = Parse(Paths[i]);
            return all;
        }

        // Supports the subset the guide uses: M, L, C, Z (absolute) and a (relative circular arc).
        private static Segment[] Parse(string path)
        {
            var tokens = path.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<Segment>();
            var current = Vector2.zero;
            var start = Vector2.zero;
            int t = 0;
            char op = ' ';
            while (t < tokens.Length)
            {
                var token = tokens[t];
                if (char.IsLetter(token[0]))
                {
                    op = token[0];
                    token = token.Substring(1);
                    if (token.Length == 0)
                    {
                        t++;
                        if (op == 'Z')
                        {
                            list.Add(new Segment { Op = Op.Close });
                            current = start;
                        }
                        continue;
                    }
                    tokens[t] = token;
                }
                switch (op)
                {
                    case 'M':
                        current = start = Point(tokens, ref t);
                        list.Add(new Segment { Op = Op.Move, A = current });
                        op = 'L';
                        break;
                    case 'L':
                        current = Point(tokens, ref t);
                        list.Add(new Segment { Op = Op.Line, A = current });
                        break;
                    case 'C':
                        var c1 = Point(tokens, ref t);
                        var c2 = Point(tokens, ref t);
                        current = Point(tokens, ref t);
                        list.Add(new Segment { Op = Op.Cubic, A = c1, B = c2, C = current });
                        break;
                    case 'a':
                        float r = Number(tokens, ref t);
                        Number(tokens, ref t); // ry: the guide only uses circles
                        Number(tokens, ref t); // x-axis rotation
                        bool large = Number(tokens, ref t) != 0f;
                        bool sweep = Number(tokens, ref t) != 0f;
                        var end = current + Point(tokens, ref t);
                        list.Add(ArcSegment(current, end, r, large, sweep));
                        current = end;
                        break;
                    default:
                        throw new FormatException($"Unsupported path command '{op}' in '{path}'.");
                }
            }
            return list.ToArray();
        }

        // SVG endpoint arc -> centre parameterisation for a circle. SVG y points down, so sweep=1 is clockwise on screen.
        private static Segment ArcSegment(Vector2 from, Vector2 to, float r, bool large, bool sweep)
        {
            var mid = (from + to) * 0.5f;
            var chord = to - from;
            float half = chord.magnitude * 0.5f;
            r = Mathf.Max(r, half);
            float h = Mathf.Sqrt(Mathf.Max(0f, r * r - half * half));
            var normal = new Vector2(-chord.y, chord.x).normalized;
            var centre = mid + (large == sweep ? -1f : 1f) * h * normal;
            float a0 = Mathf.Atan2(from.y - centre.y, from.x - centre.x) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(to.y - centre.y, to.x - centre.x) * Mathf.Rad2Deg;
            return new Segment { Op = Op.Arc, A = centre, B = new Vector2(r, 0f), C = new Vector2(a0, a1), Clockwise = sweep };
        }

        private static Vector2 Point(string[] tokens, ref int t) => new Vector2(Number(tokens, ref t), Number(tokens, ref t));

        private static float Number(string[] tokens, ref int t) =>
            float.Parse(tokens[t++], NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
