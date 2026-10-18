using System;
using NUnit.Framework;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    public class ChaCha8Tests
    {
        // RFC 7539 §2.1.1 test vector for the ChaCha quarter round.
        [Test]
        public void QuarterRound_MatchesRfc7539TestVector()
        {
            uint a = 0x11111111U;
            uint b = 0x01020304U;
            uint c = 0x9b8d6f43U;
            uint d = 0x01234567U;

            ChaCha8.QuarterRound(ref a, ref b, ref c, ref d);

            Assert.AreEqual(0xea2a92f4U, a);
            Assert.AreEqual(0xcb1cf8ceU, b);
            Assert.AreEqual(0x4581472eU, c);
            Assert.AreEqual(0x5881c4bbU, d);
        }

        // Known-answer test: ChaCha8 (8 rounds) with all-zero 256-bit key, counter 0 and 64-bit nonce 0
        // (draft-strombergson-chacha-test-vectors TC1, 8 rounds, little-endian 32-bit words).
        [Test]
        public void ComputeBlock_AllZeroKeyAndNonce_MatchesKnownAnswerVector()
        {
            Span<uint> key = stackalloc uint[8];
            Span<uint> block = stackalloc uint[16];

            ChaCha8.ComputeBlock(key, counter: 0UL, nonce0: 0U, nonce1: 0U, block);

            uint[] expected =
            {
                0x2fef003eU, 0xd6405f89U, 0xe8b85b7fU, 0xa1a5091fU,
                0xc30e842cU, 0x3b7f9aceU, 0x88e11b18U, 0x1e1a71efU,
                0x72e14c98U, 0x416f21b9U, 0x6753449fU, 0x19566d45U,
                0xa3424a31U, 0x01b086daU, 0xb8fd7b38U, 0x42fe0c0eU
            };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], block[i], $"word {i}");
        }

        [Test]
        public void SameSeed_ProducesIdenticalStreamAcrossBlocks_AndDifferentSeedsDiffer()
        {
            var a = new ChaCha8(0xCAFEBABE_DEADBEEFUL);
            var b = new ChaCha8(0xCAFEBABE_DEADBEEFUL);
            var c = new ChaCha8(0xCAFEBABE_DEADBEEEUL);

            bool anyDiff = false;
            for (int i = 0; i < 40; i++)
            {
                ulong va = a.NextULong();
                ulong vb = b.NextULong();
                ulong vc = c.NextULong();
                Assert.AreEqual(va, vb, $"draw {i}");
                if (va != vc)
                    anyDiff = true;
            }
            Assert.IsTrue(anyDiff);

            var u = new ChaCha8(42UL);
            var l = new ChaCha8(42UL);
            uint lo = u.NextUInt();
            uint hi = u.NextUInt();
            Assert.AreEqual(lo | ((ulong)hi << 32), l.NextULong());
        }

        [Test]
        public void NextInt_StaysInRange_AndRejectsInvertedBounds()
        {
            var rng = new ChaCha8(1234UL);
            for (int i = 0; i < 200; i++)
            {
                int v = rng.NextInt(-50, 50);
                Assert.That(v, Is.InRange(-50, 50));
            }
            Assert.AreEqual(7, rng.NextInt(7, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 4));
        }
    }
}
