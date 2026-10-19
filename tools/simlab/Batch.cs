using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>
    /// `batch --seeds N --days D [--first S] [--human P] [--csv path]`: runs N seeds in parallel, all seats AI-run
    /// (the human seat uses preset P, default 2), and prints a survival report per seed and player.
    /// </summary>
    public static class Batch
    {
        public static int Run(string[] args)
        {
            int seeds = Arg(args, "--seeds", 5);
            int days = Arg(args, "--days", 30);
            ulong first = (ulong)Arg(args, "--first", 1234);
            byte human = (byte)Arg(args, "--human", 2);
            string csv = ArgString(args, "--csv");

            Console.WriteLine($"Batch: {seeds} seeds x {days} days, seat 1 on preset {human}");
            var watch = Stopwatch.StartNew();
            var results = new Result[seeds];
            Parallel.For(0, seeds, i =>
            {
                var game = new LabGame(first + (ulong)i, human);
                long end = days * SimTick.TicksPerDay;
                while (game.Tick < end)
                    game.Step();
                results[i] = Summarise(game);
                Console.WriteLine($"  seed {game.Seed} done ({watch.Elapsed.TotalSeconds:0}s)");
            });

            Console.WriteLine();
            Console.WriteLine($"Done in {watch.Elapsed.TotalSeconds:0.0}s ({(double)seeds * days / watch.Elapsed.TotalSeconds:0.0} game-days/s)");
            Console.WriteLine();
            Console.WriteLine("seed   citadel-fall  directorate  orders   | player preset              cash     debt  MIL IND LOG SIG ships sites gate  verdict");
            int problems = 0;
            foreach (var r in results)
            {
                bool firstRow = true;
                foreach (var p in r.Players)
                {
                    string head = firstRow
                        ? $"{r.Seed,-6} {Day(r.CitadelFall),-13} {Day(r.Victory),-12} {r.Orders,-8}"
                        : new string(' ', 42);
                    firstRow = false;
                    if (p.Verdict != "ok") problems++;
                    Console.WriteLine($"{head} | {p.Last.PlayerId,-6} {p.PresetCode,-20} {p.Last.Cash,8} {p.Last.Debt,8}  " +
                        $"{p.Last.Tiers[0],3} {p.Last.Tiers[1],3} {p.Last.Tiers[2],3} {p.Last.Tiers[3],3} {p.Last.Ships,5} {p.Last.Sites,5} {(p.Last.GateOpen ? "open" : "shut"),5}  {p.Verdict}");
                }
            }
            Console.WriteLine();
            Console.WriteLine(problems == 0 ? "SURVIVAL: every player is alive and growing." : $"SURVIVAL: {problems} player(s) flagged (see verdicts).");

            if (csv != null)
                WriteCsv(csv, results);
            return 0;
        }

        private sealed class Result
        {
            public ulong Seed;
            public long CitadelFall, Victory, Orders;
            public List<PlayerResult> Players;
        }

        private sealed class PlayerResult
        {
            public PlayerSample Last;
            public string PresetCode;
            public string Verdict;
            public List<Sample> History;
        }

        private static Result Summarise(LabGame game)
        {
            var r = new Result
            {
                Seed = game.Seed, CitadelFall = game.CitadelFallTick, Victory = game.VictoryTick, Orders = game.Orders,
                Players = new List<PlayerResult>()
            };
            var last = game.History[game.History.Count - 1];
            foreach (var p in last.Players)
            {
                string code = p.Preset == 0 ? "human (idle)" : game.Sim.Balance.Ai.Preset(p.Preset).Code;
                r.Players.Add(new PlayerResult { Last = p, PresetCode = code, Verdict = Verdict(game, p), History = game.History });
            }
            return r;
        }

        /// <summary>Flags the ways an empire fails to survive: no ships, broke and in debt, stalled at tier 1, starving.</summary>
        private static string Verdict(LabGame game, PlayerSample p)
        {
            var flags = new List<string>();
            if (p.Ships == 0) flags.Add("NO-FLEET");
            if (p.Cash <= 0 && p.Debt > 0) flags.Add("BROKE");
            if (p.Tiers.Max() <= 1 && game.Tick >= 3 * SimTick.TicksPerDay) flags.Add("STALLED-T1");
            int fuel = p.Stock[(int)Unruled.Core.Economy.ItemId.ItemFuel - 1];
            int oxygen = p.Stock[(int)Unruled.Core.Economy.ItemId.ItemOxygen - 1];
            int rations = p.Stock[(int)Unruled.Core.Economy.ItemId.ItemRations - 1];
            if (fuel == 0 || oxygen == 0 || rations == 0) flags.Add("STARVING");
            if (p.Sites <= 1 && game.Tick >= 5 * SimTick.TicksPerDay) flags.Add("NO-EXPANSION");
            return flags.Count == 0 ? "ok" : string.Join(",", flags);
        }

        private static void WriteCsv(string path, Result[] results)
        {
            var sb = new StringBuilder("seed,day,player,preset,cash,debt,mil,ind,log,sig,ships,sites,gate,citadel\n");
            foreach (var r in results)
                foreach (var s in r.Players[0].History)
                    foreach (var p in s.Players)
                        sb.Append($"{r.Seed},{s.Tick / (double)SimTick.TicksPerDay:0.###},{p.PlayerId},{p.Preset},{p.Cash},{p.Debt}," +
                                  $"{p.Tiers[0]},{p.Tiers[1]},{p.Tiers[2]},{p.Tiers[3]},{p.Ships},{p.Sites},{(p.GateOpen ? 1 : 0)},{s.CitadelIntegrity}\n");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }

        private static string Day(long tick) => tick == 0 ? "-" : "day " + (tick / (double)SimTick.TicksPerDay).ToString("0.0");

        private static int Arg(string[] args, string name, int fallback)
        {
            string s = ArgString(args, name);
            return s == null ? fallback : int.Parse(s);
        }

        private static string ArgString(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
