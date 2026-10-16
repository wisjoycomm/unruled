using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;
using UnityEngine;

namespace Unruled.Tests.Core.Economy
{
    /// <summary>Fails when DefaultGameBalance drifts from .docs/unruled_master_data_sheets.md.</summary>
    public class DataSheetConsistencyTests
    {
        private static string SheetPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".docs", "unruled_master_data_sheets.md"));

        [Test]
        public void ItemsTab_MatchesDefaultBalance()
        {
            var rows = ReadTable("## 01_Items_20_Recipes");
            Assert.AreEqual(ItemIds.Count, rows.Count, "Item row count");

            var db = DefaultGameBalance.CreateRecipeDatabase();
            foreach (var row in rows)
            {
                var id = (ItemId)int.Parse(row[0], CultureInfo.InvariantCulture);
                Assert.AreEqual(ToEnumName(row[1]), id.ToString(), $"Code of item {row[0]}");

                ref readonly var item = ref db.GetItem(id);
                Assert.AreEqual(int.Parse(row[9], CultureInfo.InvariantCulture), item.BaseWtoPrice, $"{id} price");

                if (row[4] == "N/A")
                {
                    Assert.IsFalse(item.HasPreferredEsi, $"{id} ESI");
                }
                else
                {
                    var range = row[4].Split('-');
                    Assert.AreEqual(ToMilli(range[0]), item.PreferredEsiMinMilli, $"{id} ESI min");
                    Assert.AreEqual(ToMilli(range[1]), item.PreferredEsiMaxMilli, $"{id} ESI max");
                }

                if (row[5] == "NONE")
                {
                    Assert.IsTrue(ItemIds.IsRaw(id), $"{id} has no recipe in the sheet");
                    continue;
                }

                ref readonly var recipe = ref db.GetRecipe(id);
                Assert.AreEqual(ToEnumName(row[5]), recipe.Input1.ToString(), $"{id} input 1");
                Assert.AreEqual(int.Parse(row[6], CultureInfo.InvariantCulture), recipe.Qty1, $"{id} qty 1");
                Assert.AreEqual(ToEnumName(row[7]), recipe.Input2.ToString(), $"{id} input 2");
                Assert.AreEqual(int.Parse(row[8], CultureInfo.InvariantCulture), recipe.Qty2, $"{id} qty 2");
                Assert.AreEqual(1, recipe.OutputQty, $"{id} output qty");
            }
        }

        [Test]
        public void LifecycleTab_MatchesMiningBalance()
        {
            var rows = ReadTable("## 02_Planetary_Lifecycle");
            var mining = DefaultGameBalance.CreateMiningBalance();

            var pristine = FindRow(rows, "PRISTINE_WORLD");
            Assert.AreEqual(mining.Surface.SpeedPercent, LeadingNumber(pristine[3]), "surface speed");
            Assert.AreEqual(mining.Surface.FuelCoolantMultiplier, LeadingNumber(pristine[4]), "surface cost");

            var deep = FindRow(rows, "DEEP_CORE_STRIPPING");
            Assert.AreEqual(mining.DeepCrust.SpeedPercent, LeadingNumber(deep[3]), "deep speed");
            Assert.AreEqual(mining.DeepCrust.FuelCoolantMultiplier, LeadingNumber(deep[4]), "deep cost");
            Assert.AreEqual(mining.DeepCoreEsiDropMilli, -ToMilli(deep[5].Split(' ')[0]), "deep ESI drop");

            var husk = FindRow(rows, "DEPLETED_BARREN_HUSK");
            Assert.AreEqual(mining.BarrenHuskEsiDropMilli, -ToMilli(husk[5].Split(' ')[0]), "husk ESI drop");

            var irradiated = FindRow(rows, "IRRADIATED_DEAD_WORLD");
            Assert.AreEqual(mining.CoreFracture.FuelCoolantMultiplier, LeadingNumber(irradiated[4]), "core cost");

            var lifecycle = DefaultGameBalance.CreateLifecycleBalance();
            Assert.AreEqual("ESI = 0.00 (Xóa sổ 100% Biomass)", irradiated[5], "irradiated ESI cell changed");
            Assert.AreEqual(0, lifecycle.IrradiatedEsiMilli, "irradiated ESI");
            Assert.AreEqual("Phát xạ = Bão S4 (24h-72h)", irradiated[6], "radiation cell changed");
            Assert.AreEqual(4, lifecycle.IrradiationSolarLevel, "radiation S level");
            Assert.AreEqual(24 * 3600, lifecycle.IrradiationMinTicks, "radiation min");
            Assert.AreEqual(72 * 3600, lifecycle.IrradiationMaxTicks, "radiation max");

            var terraformed = FindRow(rows, "TERRAFORMED_RECOVERY");
            var esiCell = terraformed[5].Split(' ');
            Assert.AreEqual(lifecycle.TerraformedEsiMilli, ToMilli(esiCell[esiCell.Length - 1]), "terraformed ESI");
        }

        [Test]
        public void RoutesTab_MatchesStarlaneBalance()
        {
            var rows = ReadTable("## 04_Routes_Radar_Latency");
            var lanes = DefaultGameBalance.CreateStarlaneBalance();

            AssertTier(FindRow(rows, "STA_Toll_Highway"), lanes.Highway, "highway");
            AssertTier(FindRow(rows, "Outer_Rim_Asteroid_Bypass"), lanes.Bypass, "bypass");
            AssertTier(FindRow(rows, "Hazard_Storm_Shortcut"), lanes.Shortcut, "shortcut");

            Assert.IsTrue(lanes.Highway.ChargesStaToll && FindRow(rows, "STA_Toll_Highway")[3].Contains("Toll"));
            StringAssert.Contains($"Ẩn {lanes.Bypass.AutoRadarHiddenPercent}%", FindRow(rows, "Outer_Rim_Asteroid_Bypass")[4]);
            StringAssert.Contains($"Tech Tier {lanes.Shortcut.MinTechTier}", FindRow(rows, "Hazard_Storm_Shortcut")[3]);
        }

        // "Speed 60% | Fuel 4.5x | Storm G1-G2" -> 60, 4500, highest storm level 2
        private static void AssertTier(string[] row, StarlaneTierRules rules, string name)
        {
            var parts = row[2].Split('|');
            Assert.AreEqual(3, parts.Length, $"{name} formula '{row[2]}'");
            Assert.AreEqual(rules.SpeedPercent, LeadingNumber(parts[0].Trim().Substring("Speed ".Length)), $"{name} speed");
            Assert.AreEqual(rules.FuelMultiplierMilli, ToMilli(parts[1].Trim().Substring("Fuel ".Length).TrimEnd('x')), $"{name} fuel");

            int maxLevel = 0;
            foreach (char ch in parts[2])
                if (char.IsDigit(ch))
                    maxLevel = Math.Max(maxLevel, ch - '0');
            Assert.AreEqual(rules.MaxStormLevel, maxLevel, $"{name} storm exposure");
        }

        private static string[] FindRow(List<string[]> rows, string stateCode)
        {
            foreach (var row in rows)
                if (row[1] == stateCode)
                    return row;
            Assert.Fail($"State {stateCode} not found in lifecycle tab");
            return null;
        }

        // "100% Speed" -> 100, "2.0x FUEL / 2.0x COOLANT" -> 2
        private static int LeadingNumber(string cell)
        {
            int end = 0;
            while (end < cell.Length && (char.IsDigit(cell[end]) || cell[end] == '.'))
                end++;
            decimal value = decimal.Parse(cell.Substring(0, end), CultureInfo.InvariantCulture);
            Assert.AreEqual(decimal.Truncate(value), value, $"'{cell}' is not a whole number");
            return (int)value;
        }

        private const string EscapedPipe = @"\|";
        private const string PipePlaceholder = "\u0001";

        // Splits rows on '|' but keeps escaped "\|" inside a cell (tab 04 formulas).
        private static List<string[]> ReadTable(string heading)
        {
            Assert.IsTrue(File.Exists(SheetPath), $"Data sheet not found at {SheetPath}");
            var lines = File.ReadAllLines(SheetPath);
            int start = Array.IndexOf(lines, heading);
            Assert.GreaterOrEqual(start, 0, $"Heading '{heading}' not found in {SheetPath}");

            var rows = new List<string[]>();
            bool headerSkipped = false;
            for (int i = start + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal) || line.StartsWith("|---", StringComparison.Ordinal))
                    continue;
                if (!headerSkipped)
                {
                    headerSkipped = true;
                    continue;
                }

                var cells = line.Trim('|').Replace(EscapedPipe, PipePlaceholder).Split('|');
                for (int c = 0; c < cells.Length; c++)
                    cells[c] = cells[c].Replace(PipePlaceholder, "|").Trim();
                rows.Add(cells);
            }
            return rows;
        }

        // RAW_WATER -> RawWater, ITEM_NUCLEAR_CORE -> ItemNuclearCore
        private static string ToEnumName(string code)
        {
            var parts = code.ToLowerInvariant().Split('_');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Concat(parts);
        }

        private static int ToMilli(string value) =>
            (int)Math.Round(decimal.Parse(value.Trim(), CultureInfo.InvariantCulture) * 1000m);
    }
}
