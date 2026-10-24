using System;
using Unruled.Core.Military;
using Unruled.Core.Persistence;

namespace Unruled.Core.Net
{
    /// <summary>
    /// Encodes and decodes the frames between the game client and the server. A frame is the MessagePack array
    /// <c>[kind, payload]</c>; each payload is an array whose position is its key, like the save game: keys are only ever
    /// appended, keys an older peer lacks read as the default, keys a newer peer appended are skipped.
    /// <see cref="ProtocolVersion"/> goes up whenever a shape changes in a way an older peer can't read.
    /// </summary>
    public static class WireCodec
    {
        public const int ProtocolVersion = 1;

        /// <summary>The server's verdict on a hello's version (token checks are the server's own).</summary>
        public static RefuseReason CheckVersion(in HelloFrame hello) =>
            hello.ProtocolVersion == ProtocolVersion ? RefuseReason.None : RefuseReason.VersionMismatch;

        /// <summary>Reads a frame's kind; the payload follows and is read with the matching Read call, or <see cref="SkipPayload"/>.</summary>
        public static FrameKind ReadKind(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            if (n != 2) throw new FormatException($"A frame is [kind, payload]; found {n} items.");
            return (FrameKind)r.ReadByte();
        }

        public static void SkipPayload(MsgPackReader r) => r.Skip(1);

        public static void Write(MsgPackWriter w, in HelloFrame f)
        {
            Envelope(w, FrameKind.Hello, 2);
            w.Write(f.ProtocolVersion);
            w.Write(f.Token ?? Array.Empty<byte>());
        }

        public static HelloFrame ReadHello(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new HelloFrame { Token = Array.Empty<byte>() };
            if (n > 0) f.ProtocolVersion = r.ReadInt32();
            if (n > 1) f.Token = r.ReadBytes();
            SkipNewer(r, n, 2);
            return f;
        }

        public static void Write(MsgPackWriter w, in CommandFrame f)
        {
            Envelope(w, FrameKind.Command, 2);
            w.Write(f.Sequence);
            ComponentFormatters.Write(w, f.Command);
        }

        public static CommandFrame ReadCommand(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new CommandFrame();
            if (n > 0) f.Sequence = r.ReadInt32();
            if (n > 1) f.Command = ComponentFormatters.ReadCommand(r);
            SkipNewer(r, n, 2);
            return f;
        }

        /// <param name="kind"><see cref="FrameKind.Ping"/> from the client, <see cref="FrameKind.Pong"/> back.</param>
        public static void Write(MsgPackWriter w, FrameKind kind, in PingFrame f)
        {
            if (kind != FrameKind.Ping && kind != FrameKind.Pong) throw new ArgumentOutOfRangeException(nameof(kind));
            Envelope(w, kind, 1);
            w.Write(f.Nonce);
        }

        public static PingFrame ReadPing(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new PingFrame();
            if (n > 0) f.Nonce = r.ReadInt64();
            SkipNewer(r, n, 1);
            return f;
        }

        public static void Write(MsgPackWriter w, in WelcomeFrame f)
        {
            Envelope(w, FrameKind.Welcome, 4);
            w.Write(f.ProtocolVersion);
            w.Write(f.PlayerId);
            w.Write(f.Tick);
            w.Write(f.WorldSeed);
        }

        public static WelcomeFrame ReadWelcome(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new WelcomeFrame();
            if (n > 0) f.ProtocolVersion = r.ReadInt32();
            if (n > 1) f.PlayerId = r.ReadInt32();
            if (n > 2) f.Tick = r.ReadInt64();
            if (n > 3) f.WorldSeed = r.ReadUInt64();
            SkipNewer(r, n, 4);
            return f;
        }

        /// <param name="kind"><see cref="FrameKind.Snapshot"/> or <see cref="FrameKind.Delta"/>.</param>
        public static void Write(MsgPackWriter w, FrameKind kind, in ViewFrame f)
        {
            if (kind != FrameKind.Snapshot && kind != FrameKind.Delta) throw new ArgumentOutOfRangeException(nameof(kind));
            Envelope(w, kind, 3);
            w.Write(f.BaseTick);
            w.Write(f.Tick);
            w.Write(f.Body ?? Array.Empty<byte>());
        }

        public static ViewFrame ReadView(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new ViewFrame { Body = Array.Empty<byte>() };
            if (n > 0) f.BaseTick = r.ReadInt64();
            if (n > 1) f.Tick = r.ReadInt64();
            if (n > 2) f.Body = r.ReadBytes();
            SkipNewer(r, n, 3);
            return f;
        }

        public static void Write(MsgPackWriter w, in RefusedFrame f)
        {
            Envelope(w, FrameKind.Refused, 2);
            w.Write((int)f.Reason);
            w.Write(f.ServerProtocolVersion);
        }

        public static RefusedFrame ReadRefused(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new RefusedFrame();
            if (n > 0) f.Reason = (RefuseReason)r.ReadByte();
            if (n > 1) f.ServerProtocolVersion = r.ReadInt32();
            SkipNewer(r, n, 2);
            return f;
        }

        public static void Write(MsgPackWriter w, in CommandResultFrame f)
        {
            Envelope(w, FrameKind.CommandResult, 3);
            w.Write(f.Sequence);
            w.Write((int)f.Result);
            w.Write(f.ExecuteAtTick);
        }

        public static CommandResultFrame ReadCommandResult(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new CommandResultFrame();
            if (n > 0) f.Sequence = r.ReadInt32();
            if (n > 1) f.Result = (IssueResult)r.ReadByte();
            if (n > 2) f.ExecuteAtTick = r.ReadInt64();
            SkipNewer(r, n, 3);
            return f;
        }

        public static void Write(MsgPackWriter w, in BulletinFrame f)
        {
            Envelope(w, FrameKind.Bulletin, 1);
            ComponentFormatters.Write(w, f.Bulletin);
        }

        public static BulletinFrame ReadBulletin(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var f = new BulletinFrame();
            if (n > 0) f.Bulletin = ComponentFormatters.ReadSystemBulletin(r);
            SkipNewer(r, n, 1);
            return f;
        }

        private static void Envelope(MsgPackWriter w, FrameKind kind, int payloadKeys)
        {
            w.WriteArrayHeader(2);
            w.Write((int)kind);
            w.WriteArrayHeader(payloadKeys);
        }

        private static void SkipNewer(MsgPackReader r, int count, int known)
        {
            if (count > known) r.Skip(count - known);
        }
    }
}
