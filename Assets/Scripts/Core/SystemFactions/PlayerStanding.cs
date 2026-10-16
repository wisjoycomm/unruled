namespace Unruled.Core.SystemFactions
{
    /// <summary>
    /// ECS component on a player HQ: the player's Top-10K bracket (tab 05). Unrated (0) until an audit ranks them;
    /// the leaderboard audit owns it, everyone else reads it.
    /// </summary>
    public struct PlayerStanding
    {
        public byte Bracket;
        public int Rank; // 0 = unrated
        public bool IsAudited; // opted in to the audit
        public bool IsEvading; // left the audit while in a bracket: pays the evading escrow rate until they rejoin
    }
}
