using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Economy
{
    public class LoanSystemTests
    {
        private const int Me = 1;
        private const long Day = SimTick.TicksPerDay;

        private SimulationWorld _sim;
        private EntityId _core, _hq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 5);
            _core = _sim.World.Create();
            _sim.Sites.Add(_core, new SiteInfo { OwnerPlayerId = Me });
            _sim.Stockpiles.Add(_core, default);
            _hq = _sim.CreatePlayer(Me, _core);
        }

        private LedgerBalance Rules => _sim.Balance.Ledger;
        private ref Loan Loan => ref _sim.Loans.Get(_hq);
        private long Cash => _sim.Ledger.Balance(_hq);

        private void Send(CommandType type, int credits, long tick) =>
            _sim.Lending.Handle(new DelayedPlayerCommand { PlayerId = Me, CommandType = type, ParamA = credits }, tick);

        private long Credits(long credits) => credits * Wallet.CentsPerCredit;

        [Test]
        public void PlayersStartWithNoCredits_AndNoDebt()
        {
            Assert.AreEqual(0, Cash);
            Assert.AreEqual(0, Loan.DebtCents);
        }

        [Test]
        public void Borrow_LendsUpToTheLimit_AndTheDebtFallsDueATermAfterTheFreeMonth()
        {
            Send(CommandType.Borrow, 4_000, tick: 100);
            Assert.AreEqual(Credits(4_000), Cash);
            Assert.AreEqual(Rules.InterestFreeTicks + Rules.LoanTermTicks, Loan.DueTick);

            Send(CommandType.Borrow, 1_000_000, tick: 200);
            Assert.AreEqual(Rules.CreditLimitCents, Cash, "capped at the limit");
            Assert.AreEqual(Rules.CreditLimitCents, Loan.PrincipalCents);
            Assert.AreEqual(0, _sim.Lending.HeadroomCents(_hq, 200));
        }

        [Test]
        public void Borrow_AfterTheFreeMonth_FallsDueATermLater()
        {
            long tick = Rules.InterestFreeTicks + 5 * Day;
            Send(CommandType.Borrow, 100, tick);
            Assert.AreEqual(tick + Rules.LoanTermTicks, Loan.DueTick);
        }

        [Test]
        public void NoInterest_InTheFreeMonth_ThenADailyShareOfThePrincipal()
        {
            Send(CommandType.Borrow, 1_000, tick: 0);
            for (long t = Day; t <= Rules.InterestFreeTicks; t += Day)
                _sim.Lending.OnTick(t);
            Assert.AreEqual(0, Loan.InterestCents, "free month");

            _sim.Lending.OnTick(Rules.InterestFreeTicks + Day);
            _sim.Lending.OnTick(Rules.InterestFreeTicks + 2 * Day);
            Assert.AreEqual(2 * Credits(1_000) * Rules.DailyInterestBasisPoints / 10_000, Loan.InterestCents);
        }

        [Test]
        public void Repay_PaysTheInterestIntoTheWtoPool_ThenBurnsThePrincipal()
        {
            Send(CommandType.Borrow, 1_000, tick: 0);
            Loan.InterestCents = Credits(30);
            _sim.Ledger.Grant(_hq, Credits(30));

            Send(CommandType.Repay, 530, tick: 10);
            Assert.AreEqual(Credits(30), _sim.Ledger.Balance(_sim.WtoFeePool), "interest to the pool");
            Assert.AreEqual(0, Loan.InterestCents);
            Assert.AreEqual(Credits(500), Loan.PrincipalCents);
            Assert.AreEqual(Credits(500), Cash);

            Send(CommandType.Repay, 1_000_000, tick: 20);
            Assert.AreEqual(0, Loan.DebtCents, "repaid with what the wallet holds");
            Assert.AreEqual(0, Loan.DueTick);
            Assert.AreEqual(0, Cash);
        }

        [Test]
        public void Overdue_TakesTheCredits_ThenCoreGoodsAtTheReferencePrice_AndRefusesNewLoans()
        {
            Send(CommandType.Borrow, 1_000, tick: 0);
            _sim.Ledger.Burn(_hq, Credits(600));
            long due = Loan.DueTick;
            long price = _sim.Market.ReferencePriceCents(ItemId.RawIron, due);
            _sim.Stockpiles.Get(_core).Add(ItemId.RawIron, 100_000);
            long debt = Loan.DebtCents;

            Assert.Greater(_sim.Lending.HeadroomCents(_hq, due - 1), 0);
            Assert.AreEqual(0, _sim.Lending.HeadroomCents(_hq, due), "no new loans while overdue");

            long dueDay = (due + Day - 1) / Day * Day;
            _sim.Lending.OnTick(dueDay);
            long interest = debt - Credits(1_000) + Credits(1_000) * Rules.DailyInterestBasisPoints / 10_000;
            long owedAfterCash = Credits(1_000) + interest - Credits(400);
            long units = (owedAfterCash + price - 1) / price;
            Assert.AreEqual(0, Cash, "credits first");
            Assert.AreEqual(0, Loan.DebtCents, "then goods");
            Assert.AreEqual(100_000 - units, _sim.Stockpiles.Get(_core).Get(ItemId.RawIron));
            Assert.AreEqual(0, Loan.DueTick);
        }

        [Test]
        public void Overdue_WithNotEnoughGoods_StaysInDebt()
        {
            Send(CommandType.Borrow, 1_000, tick: 0);
            _sim.Ledger.Burn(_hq, Cash);
            _sim.Stockpiles.Get(_core).Add(ItemId.RawIron, 1);
            long dueDay = (Loan.DueTick + Day - 1) / Day * Day;

            _sim.Lending.OnTick(dueDay);
            Assert.AreEqual(0, _sim.Stockpiles.Get(_core).Get(ItemId.RawIron));
            Assert.Greater(Loan.DebtCents, 0);
            Assert.IsTrue(LoanSystem.IsOverdue(in Loan, dueDay));
        }
    }
}
