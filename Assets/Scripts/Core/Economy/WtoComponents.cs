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
    /// sits in the WTO escrow wallet, and the goods are delivered to <see cref="Site"/>.
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
