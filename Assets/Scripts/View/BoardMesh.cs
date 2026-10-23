using System.Collections.Generic;
using UnityEngine;

namespace Unruled.View
{
    /// <summary>
    /// Vertex-coloured 2D geometry for the board, built on the XY plane into one reusable mesh: thick lines, dashed
    /// lines, discs, rings, zigzags and chevrons. Sizes are passed in screen pixels and scaled by
    /// <see cref="UnitsPerPixel"/>, so line weights stay fixed whatever the zoom. Draw order = call order.
    /// </summary>
    public sealed class BoardMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(4096);
        private readonly List<Color32> _colors = new List<Color32>(4096);
        private readonly List<int> _indices = new List<int>(8192);

        public readonly Mesh Mesh;

        public float UnitsPerPixel = 1f;

        public BoardMesh()
        {
            Mesh = new Mesh { name = "Board" };
            Mesh.MarkDynamic();
        }

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
        }

        public void Upload()
        {
            Mesh.Clear();
            Mesh.SetVertices(_vertices);
            Mesh.SetColors(_colors);
            Mesh.SetTriangles(_indices, 0, false);
            Mesh.RecalculateBounds();
        }

        /// <summary>A straight line <paramref name="widthPx"/> wide.</summary>
        public void Line(Vector2 a, Vector2 b, float widthPx, Color32 color)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len <= 0f)
                return;
            var n = new Vector2(-d.y, d.x) / len * (widthPx * UnitsPerPixel * 0.5f);
            Quad(a + n, b + n, b - n, a - n, color);
        }

        /// <summary>Dashes of <paramref name="dashPx"/> separated by <paramref name="gapPx"/>, starting with a dash.</summary>
        public void DashedLine(Vector2 a, Vector2 b, float widthPx, float dashPx, float gapPx, Color32 color)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len <= 0f)
                return;
            var step = d / len;
            float dash = dashPx * UnitsPerPixel, period = (dashPx + gapPx) * UnitsPerPixel;
            for (float t = 0f; t < len; t += period)
                Line(a + step * t, a + step * Mathf.Min(t + dash, len), widthPx, color);
        }

        /// <summary>A zigzag along a-b: the hazard shortcut's mark.</summary>
        public void Zigzag(Vector2 a, Vector2 b, float widthPx, float amplitudePx, float periodPx, Color32 color)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len <= 0f)
                return;
            var step = d / len;
            var normal = new Vector2(-step.y, step.x) * (amplitudePx * UnitsPerPixel);
            float half = periodPx * UnitsPerPixel * 0.5f;
            int count = Mathf.Max(2, Mathf.RoundToInt(len / half));
            float seg = len / count;
            var prev = a;
            for (int i = 1; i <= count; i++)
            {
                var next = i == count ? b : a + step * (seg * i) + (i % 2 == 1 ? normal : -normal);
                Line(prev, next, widthPx, color);
                prev = next;
            }
        }

        public void Disc(Vector2 centre, float radiusPx, Color32 color) => DiscWorld(centre, radiusPx * UnitsPerPixel, color);

        public void DiscWorld(Vector2 centre, float radius, Color32 color)
        {
            int segments = Segments(radius);
            int start = _vertices.Count;
            AddVertex(centre, color);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                AddVertex(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color);
            }
            for (int i = 0; i < segments; i++)
            {
                _indices.Add(start);
                _indices.Add(start + 1 + (i + 1) % segments);
                _indices.Add(start + 1 + i);
            }
        }

        public void Ring(Vector2 centre, float radiusPx, float widthPx, Color32 color) =>
            RingWorld(centre, radiusPx * UnitsPerPixel, widthPx, color);

        /// <summary>A ring whose radius is in world units (orbits, storms) and whose width is in pixels.</summary>
        public void RingWorld(Vector2 centre, float radius, float widthPx, Color32 color)
        {
            float half = widthPx * UnitsPerPixel * 0.5f;
            int segments = Segments(radius);
            int start = _vertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddVertex(centre + dir * (radius + half), color);
                AddVertex(centre + dir * Mathf.Max(0f, radius - half), color);
            }
            for (int i = 0; i < segments; i++)
            {
                int o0 = start + i * 2, i0 = o0 + 1;
                int o1 = start + (i + 1) % segments * 2, i1 = o1 + 1;
                _indices.Add(o0); _indices.Add(o1); _indices.Add(i1);
                _indices.Add(o0); _indices.Add(i1); _indices.Add(i0);
            }
        }

        public void DashedRing(Vector2 centre, float radiusPx, float widthPx, float dashPx, float gapPx, Color32 color)
        {
            float radius = radiusPx * UnitsPerPixel;
            float circumference = 2f * Mathf.PI * radiusPx;
            int dashes = Mathf.Max(4, Mathf.RoundToInt(circumference / (dashPx + gapPx)));
            float arc = Mathf.PI * 2f / dashes;
            float dashArc = arc * dashPx / (dashPx + gapPx);
            for (int i = 0; i < dashes; i++)
            {
                float a0 = i * arc, a1 = a0 + dashArc;
                Line(centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                    centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, widthPx, color);
            }
        }

        /// <summary>
        /// A clockwise arc from twelve o'clock covering <paramref name="fraction"/> (0..1) of a ring, for gauges
        /// drawn on the board (Citadel shield, scan progress).
        /// </summary>
        public void Arc(Vector2 centre, float radiusPx, float widthPx, float fraction, Color32 color)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction <= 0f)
                return;
            float radius = radiusPx * UnitsPerPixel;
            float half = widthPx * UnitsPerPixel * 0.5f;
            int segments = Mathf.Max(2, Mathf.RoundToInt(Segments(radius) * fraction));
            int start = _vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 0.5f - i * fraction * Mathf.PI * 2f / segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddVertex(centre + dir * (radius + half), color);
                AddVertex(centre + dir * Mathf.Max(0f, radius - half), color);
            }
            for (int i = 0; i < segments; i++)
            {
                int o0 = start + i * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
                _indices.Add(o0); _indices.Add(o1); _indices.Add(i1);
                _indices.Add(o0); _indices.Add(i1); _indices.Add(i0);
            }
        }

        /// <summary>A triangle outline pointing up, <paramref name="sizePx"/> wide (relay stations).</summary>
        public void Triangle(Vector2 centre, float sizePx, float widthPx, Color32 color)
        {
            float h = sizePx * UnitsPerPixel * 0.5f;
            var top = centre + new Vector2(0f, h);
            var left = centre + new Vector2(-h, -h * 0.8f);
            var right = centre + new Vector2(h, -h * 0.8f);
            Line(left, top, widthPx, color);
            Line(top, right, widthPx, color);
            Line(right, left, widthPx, color);
        }

        /// <summary>An axis-aligned square outline, <paramref name="sizePx"/> on a side, centred.</summary>
        public void SquareOutline(Vector2 centre, float sizePx, float widthPx, Color32 color)
        {
            float h = sizePx * UnitsPerPixel * 0.5f, w = widthPx * UnitsPerPixel * 0.5f;
            var tl = centre + new Vector2(-h, h);
            var tr = centre + new Vector2(h, h);
            var br = centre + new Vector2(h, -h);
            var bl = centre + new Vector2(-h, -h);
            var x = new Vector2(w, 0f);
            Line(tl - x, tr + x, widthPx, color);
            Line(bl - x, br + x, widthPx, color);
            Line(tl, bl, widthPx, color);
            Line(tr, br, widthPx, color);
        }

        public void SquareFill(Vector2 centre, float sizePx, Color32 color)
        {
            float h = sizePx * UnitsPerPixel * 0.5f;
            Quad(centre + new Vector2(-h, h), centre + new Vector2(h, h), centre + new Vector2(h, -h), centre + new Vector2(-h, -h), color);
        }

        /// <summary>A stack of <paramref name="count"/> chevrons (one per tech tier) pointing up or down.</summary>
        public void Chevrons(Vector2 centre, int count, bool up, Color32 color)
        {
            const float widthPx = 20f, risePx = 6f, stepPx = 8f, strokePx = 3f;
            float u = UnitsPerPixel;
            float sign = up ? 1f : -1f;
            float top = (count - 1) * stepPx * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float y = (top - i * stepPx) * sign;
                var mid = centre + new Vector2(0f, (y + risePx * 0.5f * sign) * u);
                var left = centre + new Vector2(-widthPx * 0.5f * u, (y - risePx * 0.5f * sign) * u);
                var right = centre + new Vector2(widthPx * 0.5f * u, (y - risePx * 0.5f * sign) * u);
                Line(left, mid, strokePx, color);
                Line(mid, right, strokePx, color);
            }
        }

        private int Segments(float worldRadius)
        {
            float px = worldRadius / Mathf.Max(UnitsPerPixel, 1e-6f);
            return Mathf.Clamp(Mathf.RoundToInt(px * 0.6f), 16, 128);
        }

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            int start = _vertices.Count;
            AddVertex(a, color);
            AddVertex(b, color);
            AddVertex(c, color);
            AddVertex(d, color);
            _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
            _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
        }

        private void AddVertex(Vector2 p, Color32 color)
        {
            _vertices.Add(new Vector3(p.x, p.y, 0f));
            _colors.Add(color);
        }
    }
}
