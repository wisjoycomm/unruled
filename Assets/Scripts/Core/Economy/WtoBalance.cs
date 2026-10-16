using System;

namespace Unruled.Core.Economy
{
    /// <summary>WTO market numbers (GDD §2.2 B). Fees and escrow rates come per player from <see cref="IMarketRates"/>.</summary>
    public sealed class WtoBalance
    {
        public readonly int VwapWindowTicks;
        public readonly int PriceBandPercent; // orders must be within +/- this of the reference price

        public WtoBalance(int vwapWindowTicks, int priceBandPercent)
        {
            if (vwapWindowTicks <= 0 || vwapWindowTicks > ItemMarket.MaxWindowTicks)
                throw new ArgumentOutOfRangeException(nameof(vwapWindowTicks));
            if (priceBandPercent < 0 || priceBandPercent >= 100) throw new ArgumentOutOfRangeException(nameof(priceBandPercent));
            VwapWindowTicks = vwapWindowTicks;
            PriceBandPercent = priceBandPercent;
        }
    }

    /// <summary>A player's WTO rates, set by their leaderboard bracket.</summary>
    public interface IMarketRates
    {
        /// <summary>The fee on a sale, in 1/100 of a percent, taken from the seller's proceeds.</summary>
        int FeeBasisPoints(int playerId);

        /// <summary>The share of a sale's value the seller locks as a deposit until delivery.</summary>
        int EscrowRatePercent(int playerId);
    }

    /// <summary>Whether THE SYSTEM lets a player place WTO orders.</summary>
    public interface IMarketAccess
    {
        bool MayTrade(int playerId, long tick);
    }
}
