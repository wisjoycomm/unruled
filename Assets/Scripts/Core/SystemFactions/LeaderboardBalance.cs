using System;
using Unruled.Core.Economy;

namespace Unruled.Core.SystemFactions
{
    /// <summary>One row of data sheet tab 05: what a Top-10K bracket pays and gets.</summary>
    public sealed class BracketRules
    {
        public readonly byte Id;
        public readonly string Name;
        public readonly int RankMin; // 0 for the unrated bracket
        public readonly int RankMax;
        public readonly int EscrowRatePercent;
        public readonly int EvadingEscrowRatePercent;
        public readonly int MarketFeeBasisPoints; // 1/100 of a percent
        public readonly int DividendSharePercent; // of the tax pools, split among the bracket
        public readonly int TollDiscountPercent;
        public readonly int GrantSharePercent;
        public readonly ItemId[] GrantItems;
        public readonly int NoaaLeadTicks;
        public readonly bool SeesCurrentStorms;

        public BracketRules(
            byte id, string name, int rankMin, int rankMax, int escrowRatePercent, int evadingEscrowRatePercent,
            int marketFeeBasisPoints, int dividendSharePercent, int tollDiscountPercent, int grantSharePercent,
            ItemId[] grantItems, int noaaLeadTicks, bool seesCurrentStorms)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name required.", nameof(name));
            if (rankMin < 0 || rankMax < rankMin) throw new ArgumentOutOfRangeException(nameof(rankMax));
            if (escrowRatePercent < 0 || escrowRatePercent > 100) throw new ArgumentOutOfRangeException(nameof(escrowRatePercent));
            if (evadingEscrowRatePercent < escrowRatePercent || evadingEscrowRatePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(evadingEscrowRatePercent));
            if (marketFeeBasisPoints < 0 || marketFeeBasisPoints > 10_000) throw new ArgumentOutOfRangeException(nameof(marketFeeBasisPoints));
            if (dividendSharePercent < 0 || dividendSharePercent > 100) throw new ArgumentOutOfRangeException(nameof(dividendSharePercent));
            if (tollDiscountPercent < 0 || tollDiscountPercent > 100) throw new ArgumentOutOfRangeException(nameof(tollDiscountPercent));
            if (grantSharePercent < 0 || grantSharePercent > 100) throw new ArgumentOutOfRangeException(nameof(grantSharePercent));
            if (noaaLeadTicks < 0) throw new ArgumentOutOfRangeException(nameof(noaaLeadTicks));

            Id = id;
            Name = name;
            RankMin = rankMin;
            RankMax = rankMax;
            EscrowRatePercent = escrowRatePercent;
            EvadingEscrowRatePercent = evadingEscrowRatePercent;
            MarketFeeBasisPoints = marketFeeBasisPoints;
            DividendSharePercent = dividendSharePercent;
            TollDiscountPercent = tollDiscountPercent;
            GrantSharePercent = grantSharePercent;
            GrantItems = grantItems ?? throw new ArgumentNullException(nameof(grantItems));
            NoaaLeadTicks = noaaLeadTicks;
            SeesCurrentStorms = seesCurrentStorms;
        }
    }

    /// <summary>Data sheet tab 05: the seven brackets, indexed by id (0 = unrated), and how often the audit runs.</summary>
    public sealed class LeaderboardBalance
    {
        public const byte Unrated = 0;

        public readonly int AuditIntervalTicks;
        private readonly BracketRules[] _brackets;

        public LeaderboardBalance(BracketRules[] brackets, int auditIntervalTicks)
        {
            if (auditIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(auditIntervalTicks));
            if (brackets == null || brackets.Length == 0) throw new ArgumentException("Brackets required.", nameof(brackets));
            for (int i = 0; i < brackets.Length; i++)
                if (brackets[i] == null || brackets[i].Id != i)
                    throw new ArgumentException($"Bracket {i} missing or out of order.", nameof(brackets));
            int dividends = 0;
            int grants = 0;
            for (int i = 0; i < brackets.Length; i++)
            {
                dividends += brackets[i].DividendSharePercent;
                grants += brackets[i].GrantSharePercent;
            }
            if (dividends > 100 || grants > 100)
                throw new ArgumentException("Bracket shares add up to more than the pool.", nameof(brackets));
            _brackets = brackets;
            AuditIntervalTicks = auditIntervalTicks;
        }

        /// <summary>The bracket a rank (1 = richest) falls in; ranks no bracket covers are unrated.</summary>
        public byte BracketForRank(int rank)
        {
            for (int i = 1; i < _brackets.Length; i++)
                if (rank >= _brackets[i].RankMin && rank <= _brackets[i].RankMax)
                    return (byte)i;
            return Unrated;
        }

        public int Count => _brackets.Length;

        public BracketRules Bracket(byte id)
        {
            if (id >= _brackets.Length) throw new ArgumentOutOfRangeException(nameof(id));
            return _brackets[id];
        }
    }
}
