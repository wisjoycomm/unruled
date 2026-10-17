using System;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Owns <see cref="Loan"/>: THE SYSTEM's credit line, the one source of new money in play (players start with none).
    /// Borrow (target: any site, for latency; ParamA credits) lends up to the credit limit, never while overdue.
    /// Repay (ParamA credits) pays interest first, into the WTO fee pool (it returns as leaderboard dividends), then the
    /// principal, which is burned. Every day after the interest-free start, interest accrues on the principal; a debt
    /// falls due a loan term after interest starts on it. On each day it is overdue THE SYSTEM takes the player's credits,
    /// then core-base goods (item slot order) at the WTO reference price, until the debt is paid. Goods it takes are gone.
    /// </summary>
    public sealed class LoanSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<Loan> _loans;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly Ledger _ledger;
        private readonly EntityId _interestPool;
        private readonly WtoMarketEngine _market;
        private readonly LedgerBalance _balance;

        public LoanSystem(
            ComponentStore<Loan> loans, ComponentStore<PlayerHq> players, ComponentStore<Stockpile> stockpiles, Ledger ledger,
            EntityId interestPool, WtoMarketEngine market, LedgerBalance balance)
        {
            _loans = loans ?? throw new ArgumentNullException(nameof(loans));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (interestPool.IsNull) throw new ArgumentException("Interest pool required.", nameof(interestPool));
            _interestPool = interestPool;
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        /// <summary>What the player may still borrow now: 0 while overdue.</summary>
        public long HeadroomCents(EntityId hq, long tick)
        {
            if (!_loans.Has(hq))
                return 0;
            ref var loan = ref _loans.Get(hq);
            return IsOverdue(in loan, tick) ? 0 : Math.Max(0, _balance.CreditLimitCents - loan.DebtCents);
        }

        public static bool IsOverdue(in Loan loan, long tick) => loan.DebtCents > 0 && tick >= loan.DueTick;

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.Borrow && command.CommandType != CommandType.Repay)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (command.ParamA <= 0 || !_ledger.TryFindPlayerWallet(command.PlayerId, out var hq) || !_loans.Has(hq))
                return;
            long cents = command.ParamA * Wallet.CentsPerCredit;
            if (command.CommandType == CommandType.Borrow)
                Borrow(hq, cents, tick);
            else
                Repay(hq, Math.Min(cents, _ledger.Balance(hq)));
        }

        private void Borrow(EntityId hq, long cents, long tick)
        {
            long amount = Math.Min(cents, HeadroomCents(hq, tick));
            if (amount <= 0)
                return;
            ref var loan = ref _loans.Get(hq);
            if (loan.DebtCents == 0)
                loan.DueTick = Math.Max(tick, _balance.InterestFreeTicks) + _balance.LoanTermTicks;
            loan.PrincipalCents += amount;
            _ledger.Lend(hq, amount);
        }

        private void Repay(EntityId hq, long cents)
        {
            ref var loan = ref _loans.Get(hq);
            long interest = Math.Min(cents, loan.InterestCents);
            if (interest > 0 && _ledger.Transfer(hq, _interestPool, interest))
            {
                loan.InterestCents -= interest;
                cents -= interest;
            }
            long principal = Math.Min(cents, loan.PrincipalCents);
            if (principal > 0 && _ledger.Burn(hq, principal))
                loan.PrincipalCents -= principal;
            if (loan.DebtCents == 0)
                loan.DueTick = 0;
        }

        public void OnTick(long tick)
        {
            if (tick % SimTick.TicksPerDay != 0)
                return;
            for (int i = 0; i < _loans.Count; i++)
            {
                ref var loan = ref _loans.At(i);
                if (loan.DebtCents == 0)
                    continue;
                if (tick > _balance.InterestFreeTicks)
                    loan.InterestCents += loan.PrincipalCents * _balance.DailyInterestBasisPoints / 10_000;
                if (!IsOverdue(in loan, tick))
                    continue;
                var hq = _loans.EntityAt(i);
                Repay(hq, Math.Min(_ledger.Balance(hq), loan.DebtCents));
                if (loan.DebtCents > 0 && _players.Has(hq))
                    Seize(ref loan, _players.Get(hq).CoreBase, tick);
            }
        }

        private void Seize(ref Loan loan, EntityId core, long tick)
        {
            if (!_stockpiles.Has(core))
                return;
            ref var stock = ref _stockpiles.Get(core);
            for (int slot = 0; slot < ItemIds.Count && loan.DebtCents > 0; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                long price = _market.ReferencePriceCents(item, tick);
                int held = stock.Get(item);
                if (price <= 0 || held <= 0)
                    continue;
                long units = Math.Min(held, (loan.DebtCents + price - 1) / price);
                stock.Add(item, -(int)units);
                long value = units * price;
                long interest = Math.Min(value, loan.InterestCents);
                loan.InterestCents -= interest;
                loan.PrincipalCents -= Math.Min(value - interest, loan.PrincipalCents);
            }
            if (loan.DebtCents == 0)
                loan.DueTick = 0;
        }
    }
}
