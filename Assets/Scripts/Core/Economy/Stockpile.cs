using System;

namespace Unruled.Core.Economy
{
    /// <summary>ECS component: quantity of each of the 20 items held by a site or fleet. Blittable fixed buffer.</summary>
    public unsafe struct Stockpile
    {
        private fixed int _quantities[ItemIds.Count];

        public int Get(ItemId id) => _quantities[Slot(id)];

        public bool Has(ItemId id, int quantity)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            return _quantities[Slot(id)] >= quantity;
        }

        /// <summary>Adds (or with a negative delta, removes) items. Throws if the result would be negative or overflow.</summary>
        public void Add(ItemId id, int delta)
        {
            int slot = Slot(id);
            long result = (long)_quantities[slot] + delta;
            if (result < 0)
                throw new InvalidOperationException($"Stockpile of {id} would go negative ({_quantities[slot]} + {delta}).");
            if (result > int.MaxValue)
                throw new OverflowException($"Stockpile of {id} would overflow.");
            _quantities[slot] = (int)result;
        }

        private static int Slot(ItemId id)
        {
            if (!ItemIds.IsValid(id)) throw new ArgumentOutOfRangeException(nameof(id), id, "Not a valid item.");
            return ItemIds.ToSlot(id);
        }
    }
}
