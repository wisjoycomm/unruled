using NUnit.Framework;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.AI
{
    public class AiFinanceTests
    {
        private const int Me = 1;
        private const byte Trader = 2;

        private SimulationWorld _sim;
        private EntityId _home, _hq;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 2);
            _home = _sim.World.Create();
            _sim.Sites.Add(_home, new SiteInfo { OwnerPlayerId = Me });
            _sim.Stockpiles.Add(_home, default);
            _sim.Weather.Add(_home, default);
            _hq = _sim.CreatePlayer(Me, _home);
            _sim.Standings.Get(_hq).IsAudited = true;
        }

        private long Reserve => _sim.Balance.Ai.CashReserveCredits * Wallet.CentsPerCredit;
        private long Cash => _sim.Ledger.Balance(_hq);
        private ref Loan Loan => ref _sim.Loans.Get(_hq);

        private AiOrder? Think(AiAction action)
        {
            _sim.Ai.Assign(_hq, Trader);
            do
                _sim.Clock.Advance(1);
            while (_sim.Clock.CurrentTick % _sim.Balance.Ai.ThinkIntervalTicks != 0);
            int n = _sim.Ai.Think(_sim.Clock.CurrentTick);
            for (int i = 0; i < n; i++)
                if (_sim.Ai.OrderAt(i).Action == action)
                    return _sim.Ai.OrderAt(i);
            return null;
        }

        private void RunPendingOrders()
        {
            for (int i = 0; i < 1_000 && _sim.Clock.PendingCommandCount > 0; i++)
                _sim.Clock.Advance(1);
            Assert.AreEqual(0, _sim.Clock.PendingCommandCount, "orders arrived");
        }

        [Test]
        public void ABrokeAi_BorrowsUpToItsCashReserve_AndNoMore()
        {
            Assert.IsTrue(Think(AiAction.Borrow).HasValue);
            RunPendingOrders();
            Assert.AreEqual(Reserve, Cash);
            Assert.AreEqual(Reserve, Loan.PrincipalCents);
            Assert.IsFalse(Think(AiAction.Borrow).HasValue, "the reserve is in hand");
        }

        [Test]
        public void HoldingTheTravelCash_ItDoesNotBorrow()
        {
            _sim.Ledger.Grant(_hq, _sim.Balance.Ai.TravelCashCredits * Wallet.CentsPerCredit);
            Assert.IsFalse(Think(AiAction.Borrow).HasValue, "money locked in orders isn't topped up from loans");
            _sim.Ledger.Burn(_hq, 1);
            Assert.IsTrue(Think(AiAction.Borrow).HasValue);
        }

        [Test]
        public void RepaysTheDebt_FromCashAboveTwiceTheReserve()
        {
            _sim.Lending.Handle(new DelayedPlayerCommand { PlayerId = Me, CommandType = CommandType.Borrow, ParamA = 5_000 }, 0);
            _sim.Ledger.Burn(_hq, Cash);
            _sim.Ledger.Grant(_hq, 3 * Reserve);
            long cash = Cash;

            Assert.IsTrue(Think(AiAction.Repay).HasValue);
            RunPendingOrders();
            Assert.AreEqual(5_000 * Wallet.CentsPerCredit - (cash - Reserve), Loan.PrincipalCents, "repaid all but the reserve");
        }

        [Test]
        public void RepaysAllItCan_OnceTheDebtFallsDue()
        {
            _sim.Lending.Handle(new DelayedPlayerCommand { PlayerId = Me, CommandType = CommandType.Borrow, ParamA = 1_000 }, 0);
            Loan.DueTick = _sim.Clock.CurrentTick + 5;

            Assert.IsTrue(Think(AiAction.Repay).HasValue);
            RunPendingOrders();
            Assert.AreEqual(0, Loan.DebtCents);
            Assert.AreEqual(0, Cash);
        }

        [Test]
        public void AtTheBaseCreditLimit_UnlockingTechOrInfrastructure_RaisesTheLimitAndAllowsBorrowingAgain()
        {
            Loan.PrincipalCents = _sim.Balance.Ledger.CreditLimitCents;
            Loan.DueTick = long.MaxValue;
            Assert.IsFalse(Think(AiAction.Borrow).HasValue, "at the base limit");

            _sim.TechProgress.Get(_hq).TechTier = 2;
            var outpost = _sim.World.Create();
            _sim.Sites.Add(outpost, new SiteInfo { OwnerPlayerId = Me });
            _sim.Refineries.Add(_sim.World.Create(), new Refinery { Site = _home, Output = ItemId.ItemSteel });

            Assert.IsTrue(Think(AiAction.Borrow).HasValue, "headroom unlocked by tech, outpost and refinery");
            RunPendingOrders();
            Assert.AreEqual(Reserve, Cash);
        }
    }
}
