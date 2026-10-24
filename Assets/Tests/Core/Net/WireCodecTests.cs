using System;
using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Net;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.Net
{
    public class WireCodecTests
    {
        private static MsgPackReader Reader(MsgPackWriter w, FrameKind expected)
        {
            var r = new MsgPackReader(w.ToArray());
            Assert.AreEqual(expected, WireCodec.ReadKind(r));
            return r;
        }

        [Test]
        public void ClientFrames_RoundTrip()
        {
            var w = new MsgPackWriter();
            WireCodec.Write(w, new HelloFrame { ProtocolVersion = WireCodec.ProtocolVersion, Token = new byte[] { 1, 2, 250 } });
            var r = Reader(w, FrameKind.Hello);
            var hello = WireCodec.ReadHello(r);
            Assert.AreEqual(WireCodec.ProtocolVersion, hello.ProtocolVersion);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 250 }, hello.Token);
            Assert.IsTrue(r.End);

            var command = new DelayedPlayerCommand
            {
                IssuedAtTick = 90_000, PlayerId = 3, CommandType = CommandType.LogOut,
                Target = new EntityId(41, 2), ParamA = -7, ParamB = 1, ParamC = int.MaxValue
            };
            w = new MsgPackWriter();
            WireCodec.Write(w, new CommandFrame { Sequence = 12, Command = command });
            r = Reader(w, FrameKind.Command);
            var read = WireCodec.ReadCommand(r);
            Assert.AreEqual(12, read.Sequence);
            Assert.AreEqual(command.IssuedAtTick, read.Command.IssuedAtTick);
            Assert.AreEqual(command.CommandType, read.Command.CommandType);
            Assert.AreEqual(command.Target, read.Command.Target);
            Assert.AreEqual(command.ParamA, read.Command.ParamA);
            Assert.AreEqual(command.ParamC, read.Command.ParamC);
            Assert.IsTrue(r.End);

            w = new MsgPackWriter();
            WireCodec.Write(w, FrameKind.Ping, new PingFrame { Nonce = long.MinValue });
            r = Reader(w, FrameKind.Ping);
            Assert.AreEqual(long.MinValue, WireCodec.ReadPing(r).Nonce);
        }

        [Test]
        public void ServerFrames_RoundTrip()
        {
            var w = new MsgPackWriter();
            WireCodec.Write(w, new WelcomeFrame { ProtocolVersion = 1, PlayerId = 9, Tick = 5_000_000_000L, WorldSeed = ulong.MaxValue });
            var welcome = WireCodec.ReadWelcome(Reader(w, FrameKind.Welcome));
            Assert.AreEqual(9, welcome.PlayerId);
            Assert.AreEqual(5_000_000_000L, welcome.Tick);
            Assert.AreEqual(ulong.MaxValue, welcome.WorldSeed);

            var body = new byte[70_000];
            for (int i = 0; i < body.Length; i++) body[i] = (byte)(i * 31);
            w = new MsgPackWriter();
            WireCodec.Write(w, FrameKind.Delta, new ViewFrame { BaseTick = 10, Tick = 11, Body = body });
            var view = WireCodec.ReadView(Reader(w, FrameKind.Delta));
            Assert.AreEqual(10, view.BaseTick);
            Assert.AreEqual(11, view.Tick);
            CollectionAssert.AreEqual(body, view.Body);

            w = new MsgPackWriter();
            WireCodec.Write(w, FrameKind.Snapshot, new ViewFrame { Tick = 4, Body = new byte[] { 7 } });
            CollectionAssert.AreEqual(new byte[] { 7 }, WireCodec.ReadView(Reader(w, FrameKind.Snapshot)).Body);

            w = new MsgPackWriter();
            WireCodec.Write(w, new RefusedFrame { Reason = RefuseReason.BadToken, ServerProtocolVersion = 1 });
            Assert.AreEqual(RefuseReason.BadToken, WireCodec.ReadRefused(Reader(w, FrameKind.Refused)).Reason);

            w = new MsgPackWriter();
            WireCodec.Write(w, new CommandResultFrame { Sequence = 12, Result = IssueResult.Uncharted, ExecuteAtTick = 99 });
            var result = WireCodec.ReadCommandResult(Reader(w, FrameKind.CommandResult));
            Assert.AreEqual(12, result.Sequence);
            Assert.AreEqual(IssueResult.Uncharted, result.Result);
            Assert.AreEqual(99, result.ExecuteAtTick);

            var bulletin = new SystemBulletin
            {
                Agency = SystemAgency.Sta, Kind = BulletinKind.SystemRenamed, PlayerId = 2,
                Site = new EntityId(8, 1), Tick = 100, ExpiresTick = 200
            };
            w = new MsgPackWriter();
            WireCodec.Write(w, new BulletinFrame { Bulletin = bulletin });
            var readBulletin = WireCodec.ReadBulletin(Reader(w, FrameKind.Bulletin)).Bulletin;
            Assert.AreEqual(bulletin.Kind, readBulletin.Kind);
            Assert.AreEqual(bulletin.Site, readBulletin.Site);
            Assert.AreEqual(bulletin.ExpiresTick, readBulletin.ExpiresTick);
        }

        [Test]
        public void HelloFromAnotherVersion_IsRefused_AndTheRefusalNamesTheServerVersion()
        {
            Assert.AreEqual(RefuseReason.None,
                WireCodec.CheckVersion(new HelloFrame { ProtocolVersion = WireCodec.ProtocolVersion }));
            Assert.AreEqual(RefuseReason.VersionMismatch,
                WireCodec.CheckVersion(new HelloFrame { ProtocolVersion = WireCodec.ProtocolVersion + 1 }));
            Assert.AreEqual(RefuseReason.VersionMismatch,
                WireCodec.CheckVersion(new HelloFrame { ProtocolVersion = 0 }));
        }

        [Test]
        public void FrameFromANewerPeer_WithAppendedKeys_ReadsTheKnownKeys()
        {
            var w = new MsgPackWriter();
            w.WriteArrayHeader(2);
            w.Write((int)FrameKind.Welcome);
            w.WriteArrayHeader(6);
            w.Write(1);
            w.Write(4);
            w.Write(77L);
            w.Write(5UL);
            w.Write(true);
            w.WriteArrayHeader(2);
            w.Write(1);
            w.Write(2);
            w.WriteArrayHeader(2);
            w.Write((int)FrameKind.Pong);
            w.WriteArrayHeader(1);
            w.Write(3L);

            var r = new MsgPackReader(w.ToArray());
            Assert.AreEqual(FrameKind.Welcome, WireCodec.ReadKind(r));
            var welcome = WireCodec.ReadWelcome(r);
            Assert.AreEqual(4, welcome.PlayerId);
            Assert.AreEqual(77L, welcome.Tick);
            Assert.AreEqual(FrameKind.Pong, WireCodec.ReadKind(r));
            Assert.AreEqual(3L, WireCodec.ReadPing(r).Nonce);
            Assert.IsTrue(r.End);
        }

        [Test]
        public void UnknownFrameKind_CanBeSkipped()
        {
            var w = new MsgPackWriter();
            w.WriteArrayHeader(2);
            w.Write(200);
            w.WriteArrayHeader(1);
            w.Write(1);
            WireCodec.Write(w, FrameKind.Ping, new PingFrame { Nonce = 5 });

            var r = new MsgPackReader(w.ToArray());
            Assert.AreEqual((FrameKind)200, WireCodec.ReadKind(r));
            WireCodec.SkipPayload(r);
            Assert.AreEqual(FrameKind.Ping, WireCodec.ReadKind(r));
            Assert.AreEqual(5, WireCodec.ReadPing(r).Nonce);
        }

        [Test]
        public void TruncatedFrame_Throws()
        {
            var w = new MsgPackWriter();
            WireCodec.Write(w, new HelloFrame { ProtocolVersion = 1, Token = new byte[40] });
            byte[] bytes = w.ToArray();
            var r = new MsgPackReader(new ReadOnlyMemory<byte>(bytes, 0, bytes.Length - 5));
            WireCodec.ReadKind(r);
            Assert.Throws<FormatException>(() => WireCodec.ReadHello(r));
        }
    }
}
