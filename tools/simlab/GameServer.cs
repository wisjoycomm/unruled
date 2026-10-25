using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Unruled.Core.AI;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Net;
using Unruled.Core.Persistence;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.SimLab
{
    /// <summary>
    /// One persistent world on server time (1 tick = 1 real second at speed 1, day 1, day 2, ...): loads its save on
    /// start, autosaves on an interval and on stop. Which empires are this server's bots, and which of them are held, is
    /// host state the world save doesn't hold: it lives in a roster file next to the save (one line: bots | held | bot
    /// rules).
    /// Players log in and out through <see cref="PresenceSystem"/>; a
    /// bot manager keeps the bots at half the players online (at least <see cref="BotSettings.Min"/>, at most
    /// <see cref="BotSettings.Max"/>), plus the random spawns: it first wakes a held bot, else adds one with
    /// <see cref="NewGameSetup.Join"/>, and retires extras by handing them to a governor (a world never deletes an
    /// empire; Core has no "player leaves" rule). Random spawns
    /// can add a bot at random times (on average every few game hours) on top, up to the cap. Every touch of the world
    /// happens under one lock.
    /// Game clients connect through <see cref="WorldSocket"/>: a hello with a player's login token (issued here, kept
    /// only as a SHA-256 hash in a tokens file next to the save) logs the player in; their orders go through Core's
    /// <see cref="CommandIssuer"/>; after the ticks of each loop pass every connected player gets the change in their
    /// <see cref="PlayerView"/>.
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
        private double _viewCostMs; // all connected players' views, per loop pass that ran ticks
        private double _passTicks; // ticks per loop pass (smoothed): views cost per tick = _viewCostMs / _passTicks
        private readonly Stopwatch _viewWatch = new Stopwatch();
        private double _measuredTps;
        private DateTime _lastSave = DateTime.MinValue;
        private string _lastSaveError;
        private readonly HashSet<int> _online = new HashSet<int>();
        private readonly HashSet<int> _bots = new HashSet<int>();
        private readonly HashSet<int> _held = new HashSet<int>();
        private readonly LinkedList<LogLine> _log = new LinkedList<LogLine>();
        private SplitMix64 _spawnRng;
        private long _nextSpawnTick;
        private int _extra; // random spawns and admin adds/resumes (+1), admin holds (-1), on top of the target
        private readonly HashSet<int> _pinned = new HashSet<int>(); // held by the admin: auto-scale never wakes these
        private readonly Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
        private readonly Dictionary<string, int> _tokenOwners = new Dictionary<string, int>(); // token hash -> player
        private readonly MsgPackWriter _frame = new MsgPackWriter(256);
        private PlayerViewHub _views; // shared encodings for every session's view; a pass begins before each round of views

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
            LoadTokens();
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
            foreach (var session in new List<ClientSession>(_sessions.Values))
                session.Close("new world");
            _sessions.Clear();
            _tokenOwners.Clear();
            if (File.Exists(RosterPath))
                File.Delete(RosterPath);
            if (File.Exists(TokensPath))
                File.Delete(TokensPath);
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
                if (parts.Length > 2)
                {
                    var rules = parts[2].Split(',');
                    Bots.Min = int.Parse(rules[0]);
                    Bots.Max = int.Parse(rules[1]);
                    Bots.AutoScale = rules[2] == "1";
                    Bots.RandomSpawnHours = int.Parse(rules[3]);
                }
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
                            int tried = _sim.Ai.Think(_sim.Clock.CurrentTick);
                            for (int o = 0; o < tried; o++)
                                Remember(_sim.Ai.OrderAt(o));
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
                    if (ran > 0)
                    {
                        _viewWatch.Restart();
                        Views.BeginPass();
                        foreach (var session in _sessions.Values)
                            session.PushView(_sim.Clock.CurrentTick);
                        _viewWatch.Stop();
                        _viewCostMs = _viewCostMs * 0.95 + _viewWatch.Elapsed.TotalMilliseconds * 0.05;
                        _passTicks = _passTicks * 0.95 + ran * 0.05;
                    }
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
                    _extra++;
                    return;
                }
            }
            if (!Bots.AutoScale)
                return;
            int want = Math.Clamp(Math.Max(Bots.Min, (_online.Count + 1) / 2) + _extra, 0, Bots.Max);
            if (active < want)
            {
                if (!WakeHeldLocked("scale up"))
                    AddBotLocked((byte)(_bots.Count % _sim.Balance.Ai.Count + 1), "scale up");
            }
            else if (active > want)
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
                if (!_pinned.Contains(id))
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
                File.WriteAllText(RosterPath, string.Join(",", _bots) + "|" + string.Join(",", _held) + "|" +
                                              $"{Bots.Min},{Bots.Max},{(Bots.AutoScale ? 1 : 0)},{Bots.RandomSpawnHours}");
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
                _extra = 0;
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
                if (_sessions.Remove(playerId, out var session))
                    session.Close("logged out by the admin");
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
                _extra++;
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
                {
                    HoldLocked(playerId, "held by admin");
                    _pinned.Add(playerId);
                    _extra--;
                }
                else if (hold == false && _held.Remove(playerId))
                {
                    _pinned.Remove(playerId);
                    _extra++;
                    _sim.Ai.Assign(hq, _sim.AiControllers.Get(hq).Archetype);
                    Log(playerId, "bot resumed by admin");
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
                if (Bots.RandomSpawnHours == 0 && _extra > 0)
                    _extra = 0;
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
                    viewCostMsPerPass = Math.Round(_viewCostMs, 3),
                    viewUsPerPlayer = _sessions.Count == 0 ? 0 : Math.Round(_viewCostMs * 1000 / _sessions.Count, 1),
                    ticksPerPass = Math.Round(_passTicks, 2),
                    budgetPercent = Math.Round(_tickCostMs * _speed / 10.0, 2),
                    empires = _sim.Players.Count,
                    online = _online.Count,
                    connected = _sessions.Count,
                    clientBytesPerSecond = ClientBytesPerSecond(),
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
                    rules = Bots,
                    presets = Presets()
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
                        home = _sim.Sites.Has(core) ? Code(_sim.Sites.Get(core).ClusterId) : "-",
                        recent = _recent.TryGetValue(id, out var recent) ? new List<string>(recent) : new List<string>(),
                        cash,
                        debt,
                        tech = new int[]
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
                        connected = _sessions.ContainsKey(id),
                        hasToken = _tokenOwners.ContainsValue(id),
                        bytesPerSecond = _sessions.TryGetValue(id, out var link) ? Math.Round(link.BytesPerSecond) : 0,
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
            {
                foreach (var session in _sessions.Values)
                    session.Close("server stopping");
                SaveLocked("saved on stop");
            }
        }

        /// <summary>
        /// Load tests: joins <paramref name="count"/> new players (newcomer protected) and issues each a token. Returns
        /// the tokens; nothing is logged per player.
        /// </summary>
        public List<string> IssueTestTokens(int count)
        {
            lock (_gate)
            {
                var tokens = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    var joined = NewGameSetup.Join(_sim);
                    int playerId = _sim.Players.Get(joined.PlayerHq).PlayerId;
                    _sim.Presence.GrantNewcomerProtection(joined.PlayerHq);
                    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                    _tokenOwners[HashToken(Encoding.ASCII.GetBytes(token))] = playerId;
                    tokens.Add(token);
                }
                SaveTokens();
                Log(0, $"load test: {count} players joined with tokens");
                return tokens;
            }
        }

        /// <summary>
        /// Issues a new login token for a player (a new one with newcomer protection when <paramref name="join"/> and
        /// <paramref name="playerId"/> is 0). The token is shown once; only its hash is kept, and it replaces the player's
        /// older token.
        /// </summary>
        public object IssueToken(int playerId, bool join)
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
                if (Hq(playerId).IsNull)
                    return new { ok = false, error = $"No player {playerId}." };
                if (_bots.Contains(playerId))
                    return new { ok = false, error = $"Player {playerId} is a bot." };
                string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                var stale = new List<string>();
                foreach (var pair in _tokenOwners)
                    if (pair.Value == playerId)
                        stale.Add(pair.Key);
                foreach (var hash in stale)
                    _tokenOwners.Remove(hash);
                _tokenOwners[HashToken(Encoding.ASCII.GetBytes(token))] = playerId;
                SaveTokens();
                Log(playerId, "login token issued");
                return new { ok = true, playerId, token };
            }
        }

        /// <summary>A game client's hello: the session, or null and why it is refused.</summary>
        public ClientSession Connect(in HelloFrame hello, out RefuseReason refusal)
        {
            refusal = WireCodec.CheckVersion(hello);
            if (refusal != RefuseReason.None)
                return null;
            lock (_gate)
            {
                if (hello.Token == null || hello.Token.Length == 0 ||
                    !_tokenOwners.TryGetValue(HashToken(hello.Token), out int playerId) ||
                    _bots.Contains(playerId) || Hq(playerId).IsNull)
                {
                    refusal = RefuseReason.BadToken;
                    Log(0, "game client refused: bad token");
                    return null;
                }
                if (_sessions.Remove(playerId, out var old))
                    old.Close("logged in elsewhere");
                var hq = Hq(playerId);
                if (_sim.AiControllers.Has(hq))
                    _sim.Ai.Release(hq);
                if (_online.Add(playerId))
                    Queue(CommandType.LogIn, playerId);
                var session = new ClientSession(playerId, new PlayerView(Views, playerId));
                _sessions[playerId] = session;
                Views.BeginPass();
                _frame.Rewind(0);
                WireCodec.Write(_frame, new WelcomeFrame
                {
                    ProtocolVersion = WireCodec.ProtocolVersion, PlayerId = playerId, Tick = _sim.Clock.CurrentTick,
                    WorldSeed = _sim.WorldSeed
                });
                session.Push(_frame.ToArray());
                session.PushView(_sim.Clock.CurrentTick);
                Log(playerId, "game client connected, logged in");
                return session;
            }
        }

        /// <summary>The client's connection ended: the player logs out (standing orders keep running; no governor).</summary>
        public void Disconnect(ClientSession session, string reason)
        {
            lock (_gate)
            {
                session.Close(reason);
                if (!_sessions.TryGetValue(session.PlayerId, out var current) || current != session)
                    return;
                _sessions.Remove(session.PlayerId);
                if (_online.Remove(session.PlayerId))
                    Queue(CommandType.LogOut, session.PlayerId);
                Log(session.PlayerId, $"game client left ({reason}), {session.BytesPerSecond / 1024.0:0.0} KB/s over " +
                                      $"{(DateTime.UtcNow - session.Since).TotalMinutes:0} min");
            }
        }

        /// <summary>An order from a game client, for its own player only, through the same door the AI uses.</summary>
        public void Command(ClientSession session, CommandFrame frame)
        {
            lock (_gate)
            {
                if (session.CloseReason != null)
                    return;
                var command = frame.Command;
                command.PlayerId = session.PlayerId;
                IssueResult result;
                long executeAt = 0;
                if (command.CommandType == CommandType.LogIn || command.CommandType == CommandType.LogOut)
                    result = IssueResult.NotAllowed;
                else
                    result = _sim.Issuer.Issue(command, out executeAt);
                _frame.Rewind(0);
                WireCodec.Write(_frame, new CommandResultFrame { Sequence = frame.Sequence, Result = result, ExecuteAtTick = executeAt });
                session.Push(_frame.ToArray());
                if (result == IssueResult.Issued)
                {
                    Views.BeginPass();
                    session.PushView(_sim.Clock.CurrentTick);
                } // the order shows in flight now, even while the world is paused
            }
        }

        /// <summary>The full view of a player as the server holds it, hashed: a client mirror in sync hashes the same.</summary>
        public object ViewHash(int playerId)
        {
            lock (_gate)
            {
                if (Hq(playerId).IsNull)
                    return new { ok = false, error = $"No player {playerId}." };
                var body = new PlayerView(_sim, playerId).Write(full: true);
                return new { ok = true, tick = _sim.Clock.CurrentTick, hash = Fnv(body), bytes = body.Length };
            }
        }

        public static string Fnv(ReadOnlySpan<byte> bytes)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        private double ClientBytesPerSecond()
        {
            double total = 0;
            foreach (var session in _sessions.Values)
                total += session.BytesPerSecond;
            return Math.Round(_sessions.Count == 0 ? 0 : total / _sessions.Count);
        }

        private static string HashToken(byte[] token) => Convert.ToHexString(SHA256.HashData(token));

        private string TokensPath => _savePath + ".tokens";

        // The hub belongs to the world: a new world (reset) gets a new one.
        private PlayerViewHub Views
        {
            get
            {
                if (_views == null || _views.Sim != _sim)
                    _views = new PlayerViewHub(_sim);
                return _views;
            }
        }

        // One line per token: "<player id> <sha-256 of the token>". The tokens themselves are never stored.
        private void LoadTokens()
        {
            _tokenOwners.Clear();
            if (!File.Exists(TokensPath))
                return;
            foreach (var line in File.ReadAllLines(TokensPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                    _tokenOwners[parts[1]] = int.Parse(parts[0]);
            }
        }

        private void SaveTokens()
        {
            var lines = new List<string>();
            foreach (var pair in _tokenOwners)
                lines.Add(pair.Value + " " + pair.Key);
            File.WriteAllLines(TokensPath, lines);
        }

        // The last few orders each AI-run empire tried, for the dashboard.
        private readonly Dictionary<int, LinkedList<string>> _recent = new Dictionary<int, LinkedList<string>>();

        private void Remember(in AiOrder order)
        {
            if (order.Action == AiAction.VentCoolant || order.Action == AiAction.Resupply)
                return;
            if (!_recent.TryGetValue(order.PlayerId, out var lines))
                _recent[order.PlayerId] = lines = new LinkedList<string>();
            lines.AddFirst($"{Day(_sim.Clock.CurrentTick)}d {Clock(_sim.Clock.CurrentTick)} {order.Action}" +
                           (order.Result == IssueResult.Issued ? "" : $" ({order.Result})"));
            while (lines.Count > 8)
                lines.RemoveLast();
        }

        private string Code(int clusterId)
        {
            if (!_sim.Galaxy.TryGetStar(clusterId, out var star))
                return "C" + clusterId;
            return $"UNR-{star.SectorX}.{star.SectorY}-{star.Index}";
        }

        private List<object> Presets()
        {
            var list = new List<object>();
            for (byte i = 1; i <= _sim.Balance.Ai.Count; i++)
                list.Add(new { id = i, code = _sim.Balance.Ai.Preset(i).Code });
            return list;
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
