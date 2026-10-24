using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unruled.Core.Military;
using Unruled.Core.Net;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.View
{
    /// <summary>
    /// A server's world for one player, over a WebSocket (<c>ws://host/ws</c>, <see cref="WireCodec"/> frames). Sends a
    /// hello with the login token; on the welcome it builds a mirror <see cref="SimulationWorld"/> that is never stepped,
    /// and applies each snapshot or delta to it through <see cref="PlayerView"/>. Orders the views issue go through the
    /// mirror's <see cref="CommandIssuer"/> (same checks as offline) and on to the server. The socket runs on the thread
    /// pool; frames are queued and applied on the main thread in <see cref="Update"/>, so the views never see a world
    /// half-changed.
    /// </summary>
    public sealed class ServerLink : IGameLink, ICommandSink
    {
        private const int MaxFrameBytes = 1 << 20;
        private const int MaxFramesPerUpdate = 64;

        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly MsgPackWriter _out = new MsgPackWriter(256);
        private PlayerView _view;
        private int _sequence;
        private volatile string _status = "CONNECTING";

        /// <summary>Server refusals of orders the mirror let through (the two worlds disagreed for a moment).</summary>
        public int RefusedOrders { get; private set; }
        public IssueResult LastRefusal { get; private set; }

        public SimulationWorld World { get; private set; }
        public int PlayerId { get; private set; }
        public bool IsOnline => true;
        public string Status => _status;

        /// <summary>True once the server refused the login or the connection ended; the session falls back to offline.</summary>
        public bool Failed { get; private set; }

        /// <summary>Connects to <paramref name="address"/> (host:port or a ws:// URL) and logs in with <paramref name="token"/>.</summary>
        public ServerLink(string address, string token)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Server address required.", nameof(address));
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Login token required.", nameof(token));
            string url = address.Trim().ToLowerInvariant();
            if (!url.StartsWith("ws://", StringComparison.Ordinal) && !url.StartsWith("wss://", StringComparison.Ordinal))
                url = "ws://" + url;
            if (!url.EndsWith("/ws", StringComparison.Ordinal))
                url = url.TrimEnd('/') + "/ws";
            _ = Run(new Uri(url), Encoding.ASCII.GetBytes(token.Trim().ToLowerInvariant()), _cancel.Token);
        }

        public bool Update(float deltaSeconds)
        {
            long before = World != null ? World.Clock.CurrentTick : -1;
            for (int i = 0; i < MaxFramesPerUpdate && _inbox.TryDequeue(out var frame); i++)
                Apply(frame);
            return World != null && World.Clock.CurrentTick != before;
        }

        void ICommandSink.Send(in DelayedPlayerCommand command)
        {
            _out.Rewind(0);
            WireCodec.Write(_out, new CommandFrame { Sequence = ++_sequence, Command = command });
            _ = SendFrame(_out.ToArray(), _cancel.Token);
        }

        public void Close()
        {
            if (_cancel.IsCancellationRequested)
                return;
            _cancel.Cancel();
            _status = "LEFT THE SERVER";
            if (_socket.State == WebSocketState.Open)
                _ = _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }

        // Main thread: one frame from the server.
        private void Apply(byte[] frame)
        {
            var r = new MsgPackReader(frame);
            var kind = WireCodec.ReadKind(r);
            switch (kind)
            {
                case FrameKind.Welcome:
                    var welcome = WireCodec.ReadWelcome(r);
                    PlayerId = welcome.PlayerId;
                    var mirror = new SimulationWorld(DefaultGameBalance.Create(), welcome.WorldSeed, welcome.Tick);
                    mirror.Issuer.SendTo(this);
                    _view = new PlayerView(mirror, PlayerId);
                    World = mirror;
                    _status = "ONLINE · PLAYER " + UiFormatNumber(PlayerId);
                    break;
                case FrameKind.Snapshot:
                case FrameKind.Delta:
                    var view = WireCodec.ReadView(r);
                    if (_view != null)
                        _view.Apply(view.Body, kind == FrameKind.Snapshot);
                    break;
                case FrameKind.CommandResult:
                    var result = WireCodec.ReadCommandResult(r);
                    if (result.Result != IssueResult.Issued)
                    {
                        RefusedOrders++;
                        LastRefusal = result.Result;
                    }
                    break;
                case FrameKind.Refused:
                    var refused = WireCodec.ReadRefused(r);
                    Failed = true;
                    _status = refused.Reason == RefuseReason.VersionMismatch
                        ? "REFUSED · THE SERVER RUNS ANOTHER VERSION"
                        : refused.Reason == RefuseReason.BadToken ? "REFUSED · THE LOGIN TOKEN IS NOT VALID" : "REFUSED BY THE SERVER";
                    break;
                default:
                    WireCodec.SkipPayload(r);
                    break;
            }
        }

        private static string UiFormatNumber(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Thread pool: connect, say hello, then queue every frame for the main thread.
        private async Task Run(Uri url, byte[] token, CancellationToken cancel)
        {
            var buffer = new byte[MaxFrameBytes];
            try
            {
                await _socket.ConnectAsync(url, cancel).ConfigureAwait(false);
                var hello = new MsgPackWriter(64);
                WireCodec.Write(hello, new HelloFrame { ProtocolVersion = WireCodec.ProtocolVersion, Token = token });
                await SendFrame(hello.ToArray(), cancel).ConfigureAwait(false);
                _status = "LOGGING IN";
                while (!cancel.IsCancellationRequested)
                {
                    int length = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        if (length == buffer.Length)
                            throw new WebSocketException("A frame from the server is bigger than the client accepts.");
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), cancel)
                            .ConfigureAwait(false);
                        length += result.Count;
                    } while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Lost("THE SERVER CLOSED THE CONNECTION");
                        return;
                    }
                    var frame = new byte[length];
                    Buffer.BlockCopy(buffer, 0, frame, 0, length);
                    _inbox.Enqueue(frame);
                }
            }
            catch (OperationCanceledException)
            {
                _status = "LEFT THE SERVER";
            }
            catch (WebSocketException e)
            {
                Lost("CONNECTION LOST · " + e.Message.ToUpperInvariant());
            }
        }

        // A refusal names the cause; the close that follows it doesn't replace it.
        private void Lost(string status)
        {
            if (!_status.StartsWith("REFUSED", StringComparison.Ordinal))
                _status = status;
            Failed = true;
        }

        private async Task SendFrame(byte[] frame, CancellationToken cancel)
        {
            await _sendGate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _status = "LEFT THE SERVER";
            }
            catch (WebSocketException e)
            {
                Lost("CONNECTION LOST · " + e.Message.ToUpperInvariant());
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}
