using System;
using Unruled.Core.Ecs;

namespace Unruled.Core.Economy
{
    public enum OrderSide : byte
    {
        Buy = 1,
        Sell = 2
    }

    /// <summary>
    /// ECS component: a resting WTO limit order, on its own entity. A sell order holds the goods it still offers
    /// (taken from <see cref="Site"/>'s stockpile); a buy order's <see cref="Remaining"/> x <see cref="PriceCents"/>
    /// sits in the WTO escrow wallet, and the goods are delivered to <see cref="Site"/>. A sell order also keeps the
    /// seller's escrow deposit for what is left in the WTO escrow wallet.
    /// </summary>
    public struct WtoOrder
    {
        public int OwnerPlayerId;
        public ItemId Item;
        public OrderSide Side;
        public long PriceCents; // per unit
        public int Remaining;
        public EntityId Site;
        public long PlacedTick;
        public long DepositCents; // sell orders: deposit still locked for Remaining
    }

    /// <summary>A match handed from the order book to settlement. Value and deposit are already in WTO escrow.</summary>
    public struct TradeMatch
    {
        public int BuyerPlayerId;
        public int SellerPlayerId;
        public ItemId Item;
        public int Quantity;
        public long PriceCents;
        public long DepositCents;
        public EntityId SellerSite;
        public EntityId BuyerSite;
    }

    /// <summary>Takes over a matched trade: ships the goods (held by the order book until now) and settles the money.</summary>
    public interface ITradeSettlement
    {
        void Open(in TradeMatch trade, long tick);
    }

    /// <summary>
    /// ECS component on a fleet carrying WTO contract goods: which item and how many belong to the contract. Goods
    /// taken from it in combat become <see cref="StolenGoods"/>.
    /// </summary>
    public struct ContractCargo
    {
        public ItemId Item;
        public int Quantity;
    }

    /// <summary>
    /// ECS component next to a <see cref="Stockpile"/>: how many of its units of each item are Stolen_WTO_Cargo, which
    /// the WTO won't list. Counts above what the stockpile still holds mean the stolen units were used first.
    /// </summary>
    public unsafe struct StolenGoods
    {
        private fixed int _quantities[ItemIds.Count];

        public int Get(ItemId id) => _quantities[ItemIds.ToSlot(id)];

        public void Add(ItemId id, int quantity)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            int slot = ItemIds.ToSlot(id);
            _quantities[slot] = checked(_quantities[slot] + quantity);
        }

        public void Remove(ItemId id, int quantity)
        {
            int slot = ItemIds.ToSlot(id);
            if (quantity < 0 || quantity > _quantities[slot]) throw new ArgumentOutOfRangeException(nameof(quantity));
            _quantities[slot] -= quantity;
        }

        /// <summary>Units of <paramref name="id"/> in <paramref name="stock"/> that may be sold on the WTO.</summary>
        public int Clean(in Stockpile stock, ItemId id) => Math.Max(0, stock.Get(id) - Get(id));
    }

    /// <summary>
    /// ECS component: one item's trade history for the VWAP, one slot per tick in a ring of
    /// <see cref="MaxWindowTicks"/>. <see cref="LastVwapCents"/> is the VWAP after the latest trade (seeded with the
    /// base WTO price); it stands as the reference price while the window holds no trades.
    /// </summary>
    public unsafe struct ItemMarket
    {
        public const int MaxWindowTicks = 128;

        public ItemId Item;
        public long LastVwapCents;
        private fixed long _slotTick[MaxWindowTicks];
        private fixed long _slotValue[MaxWindowTicks]; // sum of price x volume, in cents
        private fixed long _slotVolume[MaxWindowTicks];

        public void Record(long tick, long priceCents, int volume, int windowTicks)
        {
            if (priceCents <= 0) throw new ArgumentOutOfRangeException(nameof(priceCents));
            if (volume <= 0) throw new ArgumentOutOfRangeException(nameof(volume));
            int slot = (int)(tick % MaxWindowTicks);
            if (_slotTick[slot] != tick)
            {
                _slotTick[slot] = tick;
                _slotValue[slot] = 0;
                _slotVolume[slot] = 0;
            }
            _slotValue[slot] = checked(_slotValue[slot] + priceCents * volume);
            _slotVolume[slot] += volume;
            LastVwapCents = ReferencePriceCents(tick, windowTicks);
        }

        // Ring slots for save games.
        internal void SlotAt(int i, out long tick, out long valueCents, out long volume)
        {
            if ((uint)i >= MaxWindowTicks) throw new ArgumentOutOfRangeException(nameof(i));
            tick = _slotTick[i];
            valueCents = _slotValue[i];
            volume = _slotVolume[i];
        }

        internal void RestoreSlot(int i, long tick, long valueCents, long volume)
        {
            if ((uint)i >= MaxWindowTicks) throw new ArgumentOutOfRangeException(nameof(i));
            _slotTick[i] = tick;
            _slotValue[i] = valueCents;
            _slotVolume[i] = volume;
        }

        /// <summary>VWAP over the ticks (tick - window, tick], or <see cref="LastVwapCents"/> if nothing traded then.</summary>
        public long ReferencePriceCents(long tick, int windowTicks)
        {
            if (windowTicks <= 0 || windowTicks > MaxWindowTicks) throw new ArgumentOutOfRangeException(nameof(windowTicks));
            long value = 0, volume = 0;
            for (int i = 0; i < MaxWindowTicks; i++)
            {
                long t = _slotTick[i];
                if (t > tick - windowTicks && t <= tick)
                {
                    value += _slotValue[i];
                    volume += _slotVolume[i];
                }
            }
            return volume > 0 ? value / volume : LastVwapCents;
        }
    }
}
