using System;
using System.Collections.Generic;
using Unruled.Core.AI;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>One headless game: the world, its clock and AI, plus hourly samples and an AI order log.</summary>
    public sealed class LabGame
    {
        public const int HumanPlayerId = 1;
        private const int MaxLog = 400;

        public readonly SimulationWorld Sim;
        public readonly ulong Seed;
        public readonly List<Sample> History = new List<Sample>();
        public readonly LinkedList<LogLine> Log = new LinkedList<LogLine>();
        public readonly Dictionary<int, int[]> ActionCounts = new Dictionary<int, int[]>();
        public long Orders;
        public readonly Dictionary<int, long> Refused = new Dictionary<int, long>(); // refused for any reason but a radio blackout
        public readonly Dictionary<int, long> BlackedOut = new Dictionary<int, long>(); // waited out a storm (retried each think)
        public long CitadelFallTick;
        public long VictoryTick;

        /// <param name="creditLimit">Credits a player may borrow from THE SYSTEM; null = the default balance's limit.</param>
        /// <param name="rivalCount">Number of AI rivals in addition to seat 1; null = the default balance's rivalCount (4).</param>
        public LabGame(ulong seed, byte humanPreset, long? creditLimit = null, int? rivalCount = null)
        {
            Seed = seed;
            Sim = new SimulationWorld(Balance(creditLimit, rivalCount), seed);
            NewGameSetup.Create(Sim, HumanPlayerId);
            if (humanPreset != 0)
                Sim.Ai.Assign(Hq(HumanPlayerId), humanPreset);
            TakeSample();
        }

        // The default balance, with another credit limit or rival count for custom batch runs.
        private static GameBalanceConfig Balance(long? creditLimit, int? rivalCount)
        {
            var d = DefaultGameBalance.Create();
            if (creditLimit == null && rivalCount == null)
                return d;
            var ledger = creditLimit == null
                ? d.Ledger
                : new Unruled.Core.Economy.LedgerBalance(d.Ledger.StartingCents,
                    creditLimit.Value * Unruled.Core.Economy.Wallet.CentsPerCredit, d.Ledger.InterestFreeTicks,
                    d.Ledger.DailyInterestBasisPoints, d.Ledger.LoanTermTicks,
                    d.Ledger.CreditPerTechTierCents, d.Ledger.OpenGateCreditCents,
                    d.Ledger.CreditPerOutpostCents, d.Ledger.CreditPerRefineryCents);
            var ng = d.NewGame;
            if (rivalCount != null)
            {
                var coreDeps = new ItemStack[ng.CoreBaseDepositCount];
                for (int i = 0; i < coreDeps.Length; i++) coreDeps[i] = ng.CoreBaseDeposit(i);
                var startRefs = new ItemId[ng.StartingRefineryCount];
                for (int i = 0; i < startRefs.Length; i++) startRefs[i] = ng.StartingRefinery(i);
                var coreStock = new ItemStack[ng.CoreBaseStockCount];
                for (int i = 0; i < coreStock.Length; i++) coreStock[i] = ng.CoreBaseStock(i);
                var fleetStock = new ItemStack[ng.FleetStockCount];
                for (int i = 0; i < fleetStock.Length; i++) fleetStock[i] = ng.FleetStock(i);
                ng = new NewGameBalance(
                    rivalCount.Value, ng.DepositsPerSite, ng.SurfaceReserve, ng.DeepCrustReserve, ng.CoreFractureReserve,
                    ng.CoreBaseReserveMultiplier, coreDeps, ng.FleetTechTier, ng.FleetLineShips, ng.FleetEwShips,
                    ng.FleetSupplyTenders, ng.FleetCargoConvoys, new[] { ItemId.RawTungsten, ItemId.RawUranium },
                    DefaultGameBalance.CreateRawsByPlanetClass(),
                    startRefs, coreStock, fleetStock, ng.UninhabitedClustersPerParticipant);
            }
            return new GameBalanceConfig(d.Recipes, d.Universe, d.Starlanes, d.Weather, d.Refinery, d.Mining, d.Lifecycle,
                d.Latency, d.Radar, d.Fleets, d.Combat, d.Relocation, ledger, d.Sta, d.Leaderboard, d.Wto, d.Contracts,
                d.Authority, d.SystemFactions, d.Ai, ng, d.Research, d.Shipyard, d.SiteControl, d.JumpGates,
                d.Convoys, d.Citadel, d.Galaxy);
        }

        public long Tick => Sim.Clock.CurrentTick;

        /// <summary>Advances one tick the way the host does it: tick, then think.</summary>
        public void Step()
        {
            Sim.Clock.Advance(1);
            long tick = Sim.Clock.CurrentTick;
            int tried = Sim.Ai.Think(tick);
            if (tried > 0)
                RecordOrders(tick);
            if (tick % SimTick.TicksPerHour == 0)
                TakeSample();
        }

        public EntityId Hq(int playerId)
        {
            for (int i = 0; i < Sim.Players.Count; i++)
                if (Sim.Players.At(i).PlayerId == playerId)
                    return Sim.Players.EntityAt(i);
            return default;
        }

        public byte PresetOf(int playerId)
        {
            var hq = Hq(playerId);
            return Sim.AiControllers.Has(hq) ? Sim.AiControllers.Get(hq).Archetype : (byte)0;
        }

        /// <summary>Hands a seat to an AI preset, or back to a human (0). Host-side, like GameSession's Auto-SOP.</summary>
        public void SetController(int playerId, byte preset)
        {
            var hq = Hq(playerId);
            if (hq.IsNull) throw new ArgumentException("No such player.");
            if (preset == 0)
                Sim.Ai.Release(hq);
            else
                Sim.Ai.Assign(hq, preset);
            AddLog(Tick, playerId, preset == 0 ? "seat taken by human" : "seat handed to " + Sim.Balance.Ai.Preset(preset).Code);
        }

        public IssueResult Issue(int playerId, CommandType type, EntityId target, int a, int b, int c, out long executeAt)
        {
            var command = new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = type, Target = target, ParamA = a, ParamB = b, ParamC = c
            };
            var result = Sim.Issuer.Issue(command, out executeAt);
            AddLog(Tick, playerId, "YOU " + type + " -> " + result);
            return result;
        }

        private void RecordOrders(long tick)
        {
            for (int i = 0; i < Sim.Ai.OrderCount; i++)
            {
                var order = Sim.Ai.OrderAt(i);
                Orders++;
                if (!ActionCounts.TryGetValue(order.PlayerId, out var counts))
                    ActionCounts[order.PlayerId] = counts = new int[32];
                int slot = (int)order.Action;
                if (slot >= 0 && slot < counts.Length)
                    counts[slot]++;
                if (order.Result == IssueResult.Blackout)
                    BlackedOut[order.PlayerId] = (BlackedOut.TryGetValue(order.PlayerId, out long b) ? b : 0) + 1;
                else if (order.Result != IssueResult.Issued)
                    Refused[order.PlayerId] = (Refused.TryGetValue(order.PlayerId, out long r) ? r : 0) + 1;
                if (order.Action != AiAction.VentCoolant && order.Action != AiAction.Resupply)
                    AddLog(tick, order.PlayerId, order.Action + (order.Result == IssueResult.Issued ? "" : " (" + order.Result + ")"));
            }
        }

        private void AddLog(long tick, int playerId, string text)
        {
            Log.AddFirst(new LogLine { Tick = tick, PlayerId = playerId, Text = text });
            while (Log.Count > MaxLog)
                Log.RemoveLast();
        }

        private void TakeSample()
        {
            long tick = Tick;
            int playerCount = Sim.Players.Count;
            var players = new List<PlayerSample>(playerCount);
            int maxPid = 0;
            for (int i = 0; i < playerCount; i++)
                if (Sim.Players.At(i).PlayerId > maxPid)
                    maxPid = Sim.Players.At(i).PlayerId;
            var byPid = new PlayerSample[maxPid + 1];

            for (int i = 0; i < playerCount; i++)
            {
                var hq = Sim.Players.EntityAt(i);
                int pid = Sim.Players.At(i).PlayerId;
                var p = CreateBaseSample(hq, pid);
                players.Add(p);
                if (pid >= 0 && pid < byPid.Length)
                    byPid[pid] = p;
            }
            for (int i = 0; i < Sim.Fleets.Count; i++)
            {
                ref var fleet = ref Sim.Fleets.At(i);
                int pid = fleet.OwnerPlayerId;
                if (pid <= 0 || pid >= byPid.Length || byPid[pid] == null) continue;
                var p = byPid[pid];
                p.Fleets++;
                p.Ships += fleet.TotalShips;
                p.LineShips += fleet.LineShips;
            }
            for (int i = 0; i < Sim.Sites.Count; i++)
            {
                int pid = Sim.Sites.At(i).OwnerPlayerId;
                if (pid > 0 && pid < byPid.Length && byPid[pid] != null)
                    byPid[pid].Sites++;
            }
            for (int i = 0; i < Sim.JumpGates.Count; i++)
            {
                ref var gate = ref Sim.JumpGates.At(i);
                int pid = gate.OwnerPlayerId;
                if (gate.IsOpen && pid > 0 && pid < byPid.Length && byPid[pid] != null)
                    byPid[pid].GateOpen = true;
            }
            for (int i = 0; i < Sim.Refineries.Count; i++)
            {
                var site = Sim.Refineries.At(i).Site;
                if (!Sim.Sites.Has(site)) continue;
                int pid = Sim.Sites.Get(site).OwnerPlayerId;
                if (pid > 0 && pid < byPid.Length && byPid[pid] != null)
                    byPid[pid].Refineries++;
            }
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                int techSteps = Math.Max(0, p.Tiers[0] - 1) + Math.Max(0, p.Tiers[1] - 1) +
                                Math.Max(0, p.Tiers[2] - 1) + Math.Max(0, p.Tiers[3] - 1);
                int outposts = Math.Max(0, p.Sites - 1);
                p.CreditLimit = Sim.Balance.Ledger.DynamicCreditLimitCents(techSteps, p.GateOpen, outposts, p.Refineries) /
                                Wallet.CentsPerCredit;
            }

            var sample = new Sample { Tick = tick, Players = players };
            if (Sim.Citadels.Count > 0)
            {
                ref var citadel = ref Sim.Citadels.At(0);
                sample.CitadelIntegrity = citadel.Integrity;
                if (citadel.FallenTick != 0 && CitadelFallTick == 0)
                {
                    CitadelFallTick = citadel.FallenTick;
                    AddLog(tick, 0, "THE SYSTEM CITADEL HAS FALLEN");
                }
            }
            if (Sim.VictoryRecords.Count > 0 && VictoryTick == 0 && Sim.VictoryRecords.At(0).DecidedTick != 0)
            {
                VictoryTick = Sim.VictoryRecords.At(0).DecidedTick;
                AddLog(tick, 0, "THE DIRECTORATE FORMED (" + Sim.VictoryRecords.At(0).Seats + " seats)");
            }
            History.Add(sample);
        }

        private PlayerSample CreateBaseSample(EntityId hq, int playerId)
        {
            var p = new PlayerSample { PlayerId = playerId };
            p.Cash = Sim.Wallets.Has(hq) ? Sim.Wallets.Get(hq).Cents / Wallet.CentsPerCredit : 0;
            p.Debt = Sim.Loans.Has(hq) ? Sim.Loans.Get(hq).DebtCents / Wallet.CentsPerCredit : 0;
            if (Sim.TechProgress.Has(hq))
            {
                ref var tech = ref Sim.TechProgress.Get(hq);
                p.Tiers = new int[]
                {
                    tech.Tier(ResearchBranch.Military), tech.Tier(ResearchBranch.Industry),
                    tech.Tier(ResearchBranch.Logistics), tech.Tier(ResearchBranch.Signals)
                };
            }
            else
                p.Tiers = new int[4];
            var core = Sim.Players.Get(hq).CoreBase;
            p.Stock = new int[ItemIds.Count];
            if (!core.IsNull && Sim.Stockpiles.Has(core))
            {
                ref var stock = ref Sim.Stockpiles.Get(core);
                for (int s = 0; s < ItemIds.Count; s++)
                    p.Stock[s] = stock.Get(ItemIds.FromSlot(s));
            }
            p.Preset = Sim.AiControllers.Has(hq) ? Sim.AiControllers.Get(hq).Archetype : (byte)0;
            return p;
        }

        public PlayerSample SamplePlayer(EntityId hq, int playerId)
        {
            var p = CreateBaseSample(hq, playerId);
            for (int i = 0; i < Sim.Fleets.Count; i++)
            {
                ref var fleet = ref Sim.Fleets.At(i);
                if (fleet.OwnerPlayerId != playerId) continue;
                p.Fleets++;
                p.Ships += fleet.TotalShips;
                p.LineShips += fleet.LineShips;
            }
            for (int i = 0; i < Sim.Sites.Count; i++)
                if (Sim.Sites.At(i).OwnerPlayerId == playerId)
                    p.Sites++;
            for (int i = 0; i < Sim.JumpGates.Count; i++)
                if (Sim.JumpGates.At(i).OwnerPlayerId == playerId && Sim.JumpGates.At(i).IsOpen)
                    p.GateOpen = true;
            for (int i = 0; i < Sim.Refineries.Count; i++)
            {
                var site = Sim.Refineries.At(i).Site;
                if (Sim.Sites.Has(site) && Sim.Sites.Get(site).OwnerPlayerId == playerId)
                    p.Refineries++;
            }
            p.CreditLimit = Sim.Lending.CreditLimitCents(hq) / Wallet.CentsPerCredit;
            return p;
        }
    }

    public sealed class Sample
    {
        public long Tick { get; set; }
        public int CitadelIntegrity { get; set; }
        public List<PlayerSample> Players { get; set; }
    }

    public sealed class PlayerSample
    {
        public int PlayerId { get; set; }
        public byte Preset { get; set; }
        public long Cash { get; set; }
        public long Debt { get; set; }
        public long CreditLimit { get; set; }
        public int[] Tiers { get; set; }
        public int Fleets { get; set; }
        public int Ships { get; set; }
        public int LineShips { get; set; }
        public int Sites { get; set; }
        public int Refineries { get; set; }
        public bool GateOpen { get; set; }
        public int[] Stock { get; set; }
    }

    public sealed class LogLine
    {
        public long Tick { get; set; }
        public int PlayerId { get; set; }
        public string Text { get; set; }
    }
}
