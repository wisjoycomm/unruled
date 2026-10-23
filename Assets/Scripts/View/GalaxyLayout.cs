using System.Collections.Generic;
using UnityEngine;

namespace Unruled.View
{
    /// <summary>
    /// Where each star system sits on the galaxy chart, and the background star field: a two-armed spiral galaxy
    /// generated from the world seed, so every game gets its own map but the same game always draws the same one.
    /// The contested system sits near the bright core, home systems along the arms, frontier systems out past their
    /// home. Purely a drawing aid: positions never feed the simulation.
    /// </summary>
    public sealed class GalaxyLayout
    {
        public const byte Frontier = 0, Home = 1, Contested = 2;

        /// <summary>Galaxy radius in board units.</summary>
        public const float Radius = 1500f;

        private const int ArmStars = 2600;
        private const int CoreStars = 500;
        private const int Nebulae = 9;
        private const float ArmTurns = 1.55f; // how far each arm winds, in turns
        private const float MinSpacing = 300f;

        public readonly List<Vector2> Systems = new List<Vector2>();
        public readonly List<Vector2> Stars = new List<Vector2>(ArmStars + CoreStars);
        public readonly List<Color32> StarColours = new List<Color32>(ArmStars + CoreStars);
        public readonly List<float> StarSizes = new List<float>(ArmStars + CoreStars);
        public readonly List<Vector3> Nebula = new List<Vector3>(Nebulae); // x, y, radius
        public readonly List<Color32> NebulaColours = new List<Color32>(Nebulae);

        /// <summary>Rotation of the whole spiral, from the seed.</summary>
        public float Twist { get; private set; }

        /// <param name="kinds">Per cluster id: <see cref="Frontier"/>, <see cref="Home"/> or <see cref="Contested"/>.</param>
        public void Build(ulong seed, IReadOnlyList<byte> kinds, Color32 starInk, Color32 starWarm, Color32 starHot,
            Color32 nebulaA, Color32 nebulaB)
        {
            var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
            Twist = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            BuildStars(rng, starInk, starWarm, starHot);
            BuildNebulae(rng, nebulaA, nebulaB);
            PlaceSystems(rng, kinds);
        }

        /// <summary>A point on spiral arm <paramref name="arm"/> (0 or 1) at fraction <paramref name="t"/> from the core.</summary>
        public Vector2 ArmPoint(int arm, float t)
        {
            float angle = Twist + arm * Mathf.PI + t * ArmTurns * 2f * Mathf.PI;
            float r = Radius * (0.12f + 0.88f * t);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }

        private void BuildStars(System.Random rng, Color32 ink, Color32 warm, Color32 hot)
        {
            Stars.Clear();
            StarColours.Clear();
            StarSizes.Clear();
            for (int i = 0; i < ArmStars; i++)
            {
                int arm = i & 1;
                float t = Mathf.Pow((float)rng.NextDouble(), 0.75f);
                var p = ArmPoint(arm, t);
                float spread = Radius * (0.05f + 0.09f * t);
                p += new Vector2(Gauss(rng), Gauss(rng)) * spread;
                AddStar(rng, p, ink, warm, hot);
            }
            for (int i = 0; i < CoreStars; i++)
            {
                var p = new Vector2(Gauss(rng), Gauss(rng)) * (Radius * 0.11f);
                AddStar(rng, p, warm, warm, ink);
            }
        }

        private void AddStar(System.Random rng, Vector2 p, Color32 ink, Color32 warm, Color32 hot)
        {
            double roll = rng.NextDouble();
            var c = roll < 0.82 ? ink : roll < 0.95 ? warm : hot;
            c.a = (byte)(50 + rng.Next(150));
            Stars.Add(p);
            StarColours.Add(c);
            StarSizes.Add(roll > 0.985 ? 3.5f : 1.4f + (float)rng.NextDouble() * 1.4f);
        }

        private void BuildNebulae(System.Random rng, Color32 a, Color32 b)
        {
            Nebula.Clear();
            NebulaColours.Clear();
            for (int i = 0; i < Nebulae; i++)
            {
                var p = ArmPoint(i & 1, 0.25f + 0.7f * (float)rng.NextDouble());
                p += new Vector2(Gauss(rng), Gauss(rng)) * (Radius * 0.05f);
                Nebula.Add(new Vector3(p.x, p.y, Radius * (0.07f + 0.08f * (float)rng.NextDouble())));
                NebulaColours.Add((i % 3) == 0 ? b : a);
            }
        }

        // Contested near the core; homes spread evenly in angle along the arms; each frontier further out past a home.
        private void PlaceSystems(System.Random rng, IReadOnlyList<byte> kinds)
        {
            Systems.Clear();
            int homes = 0;
            for (int c = 0; c < kinds.Count; c++)
                if (kinds[c] == Home)
                    homes++;
            int homeSlot = 0, frontierSlot = 0;
            for (int c = 0; c < kinds.Count; c++)
            {
                Vector2 p;
                if (kinds[c] == Contested)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Radius * 0.16f);
                }
                else
                {
                    bool home = kinds[c] == Home;
                    int slot = home ? homeSlot++ : frontierSlot++;
                    float baseAngle = Twist + 2f * Mathf.PI * slot / Mathf.Max(1, homes);
                    p = Vector2.zero;
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        float angle = baseAngle + (home ? 0f : 0.42f) + Gauss(rng) * 0.12f;
                        float r = Radius * (home ? 0.5f + 0.12f * (float)rng.NextDouble() : 0.82f + 0.14f * (float)rng.NextDouble());
                        p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                        if (FarFromOthers(p))
                            break;
                    }
                }
                Systems.Add(p);
            }
        }

        private bool FarFromOthers(Vector2 p)
        {
            for (int i = 0; i < Systems.Count; i++)
                if ((Systems[i] - p).sqrMagnitude < MinSpacing * MinSpacing)
                    return false;
            return true;
        }

        private static float Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }
}
