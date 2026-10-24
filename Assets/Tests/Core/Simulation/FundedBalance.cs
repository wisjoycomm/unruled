using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    /// <summary>
    /// The default balance, but players start with credits: for tests of mechanics that need money (tolls, trades,
    /// contracts) and not the loans players start the real game with.
    /// </summary>
    public static class FundedBalance
    {
        public const long StartingCredits = 10_000;

        public static GameBalanceConfig Create()
        {
            var d = DefaultGameBalance.Create();
            var ledger = new LedgerBalance(StartingCredits * Wallet.CentsPerCredit, d.Ledger.CreditLimitCents,
                d.Ledger.InterestFreeTicks, d.Ledger.DailyInterestBasisPoints, d.Ledger.LoanTermTicks,
                d.Ledger.CreditPerTechTierCents, d.Ledger.OpenGateCreditCents, d.Ledger.CreditPerOutpostCents,
                d.Ledger.CreditPerRefineryCents);
            return new GameBalanceConfig(d.Recipes, d.Universe, d.Starlanes, d.Weather, d.Refinery, d.Mining, d.Lifecycle,
                d.Latency, d.Radar, d.Fleets, d.Combat, d.Relocation, ledger, d.Sta, d.Leaderboard, d.Wto, d.Contracts,
                d.Authority, d.SystemFactions, d.Ai, d.NewGame, d.Research, d.Shipyard, d.SiteControl, d.JumpGates,
                d.Convoys, d.Citadel, d.Galaxy, d.Presence);
        }
    }
}
