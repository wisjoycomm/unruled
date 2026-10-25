using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>
    /// `profile [--empires 100,400,1000] [--hours 6] [--seed S] [--views N]`: for each empire count, joins that many bots
    /// into a new game, then times every sim system and the AI over the run. Prints the cost per tick of each, and per
    /// empire, so cost that grows faster than the empire count shows up by name. With --views, N players (0 = every
    /// empire) each get a <see cref="Unruled.Core.Net.PlayerView"/> whose delta is written after every tick, as the
    /// server does for connected players: the cost per player and the bytes. Tooling only: it wraps the clock's systems
    /// through reflection, which Core itself never does.
    /// </summary>
    public static class Profile
    {
        public static int Run(string[] args)
        {
            string counts = ArgString(args, "--empires") ?? "100,400,1000";
            double hours = double.Parse(ArgString(args, "--hours") ?? "6", System.Globalization.CultureInfo.InvariantCulture);
            ulong seed = ulong.Parse(ArgString(args, "--seed") ?? "777");
            string views = ArgString(args, "--views");
            _threads = int.Parse(ArgString(args, "--threads") ?? "1");
            Console.WriteLine($"Profile: {hours} game hours per run, seed {seed}" + (views != null ? $", views {views}, threads {_threads}" : ""));
            foreach (var part in counts.Split(','))
                RunOne(int.Parse(part), hours, seed, views == null ? -1 : int.Parse(views));
            return 0;
        }

        private static int _threads = 1;

        private static void RunOne(int empires, double hours, ulong seed, int viewers)
        {
            var game = new LabGame(seed, 2, null, 5);
            var sim = game.Sim;
            var setup = Stopwatch.StartNew();
            for (int i = sim.Players.Count; i < empires; i++)
                NewGameSetup.Join(sim, (byte)(i % sim.Balance.Ai.Count + 1));
            setup.Stop();

            var timed = Wrap(sim.Clock);
            var ai = new Stopwatch();
            var viewWatch = new Stopwatch();
            var playerViews = new List<Unruled.Core.Net.PlayerView>();
            var hub = new Unruled.Core.Net.PlayerViewHub(sim);
            if (viewers >= 0)
                for (int i = 0; i < sim.Players.Count && (viewers == 0 || playerViews.Count < viewers); i++)
                    playerViews.Add(new Unruled.Core.Net.PlayerView(hub, sim.Players.At(i).PlayerId));
            long viewBytes = 0, snapshotBytes = 0;
            hub.BeginPass();
            foreach (var v in playerViews)
                snapshotBytes += v.Write(true).Length;
            long ticks = (long)(hours * SimTick.TicksPerHour);
            GC.Collect();
            long gcBefore = GC.CollectionCount(0);
            var total = Stopwatch.StartNew();
            for (long t = 0; t < ticks; t++)
            {
                sim.Clock.Advance(1);
                ai.Start();
                sim.Ai.Think(sim.Clock.CurrentTick);
                ai.Stop();
                viewWatch.Start();
                if (_threads > 1)
                {
                    hub.BeginPass();
                    hub.PrepareInterest();
                    System.Threading.Tasks.Parallel.For(0, hub.ChannelCount,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = _threads }, hub.PrepareChannel);
                    long passBytes = 0;
                    System.Threading.Tasks.Parallel.For(0, playerViews.Count,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = _threads },
                        () => 0L, (v, _, sum) => sum + playerViews[v].Write(false).Length,
                        sum => System.Threading.Interlocked.Add(ref passBytes, sum));
                    viewBytes += passBytes;
                }
                else
                {
                    hub.BeginPass();
                    foreach (var v in playerViews)
                        viewBytes += v.Write(false).Length;
                }
                viewWatch.Stop();
            }
            total.Stop();

            double perTick = total.Elapsed.TotalMilliseconds / ticks;
            Console.WriteLine();
            Console.WriteLine($"== {sim.Players.Count} empires: {perTick:0.000} ms/tick ({perTick / sim.Players.Count * 1000:0.00} us per empire), " +
                              $"join {setup.Elapsed.TotalSeconds:0.0}s, gen0 GCs {GC.CollectionCount(0) - gcBefore}, " +
                              $"live heap {GC.GetTotalMemory(true) / (1024 * 1024)} MB, entities {sim.World.IssuedCount}");
            var rows = new List<(string name, double ms)>();
            foreach (var t in timed)
                rows.Add((t.Name, t.Watch.Elapsed.TotalMilliseconds / ticks));
            rows.Add(("UtilityAiEngine.Think", ai.Elapsed.TotalMilliseconds / ticks));
            if (playerViews.Count > 0)
            {
                ChannelCosts(sim, hub, playerViews);
                double viewMs = viewWatch.Elapsed.TotalMilliseconds / ticks;
                rows.Add(($"PlayerView x{playerViews.Count}", viewMs));
                Console.WriteLine($"   views: {viewMs / playerViews.Count * 1000:0.0} us per player per tick, " +
                                  $"snapshot {snapshotBytes / playerViews.Count} B, delta {viewBytes / (double)ticks / playerViews.Count:0} B per player per tick");
            }
            rows.Sort((a, b) => b.ms.CompareTo(a.ms));
            Console.WriteLine($"   hubs {sim.Citadels.Count}, systems {sim.StarSystems.Count}, lanes {sim.Starlanes.Count}");
            foreach (var (name, ms) in rows)
                if (ms >= 0.0005)
                    Console.WriteLine($"   {name,-32} {ms,9:0.0000} ms/tick  {ms / perTick * 100,5:0.0}%");
        }

        // One more tick, with every view's channels timed one by one (reflection: tooling only).
        private static void ChannelCosts(SimulationWorld sim, Unruled.Core.Net.PlayerViewHub hub, List<Unruled.Core.Net.PlayerView> views)
        {
            var channelsField = typeof(Unruled.Core.Net.PlayerView).GetField("_channels", BindingFlags.NonPublic | BindingFlags.Instance);
            var interestField = typeof(Unruled.Core.Net.PlayerView).GetField("_interest", BindingFlags.NonPublic | BindingFlags.Instance);
            var candidatesField = typeof(Unruled.Core.Net.PlayerView).GetField("_candidates", BindingFlags.NonPublic | BindingFlags.Instance);
            var eyes = typeof(Unruled.Core.Net.PlayerView).GetMethod("FindEyes", BindingFlags.NonPublic | BindingFlags.Instance);
            sim.Clock.Advance(1);
            hub.BeginPass();
            var costs = new double[64];
            var names = new string[64];
            var body = new Unruled.Core.Persistence.MsgPackWriter(1 << 16);
            var scratch = new Unruled.Core.Persistence.MsgPackWriter(1 << 16);
            var watch = new Stopwatch();
            foreach (var view in views)
            {
                eyes.Invoke(view, null);
                var channels = (System.Collections.IList)channelsField.GetValue(view);
                var interest = interestField.GetValue(view);
                var candidates = candidatesField.GetValue(view);
                for (int c = 0; c < channels.Count; c++)
                {
                    var channel = channels[c];
                    var write = channel.GetType().GetMethod("Write");
                    body.Rewind(0);
                    watch.Restart();
                    write.Invoke(channel, new object[] { body, scratch, interest, candidates, false, 0, 0 });
                    watch.Stop();
                    costs[c] += watch.Elapsed.TotalMilliseconds;
                    names[c] ??= channel.GetType().GetGenericArguments()[0].Name;
                }
            }
            var order = new List<int>();
            for (int c = 0; c < 64; c++) if (names[c] != null) order.Add(c);
            order.Sort((a, b) => costs[b].CompareTo(costs[a]));
            Console.Write("   channels (us per player):");
            for (int i = 0; i < Math.Min(8, order.Count); i++)
                Console.Write($" {names[order[i]]} {costs[order[i]] * 1000 / views.Count:0.0}");
            Console.WriteLine();
        }

        private sealed class Timed : ISimSystem
        {
            public readonly ISimSystem Inner;
            public readonly string Name;
            public readonly Stopwatch Watch = new Stopwatch();

            public Timed(ISimSystem inner)
            {
                Inner = inner;
                Name = inner.GetType().Name;
            }

            public void OnTick(long tick)
            {
                Watch.Start();
                Inner.OnTick(tick);
                Watch.Stop();
            }
        }

        private static List<Timed> Wrap(SimClock clock)
        {
            var field = typeof(SimClock).GetField("_systems", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? throw new InvalidOperationException("SimClock._systems not found.");
            var systems = (List<ISimSystem>)field.GetValue(clock);
            var timed = new List<Timed>();
            for (int i = 0; i < systems.Count; i++)
            {
                var t = new Timed(systems[i]);
                systems[i] = t;
                timed.Add(t);
            }
            return timed;
        }

        private static string ArgString(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
