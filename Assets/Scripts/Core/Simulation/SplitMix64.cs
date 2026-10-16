using System;

namespace Unruled.Core.Simulation
{
    /// <summary>Deterministic 64-bit PRNG (Steele/Lea/Flood SplitMix64). Same seed, same sequence on every platform.</summary>
    public struct SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed) => _state = seed;

        public ulong NextULong()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform-enough integer in [minInclusive, maxInclusive] (modulo bias is negligible for game ranges).</summary>
        public int NextInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
            ulong range = (ulong)((long)maxInclusive - minInclusive + 1);
            return (int)(minInclusive + (long)(NextULong() % range));
        }
    }
}
