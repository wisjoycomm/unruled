using System;
using NUnit.Framework;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    public class DeterministicMathTests
    {
        [Test]
        public void SplitMix64_MatchesReferenceVector()
        {
            var rng = new SplitMix64(0);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, rng.NextULong());
        }

        [Test]
        public void SplitMix64_NextInt_StaysInInclusiveRange()
        {
            var rng = new SplitMix64(42);
            bool sawMin = false, sawMax = false;
            for (int i = 0; i < 2000; i++)
            {
                int v = rng.NextInt(-3, 3);
                Assert.That(v, Is.InRange(-3, 3));
                sawMin |= v == -3;
                sawMax |= v == 3;
            }
            Assert.IsTrue(sawMin && sawMax);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(2, 1));
        }

        [Test]
        public void FixedMath_SinCos_ExactAtQuadrants()
        {
            Assert.AreEqual(0, FixedMath.Sin(0));
            Assert.AreEqual(FixedMath.One, FixedMath.Sin(FixedMath.AngleQuarter));
            Assert.AreEqual(0, FixedMath.Sin(FixedMath.AngleQuarter * 2));
            Assert.AreEqual(-FixedMath.One, FixedMath.Sin(FixedMath.AngleQuarter * 3));
            Assert.AreEqual(FixedMath.One, FixedMath.Cos(0));
            Assert.AreEqual(FixedMath.Sin(100), FixedMath.Sin(100 + FixedMath.AngleFull));
        }

        [Test]
        public void FixedMath_Sin_WithinToleranceOfDouble()
        {
            for (int angle = 0; angle < FixedMath.AngleFull; angle += 97)
            {
                double expected = Math.Sin(angle * 2.0 * Math.PI / FixedMath.AngleFull);
                double actual = FixedMath.Sin(angle) / (double)FixedMath.One;
                Assert.AreEqual(expected, actual, 5e-4, $"angle {angle}");
            }
        }
    }
}
