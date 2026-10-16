using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
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

                var cells = line.Trim('|').Split('|');
                for (int c = 0; c < cells.Length; c++)
                    cells[c] = cells[c].Trim();
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
