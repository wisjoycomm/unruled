using System;

namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// A star in the procedural galaxy: its sector (signed, centred on the galactic core) and its index in that
    /// sector. Packs into one <see cref="long"/> for saves and commands; 0 is "none".
    /// </summary>
    public readonly struct StarId : IEquatable<StarId>
    {
        private const int SectorBits = 24;
        private const int IndexBits = 8;
        private const int SectorBias = 1 << (SectorBits - 1);
        private const long SectorMask = (1L << SectorBits) - 1;
        private const long IndexMask = (1L << IndexBits) - 1;

        public readonly int SectorX;
        public readonly int SectorY;
        public readonly int Index;

        public StarId(int sectorX, int sectorY, int index)
        {
            if (sectorX <= -SectorBias || sectorX >= SectorBias) throw new ArgumentOutOfRangeException(nameof(sectorX));
            if (sectorY <= -SectorBias || sectorY >= SectorBias) throw new ArgumentOutOfRangeException(nameof(sectorY));
            if (index < 0 || index > IndexMask) throw new ArgumentOutOfRangeException(nameof(index));
            SectorX = sectorX;
            SectorY = sectorY;
            Index = index;
        }

        /// <summary>Never 0: the biased sector coordinates are always positive.</summary>
        public long Packed =>
            ((long)(SectorX + SectorBias) << (SectorBits + IndexBits)) |
            ((long)(SectorY + SectorBias) << IndexBits) |
            (uint)Index;

        public static StarId FromPacked(long packed)
        {
            if (packed <= 0) throw new ArgumentOutOfRangeException(nameof(packed));
            int x = (int)((packed >> (SectorBits + IndexBits)) & SectorMask) - SectorBias;
            int y = (int)((packed >> IndexBits) & SectorMask) - SectorBias;
            return new StarId(x, y, (int)(packed & IndexMask));
        }

        /// <summary>Unpacks an id that came from outside (a command); false for 0 or bits no star id packs to.</summary>
        public static bool TryFromPacked(long packed, out StarId id)
        {
            id = default;
            if (packed <= 0 || (packed >> (2 * SectorBits + IndexBits)) != 0)
                return false;
            int x = (int)((packed >> (SectorBits + IndexBits)) & SectorMask) - SectorBias;
            int y = (int)((packed >> IndexBits) & SectorMask) - SectorBias;
            if (x == -SectorBias || y == -SectorBias)
                return false;
            id = new StarId(x, y, (int)(packed & IndexMask));
            return true;
        }

        /// <summary>The low and high halves of a packed id, for two int command params.</summary>
        public static void Split(long packed, out int low, out int high)
        {
            low = (int)packed;
            high = (int)(packed >> 32);
        }

        /// <summary>Joins the halves <see cref="Split"/> made.</summary>
        public static long Join(int low, int high) => ((long)high << 32) | (uint)low;

        public bool Equals(StarId other) => SectorX == other.SectorX && SectorY == other.SectorY && Index == other.Index;

        public override bool Equals(object obj) => obj is StarId other && Equals(other);

        public override int GetHashCode() => Packed.GetHashCode();

        public static bool operator ==(StarId a, StarId b) => a.Equals(b);

        public static bool operator !=(StarId a, StarId b) => !a.Equals(b);
    }
}
