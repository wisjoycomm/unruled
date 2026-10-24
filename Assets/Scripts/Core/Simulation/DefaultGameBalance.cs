using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Research;
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
                CreateWtoContractBalance(), CreateSystemAuthorityBalance(),
                CreateSystemFactionBalance(), CreateAiBalance(), CreateNewGameBalance(),
                CreateResearchBalance(), CreateShipyardBalance(), CreateSiteControlBalance(),
                CreateJumpGateBalance(), CreateConvoyBalance(), CreateCitadelBalance(), CreateGalaxyBalance(),
                CreatePresenceBalance());

        /// <summary>
        /// PROVISIONAL offline shield (user decision 2026-10-24, not in the data sheets): it rises 10 minutes after
        /// logout (or after the last fight), lasts at most 8 hours per logout and 48 hours per game week.
        /// </summary>
        public static PresenceBalance CreatePresenceBalance() => new PresenceBalance(
            shieldDelayTicks: 10 * SimTick.TicksPerMinute, shieldMaxTicks: 8 * SimTick.TicksPerHour,
            shieldWeeklyTicks: 48 * SimTick.TicksPerHour);

        /// <summary>
        /// PROVISIONAL galaxy shape (No Man's Sky scale, not in the data sheets): a disc 2,000 sectors in radius
        /// (1,000 units each) holding roughly 25 million stars; a dense core of 60 sectors, two arms winding ~1.5
        /// turns, 6 stars per arm sector and 2 between arms; each star links to its 3 nearest within one sector;
        /// 1.5% of stars are hubs (a Citadel) and 12% can host a home outside the core. New players spawn in groups of
        /// 6 around a hub on a ring 900 sectors out; each group's homes and frontiers are taken within 6 sectors of
        /// its hub, and successive hubs keep 12 sectors apart. Charting a neighbour frontier star costs a fleet 300 fuel
        /// and opens a 400-long highway (the jump-gate lane length) to it. Registering a chart on THE SYSTEM's world shop
        /// costs a 50-credit STA fee, and the STA keeps 10% of each world-shop sale.
        /// </summary>
        public static GalaxyBalance CreateGalaxyBalance() => new GalaxyBalance(
            sectorSize: 1000,
            radiusSectors: 2000,
            coreRadiusSectors: 60,
            armCount: 2,
            armWindingPerSector: 49,
            armWidthAngle: 2500,
            maxStarsPerSector: 12,
            armStarsPerSector: 6,
            fieldStarsPerSector: 2,
            neighbourCount: 3,
            maxNeighbourDistance: 1000,
            hubSharePermille: 15,
            homeSharePermille: 120,
            spawnRingSectors: 900,
            playersPerHub: 6,
            searchRadiusSectors: 6,
            hubSpacingSectors: 12,
            surveyFuel: 300,
            surveyLaneLength: 400,
            chartListingFeeCents: 50 * Wallet.CentsPerCredit,
            chartSaleCutPermille: 100);

        /// <summary>
        /// GDD §1: an alliance that brings down the System Citadel forms The Directorate; Tier-5 nuclear ships are the
        /// weapon for it (tab 02). A 100,000 shield checked every 60 ticks; a line ship bombards 1/2/4/8/40
        /// by tier for 1 ammo; 50 regenerates per quiet check; the garrison (30 line + 10 EW) forms at Tier 3, Tier 4
        /// below 75% and Tier 5 below 40%, and re-forms an hour after it is destroyed; bombarding within the last hour
        /// before the fall earns a Directorate seat.
        /// </summary>
        public static CitadelBalance CreateCitadelBalance() => new CitadelBalance(
            maxIntegrity: 100_000, checkIntervalTicks: 60, bombardPerLineShip: new[] { 1, 2, 4, 8, 40 }, ammoPerLineShip: 1,
            regenPerCheck: 50, startTier: 3, tierStepPercents: new[] { 75, 40 }, garrisonLineShips: 30, garrisonEwShips: 10,
            garrisonAmmo: 3_000, garrisonBatteries: 500, garrisonRespawnTicks: 3_600, directorateWindowTicks: 3_600);

        /// <summary>
        /// GDD §1: CargoConvoys feed remote mines. 250 of each item per convoy ship, checked every 10 ticks;
        /// a route fleet keeps 3 upkeep intervals of its own oxygen and rations, and leaves 500 of each supply at its hub.
        /// </summary>
        public static ConvoyBalance CreateConvoyBalance() =>
            new ConvoyBalance(capacityPerConvoy: 250, checkIntervalTicks: 10, upkeepReserveIntervals: 3, hubSupplyReserve: 500);

        /// <summary>
        /// GDD §1.2: Tier 2 plus an energy charge opens the home gate: 50 batteries + 50 fuel from the core
        /// base; the gate lane is 400 map units long.
        /// </summary>
        public static JumpGateBalance CreateJumpGateBalance() => new JumpGateBalance(
            laneLength: 400,
            openCost: new[] { new ItemStack(ItemId.ItemBattery, 50), new ItemStack(ItemId.ItemFuel, 50) });

        /// <summary>An outpost takes 10 steel + 5 batteries from the claiming fleet's hold.</summary>
        public static SiteControlBalance CreateSiteControlBalance() => new SiteControlBalance(
            new[] { new ItemStack(ItemId.ItemSteel, 10), new ItemStack(ItemId.ItemBattery, 5) });

        /// <summary>
        /// Tab 03 Build_And_Repair_Items: tier 1 steel; 2 steel + battery; 3 alloy + ammo; 4 alloy + microchip; 5 nuclear
        /// core + alloy. 30/45/60/90/120 ticks per ship, retrofit at 50% of the new tier's cost, at most 50 ships per order.
        /// </summary>
        public static ShipyardBalance CreateShipyardBalance() => new ShipyardBalance(
            maxShipsPerOrder: 50,
            retrofitCostPercent: 50,
            costPerShip: new[]
            {
                new[] { new ItemStack(ItemId.ItemSteel, 5) },
                new[] { new ItemStack(ItemId.ItemSteel, 6), new ItemStack(ItemId.ItemBattery, 2) },
                new[] { new ItemStack(ItemId.ItemAlloy, 4), new ItemStack(ItemId.ItemAmmo, 4) },
                new[] { new ItemStack(ItemId.ItemAlloy, 5), new ItemStack(ItemId.ItemMicrochip, 3) },
                new[] { new ItemStack(ItemId.ItemNuclearCore, 2), new ItemStack(ItemId.ItemAlloy, 6) }
            },
            buildTicksPerShip: new[] { 30, 45, 60, 90, 120 });

        /// <summary>
        /// GDD §2.3: 100% upgrade success once telemetry and materials are gathered; RNG lives in storm-telemetry sampling
        /// (20% per storm level each tick, 5 microchips aboard to measure) and in rare-ore mining and high-tier refining.
        /// Gates from GDD §1.2 / §2.1 B / tab 02: laser comms (Signals) and core-fracture mining (Industry) at Tier 4,
        /// terraforming (Industry) at Tier 5, the home jump gate (Logistics) at Tier 2 (§1.2).
        /// Per-tier telemetry targets 20,000 / 140,000 / 650,000 / 1,500,000 milli and retooling 2 / 8 / 24 / 72 h for tiers 2..5.
        /// </summary>
        public static ResearchBalance CreateResearchBalance() => new ResearchBalance(
            telemetryTargetMilli: 20_000,
            telemetryMilliPerStormLevel: 1,
            telemetryMinMicrochips: 5,
            pityPermillePerFailure: 180,
            laserCommsTier: 4,
            coreFractureMiningTier: 4,
            terraformTier: 5,
            jumpGateTier: 2,
            tiers: new[]
            {
                new TierResearchRules(1000, new[] { new ItemStack(ItemId.ItemSteel, 20), new ItemStack(ItemId.ItemBattery, 20) },
                    2 * SimTick.TicksPerHour, telemetryTargetMilli: 20_000),
                new TierResearchRules(1000, new[] { new ItemStack(ItemId.ItemAlloy, 60), new ItemStack(ItemId.ItemMicrochip, 40) },
                    8 * SimTick.TicksPerHour, telemetryTargetMilli: 140_000),
                new TierResearchRules(1000, new[]
                {
                    new ItemStack(ItemId.ItemAlloy, 200), new ItemStack(ItemId.ItemMicrochip, 150),
                    new ItemStack(ItemId.ItemAmmo, 200)
                }, 24 * SimTick.TicksPerHour, telemetryTargetMilli: 650_000),
                new TierResearchRules(1000, new[]
                {
                    new ItemStack(ItemId.ItemNuclearCore, 200), new ItemStack(ItemId.ItemAlloy, 300),
                    new ItemStack(ItemId.ItemMicrochip, 200)
                }, 72 * SimTick.TicksPerHour, telemetryTargetMilli: 1_500_000)
            },
            telemetryChancePermillePerLevel: 200,
            telemetryChanceBonusPerTierPermille: 50);

        /// <summary>
        /// PROVISIONAL: what each NASA planet type can hold. Terrestrial: rock and metal (all but methane, the only
        /// home of tungsten and uranium); Super-Earth: water, a thick nitrogen atmosphere, life, iron, silicon and
        /// titanium; Neptune-like: ices of water, methane and nitrogen; Gas Giant: methane and nitrogen from the
        /// atmosphere. Indexed by <see cref="PlanetClass"/>.
        /// </summary>
        public static ItemId[][] CreateRawsByPlanetClass() => new[]
        {
            new[]
            {
                ItemId.RawWater, ItemId.RawNitrogen, ItemId.RawBiomass, ItemId.RawIron, ItemId.RawCopper, ItemId.RawSilicon,
                ItemId.RawTitanium, ItemId.RawTungsten, ItemId.RawUranium
            },
            new[]
            {
                ItemId.RawWater, ItemId.RawNitrogen, ItemId.RawBiomass, ItemId.RawIron, ItemId.RawSilicon, ItemId.RawTitanium
            },
            new[] { ItemId.RawWater, ItemId.RawMethane, ItemId.RawNitrogen },
            new[] { ItemId.RawMethane, ItemId.RawNitrogen }
        };

        /// <summary>
        /// GDD §1 and §2.1 A: own home system with no tungsten or uranium, deposits by preferred ESI window and planet class.
        /// 4 AI rivals (archetypes 1..4), 2 deposits per site of base 60,000 / 120,000 / 30,000 ore; planet class and
        /// ore richness stay at 100% for now, and each layer of a generated deposit rolls 90..110% of its base, so
        /// deposits start close but never identical (PROVISIONAL). The core base's life-support deposits stay fixed at 5x. A core base that mines its own life support (water 4 rigs, nitrogen 3, methane 2, biomass 2: the coolant
        /// and fuel refineries, which run first, outrun the drills' burn at both depths, and rations cover fleet upkeep),
        /// the core-base refineries and stock below, and a Tier-1 fleet of 10 line, 2 EW, 2 supply.
        /// </summary>
        public static NewGameBalance CreateNewGameBalance() => new NewGameBalance(
            rivalCount: 4,
            depositsPerSite: 2,
            surfaceReserve: 60_000,
            deepCrustReserve: 120_000,
            coreFractureReserve: 30_000,
            coreBaseReserveMultiplier: 5,
            coreBaseDeposits: new[]
            {
                new ItemStack(ItemId.RawWater, 4), new ItemStack(ItemId.RawNitrogen, 3), new ItemStack(ItemId.RawMethane, 2),
                new ItemStack(ItemId.RawBiomass, 2)
            },
            fleetTechTier: 1,
            fleetLineShips: 10,
            fleetEwShips: 2,
            fleetSupplyTenders: 2,
            fleetCargoConvoys: 0,
            homeExcludedRaws: new[] { ItemId.RawTungsten, ItemId.RawUranium },
            rawsByPlanetClass: CreateRawsByPlanetClass(),
            startingRefineries: new[]
            {
                ItemId.ItemCoolant, ItemId.ItemFuel, ItemId.ItemOxygen, ItemId.ItemRations, ItemId.ItemSteel, ItemId.ItemBattery
            },
            coreBaseStock: new[]
            {
                new ItemStack(ItemId.ItemFuel, 2_000), new ItemStack(ItemId.ItemCoolant, 400),
                new ItemStack(ItemId.ItemOxygen, 200), new ItemStack(ItemId.ItemRations, 200),
                new ItemStack(ItemId.ItemSteel, 150), new ItemStack(ItemId.ItemBattery, 40),
                new ItemStack(ItemId.ItemAmmo, 300), new ItemStack(ItemId.RawMethane, 100),
                new ItemStack(ItemId.ItemMicrochip, 20)
            },
            fleetStock: new[]
            {
                new ItemStack(ItemId.ItemAmmo, 120), new ItemStack(ItemId.ItemFuel, 600), new ItemStack(ItemId.ItemCoolant, 60),
                new ItemStack(ItemId.ItemRations, 40)
            },
            uninhabitedClustersPerParticipant: 1,
            richnessPercentByPlanetClass: new[] { 100, 100, 100, 100 },
            // water, nitrogen, methane, biomass, iron, copper, silicon, titanium, tungsten, uranium
            richnessPercentByRaw: new[] { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100 },
            // PROVISIONAL: THE SYSTEM opens with 20,000 cr and a stock of the basics, so trading with it works on day 1.
            systemTreasuryCents: 20_000 * Wallet.CentsPerCredit,
            systemStock: new[]
            {
                new ItemStack(ItemId.ItemOxygen, 300), new ItemStack(ItemId.ItemRations, 300),
                new ItemStack(ItemId.ItemFuel, 1_000), new ItemStack(ItemId.ItemCoolant, 200),
                new ItemStack(ItemId.ItemSteel, 300), new ItemStack(ItemId.ItemBattery, 200)
            },
            reserveRollMinPercent: 90,
            reserveRollMaxPercent: 110);

        /// <summary>
        /// Data sheet tab 02: surface 100% speed / 1x fuel+coolant, deep crust 50% / 2x and -0.05 ESI,
        /// barren husk -0.20 ESI, core fracture 4x (speed 100% per GDD §2.1 B); drills take 180 ticks to retract (§2.1 B).
        /// 60-tick cycle, 30 ore and 1 fuel + 1 coolant per rig per cycle; a rig costs 10 steel + 5 coolant,
        /// at most 10 rigs per deposit.
        /// </summary>
        public static MiningBalance CreateMiningBalance() => new MiningBalance(
            cycleTicks: 60,
            yieldPerRigPerCycle: 30,
            fuelPerRigPerCycle: 1,
            coolantPerRigPerCycle: 1,
            surface: new MiningDepth(speedPercent: 100, fuelCoolantMultiplier: 1),
            deepCrust: new MiningDepth(speedPercent: 50, fuelCoolantMultiplier: 2),
            coreFracture: new MiningDepth(speedPercent: 100, fuelCoolantMultiplier: 4),
            deepCoreEsiDropMilli: 50,
            barrenHuskEsiDropMilli: 200,
            drillRetractTicks: 180,
            maxRigsPerDeposit: 10,
            rigCost: new[] { new ItemStack(ItemId.ItemSteel, 10), new ItemStack(ItemId.ItemCoolant, 5) });

        /// <summary>
        /// GDD §2.4 B and data sheet tab 04: 2 batteries to start, RWR at 15 ticks, reveal after 60 ticks (120 under
        /// ECM), 2 batteries to jam; a reveal lasts 60 ticks.
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
        /// ticks, IAEA reveal for Tier 5. Base speed 5 map units per tick and 1 fuel per ship per 100 units
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
        /// Alpha 10 and Beta 20 (a 2:1 line:EW fleet exactly cancels an equal one), hull 100/60/80/50 for
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
        /// Costs 20 alloy + 50 fuel + 20 battery (teardown) and 20 coolant + 20 battery (bootstrap), all
        /// paid from the old core base when ordered; durations rolled uniformly inside each range.
        /// </summary>
        /// <summary>
        /// Players start with no credits, only goods, and borrow from THE SYSTEM: a 10,000-credit limit, no interest in
        /// the first 30 days, then 1% of the principal a day, due 30 days after interest starts on it.
        /// </summary>
        public static LedgerBalance CreateLedgerBalance() => new LedgerBalance(
            startingCents: 0, creditLimitCents: 10_000 * Wallet.CentsPerCredit, interestFreeTicks: 30 * SimTick.TicksPerDay,
            dailyInterestBasisPoints: 100, loanTermTicks: 30 * SimTick.TicksPerDay,
            creditPerTechTierCents: 2_500 * Wallet.CentsPerCredit,
            openGateCreditCents: 5_000 * Wallet.CentsPerCredit,
            creditPerOutpostCents: 3_000 * Wallet.CentsPerCredit,
            creditPerRefineryCents: 1_000 * Wallet.CentsPerCredit);

        /// <summary>The STA toll is 0.2 credits per ship per 100 units of highway.</summary>
        public static StaBalance CreateStaBalance() => new StaBalance(tollCentsPerShipPer100Units: 20);

        /// <summary>GDD §2.2 B: VWAP over the last 100 ticks, price band 0.8x-1.2x.</summary>
        public static WtoBalance CreateWtoBalance() => new WtoBalance(vwapWindowTicks: 100, priceBandPercent: 20, systemBidPercent: 70,
            systemAskPercent: 130);

        /// <summary>A contract convoy has twice its planned travel time plus 10 minutes to deliver.</summary>
        public static WtoContractBalance CreateWtoContractBalance() =>
            new WtoContractBalance(deadlineTravelPercent: 200, deadlineGraceTicks: 10 * 60);

        /// <summary>
        /// Call-to-Arms: offences count for a day; the first bars the WTO for an hour, the third brings a
        /// WTO defence fleet of 20 Tier-1 line ships to the offender's core base for an hour. Bulletins last an hour.
        /// </summary>
        /// <summary>
        /// Tab 06 presets (weights and ratio in milli, vent temperature in milli-degrees). Think every 10 ticks (GDD §2.5).
        /// 100% battery = 10 scan startups at the core base above 1,000 kept for fleets, and scans start only
        /// in the first 10 minutes of each hour; resupply below 5 ammo per line ship; at the
        /// core base a fleet loads 6 upkeep intervals of oxygen and rations, up to 20 ammo per line ship, 5 batteries per EW ship
        /// and 1,500 fuel (or a round trip to its next settle target); 2 rigs per bare deposit of a claimed site;
        /// 2 cargo convoys running batches of 500 per outpost; a new refinery once 100 of both its inputs sit at the core base;
        /// fleets leave 500 fuel and 500 coolant at the core base for its drills; drills pause at 5,000 ore in stock;
        /// besieging presets send Tier 3+ fleets at the Citadel; on the WTO it sells stock above 3,000 into rivals' bids,
        /// lists 500-unit batches of stock above 3,000 at 5% under the reference price, bids 5% over it (with at most half
        /// its credits) for what its next stress test lacks, and cancels orders still open after an hour; presets that never
        /// conquer still settle neutral sites of their home system at weight 1.0; from Tier 3 the shipyard refits docked battle
        /// fleets to the research tier and builds line ships of it up to 60 (outgunning the Citadel garrison of 30 line + 10 EW); a full
        /// battle line that can't take the Citadel yet waits mothballed. It borrows from THE SYSTEM up to 2,000 credits in
        /// hand once it holds under 1,000 (kept for tolls, never locked in WTO orders), repays debt from cash above 4,000,
        /// and repays all it can once the debt falls due. A core-base deposit with under 300,000 ore left no longer counts as a
        /// source, so it settles another before the home one runs dry. A fleet of 8+ line ships splits for a two-lane attack.
        /// Star charts: it lists the systems it charted first on THE SYSTEM's world shop and its other non-home charts on
        /// its own shop for 200 credits, and buys charts next to space it knows for up to 200 from idle cash (PROVISIONAL).
        /// </summary>
        public static AiBalance CreateAiBalance() => new AiBalance(
            thinkIntervalTicks: 10, fullBatteryScans: 10, resupplyAmmoPerLineShip: 5, loadAmmoPerLineShip: 20, loadFuel: 1_500,
            outpostRigs: 2, convoyShips: 2, convoyBatch: 500, refineryInputStock: 100,
            coreSupplyReserve: 500, rawStockCap: 5_000, loadUpkeepIntervals: 6, citadelAssaultTier: 3,
            sellAbove: 3_000, fillAbove: 20, sellBatch: 500, tradePricePercent: 5, tradeOrderTicks: 3_600, bidBudgetPercent: 50,
            homeSettleWeight: 1_000, battleLineShips: 60, loadBatteryPerEwShip: 5, cashReserveCredits: 2_000,
            travelCashCredits: 1_000, scanBatteryReserve: 1_000, scanWindowTicks: 600, coreOreLowReserve: 300_000,
            flankMinLineShips: 8, chartPriceCredits: 200,
            presets: new[]
            {
                new AiPreset(1, "Sentinel_Defender", 1_800, 1_700, 1_500, 1_400, 0, 0, 75_000, 650, 30, 3),
                new AiPreset(2, "Industrialist_Trader", 1_200, 1_900, 1_000, 2_000, 0, 200, 70_000, 800, 50, 3),
                new AiPreset(3, "Raider_Ambush", 600, 1_400, 1_200, 1_000, 1_900, 500, 85_000, 750, 40, 4),
                new AiPreset(4, "Warlord_Conqueror", 1_000, 700, 1_100, 1_500, 1_100, 1_800, 88_000, 350, 35, 5),
                new AiPreset(5, "Technocrat_Explorer", 1_200, 1_200, 1_800, 1_300, 800, 1_000, 80_000, 500, 25, 4),
            },
            // PROVISIONAL (user decision 2026-10-24): an offline player's governor thinks every 10 minutes, ~1/60 of a bot.
            governorThinkIntervalTicks: 600);

        public static SystemAuthorityBalance CreateSystemAuthorityBalance() => new SystemAuthorityBalance(
            offenceWindowTicks: 86_400, layerOneOffences: 1, wtoBarTicks: 3_600, layerTwoOffences: 3, defenceTier: 1,
            defenceLineShips: 20, defenceAmmo: 500, defenceTicks: 3_600, bulletinTicks: 3_600);

        /// <summary>
        /// Data sheet tab 05, one row per bracket. "Current Storm Path" = no lead but sees storms now; the unrated
        /// bracket sees no forecast. 40% escrow applies to unrated players evading the audit; the audit runs once a game
        /// day (86,400 ticks).
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
        }, auditIntervalTicks: 86_400);

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
        /// capital convoy is in transit. An R3+ radio blackout on the target blocks orders (GDD §1.1: R3+ storms lock
        /// communications; laser comms that pierce them come with Signals Tier 4). A relay costs 20 steel + 10 batteries
        /// from its site and burns 1 battery an hour there.
        /// </summary>
        public static LatencyBalance CreateLatencyBalance() => new LatencyBalance(
            baseTicks: 15,
            hopTicks: 35,
            relayedHopTicks: 8,
            relocationMultiplier: 2,
            blackoutRadioLevel: 3,
            relayBuildSteel: 20,
            relayBuildBatteries: 10,
            relayUpkeepBatteries: 1,
            relayUpkeepIntervalTicks: 3_600);

        /// <summary>
        /// Each system rolls every 10 min with a 25% chance of a new front; level odds 40/30/17/10/3% for 1..5,
        /// lasting 30 min – 3 h over a 150–500 unit disc.
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
        /// Topology chances: bypass 50% between orbits 2 apart, shortcut 25% between orbits 3 apart.
        /// </summary>
        public static StarlaneBalance CreateStarlaneBalance() => new StarlaneBalance(
            highway: new StarlaneTierRules(100, 1000, maxStormLevel: 0, minTechTier: 0, autoRadarHiddenPercent: 0, chargesStaToll: true),
            bypass: new StarlaneTierRules(60, 4500, maxStormLevel: 2, minTechTier: 0, autoRadarHiddenPercent: 90, chargesStaToll: false),
            shortcut: new StarlaneTierRules(90, 1500, maxStormLevel: 4, minTechTier: 4, autoRadarHiddenPercent: 0, chargesStaToll: false),
            bypassChancePercent: 50,
            shortcutChancePercent: 25,
            shortcutSpan: 3);

        /// <summary>
        /// Data sheet tab 02: irradiated world = S4 radiation for 24–72h and ESI 0.00; terraforming lifts ESI to 0.60
        /// for 100 water + 100 nitrogen + 100 biomass + 1 nuclear core; a nuclear strike takes 1 core from a Tier 5 fleet.
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
            terraformNuclearCores: 1,
            strikeMinFleetTier: 5,
            strikeNuclearCores: 1);

        /// <summary>
        /// GDD §2.2 A: 10-tick cycle, idle at 20% upkeep, brownout from G2. Base upkeep 0.5 cent per cycle (1.8 credits
        /// an hour) and 1 battery per storm cycle; a refinery costs 20 steel + 5 batteries, at most 10 per site (one of
        /// each refined item); it idles while its site holds 5,000 of its output.
        /// </summary>
        public static RefineryBalance CreateRefineryBalance() => new RefineryBalance(
            cycleTicks: 10,
            maxBatchesPerCycle: 4,
            baseUpkeepMilliCentsPerCycle: 500,
            idleUpkeepPercent: 20,
            brownoutMinGeoLevel: 2,
            batteryPerBrownoutCycle: 1,
            maxRefineriesPerSite: 10,
            defaultStockCap: 5_000,
            buildCost: new[] { new ItemStack(ItemId.ItemSteel, 20), new ItemStack(ItemId.ItemBattery, 5) });

        /// <summary>GDD §2.2 A.4 and data sheet tab 01: WHO quarantine after more than 30 starved ticks, for 300 ticks.</summary>
        public static SystemFactionBalance CreateSystemFactionBalance() => new SystemFactionBalance(
            whoStarvationTicks: 30,
            whoQuarantineTicks: 300);

        /// <summary>
        /// 5–12 orbits per system by seed (PROVISIONAL; homes and hubs at least 8 so gates, Citadel and core base fit),
        /// from r = 300 out by 180 ± 25 (to ~2280 at 12, min spacing 120), golden-angle spacing ±10°, habitable zone
        /// centred on orbit 3 (R_HZ 840, W_HZ 700, ±0.05 atmo noise). Planet classes (NASA types, PROVISIONAL):
        /// orbits inside r 490 are hot Terrestrials, 490–1190 Terrestrial or (35%) Super-Earth, past 1190 Gas Giants,
        /// past the 1450 ice line Neptune-likes, so every system has rocky inner worlds and giants outside.
        /// </summary>
        public static UniverseGenBalance CreateUniverseGenBalance() => new UniverseGenBalance(
            sitesPerSystem: 12,
            minRadius: 300,
            radiusStep: 180,
            radiusNoise: 25,
            minSiteSpacing: 120,
            angleStep: 40503, // 2*PI / golden ratio, in binary angle units
            angleJitter: 1820, // ~10 degrees
            habitableRadius: 840,
            habitableWidth: 700,
            atmoNoiseMilli: 50,
            iceLineRadius: 1450,
            superEarthPermille: 350,
            minSitesPerSystem: 5,
            settledMinSites: 8);

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
                new ItemDefinition(ItemId.RawTitanium, 45, 150, 450, miningChancePermille: 600, miningChanceBonusPerTierPermille: 100),
                new ItemDefinition(ItemId.RawTungsten, 65, 100, 350, miningChancePermille: 250, miningChanceBonusPerTierPermille: 150),
                new ItemDefinition(ItemId.RawUranium, 150, 0, 200, miningChancePermille: 100, miningChanceBonusPerTierPermille: 100),
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
                new Recipe(ItemId.ItemAlloy, ItemId.RawTitanium, 2, ItemId.RawIron, 1, craftChancePermille: 700, craftChanceBonusPerTierPermille: 100),
                new Recipe(ItemId.ItemMicrochip, ItemId.RawSilicon, 1, ItemId.RawCopper, 1, craftChancePermille: 700, craftChanceBonusPerTierPermille: 100),
                new Recipe(ItemId.ItemBattery, ItemId.RawCopper, 2, ItemId.RawSilicon, 1),
                new Recipe(ItemId.ItemAmmo, ItemId.RawTungsten, 1, ItemId.RawNitrogen, 1, craftChancePermille: 400, craftChanceBonusPerTierPermille: 150),
                new Recipe(ItemId.ItemNuclearCore, ItemId.RawUranium, 2, ItemId.RawTungsten, 1, craftChancePermille: 150, craftChanceBonusPerTierPermille: 100)
            };

            return new RecipeDatabase(items, recipes);
        }
    }
}
