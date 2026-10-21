using System;

namespace Unruled.Core.Economy
{
    /// <summary>Credit numbers. Amounts are in cents (<see cref="Wallet.CentsPerCredit"/>).</summary>
    public sealed class LedgerBalance
    {
        public readonly long StartingCents;
        public readonly long CreditLimitCents; // base amount a player may owe THE SYSTEM
        public readonly long InterestFreeTicks; // no interest is charged before this game tick
        public readonly int DailyInterestBasisPoints; // charged on the principal each day after that
        public readonly long LoanTermTicks; // a debt falls due this long after interest starts on it
        public readonly long CreditPerTechTierCents; // extra credit limit per branch tier unlocked above Tier 1
        public readonly long OpenGateCreditCents; // extra credit limit once the player's jump gate is open
        public readonly long CreditPerOutpostCents; // extra credit limit per owned site beyond the core base
        public readonly long CreditPerRefineryCents; // extra credit limit per refinery at an owned site

        public LedgerBalance(
            long startingCents, long creditLimitCents, long interestFreeTicks, int dailyInterestBasisPoints, long loanTermTicks,
            long creditPerTechTierCents = 0, long openGateCreditCents = 0, long creditPerOutpostCents = 0,
            long creditPerRefineryCents = 0)
        {
            if (startingCents < 0) throw new ArgumentOutOfRangeException(nameof(startingCents));
            if (creditLimitCents < 0) throw new ArgumentOutOfRangeException(nameof(creditLimitCents));
            if (interestFreeTicks < 0) throw new ArgumentOutOfRangeException(nameof(interestFreeTicks));
            if (dailyInterestBasisPoints < 0) throw new ArgumentOutOfRangeException(nameof(dailyInterestBasisPoints));
            if (loanTermTicks < 1) throw new ArgumentOutOfRangeException(nameof(loanTermTicks));
            if (creditPerTechTierCents < 0) throw new ArgumentOutOfRangeException(nameof(creditPerTechTierCents));
            if (openGateCreditCents < 0) throw new ArgumentOutOfRangeException(nameof(openGateCreditCents));
            if (creditPerOutpostCents < 0) throw new ArgumentOutOfRangeException(nameof(creditPerOutpostCents));
            if (creditPerRefineryCents < 0) throw new ArgumentOutOfRangeException(nameof(creditPerRefineryCents));
            StartingCents = startingCents;
            CreditLimitCents = creditLimitCents;
            InterestFreeTicks = interestFreeTicks;
            DailyInterestBasisPoints = dailyInterestBasisPoints;
            LoanTermTicks = loanTermTicks;
            CreditPerTechTierCents = creditPerTechTierCents;
            OpenGateCreditCents = openGateCreditCents;
            CreditPerOutpostCents = creditPerOutpostCents;
            CreditPerRefineryCents = creditPerRefineryCents;
        }

        /// <summary>
        /// Dynamic credit limit for a player: base limit plus tech progression (branch tiers unlocked above Tier 1 and an
        /// open jump gate) and productive infrastructure collateral (owned outposts beyond the core base and refineries).
        /// Returns 0 when borrowing is disabled (<see cref="CreditLimitCents"/> is 0).
        /// </summary>
        public long DynamicCreditLimitCents(int techTiersAboveOne, bool gateOpen, int outposts, int refineries)
        {
            if (CreditLimitCents <= 0)
                return 0;
            return CreditLimitCents
                + Math.Max(0, (long)techTiersAboveOne) * CreditPerTechTierCents
                + (gateOpen ? OpenGateCreditCents : 0)
                + Math.Max(0, (long)outposts) * CreditPerOutpostCents
                + Math.Max(0, (long)refineries) * CreditPerRefineryCents;
        }
    }
}
