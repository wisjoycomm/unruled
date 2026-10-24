using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;

namespace Unruled.Core.SystemFactions
{
    /// <summary>
    /// STA toll gates (tab 04 STA_Toll_Highway, tab 05 STA_Toll_Discount). Departing on a toll lane costs
    /// ships x toll per 100 units x length (rounded up), less the owner's bracket discount (rounded down), paid from the
    /// owner's wallet into the STA pool. A player without a wallet, or who can't pay, may not enter.
    /// A player's standing sits on the same HQ entity as their wallet.
    /// </summary>
    public sealed class StaTollGate : ITollGate
    {
        private readonly Ledger _ledger;
        private readonly ComponentStore<PlayerStanding> _standings;
        private readonly EntityId _pool;
        private readonly StarlaneBalance _laneBalance;
        private readonly LeaderboardBalance _brackets;
        private readonly StaBalance _balance;

        public StaTollGate(
            Ledger ledger, ComponentStore<PlayerStanding> standings, EntityId pool, StarlaneBalance laneBalance,
            LeaderboardBalance brackets, StaBalance balance)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _standings = standings ?? throw new ArgumentNullException(nameof(standings));
            if (pool.IsNull) throw new ArgumentException("STA pool required.", nameof(pool));
            _pool = pool;
            _laneBalance = laneBalance ?? throw new ArgumentNullException(nameof(laneBalance));
            _brackets = brackets ?? throw new ArgumentNullException(nameof(brackets));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public long TollCents(in FleetUnit unit, in StarlaneEdge edge, int discountPercent)
        {
            if (!_laneBalance.Get(edge.Tier).ChargesStaToll)
                return 0;
            long full = ((long)unit.TotalShips * _balance.TollCentsPerShipPer100Units * edge.Length + 99) / 100;
            return full * (100 - discountPercent) / 100;
        }

        public bool TryCharge(in FleetUnit unit, in StarlaneEdge edge)
        {
            if (!_laneBalance.Get(edge.Tier).ChargesStaToll)
                return true;
            if (!_ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var wallet))
                return false;
            return _ledger.Transfer(wallet, _pool, TollCents(unit, edge, DiscountPercent(wallet)));
        }

        public long QuoteCents(in FleetUnit unit, in StarlaneEdge edge)
        {
            if (!_laneBalance.Get(edge.Tier).ChargesStaToll)
                return 0;
            return _ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var wallet)
                ? TollCents(unit, edge, DiscountPercent(wallet))
                : TollCents(unit, edge, 0);
        }

        public bool CanPay(in FleetUnit unit, in StarlaneEdge edge)
        {
            if (!_laneBalance.Get(edge.Tier).ChargesStaToll)
                return true;
            return _ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var wallet) &&
                   _ledger.Balance(wallet) >= TollCents(unit, edge, DiscountPercent(wallet));
        }

        private int DiscountPercent(EntityId wallet)
        {
            byte bracket = _standings.Has(wallet) ? _standings.Get(wallet).Bracket : LeaderboardBalance.Unrated;
            return _brackets.Bracket(bracket).TollDiscountPercent;
        }
    }
}
