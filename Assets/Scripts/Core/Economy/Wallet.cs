namespace Unruled.Core.Economy
{
    /// <summary>Which tax pool a wallet is (GDD §4.4: rewards are paid only from these).</summary>
    public enum TaxPoolKind : byte
    {
        None = 0,
        WtoMarketFees = 1,
        StaTollGates = 2
    }

    /// <summary>
    /// ECS component: a credit balance in cents. A player's wallet sits on its HQ entity; a tax pool's on its own
    /// entity (<see cref="OwnerPlayerId"/> 0). Only <see cref="Ledger"/> changes <see cref="Cents"/>.
    /// </summary>
    public struct Wallet
    {
        public const long CentsPerCredit = 100;

        public int OwnerPlayerId;
        public TaxPoolKind Pool;
        public long Cents;
    }
}
