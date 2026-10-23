using System;

namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// Shape of the procedural galaxy (PROVISIONAL, not in the data sheets yet): a disc of square sectors whose star
    /// count follows a bright core plus two logarithmic-looking spiral arms. Distances are integer galaxy units.
    /// </summary>
    public sealed class GalaxyBalance
    {
        /// <summary>Most stars one sector can hold; the star index in a <see cref="StarId"/> stays below this.</summary>
        public const int MaxStarsPerSectorCap = 64;

        public readonly int SectorSize;
        public readonly int RadiusSectors;
        public readonly int CoreRadiusSectors;
        public readonly int ArmCount;
        public readonly int ArmWindingPerSector; // binary angle units the arm turns per sector of radius
        public readonly int ArmWidthAngle; // binary angle units either side of an arm's centre line
        public readonly int MaxStarsPerSector;
        public readonly int ArmStarsPerSector;
        public readonly int FieldStarsPerSector;
        public readonly int NeighbourCount;
        public readonly int MaxNeighbourDistance;
        public readonly int HubSharePermille; // chance a star is a hub (contested system with a Citadel)
        public readonly int HomeSharePermille; // chance a star can host a new player's home

        public GalaxyBalance(
            int sectorSize, int radiusSectors, int coreRadiusSectors, int armCount, int armWindingPerSector,
            int armWidthAngle, int maxStarsPerSector, int armStarsPerSector, int fieldStarsPerSector, int neighbourCount,
            int maxNeighbourDistance, int hubSharePermille, int homeSharePermille)
        {
            if (sectorSize < 1) throw new ArgumentOutOfRangeException(nameof(sectorSize));
            if (radiusSectors < 1 || radiusSectors > 1 << 14) throw new ArgumentOutOfRangeException(nameof(radiusSectors));
            if (coreRadiusSectors < 0 || coreRadiusSectors > radiusSectors) throw new ArgumentOutOfRangeException(nameof(coreRadiusSectors));
            if (armCount < 1) throw new ArgumentOutOfRangeException(nameof(armCount));
            if (armWidthAngle < 1) throw new ArgumentOutOfRangeException(nameof(armWidthAngle));
            if (maxStarsPerSector < 1 || maxStarsPerSector > MaxStarsPerSectorCap) throw new ArgumentOutOfRangeException(nameof(maxStarsPerSector));
            if (armStarsPerSector < 0 || armStarsPerSector > maxStarsPerSector) throw new ArgumentOutOfRangeException(nameof(armStarsPerSector));
            if (fieldStarsPerSector < 0 || fieldStarsPerSector > armStarsPerSector) throw new ArgumentOutOfRangeException(nameof(fieldStarsPerSector));
            if (neighbourCount < 1) throw new ArgumentOutOfRangeException(nameof(neighbourCount));
            if (maxNeighbourDistance < 1 || maxNeighbourDistance > sectorSize) throw new ArgumentOutOfRangeException(nameof(maxNeighbourDistance));
            if (hubSharePermille < 0 || homeSharePermille < 0 || hubSharePermille + homeSharePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(hubSharePermille));

            SectorSize = sectorSize;
            RadiusSectors = radiusSectors;
            CoreRadiusSectors = coreRadiusSectors;
            ArmCount = armCount;
            ArmWindingPerSector = armWindingPerSector;
            ArmWidthAngle = armWidthAngle;
            MaxStarsPerSector = maxStarsPerSector;
            ArmStarsPerSector = armStarsPerSector;
            FieldStarsPerSector = fieldStarsPerSector;
            NeighbourCount = neighbourCount;
            MaxNeighbourDistance = maxNeighbourDistance;
            HubSharePermille = hubSharePermille;
            HomeSharePermille = homeSharePermille;
        }

        public int RadiusUnits => RadiusSectors * SectorSize;
    }
}
