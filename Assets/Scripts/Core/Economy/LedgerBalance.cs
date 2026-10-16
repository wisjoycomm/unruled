using System;

namespace Unruled.Core.Economy
{
    /// <summary>Credit numbers. Amounts are in cents (<see cref="Wallet.CentsPerCredit"/>).</summary>
    public sealed class LedgerBalance
    {
        public readonly long StartingCents;

        public LedgerBalance(long startingCents)
        {
            if (startingCents < 0) throw new ArgumentOutOfRangeException(nameof(startingCents));
            StartingCents = startingCents;
        }
    }
}
