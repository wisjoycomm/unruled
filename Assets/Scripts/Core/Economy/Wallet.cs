namespace Unruled.Core.Economy
{
    /// <summary>
    /// Which system account a wallet is. Rewards are paid only from the two tax pools (GDD §4.4); the WTO escrow only
    /// holds money owed on open orders and contracts.
    /// </summary>
    public enum TaxPoolKind : byte
    {
        None = 0,
        WtoMarketFees = 1,
        StaTollGates = 2,
        WtoEscrow = 3
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
