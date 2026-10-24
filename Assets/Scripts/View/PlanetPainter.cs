using System;
using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Simulation;
using UnityEngine;

namespace Unruled.View
{
    /// <summary>
    /// Paints planet sprites from code (Style A: flat colour bands with hard edges, dark outline, two-step shading and
    /// a hard highlight), one per NASA type and terrain seed, so every planet in the galaxy has its own look and the
    /// same planet always looks the same. Terrestrial: mottled rock with rimmed craters; Super-Earth: oceans, lowlands,
    /// highlands, clouds and caps under a thin atmosphere; Neptune-like: banded ice giant with a white storm; Gas Giant:
    /// wavy bands with a red storm, about half of them ringed. The lifecycle shows on top: a mined world is scarred, a
    /// husk greyed and dark, an irradiated one sickly yellow, a terraformed rocky world painted alive (a giant only
    /// greened). Textures are cached by seed and state.
    /// </summary>
    public sealed class PlanetPainter
    {
        public const int Size = 128;
        private const int CacheLimit = 512;

        private static readonly Color32 Outline = Hex(0x0B1220);
        private static readonly Color32[][] RockPalettes =
        {
            new[] { Hex(0x5B5148), Hex(0x7A6C5D), Hex(0x9B8B78), Hex(0xB8A48A) },
            new[] { Hex(0x6B4A3A), Hex(0x8E5F45), Hex(0xB07A52), Hex(0xC99A6A) },
            new[] { Hex(0x4F5560), Hex(0x6C7380), Hex(0x8E95A0), Hex(0xB3B8C0) },
            new[] { Hex(0x6E3426), Hex(0x96482F), Hex(0xB9653F), Hex(0xD58D5E) }, // rust, Mars-like
            new[] { Hex(0x2C2C33), Hex(0x41414B), Hex(0x5A5A66), Hex(0x7A7A86) } // dark basalt
        };
        private static readonly Color32[][] IcePalettes =
        {
            new[] { Hex(0x123A6B), Hex(0x1D5A94), Hex(0x2F7FBF), Hex(0x8FD3E8) },
            new[] { Hex(0x0F4A5A), Hex(0x1B6E80), Hex(0x2FA0B0), Hex(0xA6E6EA) }
        };
        private static readonly Color32[][] GasPalettes =
        {
            new[] { Hex(0x7A4A2E), Hex(0xB5733F), Hex(0xD9A96B), Hex(0xEFD9B0) },
            new[] { Hex(0x5D4A3A), Hex(0x9C8166), Hex(0xCBB08C), Hex(0xECE0C4) },
            new[] { Hex(0x6E3B33), Hex(0xA85A43), Hex(0xD68C62), Hex(0xF0C9A0) }
        };
        private static readonly Color32 DeepOcean = Hex(0x1F4F8A), Ocean = Hex(0x2E6FB0), Lowland = Hex(0x5F8A3C),
            Highland = Hex(0x7A6A3E), Peak = Hex(0x9A8A5A), Cloud = Hex(0xE8EEF2), Atmosphere = Hex(0xCFE9FF),
            GasStorm = Hex(0xB5432E), IceStorm = Hex(0xE9F6FB);

        private readonly Dictionary<long, Texture2D> _cache = new Dictionary<long, Texture2D>();
        private readonly Color32[] _pixels = new Color32[Size * Size];
        private readonly int[] _perm = new int[512];
        private readonly Vector2[] _grads = new Vector2[256];
        private readonly Vector3[] _craters = new Vector3[8];

        /// <summary>The planet's sprite for its class, seed and lifecycle state (painted once, then cached).</summary>
        public Texture2D Get(PlanetClass planet, ulong seed, PlanetLifecycleState state)
        {
            long key = (long)(seed * 0x9E3779B97F4A7C15UL) ^ ((long)planet << 56) ^ ((long)state << 60);
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            if (_cache.Count >= CacheLimit)
                Clear();
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Planet", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            Paint(planet, seed, state);
            texture.SetPixels32(_pixels);
            texture.Apply(false, true);
            _cache[key] = texture;
            return texture;
        }

        /// <summary>Destroys every painted texture.</summary>
        public void Clear()
        {
            foreach (var texture in _cache.Values)
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            _cache.Clear();
        }

        private void Paint(PlanetClass planet, ulong seed, PlanetLifecycleState state)
        {
            var rng = new SplitMix64(seed ^ 0x504C414E4554UL); // "PLANET"
            SeedNoise(ref rng);
            if (state == PlanetLifecycleState.TerraformedRecovery && planet == PlanetClass.Terrestrial)
                planet = PlanetClass.SuperEarth; // a restored rocky world is painted alive; giants keep their bands
            bool ring = planet == PlanetClass.GasGiant && rng.NextInt(0, 999) < 550;
            float radius = Size * (planet == PlanetClass.Terrestrial ? 0.30f
                : planet == PlanetClass.SuperEarth ? 0.38f
                : planet == PlanetClass.NeptuneLike ? 0.36f
                : ring ? 0.24f : 0.40f);
            float tilt = Range(ref rng, -0.35f, 0.35f);
            var palette = planet == PlanetClass.Terrestrial ? RockPalettes[rng.NextInt(0, RockPalettes.Length - 1)]
                : planet == PlanetClass.NeptuneLike ? IcePalettes[rng.NextInt(0, IcePalettes.Length - 1)]
                : planet == PlanetClass.GasGiant ? GasPalettes[rng.NextInt(0, GasPalettes.Length - 1)]
                : null;
            float ox = Range(ref rng, 0f, 100f), oy = Range(ref rng, 0f, 100f);
            int craters = rng.NextInt(4, 8);
            for (int i = 0; i < craters; i++)
                _craters[i] = new Vector3(Range(ref rng, -0.7f, 0.7f), Range(ref rng, -0.7f, 0.7f), Range(ref rng, 0.08f, 0.2f));
            float stormX = Range(ref rng, -0.4f, 0.4f);
            float stormY = Range(ref rng, 0.1f, 0.45f) * (rng.NextInt(0, 1) == 0 ? -1f : 1f);
            float sin = Mathf.Sin(tilt), cos = Mathf.Cos(tilt);
            float centre = Size * 0.5f;
            float edge = 2.5f / radius; // outline width, in planet radii

            for (int py = 0; py < Size; py++)
            for (int px = 0; px < Size; px++)
            {
                // Image coordinates with y down, as the sheet was designed; written flipped for Unity's bottom-up rows.
                float dx = (px + 0.5f - centre) / radius, dy = (py + 0.5f - centre) / radius;
                float rx = dx * cos - dy * sin, ry = dx * sin + dy * cos;
                float d2 = dx * dx + dy * dy;
                var colour = new Color32(0, 0, 0, 0);
                bool ringBack = false, ringFront = false;
                Color32 ringColour = default;
                if (ring && RingAt(rx, ry, palette, out ringColour))
                {
                    ringFront = ry > 0f;
                    ringBack = !ringFront;
                }
                if (ringBack)
                    colour = ringColour;
                if (d2 <= 1f)
                {
                    float z = Mathf.Sqrt(1f - d2);
                    float u = rx / (z + 0.6f), v = ry;
                    var surface = Surface(planet, palette, rx, ry, u, v, ox, oy, stormX, stormY, craters);
                    surface = Lifecycle(surface, state, rx, ry, ox, oy, u, v);
                    float lit = -0.55f * dx - 0.55f * dy + 0.62f * z;
                    surface = lit < 0.05f ? Scale(surface, 0.55f) : lit < 0.3f ? Scale(surface, 0.78f) : surface;
                    float d = Mathf.Sqrt(d2);
                    if (d > 1f - edge)
                        surface = Outline;
                    else if (Mathf.Abs(d - 0.78f) < 1.5f / radius && Angle(dx, dy) >= 205f && Angle(dx, dy) <= 245f)
                        surface = Blend(surface, new Color32(255, 255, 255, 255), 0.66f);
                    surface.a = (byte)Mathf.Clamp((1f - d) * radius * 255f + 128f, 0f, 255f);
                    colour = surface.a >= 255 || colour.a == 0 ? surface : Blend(colour, surface, surface.a / 255f);
                }
                else if (planet == PlanetClass.SuperEarth || planet == PlanetClass.NeptuneLike)
                {
                    float d = Mathf.Sqrt(d2);
                    float a = Angle(dx, dy);
                    if (d < 1f + 2.5f / radius && a >= 200f && a <= 300f)
                        colour = new Color32(Atmosphere.r, Atmosphere.g, Atmosphere.b, 220);
                }
                if (ringFront)
                    colour = ringColour;
                _pixels[(Size - 1 - py) * Size + px] = colour;
            }
        }

        private Color32 Surface(PlanetClass planet, Color32[] palette, float rx, float ry, float u, float v, float ox, float oy,
            float stormX, float stormY, int craters)
        {
            switch (planet)
            {
                case PlanetClass.Terrestrial:
                {
                    float h = Fbm(u * 2.2f + ox, v * 2.2f + oy, 4);
                    var c = h < -0.25f ? palette[0] : h < 0f ? palette[1] : h < 0.22f ? palette[2] : palette[3];
                    for (int i = 0; i < craters; i++)
                    {
                        var k = _craters[i];
                        float q = (rx - k.x) * (rx - k.x) + (ry - k.y) * (ry - k.y);
                        if (q < k.z * k.z)
                            c = q > k.z * k.z * 0.49f ? palette[0] : palette[1];
                    }
                    return c;
                }
                case PlanetClass.SuperEarth:
                {
                    float h = Fbm(u * 1.8f + ox, v * 1.8f + oy, 4);
                    var c = h < -0.18f ? DeepOcean : h < 0.02f ? Ocean : h < 0.18f ? Lowland : h < 0.32f ? Highland : Peak;
                    if (Fbm(u * 3f + oy, v * 3f + ox, 3) > 0.42f || Mathf.Abs(ry) > 0.9f)
                        c = Cloud;
                    return c;
                }
                default:
                {
                    bool gas = planet == PlanetClass.GasGiant;
                    float band = ry * (gas ? 6f : 3.5f) + Fbm(u * 1.5f + ox, v * 1.5f + oy, 3) * 1.2f;
                    int k = ((int)Mathf.Floor(band) % palette.Length + palette.Length) % palette.Length;
                    var c = palette[k];
                    float sx = (rx - stormX) / 0.22f, sy = (ry - stormY) / 0.11f;
                    if (sx * sx + sy * sy < 1f)
                        c = gas ? GasStorm : IceStorm;
                    return c;
                }
            }
        }

        // Mined worlds show dark drill scars, a husk is grey and dim, an irradiated world glows sickly yellow.
        private Color32 Lifecycle(Color32 c, PlanetLifecycleState state, float rx, float ry, float ox, float oy, float u, float v)
        {
            switch (state)
            {
                case PlanetLifecycleState.DeepCoreStripping:
                    return Fbm(u * 5f + oy, v * 5f + ox, 2) > 0.33f ? Scale(c, 0.35f) : c;
                case PlanetLifecycleState.DepletedBarrenHusk:
                {
                    byte grey = (byte)((c.r * 30 + c.g * 59 + c.b * 11) / 100);
                    return Scale(new Color32(grey, grey, grey, 255), 0.7f);
                }
                case PlanetLifecycleState.TerraformedRecovery:
                    return Blend(c, Lowland, 0.3f);
                case PlanetLifecycleState.IrradiatedDeadWorld:
                    return Blend(Scale(c, 0.6f), new Color32(0xC8, 0xC0, 0x30, 255), Fbm(u * 4f + ox, v * 4f + oy, 2) > 0.1f ? 0.55f : 0.25f);
                default:
                    return c;
            }
        }

        // Three thin bands of a tilted ring; the back half goes behind the planet, the front half over it.
        private static bool RingAt(float rx, float ry, Color32[] palette, out Color32 colour)
        {
            colour = default;
            float t = Mathf.Sqrt(rx * rx + ry * ry / (0.24f * 0.24f));
            if (t >= 1.40f && t <= 1.52f) colour = palette[3];
            else if (t >= 1.60f && t <= 1.72f) colour = palette[2];
            else if (t >= 1.78f && t <= 1.86f) colour = palette[3];
            else return false;
            return true;
        }

        private void SeedNoise(ref SplitMix64 rng)
        {
            for (int i = 0; i < 256; i++)
                _perm[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.NextInt(0, i);
                (_perm[i], _perm[j]) = (_perm[j], _perm[i]);
            }
            for (int i = 0; i < 256; i++)
            {
                _perm[256 + i] = _perm[i];
                float a = rng.NextInt(0, 65535) / 65536f * Mathf.PI * 2f;
                _grads[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
        }

        private float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 1f, f = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Noise(x * f, y * f);
                amp *= 0.5f;
                f *= 2f;
            }
            return sum;
        }

        private float Noise(float x, float y)
        {
            float fx = Mathf.Floor(x), fy = Mathf.Floor(y);
            int xi = (int)fx & 255, yi = (int)fy & 255;
            float xf = x - fx, yf = y - fy;
            float u = Fade(xf), v = Fade(yf);
            float a = Grad(xi, yi, xf, yf), b = Grad(xi + 1, yi, xf - 1f, yf);
            float c = Grad(xi, yi + 1, xf, yf - 1f), d = Grad(xi + 1, yi + 1, xf - 1f, yf - 1f);
            float top = a + u * (b - a);
            return top + v * (c + u * (d - c) - top);
        }

        private float Grad(int ix, int iy, float dx, float dy)
        {
            var g = _grads[_perm[_perm[ix] + iy]];
            return g.x * dx + g.y * dy;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        // Degrees clockwise from +x with y down, as an image editor measures arcs.
        private static float Angle(float dx, float dy)
        {
            float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            return a < 0f ? a + 360f : a;
        }

        private static float Range(ref SplitMix64 rng, float min, float max) =>
            min + (max - min) * (rng.NextInt(0, 1_000_000) / 1_000_000f);

        private static Color32 Scale(Color32 c, float k) =>
            new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a);

        private static Color32 Blend(Color32 a, Color32 b, float t) =>
            new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t),
                (byte)Mathf.Max(a.a, b.a));

        private static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
