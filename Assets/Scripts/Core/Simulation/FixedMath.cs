namespace Unruled.Core.Simulation
{
    /// <summary>
    /// Integer trigonometry for deterministic simulation. Angles are binary angle units (65,536 = full turn);
    /// results are Q16 fixed-point (65,536 = 1.0). Max error about 1e-4.
    /// </summary>
    public static class FixedMath
    {
        public const int One = 1 << 16;
        public const int AngleFull = 1 << 16;
        public const int AngleQuarter = AngleFull / 4;
        public const int AngleMask = AngleFull - 1;

        // sin(pi/2 * t) ~= A t - B t^3 + C t^5 with A = pi/2 and f(1) = 1, f'(1) = 0 (Q16 coefficients).
        private const long SinA = 102944;
        private const long SinB = 42047;
        private const long SinC = 4640;

        public static int Sin(int angle)
        {
            angle &= AngleMask;
            int quadrant = angle / AngleQuarter;
            int x = angle % AngleQuarter;
            if (quadrant == 1 || quadrant == 3)
                x = AngleQuarter - x;
            int s = SinQuarter(x);
            return quadrant >= 2 ? -s : s;
        }

        public static int Cos(int angle) => Sin(angle + AngleQuarter);

        /// <summary>sin for x in [0, AngleQuarter], as Q16.</summary>
        private static int SinQuarter(int x)
        {
            long t = (long)x * One / AngleQuarter;
            long t2 = (t * t) >> 16;
            long inner = SinB - ((t2 * SinC) >> 16);
            inner = SinA - ((t2 * inner) >> 16);
            long result = (t * inner) >> 16;
            return result > One ? One : (int)result;
        }
    }
}
