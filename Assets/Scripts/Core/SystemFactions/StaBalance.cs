using System;

namespace Unruled.Core.SystemFactions
{
    /// <summary>STA toll highway numbers. Tolls are in cents (see <see cref="Economy.Wallet"/>).</summary>
    public sealed class StaBalance
    {
        public readonly int TollCentsPerShipPer100Units;

        public StaBalance(int tollCentsPerShipPer100Units)
        {
            if (tollCentsPerShipPer100Units < 0) throw new ArgumentOutOfRangeException(nameof(tollCentsPerShipPer100Units));
            TollCentsPerShipPer100Units = tollCentsPerShipPer100Units;
        }
    }
}
