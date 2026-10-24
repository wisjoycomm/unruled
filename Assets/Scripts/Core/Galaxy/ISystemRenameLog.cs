using Unruled.Core.Ecs;

namespace Unruled.Core.Galaxy
{
    /// <summary>Where the galaxy reports a renamed system for THE SYSTEM to publish.</summary>
    public interface ISystemRenameLog
    {
        /// <summary>The owner renamed <paramref name="system"/> (a <see cref="StarSystem"/> entity).</summary>
        void ReportRename(int playerId, EntityId system, long tick);
    }
}
