using System;
using System.Collections.Generic;
using Unruled.Core.Simulation;

namespace Unruled.Core.Galaxy
{
    /// <summary>What a star can become when it is materialised.</summary>
    public enum StarClass : byte
    {
        Frontier = 0, // uninhabited, open to settle
        Home = 1, // may host a new player's home system
        Hub = 2 // a contested system with one of THE SYSTEM's Citadels
    }

    /// <summary>One catalog star. Positions are integer galaxy units, origin at the galactic core.</summary>
    public struct StarRecord
    {
        public StarId Id;
        public int X;
        public int Y;
        public StarClass Class;
        public ulong SystemSeed;
    }

    /// <summary>
    /// The whole galaxy as a pure function of the world seed (the No Man's Sky approach): every sector's stars,
    /// their positions, classes, system seeds and hyperlane neighbours are recomputed on demand, so the catalog
    /// can hold tens of millions of stars while storing nothing. Only stars a game actually visits become
    /// simulated systems. Integer math only; the same seed gives the same galaxy on every platform. Scratch buffers
    /// make one instance single-threaded.
    /// </summary>
    public sealed class GalaxyCatalog
    {
        private const ulong SectorSalt = 0x9E3779B97F4A7C15UL;
        private const ulong RowSalt = 0xC2B2AE3D27D4EB4FUL;

        private readonly GalaxyBalance _balance;
        private readonly ulong _seed;
        private readonly int _twist;
        private readonly int _armStep;
        private readonly StarRecord[] _sector = new StarRecord[GalaxyBalance.MaxStarsPerSectorCap];
        private readonly List<StarRecord> _block = new List<StarRecord>(9 * GalaxyBalance.MaxStarsPerSectorCap);
        private readonly List<StarRecord> _outer = new List<StarRecord>(25 * GalaxyBalance.MaxStarsPerSectorCap);
        private readonly long[] _nearest;
        private readonly long[] _nearestPacked;

        public GalaxyCatalog(GalaxyBalance balance, ulong worldSeed)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _seed = worldSeed;
            var rng = new SplitMix64(worldSeed ^ 0x5DEECE66DUL);
            _twist = (int)(rng.NextULong() & FixedMath.AngleMask);
            _armStep = FixedMath.AngleFull / balance.ArmCount;
            _nearest = new long[balance.NeighbourCount];
            _nearestPacked = new long[balance.NeighbourCount];
        }

        public GalaxyBalance Balance => _balance;

        /// <summary>Whether a sector lies inside the galactic disc.</summary>
        public bool InDisc(int sectorX, int sectorY)
        {
            long r = _balance.RadiusSectors;
            return (long)sectorX * sectorX + (long)sectorY * sectorY <= r * r;
        }

        /// <summary>How many stars a sector holds: the core is dense, the arms busy, the space between thin.</summary>
        public int StarCount(int sectorX, int sectorY)
        {
            if (!InDisc(sectorX, sectorY))
                return 0;
            var rng = SectorRng(sectorX, sectorY);
            return StarCount(sectorX, sectorY, ref rng);
        }

        /// <summary>The sector containing a galaxy position.</summary>
        public void SectorOf(int x, int y, out int sectorX, out int sectorY)
        {
            int half = _balance.SectorSize / 2;
            sectorX = FloorDiv(x + half, _balance.SectorSize);
            sectorY = FloorDiv(y + half, _balance.SectorSize);
        }

        /// <summary>Writes a sector's stars into <paramref name="output"/> (cleared first), in index order.</summary>
        public void GetSector(int sectorX, int sectorY, List<StarRecord> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            int count = FillSector(sectorX, sectorY);
            for (int i = 0; i < count; i++)
                output.Add(_sector[i]);
        }

        public bool TryGetStar(StarId id, out StarRecord star)
        {
            int count = FillSector(id.SectorX, id.SectorY);
            if (id.Index < count)
            {
                star = _sector[id.Index];
                return true;
            }
            star = default;
            return false;
        }

        /// <summary>
        /// Stars inside a rectangle of galaxy units, appended to <paramref name="output"/> (cleared first) in sector
        /// row order, stopping at <paramref name="limit"/>. Returns false when the limit cut the result short.
        /// </summary>
        public bool StarsInRect(int minX, int minY, int maxX, int maxY, List<StarRecord> output, int limit)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            SectorOf(minX, minY, out int sx0, out int sy0);
            SectorOf(maxX, maxY, out int sx1, out int sy1);
            for (int sy = sy0; sy <= sy1; sy++)
            for (int sx = sx0; sx <= sx1; sx++)
            {
                int count = FillSector(sx, sy);
                for (int i = 0; i < count; i++)
                {
                    ref var s = ref _sector[i];
                    if (s.X < minX || s.X > maxX || s.Y < minY || s.Y > maxY)
                        continue;
                    if (output.Count >= limit)
                        return false;
                    output.Add(s);
                }
            }
            return true;
        }

        /// <summary>
        /// The star's hyperlane neighbours, written to <paramref name="output"/> (cleared first). Two stars are linked
        /// when either is among the other's <see cref="GalaxyBalance.NeighbourCount"/> nearest within
        /// <see cref="GalaxyBalance.MaxNeighbourDistance"/>, so links are symmetric. Sorted by packed id.
        /// </summary>
        public void Neighbours(StarId id, List<StarId> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (!TryGetStar(id, out var self))
                return;
            CollectBlock(id.SectorX, id.SectorY, 1, _block);
            // Candidates: every star within reach in the 3x3 block (reach <= sector size keeps them inside it).
            for (int i = 0; i < _block.Count; i++)
            {
                var other = _block[i];
                if (other.Id == id || Distance2(self, other) > Reach2)
                    continue;
                if (IsNearest(self, other, _block) || IsNearestAround(other, self))
                    output.Add(other.Id);
            }
            output.Sort(ComparePacked);
        }

        private long Reach2 => (long)_balance.MaxNeighbourDistance * _balance.MaxNeighbourDistance;

        // Whether `other` is among `self`'s k nearest within reach, looking only at `candidates` (a block around self).
        private bool IsNearest(in StarRecord self, in StarRecord other, List<StarRecord> candidates)
        {
            int k = _balance.NeighbourCount;
            int found = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.Id == self.Id)
                    continue;
                long d = Distance2(self, c);
                if (d > Reach2)
                    continue;
                InsertNearest(d, c.Id.Packed, ref found, k);
            }
            long target = other.Id.Packed;
            for (int i = 0; i < found; i++)
                if (_nearestPacked[i] == target)
                    return true;
            return false;
        }

        // `self`'s k-nearest test seen from `other`: needs the 3x3 block around other's sector.
        private bool IsNearestAround(in StarRecord other, in StarRecord self)
        {
            CollectBlock(other.Id.SectorX, other.Id.SectorY, 1, _outer);
            return IsNearest(other, self, _outer);
        }

        // Keeps the k smallest (distance, packed id) pairs; ties broken by packed id for determinism.
        private void InsertNearest(long distance2, long packed, ref int found, int k)
        {
            int pos = found;
            while (pos > 0 && (_nearest[pos - 1] > distance2 || (_nearest[pos - 1] == distance2 && _nearestPacked[pos - 1] > packed)))
                pos--;
            if (pos >= k)
                return;
            int last = Math.Min(found, k - 1);
            for (int i = last; i > pos; i--)
            {
                _nearest[i] = _nearest[i - 1];
                _nearestPacked[i] = _nearestPacked[i - 1];
            }
            _nearest[pos] = distance2;
            _nearestPacked[pos] = packed;
            if (found < k)
                found++;
        }

        private void CollectBlock(int sectorX, int sectorY, int radius, List<StarRecord> output)
        {
            output.Clear();
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int count = FillSector(sectorX + dx, sectorY + dy);
                for (int i = 0; i < count; i++)
                    output.Add(_sector[i]);
            }
        }

        // Generates one sector into _sector; returns its star count. The draw order is part of the galaxy contract.
        private int FillSector(int sectorX, int sectorY)
        {
            if (!InDisc(sectorX, sectorY))
                return 0;
            var rng = SectorRng(sectorX, sectorY);
            int count = StarCount(sectorX, sectorY, ref rng);
            int size = _balance.SectorSize;
            int originX = sectorX * size - size / 2;
            int originY = sectorY * size - size / 2;
            bool core = IsCore(sectorX, sectorY);
            for (int i = 0; i < count; i++)
            {
                int x = originX + rng.NextInt(0, size - 1);
                int y = originY + rng.NextInt(0, size - 1);
                int roll = rng.NextInt(0, 999);
                var cls = roll < _balance.HubSharePermille ? StarClass.Hub
                    : !core && roll < _balance.HubSharePermille + _balance.HomeSharePermille ? StarClass.Home
                    : StarClass.Frontier;
                _sector[i] = new StarRecord
                {
                    Id = new StarId(sectorX, sectorY, i),
                    X = x,
                    Y = y,
                    Class = cls,
                    SystemSeed = rng.NextULong()
                };
            }
            return count;
        }

        private int StarCount(int sectorX, int sectorY, ref SplitMix64 rng)
        {
            int baseCount = IsCore(sectorX, sectorY) ? _balance.MaxStarsPerSector
                : InArm(sectorX, sectorY) ? _balance.ArmStarsPerSector
                : _balance.FieldStarsPerSector;
            if (baseCount == 0)
                return 0;
            int jitter = baseCount / 3;
            return Math.Max(0, baseCount - rng.NextInt(0, jitter));
        }

        private bool IsCore(int sectorX, int sectorY)
        {
            long c = _balance.CoreRadiusSectors;
            return (long)sectorX * sectorX + (long)sectorY * sectorY <= c * c;
        }

        // An arm's centre line at radius r sits at angle twist + arm * step + r * winding; the sector is in the arm
        // when it lies within ArmWidthAngle of that line (as an arc length at its own radius).
        private bool InArm(int sectorX, int sectorY)
        {
            long r = FixedMath.Sqrt((long)sectorX * sectorX + (long)sectorY * sectorY);
            if (r == 0)
                return true;
            long width = Math.Max(1L, r * _balance.ArmWidthAngle * 6 / FixedMath.AngleFull + 1); // arc length ~ r * angle * 2pi
            for (int arm = 0; arm < _balance.ArmCount; arm++)
            {
                int angle = (int)((_twist + arm * _armStep + r * _balance.ArmWindingPerSector) & FixedMath.AngleMask);
                long ax = (r * FixedMath.Cos(angle)) >> 16;
                long ay = (r * FixedMath.Sin(angle)) >> 16;
                long dx = sectorX - ax, dy = sectorY - ay;
                if (dx * dx + dy * dy <= width * width)
                    return true;
            }
            return false;
        }

        private SplitMix64 SectorRng(int sectorX, int sectorY)
        {
            ulong key = _seed ^ ((ulong)(uint)sectorX * SectorSalt) ^ ((ulong)(uint)sectorY * RowSalt);
            var mix = new SplitMix64(key);
            return new SplitMix64(mix.NextULong());
        }

        private static long Distance2(in StarRecord a, in StarRecord b)
        {
            long dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        private static int ComparePacked(StarId a, StarId b) => a.Packed.CompareTo(b.Packed);
    }
}
