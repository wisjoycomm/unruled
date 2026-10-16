using System;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Moves items between two stockpiles for the cargo systems. Stolen units move first, so their
    /// <see cref="StolenGoods"/> tags follow them. The caller checks that the source holds the quantity.
    /// </summary>
    public static class CargoMover
    {
        public static void Move(
            ComponentStore<Stockpile> stockpiles, ComponentStore<StolenGoods> stolen, EntityId from, EntityId to,
            ItemId item, int quantity)
        {
            ref var source = ref stockpiles.Get(from);
            int tagged = 0;
            if (stolen.Has(from))
                tagged = Math.Min(quantity, Math.Min(stolen.Get(from).Get(item), source.Get(item)));
            source.Add(item, -quantity);
            stockpiles.Get(to).Add(item, quantity);
            if (tagged == 0)
                return;
            stolen.Get(from).Remove(item, tagged);
            if (!stolen.Has(to))
                stolen.Add(to, default);
            stolen.Get(to).Add(item, tagged);
        }
    }
}
