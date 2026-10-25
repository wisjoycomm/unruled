using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unruled.Core.Net;
using Unruled.Core.Persistence;

namespace Unruled.SimLab
{
    /// <summary>
    /// `loadtest [--url http://localhost:5090] [--clients 100] [--minutes 5] [--ramp 50]`: joins N new players on a
    /// running server (one bulk token call), then opens N WebSocket game clients that only read: each counts bytes
    /// and frames and checks every frame parses, but keeps no mirror. Every 10 seconds it prints the server's tick cost
    /// split into views and the rest, and the bytes per client. Pair it with a `client --pause-checks` run for a
    /// full mirror that checks its hash. Exits non-zero if a client is refused or dropped.
    /// </summary>
    public static class LoadTest
    {
        public static int Run(string[] args)
        {
            string url = "http://localhost:5090";
            int clients = 100, ramp = 50;
            double minutes = 5;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--url") url = args[i + 1];
                if (args[i] == "--clients") clients = int.Parse(args[i + 1]);
                if (args[i] == "--minutes") minutes = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (args[i] == "--ramp") ramp = int.Parse(args[i + 1]);
            }
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(minutes));
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
            return RunAsync(url, clients, ramp, cancel.Token).GetAwaiter().GetResult();
        }

        private sealed class Stats
        {
            public long Bytes;
            public long Frames;
            public int Connected;
            public int Lost;
            public int Refused;
        }

        private static async Task<int> RunAsync(string url, int clients, int ramp, CancellationToken cancel)
        {
            using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(5) };
            var issued = await http.PostAsync($"/api/server/test-tokens?count={clients}", null, cancel);
            var tokens = JsonSerializer.Deserialize<List<string>>(await issued.Content.ReadAsStringAsync(cancel));
            Console.WriteLine($"{tokens.Count} players joined; connecting {ramp} per second");

            var stats = new Stats();
            var tasks = new List<Task>(tokens.Count);
            var ws = new Uri(url.Replace("http", "ws") + "/ws");
            for (int i = 0; i < tokens.Count; i++)
            {
                tasks.Add(Client(ws, tokens[i], stats, cancel));
                if ((i + 1) % ramp == 0)
                    await Task.Delay(1_000, cancel).ContinueWith(_ => { });
            }

            var clock = Stopwatch.StartNew();
            long lastBytes = 0;
            double lastSeconds = 0;
            var report = Task.Run(async () =>
            {
                while (!cancel.IsCancellationRequested)
                {
                    await Task.Delay(10_000, cancel).ContinueWith(_ => { });
                    var s = JsonDocument.Parse(await http.GetStringAsync("/api/server/status")).RootElement;
                    double seconds = clock.Elapsed.TotalSeconds;
                    long bytes = Interlocked.Read(ref stats.Bytes);
                    int connected = Volatile.Read(ref stats.Connected);
                    double perClient = connected == 0 ? 0 : (bytes - lastBytes) / (seconds - lastSeconds) / connected;
                    lastBytes = bytes;
                    lastSeconds = seconds;
                    double passTicks = s.GetProperty("ticksPerPass").GetDouble();
                    double viewPass = s.GetProperty("viewCostMsPerPass").GetDouble();
                    Console.WriteLine($"{seconds,5:0}s  connected {connected,5}  lost {stats.Lost}  refused {stats.Refused}  " +
                                      $"tps {s.GetProperty("tps").GetDouble(),6:0.0}  sim {s.GetProperty("tickCostMs").GetDouble(),7:0.000} ms/tick  " +
                                      $"views {(passTicks > 0 ? viewPass / passTicks : 0),8:0.000} ms/tick " +
                                      $"({s.GetProperty("viewUsPerPlayer").GetDouble() / Math.Max(passTicks, 1),6:0.0} us/player/tick)  " +
                                      $"{perClient,7:0} B/s/client  mem {s.GetProperty("memoryMb").GetInt64()} MB");
                }
            });
            await Task.WhenAll(tasks);
            await report;
            Console.WriteLine($"done: {stats.Frames} frames, {stats.Bytes / (1024.0 * 1024.0):0.0} MB, lost {stats.Lost}, refused {stats.Refused}");
            return stats.Lost == 0 && stats.Refused == 0 ? 0 : 1;
        }

        private static async Task Client(Uri url, string token, Stats stats, CancellationToken cancel)
        {
            using var socket = new ClientWebSocket();
            var buffer = new byte[256 * 1024];
            try
            {
                await socket.ConnectAsync(url, cancel);
                var w = new MsgPackWriter(64);
                WireCodec.Write(w, new HelloFrame { ProtocolVersion = WireCodec.ProtocolVersion, Token = Encoding.ASCII.GetBytes(token) });
                await socket.SendAsync(w.ToArray(), WebSocketMessageType.Binary, true, cancel);
                Interlocked.Increment(ref stats.Connected);
                while (!cancel.IsCancellationRequested)
                {
                    int length = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), cancel);
                        length += result.Count;
                    } while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Interlocked.Increment(ref stats.Lost);
                        break;
                    }
                    var r = new MsgPackReader(new ReadOnlyMemory<byte>(buffer, 0, length));
                    if (WireCodec.ReadKind(r) == FrameKind.Refused)
                    {
                        Interlocked.Increment(ref stats.Refused);
                        break;
                    }
                    WireCodec.SkipPayload(r);
                    Interlocked.Add(ref stats.Bytes, length);
                    Interlocked.Increment(ref stats.Frames);
                }
            }
            catch (OperationCanceledException)
            {
                // the run's time is up
            }
            catch (WebSocketException)
            {
                Interlocked.Increment(ref stats.Lost);
            }
            finally
            {
                Interlocked.Decrement(ref stats.Connected);
            }
            if (!cancel.IsCancellationRequested && socket.State == WebSocketState.Open) // a cancelled receive already aborted the socket
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
    }
}
