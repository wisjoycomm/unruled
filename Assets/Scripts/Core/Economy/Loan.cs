namespace Unruled.Core.Economy
{
    /// <summary>
    /// ECS component on a player's HQ: what the player owes THE SYSTEM. Owned by <see cref="LoanSystem"/>.
    /// <see cref="DueTick"/> is set when the debt goes from nothing to something and cleared when it is paid off.
    /// </summary>
    public struct Loan
    {
        public long PrincipalCents;
        public long InterestCents;
        public long DueTick;

        public long DebtCents => PrincipalCents + InterestCents;
    }
}
