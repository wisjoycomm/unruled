using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.Galaxy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Galaxy
{
    public class GalaxyCatalogTests
    {
        private static readonly GalaxyBalance Balance = DefaultGameBalance.CreateGalaxyBalance();

        [Test]
        public void StarId_PacksAndUnpacks_IncludingNegativeSectors()
        {
            var ids = new[] { new StarId(0, 0, 0), new StarId(-1999, 1999, 11), new StarId(1500, -3, 63) };
            foreach (var id in ids)
            {
                Assert.Greater(id.Packed, 0L);
                Assert.AreEqual(id, StarId.FromPacked(id.Packed));
            }
        }

        [Test]
        public void SameSeed_SameSector_SameStars()
        {
            var a = new List<StarRecord>();
            var b = new List<StarRecord>();
            new GalaxyCatalog(Balance, 42).GetSector(120, -340, a);
            new GalaxyCatalog(Balance, 42).GetSector(120, -340, b);
            Assert.Greater(a.Count, 0);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Id, b[i].Id);
                Assert.AreEqual(a[i].X, b[i].X);
                Assert.AreEqual(a[i].Y, b[i].Y);
                Assert.AreEqual(a[i].SystemSeed, b[i].SystemSeed);
            }
        }

        [Test]
        public void DifferentSeeds_GiveDifferentGalaxies()
        {
            var a = new List<StarRecord>();
            var b = new List<StarRecord>();
            new GalaxyCatalog(Balance, 1).GetSector(300, 300, a);
            new GalaxyCatalog(Balance, 2).GetSector(300, 300, b);
            bool same = a.Count == b.Count;
            for (int i = 0; same && i < a.Count; i++)
                same = a[i].X == b[i].X && a[i].Y == b[i].Y;
            Assert.IsFalse(same);
        }

        [Test]
        public void Stars_LieInsideTheirSector()
        {
            var catalog = new GalaxyCatalog(Balance, 7);
            var stars = new List<StarRecord>();
            for (int sx = -5; sx <= 5; sx++)
            for (int sy = -5; sy <= 5; sy++)
            {
                catalog.GetSector(sx, sy, stars);
                foreach (var s in stars)
                {
                    catalog.SectorOf(s.X, s.Y, out int cx, out int cy);
                    Assert.AreEqual(sx, cx);
                    Assert.AreEqual(sy, cy);
                }
            }
        }

        [Test]
        public void OutsideTheDisc_IsEmpty_CoreIsDense()
        {
            var catalog = new GalaxyCatalog(Balance, 7);
            Assert.AreEqual(0, catalog.StarCount(Balance.RadiusSectors + 1, 0));
            Assert.GreaterOrEqual(catalog.StarCount(0, 0), Balance.MaxStarsPerSector * 2 / 3);
            Assert.LessOrEqual(catalog.StarCount(0, 0), Balance.MaxStarsPerSector);
        }

        [Test]
        public void ArmsAreDenserThanTheSpaceBetween()
        {
            var catalog = new GalaxyCatalog(Balance, 99);
            long arm = 0, total = 0;
            int ring = Balance.RadiusSectors / 2;
            for (int a = 0; a < FixedMath.AngleFull; a += 256)
            {
                int sx = (int)(((long)ring * FixedMath.Cos(a)) >> 16);
                int sy = (int)(((long)ring * FixedMath.Sin(a)) >> 16);
                int count = catalog.StarCount(sx, sy);
                total++;
                if (count > Balance.FieldStarsPerSector)
                    arm++;
            }
            // Around one ring, some sectors are on an arm and most are not.
            Assert.Greater(arm, 0);
            Assert.Less(arm, total / 2);
        }

        [Test]
        public void TryGetStar_FindsEveryStarOfItsSector()
        {
            var catalog = new GalaxyCatalog(Balance, 5);
            var stars = new List<StarRecord>();
            catalog.GetSector(-40, 25, stars);
            foreach (var s in stars)
            {
                Assert.IsTrue(catalog.TryGetStar(s.Id, out var found));
                Assert.AreEqual(s.X, found.X);
                Assert.AreEqual(s.SystemSeed, found.SystemSeed);
            }
            Assert.IsFalse(catalog.TryGetStar(new StarId(-40, 25, stars.Count), out _));
        }

        [Test]
        public void StarsInRect_ReturnsOnlyStarsInside_AndRespectsTheLimit()
        {
            var catalog = new GalaxyCatalog(Balance, 11);
            var stars = new List<StarRecord>();
            Assert.IsTrue(catalog.StarsInRect(-2500, -2500, 2500, 2500, stars, 10_000));
            Assert.Greater(stars.Count, 0);
            foreach (var s in stars)
            {
                Assert.That(s.X, Is.InRange(-2500, 2500));
                Assert.That(s.Y, Is.InRange(-2500, 2500));
            }
            Assert.IsFalse(catalog.StarsInRect(-2500, -2500, 2500, 2500, stars, 3));
            Assert.AreEqual(3, stars.Count);
        }

        [Test]
        public void Neighbours_AreSymmetric_WithinReach_AndSorted()
        {
            var catalog = new GalaxyCatalog(Balance, 21);
            var sector = new List<StarRecord>();
            var near = new List<StarId>();
            var back = new List<StarId>();
            int checkedLinks = 0;
            for (int sx = 300; sx < 306; sx++)
            {
                catalog.GetSector(sx, 40, sector);
                var copy = new List<StarRecord>(sector);
                foreach (var s in copy)
                {
                    catalog.Neighbours(s.Id, near);
                    for (int i = 1; i < near.Count; i++)
                        Assert.Less(near[i - 1].Packed, near[i].Packed);
                    var nearCopy = new List<StarId>(near);
                    foreach (var n in nearCopy)
                    {
                        Assert.AreNotEqual(s.Id, n);
                        Assert.IsTrue(catalog.TryGetStar(n, out var other));
                        long dx = other.X - s.X, dy = other.Y - s.Y;
                        Assert.LessOrEqual(dx * dx + dy * dy, (long)Balance.MaxNeighbourDistance * Balance.MaxNeighbourDistance);
                        catalog.Neighbours(n, back);
                        CollectionAssert.Contains(back, s.Id);
                        checkedLinks++;
                    }
                }
            }
            Assert.Greater(checkedLinks, 0);
        }

        [Test]
        public void Classes_IncludeHubsAndHomes_NoHomeInTheCore()
        {
            var catalog = new GalaxyCatalog(Balance, 3);
            var stars = new List<StarRecord>();
            int hubs = 0, homes = 0;
            for (int sx = -Balance.CoreRadiusSectors; sx <= Balance.CoreRadiusSectors; sx += 3)
            {
                catalog.GetSector(sx, 0, stars);
                foreach (var s in stars)
                    if (catalog.StarCount(s.Id.SectorX, s.Id.SectorY) == Balance.MaxStarsPerSector || System.Math.Abs(sx) < Balance.CoreRadiusSectors / 2)
                        Assert.AreNotEqual(StarClass.Home, s.Class);
            }
            for (int sx = 400; sx < 1400; sx++)
            {
                catalog.GetSector(sx, sx / 3, stars);
                foreach (var s in stars)
                {
                    if (s.Class == StarClass.Hub) hubs++;
                    if (s.Class == StarClass.Home) homes++;
                }
            }
            Assert.Greater(hubs, 0);
            Assert.Greater(homes, hubs);
        }
    }
}
