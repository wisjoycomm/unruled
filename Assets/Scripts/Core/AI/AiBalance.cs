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
        public readonly int LoadAmmoPerLineShip; // at the core base a fleet loads ammo up to this per line ship
        public readonly int LoadFuel; // and fuel up to this
        public readonly int OutpostRigs; // rigs a fleet builds on each bare deposit of a site it claimed
        public readonly int ConvoyShips; // cargo convoys per convoy fleet it builds for an outpost
        public readonly int ConvoyBatch; // the batch its convoy routes run, within the fleet's capacity
        public readonly int RefineryInputStock; // both inputs in core stock at this level start a refinery for a new item
        public readonly int CoreSupplyReserve; // fuel and coolant fleets leave at the core base for its drills
        public readonly int RawStockCap; // a site holding this much of a deposit's ore pauses its drills
        public readonly int LoadUpkeepIntervals; // upkeep intervals of oxygen and rations a fleet loads at the core base
        private readonly AiPreset[] _presets;

        public AiBalance(
            int thinkIntervalTicks, int fullBatteryScans, int resupplyAmmoPerLineShip, int loadAmmoPerLineShip, int loadFuel,
            int outpostRigs, int convoyShips, int convoyBatch, int refineryInputStock, int coreSupplyReserve,
            int rawStockCap, int loadUpkeepIntervals, AiPreset[] presets)
        {
            if (thinkIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(thinkIntervalTicks));
            if (fullBatteryScans < 1) throw new ArgumentOutOfRangeException(nameof(fullBatteryScans));
            if (resupplyAmmoPerLineShip < 1) throw new ArgumentOutOfRangeException(nameof(resupplyAmmoPerLineShip));
            if (loadAmmoPerLineShip < resupplyAmmoPerLineShip) throw new ArgumentOutOfRangeException(nameof(loadAmmoPerLineShip));
            if (loadFuel < 1) throw new ArgumentOutOfRangeException(nameof(loadFuel));
            if (outpostRigs < 1) throw new ArgumentOutOfRangeException(nameof(outpostRigs));
            if (convoyShips < 1) throw new ArgumentOutOfRangeException(nameof(convoyShips));
            if (convoyBatch < 1) throw new ArgumentOutOfRangeException(nameof(convoyBatch));
            if (refineryInputStock < 1) throw new ArgumentOutOfRangeException(nameof(refineryInputStock));
            if (coreSupplyReserve < 0) throw new ArgumentOutOfRangeException(nameof(coreSupplyReserve));
            if (rawStockCap < 2) throw new ArgumentOutOfRangeException(nameof(rawStockCap));
            if (loadUpkeepIntervals < 1) throw new ArgumentOutOfRangeException(nameof(loadUpkeepIntervals));
            if (presets == null || presets.Length == 0) throw new ArgumentException("Presets required.", nameof(presets));
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i] == null || presets[i].ArchetypeId != i + 1)
                    throw new ArgumentException($"Preset {i + 1} missing or out of order.", nameof(presets));
            }
            ThinkIntervalTicks = thinkIntervalTicks;
            FullBatteryScans = fullBatteryScans;
            ResupplyAmmoPerLineShip = resupplyAmmoPerLineShip;
            LoadAmmoPerLineShip = loadAmmoPerLineShip;
            LoadFuel = loadFuel;
            OutpostRigs = outpostRigs;
            ConvoyShips = convoyShips;
            ConvoyBatch = convoyBatch;
            RefineryInputStock = refineryInputStock;
            CoreSupplyReserve = coreSupplyReserve;
            RawStockCap = rawStockCap;
            LoadUpkeepIntervals = loadUpkeepIntervals;
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
