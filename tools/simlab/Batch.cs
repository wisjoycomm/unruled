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
    /// `batch --seeds N --days D [--first S] [--human P] [--parallel K] [--credit C] [--csv path]`: runs N seeds in parallel, all seats AI-run
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
            int parallel = Arg(args, "--parallel", Environment.ProcessorCount);
            string creditArg = ArgString(args, "--credit");
            long? credit = creditArg == null ? (long?)null : long.Parse(creditArg);
            string botsArg = ArgString(args, "--bots");
            int? rivalCount = botsArg == null ? (int?)null : Math.Max(1, int.Parse(botsArg) - 1);

            Console.WriteLine($"Batch: {seeds} seeds x {days} days, {(rivalCount == null ? 5 : 1 + rivalCount.Value)} bots (seat 1 on preset {human})" +
                (credit == null ? "" : $", credit limit {credit} credits"));
            var watch = Stopwatch.StartNew();
            var results = new Result[seeds];
            Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
            {
                var game = new LabGame(first + (ulong)i, human, credit, rivalCount);
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

            Console.WriteLine();
            Console.WriteLine("LONG-RUN HEALTH (from hourly samples)");
            Console.WriteLine("seed   player preset               minFuel  minO2 minRat  dryHrs limitHrs  fleets/ships  orders/d refused% blackout%  T5 day MIL IND LOG SIG");
            foreach (var r in results)
                foreach (var p in r.Players)
                {
                    var h = p.Health;
                    Console.WriteLine($"{r.Seed,-6} {p.Last.PlayerId,-6} {p.PresetCode,-20} {h.MinFuel,7} {h.MinOxygen,6} {h.MinRations,6} {h.DryHours,7} {h.LimitHours,8}  " +
                        $"{p.Last.Fleets,5}/{p.Last.Ships,-6}  {h.OrdersPerDay,8:0} {h.RefusedPercent,7:0.0} {h.BlackoutPercent,9:0.0}  " +
                        $"{T5(h.T5Day[0]),7} {T5(h.T5Day[1]),3} {T5(h.T5Day[2]),3} {T5(h.T5Day[3]),3}");
                }
            Console.WriteLine();
            var all = results.SelectMany(r => r.Players).ToList();
            Console.WriteLine($"Totals: {all.Count(p => p.Health.DryHours > 0)}/{all.Count} ran a core supply dry, " +
                $"{all.Count(p => p.Health.LimitHours > 24)}/{all.Count} spent >1 day at the loan limit, " +
                $"{all.Count(p => p.Last.Fleets > 2 * Math.Max(1, p.Last.Ships / 10))}/{all.Count} fragmented fleets, " +
                $"median T5-all day {Median(all.Select(p => p.Health.T5Day.Max()).ToList())}.");

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

        private sealed class Health
        {
            public int MinFuel = int.MaxValue, MinOxygen = int.MaxValue, MinRations = int.MaxValue;
            public int DryHours, LimitHours;
            public double OrdersPerDay, RefusedPercent, BlackoutPercent;
            public double[] T5Day = { -1, -1, -1, -1 }; // -1 = never
        }

        private sealed class PlayerResult
        {
            public Health Health;
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
            for (int idx = 0; idx < last.Players.Count; idx++)
            {
                var p = last.Players[idx];
                string code = p.Preset == 0 ? "human (idle)" : game.Sim.Balance.Ai.Preset(p.Preset).Code;
                r.Players.Add(new PlayerResult
                {
                    Last = p, PresetCode = code, Verdict = Verdict(game, p), History = game.History, Health = MeasureHealth(game, idx, p.PlayerId)
                });
            }
            return r;
        }

        private static Health MeasureHealth(LabGame game, int playerIndex, int playerId)
        {
            const int fuel = (int)Unruled.Core.Economy.ItemId.ItemFuel - 1;
            const int oxygen = (int)Unruled.Core.Economy.ItemId.ItemOxygen - 1;
            const int rations = (int)Unruled.Core.Economy.ItemId.ItemRations - 1;
            long limit = game.Sim.Balance.Ledger.CreditLimitCents / Unruled.Core.Economy.Wallet.CentsPerCredit;
            var h = new Health();
            foreach (var s in game.History)
            {
                if (s.Tick == 0) continue;
                var p = playerIndex < s.Players.Count && s.Players[playerIndex].PlayerId == playerId
                    ? s.Players[playerIndex]
                    : s.Players.Find(q => q.PlayerId == playerId);
                if (p == null) continue;
                h.MinFuel = Math.Min(h.MinFuel, p.Stock[fuel]);
                h.MinOxygen = Math.Min(h.MinOxygen, p.Stock[oxygen]);
                h.MinRations = Math.Min(h.MinRations, p.Stock[rations]);
                if (p.Stock[fuel] == 0 || p.Stock[oxygen] == 0 || p.Stock[rations] == 0) h.DryHours++;
                if (p.Debt >= limit) h.LimitHours++;
                for (int b = 0; b < 4; b++)
                    if (h.T5Day[b] < 0 && p.Tiers[b] >= 5) h.T5Day[b] = s.Tick / (double)SimTick.TicksPerDay;
            }
            double days = Math.Max(1, game.Tick / (double)SimTick.TicksPerDay);
            long orders = game.ActionCounts.TryGetValue(playerId, out var counts) ? counts.Sum(c => (long)c) : 0;
            long refused = game.Refused.TryGetValue(playerId, out long x) ? x : 0;
            long blackedOut = game.BlackedOut.TryGetValue(playerId, out long y) ? y : 0;
            h.OrdersPerDay = orders / days;
            h.RefusedPercent = orders == 0 ? 0 : 100.0 * refused / orders;
            h.BlackoutPercent = orders == 0 ? 0 : 100.0 * blackedOut / orders;
            return h;
        }

        private static string T5(double day) => day < 0 ? "-" : day.ToString("0");

        private static string Median(List<double> days)
        {
            if (days.Any(d => d < 0)) return "never (some branch not T5)";
            days.Sort();
            return "day " + days[days.Count / 2].ToString("0.0");
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
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, sb.ToString());
            Console.WriteLine("CSV: " + fullPath);
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
