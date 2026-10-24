using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Unruled.Core.Net;
using Unruled.Core.Persistence;

namespace Unruled.SimLab
{
    /// <summary>
    /// The game client's WebSocket (<c>/ws</c>): binary MessagePack frames (<see cref="WireCodec"/>), one per message.
    /// The first frame must be a hello; the server answers with a welcome and a snapshot, or a refusal and closes. Then
    /// the client sends commands and pings, and gets a delta of its view after every tick the world runs.
    /// </summary>
    public static class WorldSocket
    {
        private const int MaxFrameBytes = 64 * 1024;

        public static async Task Run(WebSocket socket, GameServer server, CancellationToken aborted)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxFrameBytes);
            ClientSession session = null;
            try
            {
                int length = await Receive(socket, buffer, aborted);
                if (length <= 0)
                    return;
                var refusal = RefuseReason.BadFrame;
                try
                {
                    var r = new MsgPackReader(new ReadOnlyMemory<byte>(buffer, 0, length));
                    if (WireCodec.ReadKind(r) == FrameKind.Hello)
                        session = server.Connect(WireCodec.ReadHello(r), out refusal);
                }
                catch (FormatException)
                {
                    refusal = RefuseReason.BadFrame;
                }
                if (session == null)
                {
                    var w = new MsgPackWriter(64);
                    WireCodec.Write(w, new RefusedFrame { Reason = refusal, ServerProtocolVersion = WireCodec.ProtocolVersion });
                    await socket.SendAsync(w.ToArray(), WebSocketMessageType.Binary, true, aborted);
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, refusal.ToString(), aborted);
                    return;
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(aborted, session.Closed);
                var sending = Send(socket, server, session, linked.Token);
                while (!linked.IsCancellationRequested)
                {
                    length = await Receive(socket, buffer, linked.Token);
                    if (length <= 0)
                        break;
                    if (!Handle(server, session, new ReadOnlyMemory<byte>(buffer, 0, length)))
                        break;
                }
                server.Disconnect(session, "connection closed");
                await sending;
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, session.CloseReason ?? "bye", CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                if (session != null) server.Disconnect(session, "connection closed");
            }
            catch (WebSocketException)
            {
                if (session != null) server.Disconnect(session, "connection lost");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // False on a frame the server can't read: the session ends.
        private static bool Handle(GameServer server, ClientSession session, ReadOnlyMemory<byte> frame)
        {
            try
            {
                var r = new MsgPackReader(frame);
                switch (WireCodec.ReadKind(r))
                {
                    case FrameKind.Command:
                        server.Command(session, WireCodec.ReadCommand(r));
                        return true;
                    case FrameKind.Ping:
                        var w = new MsgPackWriter(32);
                        WireCodec.Write(w, FrameKind.Pong, WireCodec.ReadPing(r));
                        session.Push(w.ToArray());
                        return true;
                    default:
                        WireCodec.SkipPayload(r);
                        return true;
                }
            }
            catch (FormatException)
            {
                server.Disconnect(session, "bad frame");
                return false;
            }
        }

        // Ends the session when the connection or the session closes, so the receive loop stops too.
        private static async Task Send(WebSocket socket, GameServer server, ClientSession session, CancellationToken token)
        {
            try
            {
                while (await session.Outbox.WaitToReadAsync(token))
                    while (session.Outbox.TryRead(out var frame))
                    {
                        await socket.SendAsync(frame, WebSocketMessageType.Binary, true, token);
                        session.CountSent(frame.Length);
                    }
                server.Disconnect(session, session.CloseReason ?? "session ended");
            }
            catch (OperationCanceledException)
            {
                server.Disconnect(session, session.CloseReason ?? "connection closed");
            }
            catch (WebSocketException)
            {
                server.Disconnect(session, "connection lost");
            }
        }

        // One whole message into the buffer; 0 when the peer closes, -1 when it is too big or not binary.
        private static async Task<int> Receive(WebSocket socket, byte[] buffer, CancellationToken token)
        {
            int length = 0;
            while (true)
            {
                if (length == MaxFrameBytes)
                    return -1;
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, MaxFrameBytes - length), token);
                if (result.MessageType == WebSocketMessageType.Close)
                    return 0;
                if (result.MessageType != WebSocketMessageType.Binary)
                    return -1;
                length += result.Count;
                if (result.EndOfMessage)
                    return length;
            }
        }
    }
}
