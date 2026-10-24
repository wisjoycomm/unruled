using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "play")
                return Play.Run(args);
            if (args.Length > 0 && args[0] == "server")
                return ServeWorld(args);
            if (args.Length > 0 && args[0] == "client")
                return ConsoleClient.Run(args);
            if (args.Length > 0 && args[0] == "profile")
                return Profile.Run(args);
            if (args.Length > 0 && args[0] == "batch")
                return Batch.Run(args);
            return Serve(args);
        }

        // `server [--port 5090] [--save path] [--seed S]`: the persistent world plus its admin dashboard (server.html).
        // Localhost only: the admin API has no login yet.
        private static int ServeWorld(string[] args)
        {
            int port = 5090;
            ulong seed = 4242;
            string save = System.IO.Path.Combine("..", "..", ".temp", "server", "world.sav");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port") port = int.Parse(args[i + 1]);
                if (args[i] == "--seed") seed = ulong.Parse(args[i + 1]);
                if (args[i] == "--save") save = args[i + 1];
            }

            var server = new GameServer(save, seed);
            var loop = new Thread(server.Loop) { IsBackground = true, Name = "world" };
            loop.Start();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = "wwwroot" });
            builder.WebHost.UseUrls("http://localhost:" + port);
            var app = builder.Build();
            app.UseStaticFiles();
            app.UseWebSockets();
            app.Map("/ws", async (HttpContext http) =>
            {
                if (!http.WebSockets.IsWebSocketRequest)
                {
                    http.Response.StatusCode = 400;
                    return;
                }
                using var socket = await http.WebSockets.AcceptWebSocketAsync();
                await WorldSocket.Run(socket, server, http.RequestAborted);
            });
            app.MapGet("/", () => Results.Redirect("/server.html"));
            app.MapGet("/api/server/status", () => server.Status());
            app.MapGet("/api/server/players", () => server.Players());
            app.MapGet("/api/server/log", (long? since) => server.LogSince(since ?? 0));
            app.MapPost("/api/server/run", (ServerRunRequest r) => server.SetRun(r.Running, r.Speed));
            app.MapPost("/api/server/save", () => server.Save());
            app.MapPost("/api/server/reset", (ServerResetRequest r) => server.Reset(r.Seed, r.Confirm));
            app.MapPost("/api/server/login", (PlayerRequest r) => server.LogIn(r.PlayerId, r.Join));
            app.MapPost("/api/server/logout", (PlayerRequest r) => server.LogOut(r.PlayerId, r.Governor));
            app.MapPost("/api/server/tutorial-done", (PlayerRequest r) => server.EndTutorial(r.PlayerId));
            app.MapPost("/api/server/token", (PlayerRequest r) => server.IssueToken(r.PlayerId, r.Join));
            app.MapGet("/api/server/viewhash", (int playerId) => server.ViewHash(playerId));
            app.MapPost("/api/bots/add", (BotRequest r) => server.AddBot(r.Preset ?? 1));
            app.MapPost("/api/bots/set", (BotRequest r) => server.SetBot(r.PlayerId, r.Preset, r.Hold));
            app.MapPost("/api/bots/rules", (BotSettings r) => server.SetBotRules(r));
            app.MapPost("/api/server/stop", (IHostApplicationLifetime life) => { life.StopApplication(); return Results.Ok(); });
            app.Lifetime.ApplicationStopping.Register(server.Stop);

            Console.WriteLine("Unruled server: http://localhost:" + port + "  (Ctrl+C saves and stops)");
            app.Run();
            return 0;
        }

        private static int Serve(string[] args)
        {
            int port = 5080;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--port") port = int.Parse(args[i + 1]);

            var host = new LabHost();
            host.Reset(1234, 0);
            var loop = new Thread(host.Loop) { IsBackground = true, Name = "sim" };
            loop.Start();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = "wwwroot" });
            builder.WebHost.UseUrls("http://localhost:" + port);
            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/meta", () => host.Meta());
            app.MapGet("/api/state", () => host.State());
            app.MapGet("/api/history", (int? since) => host.HistorySince(since ?? 0));
            app.MapPost("/api/run", (RunRequest r) => host.SetRun(r));
            app.MapPost("/api/step", (StepRequest r) => host.StepFor(r.Ticks));
            app.MapPost("/api/reset", (ResetRequest r) => { host.Reset(r.Seed, r.HumanPreset); return Results.Ok(); });
            app.MapPost("/api/seat", (SeatRequest r) => host.Seat(r.PlayerId, r.Preset));
            app.MapPost("/api/command", (CommandRequest r) => host.Command(r));

            Console.WriteLine("Sim Lab: http://localhost:" + port + "  (Ctrl+C to stop)");
            app.Run();
            return 0;
        }
    }

    /// <summary>Owns the running game; every touch of the world happens under one lock.</summary>
    public sealed class LabHost
    {
        private readonly object _gate = new object();
        private LabGame _game;
        private bool _running;
        private int _speed = 3_600; // sim ticks per real second; 0 = as fast as possible
        private long _stopAtTick;
        private double _measuredTps;

        public void Reset(ulong seed, byte humanPreset)
        {
            lock (_gate)
            {
                _game = new LabGame(seed, humanPreset);
                _running = false;
                _stopAtTick = 0;
            }
        }

        public void Loop()
        {
            var clock = Stopwatch.StartNew();
            double owed = 0;
            long last = clock.ElapsedTicks;
            long rateTicks = 0;
            var rateClock = Stopwatch.StartNew();
            while (true)
            {
                long now = clock.ElapsedTicks;
                double seconds = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                int toRun;
                lock (_gate)
                {
                    if (!_running)
                    {
                        owed = 0;
                        toRun = 0;
                    }
                    else if (_speed == 0)
                        toRun = 500;
                    else
                    {
                        owed = Math.Min(owed + seconds * _speed, _speed);
                        toRun = (int)Math.Min(owed, 2_000);
                        owed -= toRun;
                    }
                    for (int i = 0; i < toRun; i++)
                    {
                        _game.Step();
                        if (_stopAtTick != 0 && _game.Tick >= _stopAtTick)
                        {
                            _running = false;
                            _stopAtTick = 0;
                            break;
                        }
                    }
                }
                rateTicks += toRun;
                if (rateClock.ElapsedMilliseconds >= 1_000)
                {
                    _measuredTps = rateTicks * 1_000.0 / rateClock.ElapsedMilliseconds;
                    rateTicks = 0;
                    rateClock.Restart();
                }
                // a short sleep even at MAX lets HTTP requests take the lock between chunks
                Thread.Sleep(toRun == 0 ? 20 : _speed == 0 ? 1 : 5);
            }
        }

        public object SetRun(RunRequest r)
        {
            lock (_gate)
            {
                if (r.Speed.HasValue) _speed = Math.Max(0, r.Speed.Value);
                if (r.Running.HasValue) _running = r.Running.Value;
                if (r.UntilDay.HasValue)
                {
                    _stopAtTick = r.UntilDay.Value * SimTick.TicksPerDay;
                    _running = _stopAtTick > _game.Tick;
                }
                return new { _running, _speed };
            }
        }

        public object StepFor(long ticks)
        {
            lock (_gate)
            {
                for (long i = 0; i < Math.Clamp(ticks, 1, 7 * SimTick.TicksPerDay); i++)
                    _game.Step();
                return new { tick = _game.Tick };
            }
        }

        public object Seat(int playerId, byte preset)
        {
            lock (_gate)
            {
                try
                {
                    _game.SetController(playerId, preset);
                    return new { ok = true };
                }
                catch (Exception e)
                {
                    return new { ok = false, error = e.Message };
                }
            }
        }

        public object Command(CommandRequest r)
        {
            lock (_gate)
            {
                if (!Enum.TryParse<CommandType>(r.Type, out var type))
                    return new { ok = false, error = "unknown command " + r.Type };
                var hq = _game.Hq(r.PlayerId);
                if (hq.IsNull)
                    return new { ok = false, error = "no such player" };
                EntityId target;
                switch (r.Target)
                {
                    case "core": target = _game.Sim.Players.Get(hq).CoreBase; break;
                    case "hq": target = hq; break;
                    case "fleet": target = FirstFleet(r.PlayerId); break;
                    default:
                        target = r.TargetIndex.HasValue ? new EntityId(r.TargetIndex.Value, r.TargetGeneration ?? 1) : default;
                        break;
                }
                var result = _game.Issue(r.PlayerId, type, target, r.A, r.B, r.C, out long at);
                return new { ok = result == IssueResult.Issued, result = result.ToString(), landsIn = at - _game.Tick };
            }
        }

        private EntityId FirstFleet(int playerId)
        {
            var sim = _game.Sim;
            for (int i = 0; i < sim.Fleets.Count; i++)
                if (sim.Fleets.At(i).OwnerPlayerId == playerId && !sim.Fleets.At(i).IsConvoyOnly)
                    return sim.Fleets.EntityAt(i);
            return default;
        }

        public object Meta()
        {
            lock (_gate)
            {
                var ai = _game.Sim.Balance.Ai;
                var presets = new List<object>();
                for (byte i = 1; i <= ai.Count; i++)
                    presets.Add(new { id = i, code = ai.Preset(i).Code });
                var items = new List<string>();
                for (int s = 0; s < ItemIds.Count; s++)
                    items.Add(ItemIds.FromSlot(s).ToString());
                return new
                {
                    presets,
                    items,
                    commands = Enum.GetNames(typeof(CommandType)).Where(n => n != "None"),
                    humanPlayerId = LabGame.HumanPlayerId
                };
            }
        }

        public object State()
        {
            lock (_gate)
            {
                var g = _game;
                var players = new List<PlayerSample>();
                for (int i = 0; i < g.Sim.Players.Count; i++)
                    players.Add(g.SamplePlayer(g.Sim.Players.EntityAt(i), g.Sim.Players.At(i).PlayerId));
                int integrity = g.Sim.Citadels.Count > 0 ? g.Sim.Citadels.At(0).Integrity : 0;
                var actions = g.ActionCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
                return new
                {
                    seed = g.Seed,
                    tick = g.Tick,
                    running = _running,
                    speed = _speed,
                    tps = Math.Round(_measuredTps),
                    stopAtTick = _stopAtTick,
                    citadel = integrity,
                    citadelMax = g.Sim.Balance.Citadel.MaxIntegrity,
                    citadelFallTick = g.CitadelFallTick,
                    victoryTick = g.VictoryTick,
                    orders = g.Orders,
                    players,
                    actions,
                    log = g.Log.Take(120)
                };
            }
        }

        public object HistorySince(int since)
        {
            lock (_gate)
            {
                var h = _game.History;
                since = Math.Clamp(since, 0, h.Count);
                return new { from = since, samples = h.GetRange(since, h.Count - since) };
            }
        }
    }

    public sealed class ServerRunRequest
    {
        public bool? Running { get; set; }
        public int? Speed { get; set; }
    }

    public sealed class ServerResetRequest
    {
        public ulong Seed { get; set; }
        public bool Confirm { get; set; }
    }

    public sealed class PlayerRequest
    {
        public int PlayerId { get; set; }
        public bool Join { get; set; }
        public bool Governor { get; set; }
    }

    public sealed class BotRequest
    {
        public int PlayerId { get; set; }
        public byte? Preset { get; set; }
        public bool? Hold { get; set; }
    }

    public sealed class RunRequest
    {
        public bool? Running { get; set; }
        public int? Speed { get; set; }
        public long? UntilDay { get; set; }
    }

    public sealed class StepRequest { public long Ticks { get; set; } }

    public sealed class ResetRequest
    {
        public ulong Seed { get; set; }
        public byte HumanPreset { get; set; }
    }

    public sealed class SeatRequest
    {
        public int PlayerId { get; set; }
        public byte Preset { get; set; }
    }

    public sealed class CommandRequest
    {
        public int PlayerId { get; set; }
        public string Type { get; set; }
        public string Target { get; set; }
        public int? TargetIndex { get; set; }
        public int? TargetGeneration { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
    }
}
