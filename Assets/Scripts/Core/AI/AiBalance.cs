using System;
using Unruled.Core.Simulation;

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
        public readonly int LoadBatteryPerEwShip; // batteries per EW ship a fleet carries for combat, on top of a claim cost
        public readonly long CashReserveCredits; // borrows up to this cash, repays debt from cash above twice it
        public readonly long TravelCashCredits; // kept for tolls: WTO orders never lock it, and it borrows only below it
        public readonly int ScanBatteryReserve; // core batteries radar scans leave for fleets to load
        public readonly int ScanWindowTicks; // radar scans start only in this many ticks at the top of each hour
        public readonly int OutpostRigs; // rigs a fleet builds on each bare deposit of a site it claimed
        public readonly int ConvoyShips; // cargo convoys per convoy fleet it builds for an outpost
        public readonly int ConvoyBatch; // the batch its convoy routes run, within the fleet's capacity
        public readonly int RefineryInputStock; // both inputs in core stock at this level start a refinery for a new item
        public readonly int CoreSupplyReserve; // fuel and coolant fleets leave at the core base for its drills
        public readonly int RawStockCap; // a site holding this much of a deposit's ore (an outpost counting its core's too) pauses its drills
        public readonly int LoadUpkeepIntervals; // upkeep intervals of oxygen and rations a fleet loads at the core base
        public readonly byte CitadelAssaultTier; // besieging presets send fleets of this tech tier or higher at the Citadel
        public readonly int SellAbove; // core stock above this (plus research needs) is surplus it lists on the WTO
        public readonly int FillAbove; // core stock above this (plus research needs) may be sold into a rival's bid
        public readonly int SellBatch; // units per sell order
        public readonly int TradePricePercent; // sells this far below the reference price, bids this far above it
        public readonly int TradeOrderTicks; // its orders still open after this long are cancelled
        public readonly int BidBudgetPercent; // a bid spends at most this share of the wallet, leaving deposits for sales
        public readonly int HomeSettleWeight; // presets with no conquest weight still claim neutral home-system sites at this weight
        public readonly int BattleLineShips; // line ships the shipyard builds up to, at the research tier
        public readonly int CoreOreLowReserve; // a core-base deposit with less ore left than this no longer counts as a source: it settles another
        public readonly int FlankMinLineShips; // a fleet splits for a two-lane attack only with at least this many line ships
        public readonly int ChartPriceCredits; // the price it lists its star charts at, and the most it pays for one
        private readonly AiPreset[] _presets;

        public AiBalance(
            int thinkIntervalTicks, int fullBatteryScans, int resupplyAmmoPerLineShip, int loadAmmoPerLineShip, int loadFuel,
            int outpostRigs, int convoyShips, int convoyBatch, int refineryInputStock, int coreSupplyReserve,
            int rawStockCap, int loadUpkeepIntervals, byte citadelAssaultTier, int sellAbove, int fillAbove, int sellBatch,
            int tradePricePercent, int tradeOrderTicks, int bidBudgetPercent, int homeSettleWeight,
            int battleLineShips, int loadBatteryPerEwShip, long cashReserveCredits, long travelCashCredits, int scanBatteryReserve, int scanWindowTicks,
            int coreOreLowReserve, int flankMinLineShips, int chartPriceCredits, AiPreset[] presets)
        {
            if (chartPriceCredits < 1) throw new ArgumentOutOfRangeException(nameof(chartPriceCredits));
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
            if (citadelAssaultTier < 1 || citadelAssaultTier > 5) throw new ArgumentOutOfRangeException(nameof(citadelAssaultTier));
            if (sellAbove < 0) throw new ArgumentOutOfRangeException(nameof(sellAbove));
            if (fillAbove < 0 || fillAbove > sellAbove) throw new ArgumentOutOfRangeException(nameof(fillAbove));
            if (sellBatch < 1) throw new ArgumentOutOfRangeException(nameof(sellBatch));
            if (tradePricePercent < 0 || tradePricePercent >= 100) throw new ArgumentOutOfRangeException(nameof(tradePricePercent));
            if (tradeOrderTicks < 1) throw new ArgumentOutOfRangeException(nameof(tradeOrderTicks));
            if (bidBudgetPercent < 1 || bidBudgetPercent > 100) throw new ArgumentOutOfRangeException(nameof(bidBudgetPercent));
            if (homeSettleWeight < 0) throw new ArgumentOutOfRangeException(nameof(homeSettleWeight));
            if (battleLineShips < 0) throw new ArgumentOutOfRangeException(nameof(battleLineShips));
            if (loadBatteryPerEwShip < 0) throw new ArgumentOutOfRangeException(nameof(loadBatteryPerEwShip));
            if (cashReserveCredits < 0) throw new ArgumentOutOfRangeException(nameof(cashReserveCredits));
            if (travelCashCredits < 0 || travelCashCredits > cashReserveCredits)
                throw new ArgumentOutOfRangeException(nameof(travelCashCredits));
            if (scanBatteryReserve < 0) throw new ArgumentOutOfRangeException(nameof(scanBatteryReserve));
            if (scanWindowTicks < 1 || scanWindowTicks > SimTick.TicksPerHour) throw new ArgumentOutOfRangeException(nameof(scanWindowTicks));
            if (coreOreLowReserve < 0) throw new ArgumentOutOfRangeException(nameof(coreOreLowReserve));
            if (flankMinLineShips < 2) throw new ArgumentOutOfRangeException(nameof(flankMinLineShips));
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
            LoadBatteryPerEwShip = loadBatteryPerEwShip;
            CashReserveCredits = cashReserveCredits;
            TravelCashCredits = travelCashCredits;
            ScanBatteryReserve = scanBatteryReserve;
            ScanWindowTicks = scanWindowTicks;
            OutpostRigs = outpostRigs;
            ConvoyShips = convoyShips;
            ConvoyBatch = convoyBatch;
            RefineryInputStock = refineryInputStock;
            CoreSupplyReserve = coreSupplyReserve;
            RawStockCap = rawStockCap;
            LoadUpkeepIntervals = loadUpkeepIntervals;
            CitadelAssaultTier = citadelAssaultTier;
            SellAbove = sellAbove;
            FillAbove = fillAbove;
            SellBatch = sellBatch;
            TradePricePercent = tradePricePercent;
            TradeOrderTicks = tradeOrderTicks;
            BidBudgetPercent = bidBudgetPercent;
            HomeSettleWeight = homeSettleWeight;
            BattleLineShips = battleLineShips;
            CoreOreLowReserve = coreOreLowReserve;
            FlankMinLineShips = flankMinLineShips;
            ChartPriceCredits = chartPriceCredits;
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
