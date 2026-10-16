using System;

namespace Unruled.Core.AI
{
    /// <summary>One Utility AI archetype (tab 06). Weights and the force ratio are milli-units.</summary>
    public sealed class AiPreset
    {
        public readonly byte ArchetypeId;
        public readonly string Code;
        public readonly int DefenseHoldWeight;
        public readonly int EvacuateSafetyWeight;
        public readonly int RadarBeltScanWeight;
        public readonly int ResupplyLogisticsWeight;
        public readonly int StealthRaidWeight;
        public readonly int SiegeConquestWeight;
        public readonly int CoolantVentTempMilli; // milli-degrees C, like FleetCombatState.TemperatureMilli
        public readonly int RetreatForceRatioMilli; // own power / hostile power below this: retreat
        public readonly int MinBatteryForScanPercent;
        public readonly byte EvacuateStormLevel; // G or S level that pulls the drills

        public AiPreset(
            byte archetypeId, string code, int defenseHoldWeight, int evacuateSafetyWeight, int radarBeltScanWeight,
            int resupplyLogisticsWeight, int stealthRaidWeight, int siegeConquestWeight, int coolantVentTempMilli,
            int retreatForceRatioMilli, int minBatteryForScanPercent, byte evacuateStormLevel)
        {
            if (archetypeId < 1) throw new ArgumentOutOfRangeException(nameof(archetypeId));
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("Preset code required.", nameof(code));
            if (defenseHoldWeight < 0 || evacuateSafetyWeight < 0 || radarBeltScanWeight < 0 || resupplyLogisticsWeight < 0 ||
                stealthRaidWeight < 0 || siegeConquestWeight < 0)
                throw new ArgumentOutOfRangeException(nameof(defenseHoldWeight), "Weights cannot be negative.");
            if (coolantVentTempMilli < 1) throw new ArgumentOutOfRangeException(nameof(coolantVentTempMilli));
            if (retreatForceRatioMilli < 1) throw new ArgumentOutOfRangeException(nameof(retreatForceRatioMilli));
            if (minBatteryForScanPercent < 0 || minBatteryForScanPercent >= 100)
                throw new ArgumentOutOfRangeException(nameof(minBatteryForScanPercent));
            if (evacuateStormLevel < 1 || evacuateStormLevel > 5) throw new ArgumentOutOfRangeException(nameof(evacuateStormLevel));
            ArchetypeId = archetypeId;
            Code = code;
            DefenseHoldWeight = defenseHoldWeight;
            EvacuateSafetyWeight = evacuateSafetyWeight;
            RadarBeltScanWeight = radarBeltScanWeight;
            ResupplyLogisticsWeight = resupplyLogisticsWeight;
            StealthRaidWeight = stealthRaidWeight;
            SiegeConquestWeight = siegeConquestWeight;
            CoolantVentTempMilli = coolantVentTempMilli;
            RetreatForceRatioMilli = retreatForceRatioMilli;
            MinBatteryForScanPercent = minBatteryForScanPercent;
            EvacuateStormLevel = evacuateStormLevel;
        }
    }

    /// <summary>Utility AI settings: the tab-06 presets plus how often and on what scale the AI reads the world.</summary>
    public sealed class AiBalance
    {
        public readonly int ThinkIntervalTicks;
        public readonly int FullBatteryScans; // core-base batteries for this many scan startups read as 100%
        public readonly int ResupplyAmmoPerLineShip; // below this much ammo per line ship a fleet goes home
        private readonly AiPreset[] _presets;

        public AiBalance(int thinkIntervalTicks, int fullBatteryScans, int resupplyAmmoPerLineShip, AiPreset[] presets)
        {
            if (thinkIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(thinkIntervalTicks));
            if (fullBatteryScans < 1) throw new ArgumentOutOfRangeException(nameof(fullBatteryScans));
            if (resupplyAmmoPerLineShip < 1) throw new ArgumentOutOfRangeException(nameof(resupplyAmmoPerLineShip));
            if (presets == null || presets.Length == 0) throw new ArgumentException("Presets required.", nameof(presets));
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i] == null || presets[i].ArchetypeId != i + 1)
                    throw new ArgumentException($"Preset {i + 1} missing or out of order.", nameof(presets));
            }
            ThinkIntervalTicks = thinkIntervalTicks;
            FullBatteryScans = fullBatteryScans;
            ResupplyAmmoPerLineShip = resupplyAmmoPerLineShip;
            _presets = (AiPreset[])presets.Clone();
        }

        public int Count => _presets.Length;

        public bool IsValid(byte archetypeId) => archetypeId >= 1 && archetypeId <= _presets.Length;

        public AiPreset Preset(byte archetypeId)
        {
            if (!IsValid(archetypeId)) throw new ArgumentOutOfRangeException(nameof(archetypeId));
            return _presets[archetypeId - 1];
        }
    }
}
