using System;
using System.Threading;
using System.Threading.Channels;
using Unruled.Core.Net;
using Unruled.Core.Persistence;

namespace Unruled.SimLab
{
    /// <summary>
    /// One game client connected to the <see cref="GameServer"/>: its player, its <see cref="PlayerView"/> and the frames
    /// waiting to go out. The world thread pushes frames under the server's lock; <see cref="WorldSocket"/> sends them.
    /// A full outbox drops the frame and the next view goes out as a snapshot, so a slow client resyncs rather than
    /// drifting.
    /// </summary>
    public sealed class ClientSession
    {
        public readonly int PlayerId;
        public readonly DateTime Since = DateTime.UtcNow;
        internal readonly PlayerView View;
        private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private readonly MsgPackWriter _frame = new MsgPackWriter(16 * 1024);
        private long _bytesSent;
        private long _lastTick;
        private bool _needsSnapshot = true;

        public ClientSession(int playerId, PlayerView view)
        {
            PlayerId = playerId;
            View = view ?? throw new ArgumentNullException(nameof(view));
        }

        public ChannelReader<byte[]> Outbox => _outbox.Reader;
        public CancellationToken Closed => _closed.Token;
        public string CloseReason { get; private set; }
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long Snapshots { get; private set; }
        public long Deltas { get; private set; }

        public void CountSent(int bytes) => Interlocked.Add(ref _bytesSent, bytes);

        public double BytesPerSecond
        {
            get
            {
                double seconds = (DateTime.UtcNow - Since).TotalSeconds;
                return seconds > 0 ? BytesSent / seconds : 0;
            }
        }

        /// <summary>World thread, under the server lock: the view since the last one sent (a snapshot first).</summary>
        internal void PushView(long tick)
        {
            bool full = _needsSnapshot;
            var body = View.Write(full).ToArray();
            _frame.Rewind(0);
            WireCodec.Write(_frame, full ? FrameKind.Snapshot : FrameKind.Delta,
                new ViewFrame { BaseTick = full ? 0 : _lastTick, Tick = tick, Body = body });
            if (!_outbox.Writer.TryWrite(_frame.ToArray()))
            {
                _needsSnapshot = true;
                return;
            }
            _needsSnapshot = false;
            _lastTick = tick;
            if (full) Snapshots++;
            else Deltas++;
        }

        /// <summary>A one-off frame (welcome, command result, pong). False when the outbox is full.</summary>
        internal bool Push(byte[] frame) => _outbox.Writer.TryWrite(frame);

        internal void Close(string reason)
        {
            if (CloseReason != null) return;
            CloseReason = reason;
            _outbox.Writer.TryComplete();
            _closed.Cancel();
        }
    }
}
