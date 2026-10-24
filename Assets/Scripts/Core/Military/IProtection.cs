using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    /// <summary>Which players can't be attacked where (newcomer protection, the offline shield).</summary>
    public interface IProtection
    {
        /// <summary>
        /// Whether the player can't be fought, claimed, struck or raided at <paramref name="site"/> (null: out in space).
        /// </summary>
        bool IsProtectedAt(int playerId, EntityId site, long tick);

        /// <summary>The player has been in a fight: an offline shield that is not up yet waits again.</summary>
        void NoteCombat(int playerId, long tick);

        /// <summary>The player acted with hostility: newcomer protection and any offline shield end.</summary>
        void EndProtection(int playerId, long tick);
    }
}
