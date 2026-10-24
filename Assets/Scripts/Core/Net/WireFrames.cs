using Unruled.Core.Military;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Net
{
    /// <summary>
    /// What a frame on the wire carries. Values are only ever appended, never reused or renumbered; a reader steps over
    /// a kind it doesn't know.
    /// </summary>
    public enum FrameKind : byte
    {
        Unknown = 0,
        // Client to server.
        Hello = 1,
        Command = 2,
        Ping = 3,
        // Server to client.
        Welcome = 10,
        Snapshot = 11,
        Delta = 12,
        Refused = 13,
        Bulletin = 14,
        CommandResult = 15,
        Pong = 16
    }

    /// <summary>Why the server turned a session down. Append only.</summary>
    public enum RefuseReason : byte
    {
        None = 0,
        VersionMismatch = 1,
        BadToken = 2,
        NotLoggedIn = 3,
        ServerFull = 4,
        BadFrame = 5
    }

    /// <summary>The first frame a client sends: its protocol version and its login token.</summary>
    public struct HelloFrame
    {
        public int ProtocolVersion;
        public byte[] Token;
    }

    /// <summary>
    /// An order from the client. <see cref="Sequence"/> is the client's own number, echoed in the
    /// <see cref="CommandResultFrame"/>. The server overwrites the command's player and ticks.
    /// </summary>
    public struct CommandFrame
    {
        public int Sequence;
        public DelayedPlayerCommand Command;
    }

    /// <summary>A ping (client) or its echo (server, as <see cref="FrameKind.Pong"/>).</summary>
    public struct PingFrame
    {
        public long Nonce;
    }

    /// <summary>The server's answer to an accepted hello: who the client plays and the server tick.</summary>
    public struct WelcomeFrame
    {
        public int ProtocolVersion;
        public int PlayerId;
        public long Tick;
        public ulong WorldSeed;
    }

    /// <summary>
    /// A player's whole view at <see cref="Tick"/> (<see cref="FrameKind.Snapshot"/>), or the change since the view at
    /// <see cref="BaseTick"/> (<see cref="FrameKind.Delta"/>). The body is the encoded player view.
    /// </summary>
    public struct ViewFrame
    {
        public long BaseTick; // 0 for a snapshot
        public long Tick;
        public byte[] Body;
    }

    /// <summary>The server turns the session down; it closes the connection after sending this.</summary>
    public struct RefusedFrame
    {
        public RefuseReason Reason;
        public int ServerProtocolVersion;
    }

    /// <summary>How the server's <see cref="CommandIssuer"/> took a client's order.</summary>
    public struct CommandResultFrame
    {
        public int Sequence;
        public IssueResult Result;
        public long ExecuteAtTick;
    }

    /// <summary>A public bulletin from THE SYSTEM.</summary>
    public struct BulletinFrame
    {
        public SystemBulletin Bulletin;
    }
}
