using System;

namespace Unruled.Core.Economy
{
    /// <summary>Credit numbers. Amounts are in cents (<see cref="Wallet.CentsPerCredit"/>).</summary>
    public sealed class LedgerBalance
    {
        public readonly long StartingCents;
        public readonly long CreditLimitCents; // most a player may owe THE SYSTEM
        public readonly long InterestFreeTicks; // no interest is charged before this game tick
        public readonly int DailyInterestBasisPoints; // charged on the principal each day after that
        public readonly long LoanTermTicks; // a debt falls due this long after interest starts on it

        public LedgerBalance(
            long startingCents, long creditLimitCents, long interestFreeTicks, int dailyInterestBasisPoints, long loanTermTicks)
        {
            if (startingCents < 0) throw new ArgumentOutOfRangeException(nameof(startingCents));
            if (creditLimitCents < 0) throw new ArgumentOutOfRangeException(nameof(creditLimitCents));
            if (interestFreeTicks < 0) throw new ArgumentOutOfRangeException(nameof(interestFreeTicks));
            if (dailyInterestBasisPoints < 0) throw new ArgumentOutOfRangeException(nameof(dailyInterestBasisPoints));
            if (loanTermTicks < 1) throw new ArgumentOutOfRangeException(nameof(loanTermTicks));
            StartingCents = startingCents;
            CreditLimitCents = creditLimitCents;
            InterestFreeTicks = interestFreeTicks;
            DailyInterestBasisPoints = dailyInterestBasisPoints;
            LoanTermTicks = loanTermTicks;
        }
    }
}
