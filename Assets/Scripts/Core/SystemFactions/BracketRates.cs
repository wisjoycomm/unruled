using System;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;

namespace Unruled.Core.SystemFactions
{
    /// <summary>
    /// A player's bracket rules (tab 05), read from the <see cref="PlayerStanding"/> on their HQ (the wallet entity).
    /// A player without a standing is unrated; a player evading the audit pays the unrated evading escrow rate.
    /// </summary>
    public sealed class BracketRates : IMarketRates
    {
        private readonly Ledger _ledger;
        private readonly ComponentStore<PlayerStanding> _standings;
        private readonly LeaderboardBalance _brackets;

        public BracketRates(Ledger ledger, ComponentStore<PlayerStanding> standings, LeaderboardBalance brackets)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _standings = standings ?? throw new ArgumentNullException(nameof(standings));
            _brackets = brackets ?? throw new ArgumentNullException(nameof(brackets));
        }

        public BracketRules For(int playerId)
        {
            byte bracket = _ledger.TryFindPlayerWallet(playerId, out var hq) && _standings.Has(hq)
                ? _standings.Get(hq).Bracket
                : LeaderboardBalance.Unrated;
            return _brackets.Bracket(bracket);
        }

        public int FeeBasisPoints(int playerId) => For(playerId).MarketFeeBasisPoints;

        public int EscrowRatePercent(int playerId)
        {
            if (_ledger.TryFindPlayerWallet(playerId, out var hq) && _standings.Has(hq) && _standings.Get(hq).IsEvading)
                return _brackets.Bracket(LeaderboardBalance.Unrated).EvadingEscrowRatePercent;
            return For(playerId).EscrowRatePercent;
        }
    }
}
