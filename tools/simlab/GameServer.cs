using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Unruled.Core.AI;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>
    /// One persistent world on server time (1 tick = 1 real second at speed 1, day 1, day 2, ...): loads its save on
    /// start, autosaves on an interval and on stop. Which empires are this server's bots, and which of them are held, is
    /// host state the world save doesn't hold: it lives in a roster file next to the save (one line: bots, then held).
    /// Players log in and out through <see cref="PresenceSystem"/>; a
    /// bot manager keeps the bots at half the players online (at least <see cref="BotSettings.Min"/>, at most
    /// <see cref="BotSettings.Max"/>), plus the random spawns: it first wakes a held bot, else adds one with
    /// <see cref="NewGameSetup.Join"/>, and retires extras by handing them to a governor (a world never deletes an
    /// empire; Core has no "player leaves" rule). Random spawns
    /// can add a bot at random times (on average every few game hours) on top, up to the cap. Every touch of the world
    /// happens under one lock.
    /// </summary>
    public sealed class GameServer
    {
        private readonly object _gate = new object();
        private readonly string _savePath;
        private SimulationWorld _sim;
        private bool _running = true;
        private int _speed = 1; // sim ticks per real second
        private double _tickCostMs;
        private double _aiCostMs;
        private double _measuredTps;
        private DateTime _lastSave = DateTime.MinValue;
        private string _lastSaveError;
        private readonly HashSet<int> _online = new HashSet<int>();
        private readonly HashSet<int> _bots = new HashSet<int>();
        private readonly HashSet<int> _held = new HashSet<int>();
        private readonly LinkedList<LogLine> _log = new LinkedList<LogLine>();
        private SplitMix64 _spawnRng;
        private long _nextSpawnTick;
        private int _spawned; // random spawns: kept on top of what the players online call for

        public readonly BotSettings Bots = new BotSettings();
        public int AutosaveMinutes = 5;

        public GameServer(string savePath, ulong seed)
        {
            _savePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath)));
            if (File.Exists(savePath))
            {
                _sim = SaveGame.Load(DefaultGameBalance.Create(), File.ReadAllBytes(savePath));
                Log(0, $"world loaded: day {Day(_sim.Clock.CurrentTick)}, {_sim.Players.Count} empires");
            }
            else
                NewWorld(seed);
            AdoptExistingBots();
            LogOutStaleSessions();
            _spawnRng = new SplitMix64(_sim.WorldSeed ^ 0x5EEDB075UL);
        }

        // Sessions don't survive a restart: anyone the loaded world still has online logs out now, so their shield
        // rises as it would have.
        private void LogOutStaleSessions()
        {
            for (int i = 0; i < _sim.Players.Count; i++)
            {
                var hq = _sim.Players.EntityAt(i);
                if (_sim.Presences.Has(hq) && _sim.Presences.Get(hq).IsOnline)
                    Queue(CommandType.LogOut, _sim.Players.At(i).PlayerId);
            }
        }

        private void NewWorld(ulong seed)
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), seed);
            var first = NewGameSetup.Create(_sim, 1);
            _sim.Ai.Assign(first.PlayerHq, 2);
            _online.Clear();
            _held.Clear();
            if (File.Exists(RosterPath))
                File.Delete(RosterPath);
            Log(0, $"new world, seed {seed}");
        }

        // The roster file names the bots and the held ones; without one (a new world) every AI-run empire is a bot.
        private void AdoptExistingBots()
        {
            _bots.Clear();
            _held.Clear();
            string roster = RosterPath;
            if (File.Exists(roster))
            {
                var parts = File.ReadAllText(roster).Split('|');
                Fill(_bots, parts[0]);
                if (parts.Length > 1)
                    Fill(_held, parts[1]);
                return;
            }
            for (int i = 0; i < _sim.AiControllers.Count; i++)
                _bots.Add(_sim.Players.Get(_sim.AiControllers.EntityAt(i)).PlayerId);
        }

        private string RosterPath => _savePath + ".bots";

        private static void Fill(HashSet<int> set, string csv)
        {
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                set.Add(int.Parse(part.Trim()));
        }

        public void Loop()
        {
            var clock = Stopwatch.StartNew();
            double owed = 0;
            long last = clock.ElapsedTicks;
            long rateTicks = 0;
            var rateClock = Stopwatch.StartNew();
            var tickWatch = new Stopwatch();
            var aiWatch = new Stopwatch();
            while (true)
            {
                long now = clock.ElapsedTicks;
                double seconds = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                int ran = 0;
                lock (_gate)
                {
                    if (_running)
                    {
                        owed = Math.Min(owed + seconds * _speed, _speed);
                        int toRun = (int)Math.Min(owed, 2_000);
                        owed -= toRun;
                        for (int i = 0; i < toRun; i++)
                        {
                            tickWatch.Restart();
                            _sim.Clock.Advance(1);
                            aiWatch.Restart();
                            _sim.Ai.Think(_sim.Clock.CurrentTick);
                            aiWatch.Stop();
                            tickWatch.Stop();
                            _tickCostMs = _tickCostMs * 0.99 + tickWatch.Elapsed.TotalMilliseconds * 0.01;
                            _aiCostMs = _aiCostMs * 0.99 + aiWatch.Elapsed.TotalMilliseconds * 0.01;
                            ManageBots();
                            ran++;
                        }
                    }
                    else
                        owed = 0;
                    if ((DateTime.UtcNow - _lastSave).TotalMinutes >= AutosaveMinutes)
                        SaveLocked("autosave");
                }
                rateTicks += ran;
                if (rateClock.ElapsedMilliseconds >= 1_000)
                {
                    _measuredTps = rateTicks * 1_000.0 / rateClock.ElapsedMilliseconds;
                    rateTicks = 0;
                    rateClock.Restart();
                }
                Thread.Sleep(ran == 0 ? 20 : 5);
            }
        }

        // Bots at half the players online (rounded up), within the limits; random spawns on top, up to the cap.
        private void ManageBots()
        {
            long tick = _sim.Clock.CurrentTick;
            if (tick % 60 != 0)
                return;
            int active = _bots.Count - _held.Count;
            if (Bots.RandomSpawnHours > 0 && tick >= _nextSpawnTick)
            {
                // the next spawn lands somewhere in the following window, so bots don't arrive like clockwork
                long window = (long)Bots.RandomSpawnHours * SimTick.TicksPerHour;
                _nextSpawnTick = tick + window / 2 + _spawnRng.NextInt(0, (int)Math.Min(window, int.MaxValue));
                if (active < Bots.Max)
                {
                    AddBotLocked((byte)_spawnRng.NextInt(1, _sim.Balance.Ai.Count), "random spawn");
                    _spawned++;
                    return;
                }
            }
            if (!Bots.AutoScale)
                return;
            int want = Math.Clamp((_online.Count + 1) / 2 + _spawned, Bots.Min, Bots.Max);
            if (active < want)
            {
                if (!WakeHeldLocked("scale up"))
                    AddBotLocked((byte)(_bots.Count % _sim.Balance.Ai.Count + 1), "scale up");
            }
            else if (active > want && active > Bots.Min)
                for (int i = _sim.AiControllers.Count - 1; i >= 0; i--)
                {
                    int id = _sim.Players.Get(_sim.AiControllers.EntityAt(i)).PlayerId;
                    if (!_bots.Contains(id) || _held.Contains(id))
                        continue;
                    HoldLocked(id, "scale down");
                    return;
                }
        }

        // A held bot (lowest id) goes back to its full AI; false when none is held.
        private bool WakeHeldLocked(string why)
        {
            int best = int.MaxValue;
            foreach (int id in _held)
                best = Math.Min(best, id);
            if (best == int.MaxValue)
                return false;
            var hq = Hq(best);
            _held.Remove(best);
            _sim.Ai.Assign(hq, _sim.AiControllers.Get(hq).Archetype);
            Log(best, $"bot resumed ({why})");
            return true;
        }

        private int AddBotLocked(byte preset, string why)
        {
            var joined = NewGameSetup.Join(_sim, preset);
            int id = _sim.Players.Get(joined.PlayerHq).PlayerId;
            _bots.Add(id);
            Log(id, $"bot joined ({_sim.Balance.Ai.Preset(preset).Code}, {why})");
            return id;
        }

        private void HoldLocked(int id, string why)
        {
            var hq = Hq(id);
            byte preset = _sim.AiControllers.Has(hq) ? _sim.AiControllers.Get(hq).Archetype : (byte)1;
            _sim.Ai.Assign(hq, preset, governor: true);
            _held.Add(id);
            Log(id, $"bot held: a governor keeps its empire ({why})");
        }

        private void SaveLocked(string why)
        {
            try
            {
                string temp = _savePath + ".tmp";
                File.WriteAllBytes(temp, SaveGame.Write(_sim));
                File.Move(temp, _savePath, overwrite: true);
                File.WriteAllText(RosterPath, string.Join(",", _bots) + "|" + string.Join(",", _held));
                _lastSave = DateTime.UtcNow;
                _lastSaveError = null;
                Log(0, why + $": day {Day(_sim.Clock.CurrentTick)}");
            }
            catch (IOException e)
            {
                _lastSave = DateTime.UtcNow;
                _lastSaveError = e.Message;
                Log(0, why + " failed: " + e.Message);
            }
        }

        public object Save()
        {
            lock (_gate)
            {
                SaveLocked("saved");
                return new { ok = _lastSaveError == null, error = _lastSaveError };
            }
        }

        public object SetRun(bool? running, int? speed)
        {
            lock (_gate)
            {
                if (running.HasValue)
                {
                    _running = running.Value;
                    Log(0, _running ? "server resumed" : "server paused");
                }
                if (speed.HasValue)
                {
                    _speed = Math.Clamp(speed.Value, 1, 3_600);
                    Log(0, $"speed x{_speed}");
                }
                return new { running = _running, speed = _speed };
            }
        }

        public object Reset(ulong seed, bool confirm)
        {
            if (!confirm)
                return new { ok = false, error = "New world wipes the running one. Send confirm: true." };
            lock (_gate)
            {
                NewWorld(seed);
                AdoptExistingBots();
                _spawnRng = new SplitMix64(_sim.WorldSeed ^ 0x5EEDB075UL);
                _nextSpawnTick = 0;
                _spawned = 0;
                SaveLocked("new world saved");
                return new { ok = true };
            }
        }

        public object LogIn(int playerId, bool join)
        {
            lock (_gate)
            {
                if (join && playerId == 0)
                {
                    var joined = NewGameSetup.Join(_sim);
                    playerId = _sim.Players.Get(joined.PlayerHq).PlayerId;
                    _sim.Presence.GrantNewcomerProtection(joined.PlayerHq);
                    Log(playerId, "new player joined (protected until the tutorial ends)");
                }
                var hq = Hq(playerId);
                if (hq.IsNull)
                    return new { ok = false, error = $"No player {playerId}." };
                if (_bots.Contains(playerId))
                    return new { ok = false, error = $"Player {playerId} is a bot." };
                if (_sim.AiControllers.Has(hq))
                    _sim.Ai.Release(hq);
                Queue(CommandType.LogIn, playerId);
                _online.Add(playerId);
                Log(playerId, "logged in");
                return new { ok = true, playerId };
            }
        }

        public object LogOut(int playerId, bool governor)
        {
            lock (_gate)
            {
                var hq = Hq(playerId);
                if (hq.IsNull || !_online.Remove(playerId))
                    return new { ok = false, error = $"Player {playerId} is not online." };
                Queue(CommandType.LogOut, playerId);
                if (governor)
                    _sim.Ai.Assign(hq, 1, governor: true);
                Log(playerId, governor ? "logged out, governor on" : "logged out");
                return new { ok = true };
            }
        }

        public object EndTutorial(int playerId)
        {
            lock (_gate)
            {
                if (Hq(playerId).IsNull)
                    return new { ok = false, error = $"No player {playerId}." };
                Queue(CommandType.EndProtection, playerId);
                Log(playerId, "newcomer protection ended");
                return new { ok = true };
            }
        }

        public object AddBot(byte preset)
        {
            lock (_gate)
            {
                if (_bots.Count - _held.Count >= Bots.Max)
                    return new { ok = false, error = $"Already {Bots.Max} active bots (the cap)." };
                if (!_sim.Balance.Ai.IsValid(preset))
                    return new { ok = false, error = "Unknown preset." };
                return new { ok = true, playerId = AddBotLocked(preset, "added by admin") };
            }
        }

        public object SetBot(int playerId, byte? preset, bool? hold)
        {
            lock (_gate)
            {
                var hq = Hq(playerId);
                if (hq.IsNull || !_bots.Contains(playerId))
                    return new { ok = false, error = $"Player {playerId} is not a bot." };
                if (preset.HasValue)
                {
                    if (!_sim.Balance.Ai.IsValid(preset.Value))
                        return new { ok = false, error = "Unknown preset." };
                    _sim.Ai.Assign(hq, preset.Value, _held.Contains(playerId));
                    Log(playerId, "preset set to " + _sim.Balance.Ai.Preset(preset.Value).Code);
                }
                if (hold == true && !_held.Contains(playerId))
                    HoldLocked(playerId, "held by admin");
                else if (hold == false && _held.Remove(playerId))
                {
                    _sim.Ai.Assign(hq, _sim.AiControllers.Get(hq).Archetype);
                    Log(playerId, "bot resumed");
                }
                return new { ok = true };
            }
        }

        public object SetBotRules(BotSettings rules)
        {
            lock (_gate)
            {
                Bots.Min = Math.Clamp(rules.Min, 0, 100);
                Bots.Max = Math.Clamp(rules.Max, Bots.Min, 100);
                Bots.AutoScale = rules.AutoScale;
                Bots.RandomSpawnHours = Math.Clamp(rules.RandomSpawnHours, 0, 24 * 7);
                if (Bots.RandomSpawnHours == 0)
                    _spawned = 0;
                Log(0, $"bot rules: {Bots.Min}..{Bots.Max}, auto {(Bots.AutoScale ? "on" : "off")}, random spawn " +
                       (Bots.RandomSpawnHours > 0 ? $"every {Bots.RandomSpawnHours} h" : "off"));
                return Bots;
            }
        }

        // The host's own orders: no signal latency, they land on the next tick.
        private void Queue(CommandType type, int playerId)
        {
            long tick = _sim.Clock.CurrentTick;
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                IssuedAtTick = tick, ExecuteAtTick = tick + 1, PlayerId = playerId, CommandType = type,
                Target = _sim.Players.Get(Hq(playerId)).CoreBase
            });
        }

        public object Status()
        {
            lock (_gate)
            {
                long tick = _sim.Clock.CurrentTick;
                int shielded = 0, newcomers = 0, governed = 0;
                for (int i = 0; i < _sim.Players.Count; i++)
                {
                    var hq = _sim.Players.EntityAt(i);
                    if (_sim.Presence.IsShieldUp(hq, tick)) shielded++;
                    if (_sim.Presences.Has(hq) && _sim.Presences.Get(hq).IsNewcomer) newcomers++;
                    if (_sim.AiControllers.Has(hq) && _sim.AiControllers.Get(hq).IsGovernor) governed++;
                }
                return new
                {
                    seed = _sim.WorldSeed,
                    tick,
                    day = Day(tick),
                    clock = Clock(tick),
                    running = _running,
                    speed = _speed,
                    tps = Math.Round(_measuredTps, 1),
                    tickCostMs = Math.Round(_tickCostMs, 3),
                    aiCostMs = Math.Round(_aiCostMs, 3),
                    budgetPercent = Math.Round(_tickCostMs * _speed / 10.0, 2),
                    empires = _sim.Players.Count,
                    online = _online.Count,
                    bots = _bots.Count - _held.Count,
                    heldBots = _held.Count,
                    shielded,
                    newcomers,
                    governed,
                    hubs = _sim.Citadels.Count,
                    systems = _sim.StarSystems.Count,
                    memoryMb = GC.GetTotalMemory(false) / (1024 * 1024),
                    autosaveAgeSeconds = _lastSave == DateTime.MinValue ? -1 : (long)(DateTime.UtcNow - _lastSave).TotalSeconds,
                    autosaveMinutes = AutosaveMinutes,
                    lastSaveError = _lastSaveError,
                    rules = Bots
                };
            }
        }

        public object Players()
        {
            lock (_gate)
            {
                long tick = _sim.Clock.CurrentTick;
                int n = _sim.Players.Count;
                var ships = new Dictionary<int, int>();
                var fleets = new Dictionary<int, int>();
                var sites = new Dictionary<int, int>();
                for (int i = 0; i < _sim.Fleets.Count; i++)
                {
                    int owner = _sim.Fleets.At(i).OwnerPlayerId;
                    ships[owner] = (ships.TryGetValue(owner, out int s) ? s : 0) + _sim.Fleets.At(i).TotalShips;
                    fleets[owner] = (fleets.TryGetValue(owner, out int f) ? f : 0) + 1;
                }
                for (int i = 0; i < _sim.Sites.Count; i++)
                {
                    int owner = _sim.Sites.At(i).OwnerPlayerId;
                    if (owner > 0)
                        sites[owner] = (sites.TryGetValue(owner, out int s) ? s : 0) + 1;
                }
                var list = new List<object>(n);
                for (int i = 0; i < n; i++)
                {
                    var hq = _sim.Players.EntityAt(i);
                    ref var player = ref _sim.Players.At(i);
                    int id = player.PlayerId;
                    var tech = _sim.TechProgress.Has(hq) ? _sim.TechProgress.Get(hq) : default;
                    var presence = _sim.Presences.Has(hq) ? _sim.Presences.Get(hq) : default;
                    bool ai = _sim.AiControllers.Has(hq);
                    byte preset = ai ? _sim.AiControllers.Get(hq).Archetype : (byte)0;
                    var core = player.CoreBase;
                    int fuel = 0, oxygen = 0, rations = 0;
                    if (_sim.Stockpiles.Has(core))
                    {
                        ref var stock = ref _sim.Stockpiles.Get(core);
                        fuel = stock.Get(ItemId.ItemFuel);
                        oxygen = stock.Get(ItemId.ItemOxygen);
                        rations = stock.Get(ItemId.ItemRations);
                    }
                    long cash = _sim.Wallets.Has(hq) ? _sim.Wallets.Get(hq).Cents / Wallet.CentsPerCredit : 0;
                    long debt = _sim.Loans.Has(hq) ? _sim.Loans.Get(hq).DebtCents / Wallet.CentsPerCredit : 0;
                    int shipCount = ships.TryGetValue(id, out int sc) ? sc : 0;
                    var flags = new List<string>();
                    if (shipCount == 0) flags.Add("no fleet");
                    if (cash <= 0 && debt > 0) flags.Add("broke");
                    if (fuel == 0 || oxygen == 0 || rations == 0) flags.Add("starving");
                    string kind = _bots.Contains(id) ? (_held.Contains(id) ? "held" : "bot")
                        : _online.Contains(id) ? "online" : ai ? "governed" : "offline";
                    list.Add(new
                    {
                        id,
                        kind,
                        preset = preset == 0 ? null : _sim.Balance.Ai.Preset(preset).Code,
                        home = _sim.Sites.Has(core) ? _sim.Sites.Get(core).ClusterId : -1,
                        cash,
                        debt,
                        tech = new[]
                        {
                            tech.Tier(ResearchBranch.Military), tech.Tier(ResearchBranch.Industry),
                            tech.Tier(ResearchBranch.Logistics), tech.Tier(ResearchBranch.Signals)
                        },
                        fleets = fleets.TryGetValue(id, out int fc) ? fc : 0,
                        ships = shipCount,
                        sites = sites.TryGetValue(id, out int st) ? st : 0,
                        fuel,
                        oxygen,
                        rations,
                        shield = _sim.Presence.IsShieldUp(hq, tick),
                        shieldEndsIn = _sim.Presence.IsShieldUp(hq, tick) ? presence.ShieldUntilTick - tick : 0,
                        newcomer = presence.IsNewcomer,
                        weekShieldHours = Math.Round(presence.WeekShieldTicks / (double)SimTick.TicksPerHour, 1),
                        flags
                    });
                }
                return list;
            }
        }

        public object LogSince(long tick)
        {
            lock (_gate)
            {
                var lines = new List<LogLine>();
                foreach (var line in _log)
                {
                    if (line.Tick < tick || lines.Count >= 200)
                        break;
                    lines.Add(line);
                }
                return lines;
            }
        }

        public void Stop()
        {
            lock (_gate)
                SaveLocked("saved on stop");
        }

        private EntityId Hq(int playerId)
        {
            for (int i = 0; i < _sim.Players.Count; i++)
                if (_sim.Players.At(i).PlayerId == playerId)
                    return _sim.Players.EntityAt(i);
            return default;
        }

        private void Log(int playerId, string text)
        {
            _log.AddFirst(new LogLine { Tick = _sim.Clock.CurrentTick, PlayerId = playerId, Text = text });
            while (_log.Count > 1_000)
                _log.RemoveLast();
        }

        private static long Day(long tick) => tick / SimTick.TicksPerDay + 1;

        private static string Clock(long tick) =>
            $"{tick % SimTick.TicksPerDay / SimTick.TicksPerHour:00}:{tick % SimTick.TicksPerHour / SimTick.TicksPerMinute:00}";
    }

    /// <summary>How the server keeps its bots: within [Min, Max] at half the players online, plus random spawns.</summary>
    public sealed class BotSettings
    {
        public int Min { get; set; } = 5;
        public int Max { get; set; } = 100;
        public bool AutoScale { get; set; } = true;
        public int RandomSpawnHours { get; set; }
    }
}
