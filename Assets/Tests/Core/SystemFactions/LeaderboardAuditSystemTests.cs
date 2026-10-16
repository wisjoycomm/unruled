using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.SystemFactions
{
    public class LeaderboardAuditSystemTests
    {
        private SimulationWorld _sim;
        private readonly EntityId[] _hq = new EntityId[5];
        private readonly EntityId[] _site = new EntityId[5];

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 4);
            for (int p = 1; p <= 4; p++)
            {
                _site[p] = _sim.World.Create();
                _sim.Sites.Add(_site[p], new SiteInfo { OwnerPlayerId = p });
                _sim.Stockpiles.Add(_site[p], default);
                _hq[p] = _sim.CreatePlayer(p, _site[p]);
            }
        }

        private void OptIn(int player, bool join) =>
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                ExecuteAtTick = _sim.Clock.CurrentTick + 1, PlayerId = player, CommandType = CommandType.AuditOptIn,
                Target = _site[player], ParamA = join ? 1 : 0
            });

        private void RunToAudit()
        {
            long interval = _sim.Balance.Leaderboard.AuditIntervalTicks;
            _sim.Clock.Advance(interval - _sim.Clock.CurrentTick % interval);
        }

        private PlayerStanding Standing(int player) => _sim.Standings.Get(_hq[player]);

        private long TotalCents()
        {
            long total = 0;
            for (int i = 0; i < _sim.Wallets.Count; i++)
                total += _sim.Wallets.At(i).Cents;
            return total;
        }

        [Test]
        public void BracketForRank_FollowsTab05()
        {
            var b = _sim.Balance.Leaderboard;
            Assert.AreEqual(6, b.BracketForRank(1));
            Assert.AreEqual(6, b.BracketForRank(10));
            Assert.AreEqual(5, b.BracketForRank(11));
            Assert.AreEqual(5, b.BracketForRank(100));
            Assert.AreEqual(4, b.BracketForRank(101));
            Assert.AreEqual(3, b.BracketForRank(2_000));
            Assert.AreEqual(2, b.BracketForRank(2_001));
            Assert.AreEqual(1, b.BracketForRank(10_000));
            Assert.AreEqual(LeaderboardBalance.Unrated, b.BracketForRank(10_001));
        }

        [Test]
        public void Audit_RanksByNetWorth_AndPaysOnlyFromThePools()
        {
            _sim.Stockpiles.Get(_site[2]).Add(ItemId.ItemSteel, 100); // +100 x 50 credits
            _sim.Stockpiles.Get(_site[3]).Add(ItemId.ItemSteel, 10);
            _sim.Ledger.Grant(_sim.WtoFeePool, 900_000);
            _sim.Ledger.Grant(_sim.StaTollPool, 300_000);
            for (int p = 1; p <= 3; p++)
                OptIn(p, true);
            long total = TotalCents();

            RunToAudit();

            Assert.AreEqual(1, Standing(2).Rank);
            Assert.AreEqual(2, Standing(3).Rank);
            Assert.AreEqual(3, Standing(1).Rank);
            Assert.AreEqual(6, Standing(1).Bracket, "three players all fit in the top 10");
            Assert.AreEqual(LeaderboardBalance.Unrated, Standing(4).Bracket, "player 4 never opted in");
            Assert.AreEqual(0, Standing(4).Rank);

            long dividend = 900_000L * 15 / 100 / 3, grant = 300_000L * 15 / 100 / 3;
            long start = _sim.Balance.Ledger.StartingCents;
            for (int p = 1; p <= 3; p++)
                Assert.AreEqual(start + dividend + grant, _sim.Ledger.Balance(_hq[p]), $"player {p} payout");
            Assert.AreEqual(start, _sim.Ledger.Balance(_hq[4]));
            Assert.AreEqual(900_000 - 3 * dividend, _sim.Ledger.Balance(_sim.WtoFeePool));
            Assert.AreEqual(300_000 - 3 * grant, _sim.Ledger.Balance(_sim.StaTollPool));
            Assert.AreEqual(total, TotalCents(), "zero inflation");
        }

        [Test]
        public void LeavingTheAudit_InABracket_IsEvading_UntilRejoining()
        {
            OptIn(1, true);
            RunToAudit();
            Assert.AreEqual(6, Standing(1).Bracket);
            Assert.AreEqual(15, _sim.Rates.EscrowRatePercent(1));

            OptIn(1, false);
            _sim.Clock.Advance(1);
            Assert.IsTrue(Standing(1).IsEvading);
            Assert.AreEqual(LeaderboardBalance.Unrated, Standing(1).Bracket);
            Assert.AreEqual(40, _sim.Rates.EscrowRatePercent(1));
            Assert.AreEqual(30, _sim.Rates.EscrowRatePercent(2), "never audited: plain unrated rate");

            OptIn(1, true);
            _sim.Clock.Advance(1);
            Assert.IsFalse(Standing(1).IsEvading);
            Assert.AreEqual(30, _sim.Rates.EscrowRatePercent(1), "unrated until the next audit");
        }
    }
}
