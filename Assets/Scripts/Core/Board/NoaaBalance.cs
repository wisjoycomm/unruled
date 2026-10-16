using System;

namespace Unruled.Core.Board
{
    /// <summary>
    /// Storm spawning: every <see cref="RollIntervalTicks"/> each star system rolls once; on a hit a front of a random
    /// scale spawns over a random site. Level odds are percent weights for levels 1..5.
    /// </summary>
    public sealed class NoaaBalance
    {
        public const int LevelCount = 5;

        public readonly int RollIntervalTicks;
        public readonly int SpawnChancePercent;
        private readonly int[] _levelWeights;
        public readonly int LevelWeightTotal;
        public readonly int MinDurationTicks;
        public readonly int MaxDurationTicks;
        public readonly int MinRadius;
        public readonly int MaxRadius;

        public NoaaBalance(
            int rollIntervalTicks, int spawnChancePercent, int[] levelWeights, int minDurationTicks,
            int maxDurationTicks, int minRadius, int maxRadius)
        {
            if (rollIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(rollIntervalTicks));
            if (spawnChancePercent < 0 || spawnChancePercent > 100) throw new ArgumentOutOfRangeException(nameof(spawnChancePercent));
            if (levelWeights == null) throw new ArgumentNullException(nameof(levelWeights));
            if (levelWeights.Length != LevelCount) throw new ArgumentException("One weight per level 1..5.", nameof(levelWeights));
            if (minDurationTicks < 1 || maxDurationTicks < minDurationTicks) throw new ArgumentOutOfRangeException(nameof(maxDurationTicks));
            if (minRadius < 1 || maxRadius < minRadius) throw new ArgumentOutOfRangeException(nameof(maxRadius));

            int total = 0;
            foreach (int weight in levelWeights)
            {
                if (weight < 0) throw new ArgumentOutOfRangeException(nameof(levelWeights));
                total += weight;
            }
            if (total == 0) throw new ArgumentException("At least one level needs weight.", nameof(levelWeights));

            RollIntervalTicks = rollIntervalTicks;
            SpawnChancePercent = spawnChancePercent;
            _levelWeights = (int[])levelWeights.Clone();
            LevelWeightTotal = total;
            MinDurationTicks = minDurationTicks;
            MaxDurationTicks = maxDurationTicks;
            MinRadius = minRadius;
            MaxRadius = maxRadius;
        }

        public int LevelWeight(int level) => _levelWeights[level - 1];
    }
}
