using System;

namespace Unruled.Core.Simulation
{
    /// <summary>
    /// Deterministic ChaCha8 CSPRNG / stream generator (RFC 7539 quarter-round core with 8 rounds = 4 double-rounds,
    /// GDD §2.1 A). A 64-bit seed expands through <see cref="SplitMix64"/> into a 256-bit key and 64-bit nonce; each
    /// 64-byte block increments the 64-bit block counter and feeds forward the initial state.
    /// </summary>
    public unsafe struct ChaCha8
    {
        private const uint C0 = 0x61707865U;
        private const uint C1 = 0x3320646EU;
        private const uint C2 = 0x79622D32U;
        private const uint C3 = 0x6B206574U;
        private const int BlockWords = 16;

        private fixed uint _key[8];
        private uint _nonce0;
        private uint _nonce1;
        private ulong _counter;
        private fixed uint _block[BlockWords];
        private int _index;

        public ChaCha8(ulong seed)
        {
            var sm = new SplitMix64(seed);
            for (int i = 0; i < 4; i++)
            {
                ulong word = sm.NextULong();
                _key[i * 2] = (uint)word;
                _key[i * 2 + 1] = (uint)(word >> 32);
            }
            ulong nonce = sm.NextULong();
            _nonce0 = (uint)nonce;
            _nonce1 = (uint)(nonce >> 32);
            _counter = 0;
            _index = BlockWords;
        }

        public uint NextUInt()
        {
            if (_index >= BlockWords)
                Refill();
            return _block[_index++];
        }

        public ulong NextULong()
        {
            uint lo = NextUInt();
            uint hi = NextUInt();
            return lo | ((ulong)hi << 32);
        }

        /// <summary>Uniform-enough integer in [minInclusive, maxInclusive] (modulo bias is negligible for game ranges).</summary>
        public int NextInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
            ulong range = (ulong)((long)maxInclusive - minInclusive + 1);
            return (int)(minInclusive + (long)(NextULong() % range));
        }

        /// <summary>RFC 7539 §2.1 quarter-round on four 32-bit words.</summary>
        public static void QuarterRound(ref uint a, ref uint b, ref uint c, ref uint d)
        {
            a += b; d ^= a; d = (d << 16) | (d >> 16);
            c += d; b ^= c; b = (b << 12) | (b >> 20);
            a += b; d ^= a; d = (d << 8) | (d >> 24);
            c += d; b ^= c; b = (b << 7) | (b >> 25);
        }

        /// <summary>Computes one 64-byte (16-word) ChaCha8 block for the given 8-word key, 64-bit counter and 64-bit nonce.</summary>
        public static void ComputeBlock(
            ReadOnlySpan<uint> key, ulong counter, uint nonce0, uint nonce1, Span<uint> output)
        {
            if (key.Length < 8) throw new ArgumentException("Key needs 8 words.", nameof(key));
            if (output.Length < BlockWords) throw new ArgumentException("Output needs 16 words.", nameof(output));

            uint s0 = C0, s1 = C1, s2 = C2, s3 = C3;
            uint s4 = key[0], s5 = key[1], s6 = key[2], s7 = key[3];
            uint s8 = key[4], s9 = key[5], s10 = key[6], s11 = key[7];
            uint s12 = (uint)counter, s13 = (uint)(counter >> 32);
            uint s14 = nonce0, s15 = nonce1;

            uint w0 = s0, w1 = s1, w2 = s2, w3 = s3;
            uint w4 = s4, w5 = s5, w6 = s6, w7 = s7;
            uint w8 = s8, w9 = s9, w10 = s10, w11 = s11;
            uint w12 = s12, w13 = s13, w14 = s14, w15 = s15;

            for (int r = 0; r < 4; r++)
            {
                QuarterRound(ref w0, ref w4, ref w8, ref w12);
                QuarterRound(ref w1, ref w5, ref w9, ref w13);
                QuarterRound(ref w2, ref w6, ref w10, ref w14);
                QuarterRound(ref w3, ref w7, ref w11, ref w15);

                QuarterRound(ref w0, ref w5, ref w10, ref w15);
                QuarterRound(ref w1, ref w6, ref w11, ref w12);
                QuarterRound(ref w2, ref w7, ref w8, ref w13);
                QuarterRound(ref w3, ref w4, ref w9, ref w14);
            }

            output[0] = w0 + s0; output[1] = w1 + s1; output[2] = w2 + s2; output[3] = w3 + s3;
            output[4] = w4 + s4; output[5] = w5 + s5; output[6] = w6 + s6; output[7] = w7 + s7;
            output[8] = w8 + s8; output[9] = w9 + s9; output[10] = w10 + s10; output[11] = w11 + s11;
            output[12] = w12 + s12; output[13] = w13 + s13; output[14] = w14 + s14; output[15] = w15 + s15;
        }

        private void Refill()
        {
            fixed (uint* k = _key)
            fixed (uint* b = _block)
            {
                ComputeBlock(new ReadOnlySpan<uint>(k, 8), _counter, _nonce0, _nonce1, new Span<uint>(b, BlockWords));
            }
            _counter++;
            _index = 0;
        }
    }
}
