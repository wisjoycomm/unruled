using Unruled.Core.Simulation;

namespace Unruled.View
{
    /// <summary>
    /// Where the world the views read comes from. <see cref="LocalLink"/> runs it in-process (single-player, offline);
    /// <see cref="ServerLink"/> mirrors a server's world for one player. The views read <see cref="World"/> the same way
    /// either way, and send orders through its <c>Issuer</c>.
    /// </summary>
    public interface IGameLink
    {
        /// <summary>The world to show; null until a server link has its first snapshot.</summary>
        SimulationWorld World { get; }

        int PlayerId { get; }

        /// <summary>True when the world is a server's: no local saves, no pause.</summary>
        bool IsOnline { get; }

        /// <summary>One line for the menu: the link's state.</summary>
        string Status { get; }

        /// <summary>Once per frame: runs the local clock, or applies what the server sent. True when the tick moved.</summary>
        bool Update(float deltaSeconds);

        void Close();
    }
}
