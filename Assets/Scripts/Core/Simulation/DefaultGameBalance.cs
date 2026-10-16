using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Simulation
{
    /// <summary>
    /// Balance values transcribed from .docs/unruled_master_data_sheets.md. This is the only place they are written;
    /// DataSheetConsistencyTests fails if the sheet and this file drift apart.
    /// </summary>
    public static class DefaultGameBalance
    {
        public static GameBalanceConfig Create() =>
            new GameBalanceConfig(
                CreateRecipeDatabase(), CreateUniverseGenBalance(), CreateStarlaneBalance(), CreateNoaaBalance(),
                CreateRefineryBalance(), CreateMiningBalance(),
                CreateLifecycleBalance(), CreateLatencyBalance(), CreateRadarBalance(), CreateFleetBalance(),
                CreateCombatBalance(), CreateRelocationBalance(), CreateLedgerBalance(),
                CreateStaBalance(), CreateLeaderboardBalance(), CreateWtoBalance(),
                CreateWtoContractBalance(),
                CreateSystemFactionBalance());

        /// <summary>
        /// Data sheet tab 02: surface 100% speed / 1x fuel+coolant, deep crust 50% / 2x and -0.05 ESI,
        /// barren husk -0.20 ESI, core fracture 4x (speed 100% per GDD §2.1 B); drills take 180 ticks to retract (§2.1 B).
        /// PROVISIONAL: 10-tick cycle, 10 ore and 1 fuel + 1 coolant per rig per cycle.
        /// </summary>
        public static MiningBalance CreateMiningBalance() => new MiningBalance(
            cycleTicks: 10,
            yieldPerRigPerCycle: 10,
            fuelPerRigPerCycle: 1,
            coolantPerRigPerCycle: 1,
            surface: new MiningDepth(speedPercent: 100, fuelCoolantMultiplier: 1),
            deepCrust: new MiningDepth(speedPercent: 50, fuelCoolantMultiplier: 2),
            coreFracture: new MiningDepth(speedPercent: 100, fuelCoolantMultiplier: 4),
            deepCoreEsiDropMilli: 50,
            barrenHuskEsiDropMilli: 200,
            drillRetractTicks: 180);

        /// <summary>
        /// GDD §2.4 B and data sheet tab 04: 2 batteries to start, RWR at 15 ticks, reveal after 60 ticks (120 under
        /// ECM), 2 batteries to jam. PROVISIONAL: a reveal lasts 60 ticks.
        /// </summary>
        public static RadarBalance CreateRadarBalance() => new RadarBalance(
            startupBatteries: 2,
            rwrDelayTicks: 15,
            integrationTicks: 60,
            jammedIntegrationTicks: 120,
            jamBatteries: 2,
            revealTicks: 60);

        /// <summary>
        /// Data sheet tab 03: upkeep per ship each hour, combat burn multipliers, 80% mothball discount, warm-up 60–180
        /// ticks, IAEA reveal for Tier 5. PROVISIONAL: base speed 5 map units per tick and 1 fuel per ship per 100 units
        /// (scaled by the lane tier's speed and fuel multiplier).
        /// </summary>
        public static FleetBalance CreateFleetBalance() => new FleetBalance(
            new[]
            {
                new ShipTierRules(
                    new[] { Milli(ItemId.ItemOxygen, 1000), Milli(ItemId.ItemRations, 1000) },
                    new[] { Milli(ItemId.ItemAmmo, 1000), Milli(ItemId.ItemFuel, 1000) },
                    mothballUpkeepDiscountPercent: 80, mothballWarmupTicks: 60, iaeaGlobalReveal: false),
                new ShipTierRules(
                    new[] { Milli(ItemId.ItemOxygen, 1000), Milli(ItemId.ItemRations, 1000), Milli(ItemId.ItemSteel, 500) },
                    new[] { Milli(ItemId.ItemAmmo, 1200), Milli(ItemId.ItemFuel, 1200) },
                    mothballUpkeepDiscountPercent: 80, mothballWarmupTicks: 90, iaeaGlobalReveal: false),
                new ShipTierRules(
                    new[] { Milli(ItemId.ItemOxygen, 2000), Milli(ItemId.ItemCoolant, 2000), Milli(ItemId.ItemAlloy, 1000) },
                    new[] { Milli(ItemId.ItemAmmo, 2000), Milli(ItemId.ItemCoolant, 2000) },
                    mothballUpkeepDiscountPercent: 80, mothballWarmupTicks: 120, iaeaGlobalReveal: false),
                new ShipTierRules(
                    new[] { Milli(ItemId.ItemCoolant, 3000), Milli(ItemId.ItemBattery, 2000), Milli(ItemId.ItemMicrochip, 1000) },
                    new[] { Milli(ItemId.ItemAmmo, 2500), Milli(ItemId.ItemCoolant, 2500), Milli(ItemId.ItemBattery, 2000) },
                    mothballUpkeepDiscountPercent: 80, mothballWarmupTicks: 150, iaeaGlobalReveal: false),
                new ShipTierRules(
                    new[] { Milli(ItemId.ItemCoolant, 5000), Milli(ItemId.ItemAlloy, 2000), Milli(ItemId.ItemMicrochip, 2000) },
                    new[] { Milli(ItemId.ItemAmmo, 3500), Milli(ItemId.ItemCoolant, 4000) },
                    mothballUpkeepDiscountPercent: 80, mothballWarmupTicks: 180, iaeaGlobalReveal: true)
            },
            upkeepIntervalTicks: 3_600,
            baseSpeedMilliPerTick: 5000,
            fuelPerShipPer100UnitsMilli: 1000);

        /// <summary>
        /// GDD §2.4 C and data sheet tab 04: 5-tick salvo, ROF 50/100/150%, defender +35% PD at its own site, -20% PD
        /// per extra lane (floor 20%, at most 3 extra), guns lock above 90 °C until below 75 °C, cook-off 5% hull.
        /// PROVISIONAL: Alpha 10 and Beta 20 (a 2:1 line:EW fleet exactly cancels an equal one), hull 100/60/80/50 for
        /// line/EW/supply/cargo, 0.1 ammo per line ship and 0.1 battery per EW ship per salvo, 0.2 coolant per ship per
        /// venting salvo, tenders hand over 2 of each per salvo up to 10 in a hold, ambient 20 °C, +4 °C per salvo at
        /// 100% ROF (x ROF²), -4 °C passive, -15 °C venting, 0.02 fuel per acting ship per salvo (x the tab-03 fuel burn).
        /// </summary>
        public static CombatBalance CreateCombatBalance() => new CombatBalance(
            salvoCycleTicks: 5,
            lineAlpha: 10,
            ewBeta: 20,
            conservationRofPercent: 50,
            standardRofPercent: 100,
            overdriveRofPercent: 150,
            fortificationPdPercent: 35,
            flankStepPercent: 20,
            flankFloorPercent: 20,
            maxExtraLanes: 3,
            lineHp: 100,
            ewHp: 60,
            supplyHp: 80,
            cargoHp: 50,
            ammoPerLineShipMilli: 100,
            batteryPerEwShipMilli: 100,
            ventCoolantPerShipMilli: 200,
            tenderTransferPerSalvo: 2,
            resupplyTarget: 10,
            ambientMilli: 20_000,
            fireHeatMilli: 4_000,
            passiveCoolMilli: 4_000,
            ventCoolMilli: 15_000,
            overheatMilli: 90_000,
            overheatReleaseMilli: 75_000,
            cookOffHullPercent: 5,
            fuelPerShipPerSalvoMilli: 20);

        /// <summary>
        /// GDD §2.4 A, §4.2 and data sheet tab 04: teardown 900–1800 ticks with the old base at 50% defence bonus,
        /// convoy 1800–3600 ticks (latency x2, paralysis 15 min if destroyed), bootstrap 1800 ticks, cooldown 48 h.
        /// PROVISIONAL: costs 20 alloy + 50 fuel + 20 battery (teardown) and 20 coolant + 20 battery (bootstrap), all
        /// paid from the old core base when ordered; durations rolled uniformly inside each range.
        /// </summary>
        /// <summary>PROVISIONAL: every player starts with 10,000 credits (the GDD gives no starting money).</summary>
        public static LedgerBalance CreateLedgerBalance() => new LedgerBalance(startingCents: 10_000 * Wallet.CentsPerCredit);

        /// <summary>PROVISIONAL: the STA toll is 2 credits per ship per 100 units of highway (tab 04 names no amount).</summary>
        public static StaBalance CreateStaBalance() => new StaBalance(tollCentsPerShipPer100Units: 200);

        /// <summary>GDD §2.2 B: VWAP over the last 100 ticks, price band 0.8x-1.2x.</summary>
        public static WtoBalance CreateWtoBalance() => new WtoBalance(vwapWindowTicks: 100, priceBandPercent: 20);

        /// <summary>PROVISIONAL: a contract convoy has twice its planned travel time plus 10 minutes to deliver.</summary>
        public static WtoContractBalance CreateWtoContractBalance() =>
            new WtoContractBalance(deadlineTravelPercent: 200, deadlineGraceTicks: 10 * 60);

        /// <summary>
        /// Data sheet tab 05, one row per bracket. "Current Storm Path" = no lead but sees storms now; the unrated
        /// bracket sees no forecast. 40% escrow applies to unrated players evading the audit.
        /// </summary>
        public static LeaderboardBalance CreateLeaderboardBalance() => new LeaderboardBalance(new[]
        {
            new BracketRules(0, "Unrated_Ghost", 0, 0, 30, 40, 500, 0, 0, 0, new ItemId[0], 0, false),
            new BracketRules(1, "Registered_BB_D", 5_001, 10_000, 28, 28, 450, 5, 1, 5,
                new[] { ItemId.ItemFuel, ItemId.ItemOxygen }, 0, true),
            new BracketRules(2, "Standard_BBB_C", 2_001, 5_000, 26, 26, 420, 10, 2, 10, new[] { ItemId.ItemFuel }, 0, true),
            new BracketRules(3, "MidGrade_A_B", 501, 2_000, 24, 24, 380, 20, 4, 20, new[] { ItemId.ItemFuel }, 5 * 60, true),
            new BracketRules(4, "HighGrade_AA_A", 101, 500, 21, 21, 300, 25, 6, 25,
                new[] { ItemId.ItemFuel, ItemId.ItemSteel }, 12 * 60, true),
            new BracketRules(5, "Superpower_AAA_S", 11, 100, 18, 18, 250, 25, 8, 25,
                new[] { ItemId.ItemFuel, ItemId.ItemCoolant, ItemId.ItemSteel }, 20 * 60, true),
            new BracketRules(6, "Apex_Tier0", 1, 10, 15, 15, 200, 15, 10, 15,
                new[] { ItemId.ItemFuel, ItemId.ItemCoolant, ItemId.ItemAlloy }, 30 * 60, true)
        });

        public static RelocationBalance CreateRelocationBalance() => new RelocationBalance(
            teardownMinTicks: 900,
            teardownMaxTicks: 1_800,
            convoyMinTicks: 1_800,
            convoyMaxTicks: 3_600,
            bootstrapTicks: 1_800,
            cooldownTicks: 172_800,
            paralysisTicks: 900,
            teardownDefencePercent: 50,
            teardownCost: new[] { Milli(ItemId.ItemAlloy, 20_000), Milli(ItemId.ItemFuel, 50_000), Milli(ItemId.ItemBattery, 20_000) },
            bootstrapCost: new[] { Milli(ItemId.ItemCoolant, 20_000), Milli(ItemId.ItemBattery, 20_000) });

        private static ItemAmount Milli(ItemId item, int milli) => new ItemAmount(item, milli);

        /// <summary>
        /// GDD §2.4 A and data sheet tab 04: 15 ticks at the core base, 35 per hop, 8 per relayed hop, x2 while the
        /// capital convoy is in transit. PROVISIONAL: an R3+ radio blackout on the target blocks orders (GDD §1.1 says
        /// R3+ storms "lock communications"; laser comms that pierce them come with Tier 4 research).
        /// </summary>
        public static LatencyBalance CreateLatencyBalance() => new LatencyBalance(
            baseTicks: 15,
            hopTicks: 35,
            relayedHopTicks: 8,
            relocationMultiplier: 2,
            blackoutRadioLevel: 3);

        /// <summary>
        /// PROVISIONAL (not in the GDD or data sheet): each system rolls every 10 min with a 25% chance of a new front;
        /// level odds 40/30/17/10/3% for 1..5, lasting 30 min – 3 h over a 150–500 unit disc.
        /// </summary>
        public static NoaaBalance CreateNoaaBalance() => new NoaaBalance(
            rollIntervalTicks: 600,
            spawnChancePercent: 25,
            levelWeights: new[] { 40, 30, 17, 10, 3 },
            minDurationTicks: 1_800,
            maxDurationTicks: 10_800,
            minRadius: 150,
            maxRadius: 500);

        /// <summary>
        /// Data sheet tab 04: highway 100% speed / 1.0x fuel / sheltered / STA toll; bypass 60% / 4.5x / storms up to
        /// G2 / 90% hidden from auto radar; shortcut 90% / 1.5x / storms up to level 4 / tech tier 4+.
        /// PROVISIONAL: topology chances (bypass 50%, shortcut 25% between orbits 3 apart).
        /// </summary>
        public static StarlaneBalance CreateStarlaneBalance() => new StarlaneBalance(
            highway: new StarlaneTierRules(100, 1000, maxStormLevel: 0, minTechTier: 0, autoRadarHiddenPercent: 0, chargesStaToll: true),
            bypass: new StarlaneTierRules(60, 4500, maxStormLevel: 2, minTechTier: 0, autoRadarHiddenPercent: 90, chargesStaToll: false),
            shortcut: new StarlaneTierRules(90, 1500, maxStormLevel: 4, minTechTier: 4, autoRadarHiddenPercent: 0, chargesStaToll: false),
            bypassChancePercent: 50,
            shortcutChancePercent: 25,
            shortcutSpan: 3);

        /// <summary>
        /// Data sheet tab 02: irradiated world = S4 radiation for 24–72h and ESI 0.00; terraforming lifts ESI to 0.60.
        /// PROVISIONAL: terraform cost of 100 water + 100 nitrogen + 100 biomass + 1 nuclear core (the sheet names
        /// the items but not the amounts).
        /// </summary>
        public static LifecycleBalance CreateLifecycleBalance() => new LifecycleBalance(
            irradiationMinTicks: 86_400,
            irradiationMaxTicks: 259_200,
            irradiationSolarLevel: 4,
            irradiatedEsiMilli: 0,
            terraformedEsiMilli: 600,
            terraformWater: 100,
            terraformNitrogen: 100,
            terraformBiomass: 100,
            terraformNuclearCores: 1);

        /// <summary>
        /// GDD §2.2 A: 10-tick cycle, idle at 20% upkeep, brownout from G2.
        /// PROVISIONAL: base upkeep 10 per cycle and 1 battery per storm cycle are not in the GDD or data sheet.
        /// </summary>
        public static RefineryBalance CreateRefineryBalance() => new RefineryBalance(
            cycleTicks: 10,
            baseUpkeepPerCycle: 10,
            idleUpkeepPercent: 20,
            brownoutMinGeoLevel: 2,
            batteryPerBrownoutCycle: 1);

        /// <summary>GDD §2.2 A.4 and data sheet tab 01: WHO quarantine after more than 30 starved ticks, for 300 ticks.</summary>
        public static SystemFactionBalance CreateSystemFactionBalance() => new SystemFactionBalance(
            whoStarvationTicks: 30,
            whoQuarantineTicks: 300);

        /// <summary>
        /// PROVISIONAL: only MinSiteSpacing (120) comes from the GDD; the rest are proposed defaults until the
        /// data sheet defines them. 8 orbits from r = 300 to ~1560, habitable zone centred on orbit 3.
        /// </summary>
        public static UniverseGenBalance CreateUniverseGenBalance() => new UniverseGenBalance(
            sitesPerSystem: 8,
            minRadius: 300,
            radiusStep: 180,
            radiusNoise: 25,
            minSiteSpacing: 120,
            angleStep: 40503, // 2*PI / golden ratio, in binary angle units
            angleJitter: 1820, // ~10 degrees
            habitableRadius: 840,
            habitableWidth: 700,
            atmoNoiseMilli: 50);

        public static RecipeDatabase CreateRecipeDatabase()
        {
            var items = new[]
            {
                new ItemDefinition(ItemId.RawWater, 10, 400, 900),
                new ItemDefinition(ItemId.RawNitrogen, 12, 500, 1000),
                new ItemDefinition(ItemId.RawMethane, 15, 200, 550),
                new ItemDefinition(ItemId.RawBiomass, 18, 700, 1000),
                new ItemDefinition(ItemId.RawIron, 14, 300, 850),
                new ItemDefinition(ItemId.RawCopper, 20, 300, 800),
                new ItemDefinition(ItemId.RawSilicon, 22, 200, 900),
                new ItemDefinition(ItemId.RawTitanium, 45, 150, 450),
                new ItemDefinition(ItemId.RawTungsten, 65, 100, 350),
                new ItemDefinition(ItemId.RawUranium, 150, 0, 200),
                new ItemDefinition(ItemId.ItemOxygen, 30),
                new ItemDefinition(ItemId.ItemRations, 55),
                new ItemDefinition(ItemId.ItemCoolant, 35),
                new ItemDefinition(ItemId.ItemFuel, 38),
                new ItemDefinition(ItemId.ItemSteel, 50),
                new ItemDefinition(ItemId.ItemAlloy, 125),
                new ItemDefinition(ItemId.ItemMicrochip, 60),
                new ItemDefinition(ItemId.ItemBattery, 80),
                new ItemDefinition(ItemId.ItemAmmo, 95),
                new ItemDefinition(ItemId.ItemNuclearCore, 420)
            };

            var recipes = new[]
            {
                new Recipe(ItemId.ItemOxygen, ItemId.RawWater, 1, ItemId.RawNitrogen, 1),
                new Recipe(ItemId.ItemRations, ItemId.RawBiomass, 2, ItemId.RawWater, 1),
                new Recipe(ItemId.ItemCoolant, ItemId.RawNitrogen, 1, ItemId.RawWater, 1),
                new Recipe(ItemId.ItemFuel, ItemId.RawMethane, 1, ItemId.RawWater, 1),
                new Recipe(ItemId.ItemSteel, ItemId.RawIron, 2, ItemId.RawMethane, 1),
                new Recipe(ItemId.ItemAlloy, ItemId.RawTitanium, 2, ItemId.RawIron, 1),
                new Recipe(ItemId.ItemMicrochip, ItemId.RawSilicon, 1, ItemId.RawCopper, 1),
                new Recipe(ItemId.ItemBattery, ItemId.RawCopper, 2, ItemId.RawSilicon, 1),
                new Recipe(ItemId.ItemAmmo, ItemId.RawTungsten, 1, ItemId.RawNitrogen, 1),
                new Recipe(ItemId.ItemNuclearCore, ItemId.RawUranium, 2, ItemId.RawTungsten, 1)
            };

            return new RecipeDatabase(items, recipes);
        }
    }
}
