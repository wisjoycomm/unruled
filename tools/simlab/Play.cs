using System;
using System.IO;
using System.Text;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>
    /// `play ...`: a turn-by-turn game for one human seat driven from the command line, kept in a save file between
    /// calls (so an agent or a person can play a long game in steps). Seat 1 is the human; every other seat is a bot.
    /// <list type="bullet">
    /// <item>`play new [--seed S] [--bots N] [--save path]`: N bots besides you (default 5).</item>
    /// <item>`play status [--save path]`: your empire, fleets, sites, nearby planets and the rivals.</item>
    /// <item>`play do Type target [a] [b] [c] [--pe i:g]`: one order through <see cref="CommandIssuer"/>; target is
    /// `core`, `hq`, `fleet` (your first battle fleet), `fleetN`, or an entity `i:g`; --pe sets ParamEntity.</item>
    /// <item>`play run H`: advances H game hours (bots think, your orders land).</item>
    /// </list>
    /// </summary>
    public static class Play
    {
        private const int Me = LabGame.HumanPlayerId;
        private static readonly bool Brief = Environment.GetEnvironmentVariable("PLAY_BRIEF") == "1";

        public static int Run(string[] args)
        {
            string save = ArgString(args, "--save") ?? Path.Combine("..", "..", ".temp", "play", "game.sav");
            string verb = args.Length > 1 ? args[1] : "status";
            if (verb == "new")
            {
                ulong seed = (ulong)Arg(args, "--seed", 4242);
                int bots = Arg(args, "--bots", 5);
                var game = new LabGame(seed, 0, null, bots);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(save)));
                Save(game.Sim, save);
                Console.WriteLine($"New game: seed {seed}, you + {bots} bots.");
                Console.Write(Status(game.Sim));
                return 0;
            }
            var sim = SaveGame.Load(DefaultGameBalance.Create(), File.ReadAllBytes(save));
            switch (verb)
            {
                case "status":
                    Console.Write(Status(sim));
                    return 0;
                case "do":
                    Console.WriteLine(Order(sim, args));
                    Save(sim, save);
                    return 0;
                case "run":
                    double hours = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                    long end = sim.Clock.CurrentTick + (long)(hours * SimTick.TicksPerHour);
                    while (sim.Clock.CurrentTick < end)
                    {
                        sim.Clock.Advance(1);
                        sim.Ai.Think(sim.Clock.CurrentTick);
                    }
                    Save(sim, save);
                    Console.Write(Status(sim));
                    return 0;
                case "join":
                {
                    // play join [archetype]: a bot (or an idle human seat with 0) joins the running game.
                    byte preset = args.Length > 2 ? byte.Parse(args[2]) : (byte)1;
                    var joined = NewGameSetup.Join(sim, preset);
                    Save(sim, save);
                    Console.WriteLine($"P{sim.Players.Get(joined.PlayerHq).PlayerId} joined at {Site(sim, joined.CoreBase)}");
                    return 0;
                }
                case "route":
                {
                    // play route fleet site [tollfree]: the lanes a Move would take, with fuel and toll per lane.
                    var fleet = Resolve(sim, args[2]);
                    var to = ParseId(args[3]);
                    bool free = args.Length > 4 && args[4] == "1";
                    ref var unit = ref sim.Fleets.Get(fleet);
                    var route = default(Unruled.Core.Military.FleetRoute);
                    if (!sim.Routes.TryPlan(sim.FleetMotions.Get(fleet).Site, to, unit.TechTier, free, ref route))
                    {
                        Console.WriteLine("no route");
                        return 0;
                    }
                    long fuel = 0, toll = 0;
                    for (int i = 0; i < route.Count; i++)
                    {
                        ref var edge = ref sim.Starlanes.Get(route.LaneAt(i));
                        int f = sim.Movement.FuelFor(in unit, in edge);
                        long t = sim.Tolls.QuoteCents(in unit, in edge);
                        fuel += f;
                        toll += t;
                        Console.WriteLine($"{route.LaneAt(i)} {edge.Tier} len{edge.Length} {Site(sim, edge.SiteA)}<->{Site(sim, edge.SiteB)} fuel {f} toll {t / 100}");
                    }
                    Console.WriteLine($"total fuel {fuel}, toll {toll / 100} cr");
                    return 0;
                }
                default:
                    Console.WriteLine("verbs: new | status | do | run | route");
                    return 1;
            }
        }

        private static void Save(SimulationWorld sim, string path) => File.WriteAllBytes(path, SaveGame.Write(sim));

        private static string Order(SimulationWorld sim, string[] args)
        {
            if (args.Length < 4 || !Enum.TryParse<CommandType>(args[2], out var type))
                return "usage: play do Type target [a] [b] [c] [--pe i:g]";
            var command = new DelayedPlayerCommand { PlayerId = Me, CommandType = type, Target = Resolve(sim, args[3]) };
            int[] p = new int[3];
            for (int i = 0; i < 3 && 4 + i < args.Length && !args[4 + i].StartsWith("--"); i++)
                p[i] = int.Parse(args[4 + i]);
            command.ParamA = p[0];
            command.ParamB = p[1];
            command.ParamC = p[2];
            string pe = ArgString(args, "--pe");
            if (pe != null)
                command.ParamEntity = ParseId(pe);
            var result = sim.Issuer.Issue(command, out long at);
            return $"{type} -> {command.Target} : {result}" + (result == IssueResult.Issued ? $", lands in {at - sim.Clock.CurrentTick}t" : "");
        }

        private static EntityId Resolve(SimulationWorld sim, string target)
        {
            var hq = Hq(sim);
            if (target == "core") return sim.Players.Get(hq).CoreBase;
            if (target == "hq") return hq;
            if (target.StartsWith("fleet"))
            {
                int want = target.Length > 5 ? int.Parse(target.Substring(5)) : 0;
                for (int i = 0, n = 0; i < sim.Fleets.Count; i++)
                    if (sim.Fleets.At(i).OwnerPlayerId == Me && sim.Fleets.At(i).TotalShips > 0 && n++ == want)
                        return sim.Fleets.EntityAt(i);
                return default;
            }
            return ParseId(target);
        }

        private static EntityId ParseId(string text)
        {
            var parts = text.Split(':');
            return new EntityId(int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 1);
        }

        private static EntityId Hq(SimulationWorld sim)
        {
            for (int i = 0; i < sim.Players.Count; i++)
                if (sim.Players.At(i).PlayerId == Me)
                    return sim.Players.EntityAt(i);
            throw new InvalidOperationException("No human seat.");
        }

        private static string Status(SimulationWorld sim)
        {
            var s = new StringBuilder();
            long tick = sim.Clock.CurrentTick;
            var hq = Hq(sim);
            var core = sim.Players.Get(hq).CoreBase;
            int home = sim.Sites.Get(core).ClusterId;
            s.Append($"== DAY {tick / SimTick.TicksPerDay + 1} {tick % SimTick.TicksPerDay / SimTick.TicksPerHour:00}h (tick {tick}) ==\n");

            long cash = sim.Wallets.Has(hq) ? sim.Wallets.Get(hq).Cents / 100 : 0;
            long debt = sim.Loans.Has(hq) ? sim.Loans.Get(hq).DebtCents / 100 : 0;
            s.Append($"cash {cash} cr, debt {debt} cr, can borrow {sim.Lending.HeadroomCents(hq, tick) / 100} cr\n");
            if (sim.TechProgress.Has(hq))
            {
                ref var t = ref sim.TechProgress.Get(hq);
                s.Append($"tech MIL{t.Tier(ResearchBranch.Military)} IND{t.Tier(ResearchBranch.Industry)} LOG{t.Tier(ResearchBranch.Logistics)} SIG{t.Tier(ResearchBranch.Signals)}" +
                         $"  active {t.ActiveBranch} stage {t.Stage} telemetry {t.TelemetryMilli}" +
                         (t.Stage == ResearchStage.Retooling ? $" retool until +{(t.RetoolUntilTick - tick) / 60}m" : "") + "\n");
            }
            s.Append("core stock: ").Append(Stock(sim, core)).Append('\n');

            s.Append("-- fleets --\n");
            for (int i = 0; i < sim.Fleets.Count; i++)
            {
                ref var f = ref sim.Fleets.At(i);
                if (f.OwnerPlayerId != Me || f.TotalShips == 0) continue;
                var id = sim.Fleets.EntityAt(i);
                ref var m = ref sim.FleetMotions.Get(id);
                string where = m.Lane.IsNull ? "at " + Site(sim, m.Site) : $"on lane {m.Lane} from {Site(sim, m.FromSite)} ({m.TravelledMilli / 1000}/{sim.Starlanes.Get(m.Lane).Length})";
                string ready = sim.FleetReadiness.Has(id) ? (sim.FleetReadiness.Get(id).IsMothballed ? " MOTHBALLED" : sim.FleetReadiness.Get(id).IsUnsupplied ? " UNSUPPLIED" : !sim.FleetReadiness.Get(id).CanAct(tick) ? " WARMING" : "") : "";
                if (sim.FleetCombat.Has(id) && sim.FleetCombat.Get(id).EngagedUntilTick > tick) ready += " ENGAGED";
                s.Append($"{id} T{f.TechTier} line{f.LineShips} ew{f.EwShips} sup{f.SupplyTenders} cargo{f.CargoConvoys} {where}{ready}\n   hold: {Stock(sim, id)}\n");
            }
            for (int i = 0; i < sim.ShipOrders.Count; i++)
            {
                ref var o = ref sim.ShipOrders.At(i);
                if (o.OwnerPlayerId == Me)
                    s.Append($"   building {o.Count} {o.ShipClass} T{o.TechTier}, done in {(o.DoneTick - tick) / 60}m\n");
            }

            s.Append("-- my sites --\n");
            for (int i = 0; i < sim.Sites.Count; i++)
                if (sim.Sites.At(i).OwnerPlayerId == Me)
                    AppendSite(s, sim, sim.Sites.EntityAt(i), true);

            s.Append("-- planets in systems I know (unowned or rival) --\n");
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                ref var site = ref sim.Sites.At(i);
                if (site.OwnerPlayerId == Me || !sim.Galaxy.Knows(Me, site.ClusterId) ||
                    (Brief && site.ClusterId != home && site.OwnerPlayerId == 0)) continue;
                AppendSite(s, sim, sim.Sites.EntityAt(i), site.ClusterId == home);
            }

            s.Append("-- lanes from my sites/fleets' sites --\n");
            for (int i = 0; i < sim.Starlanes.Count; i++)
            {
                ref var lane = ref sim.Starlanes.At(i);
                if (!Touches(sim, lane.SiteA) && !Touches(sim, lane.SiteB)) continue;
                s.Append($"{sim.Starlanes.EntityAt(i)} {lane.Tier} len{lane.Length}: {Site(sim, lane.SiteA)} <-> {Site(sim, lane.SiteB)}\n");
            }
            for (int i = 0; i < sim.JumpGates.Count; i++)
            {
                ref var g = ref sim.JumpGates.At(i);
                if (g.OwnerPlayerId == Me)
                    s.Append($"jump gate at {Site(sim, g.HomeSite)} -> {Site(sim, g.FarSite)}: {(g.IsOpen ? "OPEN" : "dormant")}\n");
            }

            s.Append("-- rivals --\n");
            for (int i = 0; i < sim.Players.Count; i++)
            {
                int pid = sim.Players.At(i).PlayerId;
                if (pid == Me) continue;
                var rhq = sim.Players.EntityAt(i);
                int ships = 0, sites = 0;
                for (int f = 0; f < sim.Fleets.Count; f++) if (sim.Fleets.At(f).OwnerPlayerId == pid) ships += sim.Fleets.At(f).TotalShips;
                for (int x = 0; x < sim.Sites.Count; x++) if (sim.Sites.At(x).OwnerPlayerId == pid) sites++;
                long rc = sim.Wallets.Has(rhq) ? sim.Wallets.Get(rhq).Cents / 100 : 0;
                var tp = sim.TechProgress.Has(rhq) ? sim.TechProgress.Get(rhq) : default;
                string preset = sim.AiControllers.Has(rhq) ? sim.Balance.Ai.Preset(sim.AiControllers.Get(rhq).Archetype).Code : "human";
                s.Append($"P{pid} {preset}: cash {rc}, ships {ships}, sites {sites}, MIL{tp.Tier(ResearchBranch.Military)} IND{tp.Tier(ResearchBranch.Industry)} LOG{tp.Tier(ResearchBranch.Logistics)} SIG{tp.Tier(ResearchBranch.Signals)}\n");
            }
            if (sim.Citadels.Count > 0)
                s.Append($"citadel integrity {sim.Citadels.At(0).Integrity}\n");
            for (int i = 0; i < sim.WtoOrders.Count; i++)
            {
                ref var o = ref sim.WtoOrders.At(i);
                if (o.Remaining > 0)
                    s.Append($"wto P{o.OwnerPlayerId} {o.Side} {o.Remaining} {o.Item} @ {o.PriceCents / 100.0:0.00}\n");
            }
            for (int i = 0; i < sim.Clock.PendingCommandCount; i++)
            {
                var c = sim.Clock.PendingAt(i);
                if (c.PlayerId == Me)
                    s.Append($"in flight: {c.CommandType} -> {c.Target} lands in {c.ExecuteAtTick - tick}t\n");
            }
            return s.ToString();
        }

        private static bool Touches(SimulationWorld sim, EntityId site)
        {
            if (sim.Sites.Get(site).OwnerPlayerId == Me) return true;
            for (int i = 0; i < sim.Fleets.Count; i++)
                if (sim.Fleets.At(i).OwnerPlayerId == Me && sim.FleetMotions.Has(sim.Fleets.EntityAt(i)) &&
                    sim.FleetMotions.Get(sim.Fleets.EntityAt(i)).Site == site)
                    return true;
            return false;
        }

        private static void AppendSite(StringBuilder s, SimulationWorld sim, EntityId id, bool detail)
        {
            ref var site = ref sim.Sites.Get(id);
            s.Append($"{Site(sim, id)} {PlanetClasses.Of(in site, sim.Balance.Universe)} owner P{site.OwnerPlayerId} ESI {site.CurrentEsiMilli} {site.LifecycleState}");
            if (!detail) { s.Append('\n'); return; }
            s.Append('\n');
            for (int d = 0; d < sim.Deposits.Count; d++)
            {
                ref var dep = ref sim.Deposits.At(d);
                if (dep.Site != id) continue;
                s.Append($"   deposit {sim.Deposits.EntityAt(d)} {dep.RawItem} {dep.SurfaceReserve}/{dep.DeepCrustReserve}/{dep.CoreFractureReserve} rigs {dep.RigCount}{(dep.IsPaused ? " PAUSED" : "")}\n");
            }
            if (site.OwnerPlayerId == Me)
            {
                for (int r = 0; r < sim.Refineries.Count; r++)
                    if (sim.Refineries.At(r).Site == id)
                        s.Append($"   refinery {sim.Refineries.At(r).Output} {sim.Refineries.At(r).State}\n");
                if (sim.Stockpiles.Has(id) && id != sim.Players.Get(Hq(sim)).CoreBase)
                    s.Append("   stock: ").Append(Stock(sim, id)).Append('\n');
            }
        }

        private static string Site(SimulationWorld sim, EntityId id)
        {
            if (!sim.Sites.Has(id)) return id.ToString();
            ref var site = ref sim.Sites.Get(id);
            return $"{id}[c{site.ClusterId}o{site.OrbitIndex}]";
        }

        private static string Stock(SimulationWorld sim, EntityId id)
        {
            if (!sim.Stockpiles.Has(id)) return "-";
            ref var stock = ref sim.Stockpiles.Get(id);
            var s = new StringBuilder();
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                int q = stock.Get(ItemIds.FromSlot(slot));
                if (q != 0) s.Append(ItemIds.FromSlot(slot).ToString().Replace("Raw", "r").Replace("Item", "")).Append(' ').Append(q).Append(", ");
            }
            return s.Length == 0 ? "empty" : s.ToString(0, s.Length - 2);
        }

        private static int Arg(string[] args, string name, int fallback)
        {
            string v = ArgString(args, name);
            return v == null ? fallback : int.Parse(v);
        }

        private static string ArgString(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
