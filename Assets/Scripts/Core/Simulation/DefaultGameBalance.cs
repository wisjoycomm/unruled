using Unruled.Core.Board;
using Unruled.Core.Economy;
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
                CreateRecipeDatabase(), CreateUniverseGenBalance(), CreateRefineryBalance(), CreateMiningBalance(),
                CreateLifecycleBalance(), CreateSystemFactionBalance());

        /// <summary>
        /// Data sheet tab 02: surface 100% speed / 1x fuel+coolant, deep crust 50% / 2x and -0.05 ESI,
        /// barren husk -0.20 ESI, core fracture 4x (speed 100% per GDD §2.1 B).
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
            barrenHuskEsiDropMilli: 200);

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
