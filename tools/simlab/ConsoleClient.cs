using System;
using System.Buffers;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unruled.Core.Military;
using Unruled.Core.Net;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>
    /// `client [--url http://localhost:5090] [--token T | --join] [--minutes 10] [--bad-token] [--pause-checks]`: a
    /// headless game client. It logs in over /ws, keeps a mirror world from the server's snapshot and deltas, sends an
    /// order now and then (rate of fire on its fleets), and every 30 seconds compares a hash of its mirror's view with
    /// the server's. With --pause-checks (a test server at high speed) it pauses the world for each check, catches up
    /// to the server's tick, compares, and resumes; otherwise a check counts only when both are on the same tick.
    /// Prints bytes per second and exits non-zero if the mirror ever drifts or the server refuses it.
    /// </summary>
    public static class ConsoleClient
    {
        public static int Run(string[] args)
        {
            string url = "http://localhost:5090";
            string token = null;
            bool join = false, badToken = false, pauseChecks = false;
            double minutes = 10;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--url" && i + 1 < args.Length) url = args[++i];
                else if (args[i] == "--token" && i + 1 < args.Length) token = args[++i];
                else if (args[i] == "--minutes" && i + 1 < args.Length) minutes = double.Parse(args[++i]);
                else if (args[i] == "--join") join = true;
                else if (args[i] == "--bad-token") badToken = true;
                else if (args[i] == "--pause-checks") pauseChecks = true;
            }
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(minutes));
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
            return RunAsync(url, token, join, badToken, pauseChecks, cancel.Token).GetAwaiter().GetResult();
        }

        private static async Task<int> RunAsync(string url, string token, bool join, bool badToken, bool pauseChecks,
            CancellationToken cancel)
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            if (badToken)
                token = "not-a-token";
            else if (token == null)
            {
                if (!join)
                {
                    Console.WriteLine("Pass --token T, or --join for a new player.");
                    return 2;
                }
                var issued = await http.PostAsJsonAsync("/api/server/token", new { playerId = 0, join = true }, cancel);
                var doc = JsonDocument.Parse(await issued.Content.ReadAsStringAsync(cancel));
                token = doc.RootElement.GetProperty("token").GetString();
                Console.WriteLine("joined as player " + doc.RootElement.GetProperty("playerId").GetInt32());
            }

            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(url.Replace("http", "ws") + "/ws"), cancel);
            var w = new MsgPackWriter(256);
            WireCodec.Write(w, new HelloFrame { ProtocolVersion = WireCodec.ProtocolVersion, Token = Encoding.ASCII.GetBytes(token) });
            await socket.SendAsync(w.ToArray(), WebSocketMessageType.Binary, true, cancel);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
            try
            {
                SimulationWorld mirror = null;
                PlayerView view = null;
                int playerId = 0, sequence = 0, refused = 0, issuedOrders = 0, drift = 0, checks = 0;
                long bytes = 0, frames = 0, snapshots = 0;
                var clock = Stopwatch.StartNew();
                long nextOrderMs = 5_000, nextCheckMs = 30_000;
                long checkAtTick = -1; // paused for a check: compare once the mirror reaches this tick
                while (!cancel.IsCancellationRequested)
                {
                    int length;
                    try
                    {
                        length = await Receive(socket, buffer, cancel);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    if (length <= 0)
                    {
                        Console.WriteLine("server closed the connection: " + socket.CloseStatusDescription);
                        return 1;
                    }
                    bytes += length;
                    frames++;
                    var r = new MsgPackReader(new ReadOnlyMemory<byte>(buffer, 0, length));
                    var kind = WireCodec.ReadKind(r);
                    switch (kind)
                    {
                        case FrameKind.Refused:
                            var refusal = WireCodec.ReadRefused(r);
                            Console.WriteLine($"refused: {refusal.Reason} (server protocol {refusal.ServerProtocolVersion})");
                            return badToken && refusal.Reason == RefuseReason.BadToken ? 0 : 1;
                        case FrameKind.Welcome:
                            var welcome = WireCodec.ReadWelcome(r);
                            playerId = welcome.PlayerId;
                            mirror = new SimulationWorld(DefaultGameBalance.Create(), welcome.WorldSeed, welcome.Tick);
                            view = new PlayerView(mirror, playerId);
                            Console.WriteLine($"welcome: player {playerId}, tick {welcome.Tick}");
                            break;
                        case FrameKind.Snapshot:
                        case FrameKind.Delta:
                            bool full = kind == FrameKind.Snapshot;
                            var frame = WireCodec.ReadView(r);
                            view.Apply(frame.Body, full);
                            if (full) snapshots++;
                            break;
                        case FrameKind.CommandResult:
                            var result = WireCodec.ReadCommandResult(r);
                            if (result.Result != IssueResult.Issued) refused++;
                            break;
                        default:
                            WireCodec.SkipPayload(r);
                            break;
                    }
                    if (mirror == null)
                        continue;

                    if (clock.ElapsedMilliseconds >= nextOrderMs)
                    {
                        nextOrderMs += 20_000;
                        for (int i = 0; i < mirror.Fleets.Count; i++)
                        {
                            if (mirror.Fleets.At(i).OwnerPlayerId != playerId) continue;
                            var command = new DelayedPlayerCommand
                            {
                                PlayerId = playerId, CommandType = CommandType.SetRof, Target = mirror.Fleets.EntityAt(i),
                                ParamA = (int)(sequence % 2 == 0 ? RofStance.Conservation50 : RofStance.Standard100)
                            };
                            w.Rewind(0);
                            WireCodec.Write(w, new CommandFrame { Sequence = ++sequence, Command = command });
                            await socket.SendAsync(w.ToArray(), WebSocketMessageType.Binary, true, cancel);
                            issuedOrders++;
                            break;
                        }
                    }
                    if (clock.ElapsedMilliseconds >= nextCheckMs && pauseChecks && checkAtTick < 0)
                    {
                        nextCheckMs += 30_000;
                        await http.PostAsJsonAsync("/api/server/run", new { running = false }, cancel);
                        var paused = JsonDocument.Parse(await http.GetStringAsync("/api/server/status", cancel)).RootElement;
                        checkAtTick = paused.GetProperty("tick").GetInt64();
                    }
                    if (checkAtTick >= 0 && mirror.Clock.CurrentTick >= checkAtTick)
                    {
                        var server = JsonDocument.Parse(await http.GetStringAsync($"/api/server/viewhash?playerId={playerId}", cancel)).RootElement;
                        string mine = GameServer.Fnv(new PlayerView(mirror, playerId).Write(full: true));
                        checks++;
                        if (server.GetProperty("tick").GetInt64() != mirror.Clock.CurrentTick || server.GetProperty("hash").GetString() != mine)
                            drift++;
                        checkAtTick = -1;
                        await http.PostAsJsonAsync("/api/server/run", new { running = true }, cancel);
                        Console.WriteLine($"t={mirror.Clock.CurrentTick} (day {mirror.Clock.CurrentTick / SimTick.TicksPerDay + 1}) " +
                                          $"frames {frames} snapshots {snapshots} {bytes / clock.Elapsed.TotalSeconds / 1024:0.00} KB/s, " +
                                          $"orders {issuedOrders} (refused {refused}), checks {checks} drift {drift}");
                    }
                    if (clock.ElapsedMilliseconds >= nextCheckMs && !pauseChecks)
                    {
                        nextCheckMs += 30_000;
                        var server = JsonDocument.Parse(await http.GetStringAsync($"/api/server/viewhash?playerId={playerId}", cancel)).RootElement;
                        string mine = GameServer.Fnv(new PlayerView(mirror, playerId).Write(full: true));
                        long serverTick = server.GetProperty("tick").GetInt64();
                        checks++;
                        // the world may tick between our last frame and the request; only a hash at the same tick counts
                        if (serverTick == mirror.Clock.CurrentTick && server.GetProperty("hash").GetString() != mine)
                            drift++;
                        double seconds = clock.Elapsed.TotalSeconds;
                        Console.WriteLine($"t={mirror.Clock.CurrentTick} frames {frames} snapshots {snapshots} " +
                                          $"{bytes / seconds / 1024:0.00} KB/s, orders {issuedOrders} (refused {refused}), " +
                                          $"checks {checks} drift {drift}" + (serverTick == mirror.Clock.CurrentTick ? "" : " (tick moved)"));
                    }
                }
                double total = clock.Elapsed.TotalSeconds;
                Console.WriteLine($"done: {total / 60:0.0} min, {bytes / total:0} B/s, {frames} frames, {snapshots} snapshots, " +
                                  $"{issuedOrders} orders ({refused} refused), drift {drift} in {checks} checks");
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                return drift == 0 ? 0 : 1;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async Task<int> Receive(ClientWebSocket socket, byte[] buffer, CancellationToken cancel)
        {
            int length = 0;
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                    return 0;
                length += result.Count;
                if (result.EndOfMessage)
                    return length;
                if (length == buffer.Length)
                    throw new InvalidOperationException("Frame bigger than the client buffer.");
            }
        }
    }
}
