using System;
using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;

namespace Unruled.Tests.Core.Economy
{
    public class StockpileTests
    {
        [Test]
        public void ItemIds_SplitIntoTenRawAndTenRefined()
        {
            int raw = 0, refined = 0;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var id = ItemIds.FromSlot(slot);
                Assert.IsTrue(ItemIds.IsValid(id));
                Assert.AreEqual(slot, ItemIds.ToSlot(id));
                if (ItemIds.IsRaw(id)) raw++;
                if (ItemIds.IsRefined(id)) refined++;
            }

            Assert.AreEqual(ItemIds.RawCount, raw);
            Assert.AreEqual(ItemIds.Count - ItemIds.RawCount, refined);
            Assert.IsFalse(ItemIds.IsValid(ItemId.None));
            Assert.IsFalse(ItemIds.IsValid((ItemId)21));
        }

        [Test]
        public void Add_AndRemove_TracksEachItemSeparately()
        {
            var stock = default(Stockpile);
            stock.Add(ItemId.RawWater, 10);
            stock.Add(ItemId.ItemNuclearCore, 2);
            stock.Add(ItemId.RawWater, -4);

            Assert.AreEqual(6, stock.Get(ItemId.RawWater));
            Assert.AreEqual(2, stock.Get(ItemId.ItemNuclearCore));
            Assert.AreEqual(0, stock.Get(ItemId.ItemOxygen));
            Assert.IsTrue(stock.Has(ItemId.RawWater, 6));
            Assert.IsFalse(stock.Has(ItemId.RawWater, 7));
        }

        [Test]
        public void Add_RejectsNegativeResultOverflowAndInvalidItem()
        {
            var stock = default(Stockpile);
            stock.Add(ItemId.RawIron, 3);

            Assert.Throws<InvalidOperationException>(() => stock.Add(ItemId.RawIron, -4));
            Assert.AreEqual(3, stock.Get(ItemId.RawIron));

            stock.Add(ItemId.RawCopper, int.MaxValue);
            Assert.Throws<OverflowException>(() => stock.Add(ItemId.RawCopper, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stock.Get(ItemId.None));
        }

        [Test]
        public void Stockpile_WorksAsEcsComponent_ByReference()
        {
            var world = new World();
            var stockpiles = world.CreateStore<Stockpile>();
            var site = world.Create();
            stockpiles.Add(site, default);

            stockpiles.Get(site).Add(ItemId.ItemFuel, 5);

            Assert.AreEqual(5, stockpiles.Get(site).Get(ItemId.ItemFuel));
        }
    }
}
