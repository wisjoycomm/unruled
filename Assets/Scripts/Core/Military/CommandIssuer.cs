using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    public enum IssueResult : byte
    {
        Issued = 0,
        NoHq = 1,
        Unreachable = 2,
        Blackout = 3,
        UnknownTarget = 4,
        Paralysed = 5
    }

    /// <summary>
    /// The door for player and AI orders: stamps <see cref="DelayedPlayerCommand.ExecuteAtTick"/> with the signal
    /// latency to the target's location (a site, a lane, the site/lane a fleet is on, or a deposit's site) and queues
    /// it on the clock.
    /// Refuses every order while the player's command network is paralysed (capital convoy destroyed).
    /// </summary>
    public sealed class CommandIssuer
    {
        private readonly SimClock _clock;
        private readonly SignalLatencySolver _solver;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<FleetMotion> _fleets;
        private readonly ComponentStore<MineralDeposit> _deposits;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<CapitalRelocation> _relocations;

        public CommandIssuer(
            SimClock clock, SignalLatencySolver solver, ComponentStore<SiteInfo> sites,
            ComponentStore<StarlaneEdge> lanes, ComponentStore<FleetMotion> fleets, ComponentStore<MineralDeposit> deposits,
            ComponentStore<PlayerHq> players,
            ComponentStore<CapitalRelocation> relocations)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _solver = solver ?? throw new ArgumentNullException(nameof(solver));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _relocations = relocations ?? throw new ArgumentNullException(nameof(relocations));
        }

        public IssueResult Issue(DelayedPlayerCommand command, out long executeAtTick)
        {
            executeAtTick = 0;
            if (IsParalysed(command.PlayerId))
                return IssueResult.Paralysed;
            var target = command.Target;
            EntityId site = default, lane = default;
            if (_sites.Has(target))
            {
                site = target;
            }
            else if (_lanes.Has(target))
            {
                lane = target;
            }
            else if (_fleets.Has(target))
            {
                ref var fleet = ref _fleets.Get(target);
                site = fleet.Site;
                lane = fleet.Lane;
            }
            else if (_deposits.Has(target))
            {
                site = _deposits.Get(target).Site;
            }

            LatencyStatus status;
            int latency;
            if (!site.IsNull && _sites.Has(site))
                status = _solver.TryComputeToSite(command.PlayerId, site, out latency);
            else if (!lane.IsNull && _lanes.Has(lane))
                status = _solver.TryComputeToLane(command.PlayerId, lane, out latency);
            else
                return IssueResult.UnknownTarget;

            switch (status)
            {
                case LatencyStatus.Ok:
                    break;
                case LatencyStatus.NoHq:
                    return IssueResult.NoHq;
                case LatencyStatus.Unreachable:
                    return IssueResult.Unreachable;
                case LatencyStatus.Blackout:
                    return IssueResult.Blackout;
                default:
                    throw new InvalidOperationException($"Unexpected latency status {status}.");
            }

            command.IssuedAtTick = _clock.CurrentTick;
            command.ExecuteAtTick = _clock.CurrentTick + latency;
            bool wasSynced = _syncedVersion == _clock.Version;
            _clock.Enqueue(command);
            if (wasSynced)
            {
                AppendIndexed(in command);
                _syncedVersion = _clock.Version;
            }
            executeAtTick = command.ExecuteAtTick;
            return IssueResult.Issued;
        }

        private int _syncedVersion = -1;
        private int _indexStamp;
        private int[] _playerStamp = Array.Empty<int>();
        private int[] _firstByPlayer = Array.Empty<int>();
        private DelayedPlayerCommand[] _indexed = Array.Empty<DelayedPlayerCommand>();
        private int[] _nextInPlayer = Array.Empty<int>();
        private int[] _orderInPlayer = Array.Empty<int>();
        private int _indexedCount;

        private int FirstForPlayer(int playerId)
        {
            if (playerId <= 0 || _clock.PendingCommandCount == 0)
                return -1;
            if (_syncedVersion != _clock.Version)
                RebuildIndex();
            return playerId < _playerStamp.Length && _playerStamp[playerId] == _indexStamp ? _firstByPlayer[playerId] : -1;
        }

        private void RebuildIndex()
        {
            if (++_indexStamp == int.MaxValue)
            {
                Array.Clear(_playerStamp, 0, _playerStamp.Length);
                _indexStamp = 1;
            }
            _indexedCount = 0;
            int n = _clock.PendingCommandCount;
            for (int i = 0; i < n; i++)
            {
                var cmd = _clock.PendingAt(i);
                AppendIndexed(in cmd);
            }
            _syncedVersion = _clock.Version;
        }

        private void AppendIndexed(in DelayedPlayerCommand cmd)
        {
            int pid = cmd.PlayerId;
            if (pid <= 0)
                return;
            if (pid >= _playerStamp.Length)
            {
                int cap = Math.Max(pid + 1, _playerStamp.Length == 0 ? 32 : _playerStamp.Length * 2);
                Array.Resize(ref _playerStamp, cap);
                Array.Resize(ref _firstByPlayer, cap);
            }
            if (_playerStamp[pid] != _indexStamp)
            {
                _playerStamp[pid] = _indexStamp;
                _firstByPlayer[pid] = -1;
            }
            if (_indexedCount == _indexed.Length)
            {
                int cap = _indexed.Length == 0 ? 64 : _indexed.Length * 2;
                Array.Resize(ref _indexed, cap);
                Array.Resize(ref _nextInPlayer, cap);
                Array.Resize(ref _orderInPlayer, cap);
            }
            int slot = _indexedCount++;
            _indexed[slot] = cmd;
            _orderInPlayer[slot] = slot;
            _nextInPlayer[slot] = _firstByPlayer[pid];
            _firstByPlayer[pid] = slot;
        }

        /// <summary>True while an order of this type from the player for this target waits out its latency.</summary>
        public bool IsInFlight(int playerId, CommandType type, EntityId target)
        {
            for (int s = FirstForPlayer(playerId); s >= 0; s = _nextInPlayer[s])
            {
                ref var pending = ref _indexed[s];
                if (pending.CommandType == type && pending.Target == target)
                    return true;
            }
            return false;
        }

        /// <summary>True while a Move from the player to this destination site waits out its latency; gives its fleet.</summary>
        public bool IsMoveInFlight(int playerId, EntityId destination, out EntityId fleet)
        {
            fleet = default;
            long bestTick = long.MaxValue;
            int bestOrder = int.MaxValue;
            for (int s = FirstForPlayer(playerId); s >= 0; s = _nextInPlayer[s])
            {
                ref var pending = ref _indexed[s];
                if (pending.CommandType == CommandType.Move && pending.ParamEntity == destination)
                {
                    int ord = _orderInPlayer[s];
                    if (pending.ExecuteAtTick < bestTick || (pending.ExecuteAtTick == bestTick && ord < bestOrder))
                    {
                        bestTick = pending.ExecuteAtTick;
                        bestOrder = ord;
                        fleet = pending.Target;
                    }
                }
            }
            return bestOrder != int.MaxValue;
        }

        /// <summary>True while a command of this type from the player naming this entity (ParamEntity) waits out its latency.</summary>
        public bool IsInFlightTo(int playerId, CommandType type, EntityId paramEntity)
        {
            for (int s = FirstForPlayer(playerId); s >= 0; s = _nextInPlayer[s])
            {
                ref var pending = ref _indexed[s];
                if (pending.CommandType == type && pending.ParamEntity == paramEntity)
                    return true;
            }
            return false;
        }

        private bool IsParalysed(int playerId)
        {
            if (_relocations.Count == 0)
                return false;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players.At(i).PlayerId != playerId)
                    continue;
                var hq = _players.EntityAt(i);
                return _relocations.Has(hq) && _clock.CurrentTick < _relocations.Get(hq).ParalysedUntilTick;
            }
            return false;
        }
    }
}
