using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
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
    /// latency to the target's location (a site, a lane, or the site/lane a fleet is on) and queues it on the clock.
    /// Refuses every order while the player's command network is paralysed (capital convoy destroyed).
    /// </summary>
    public sealed class CommandIssuer
    {
        private readonly SimClock _clock;
        private readonly SignalLatencySolver _solver;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<FleetMotion> _fleets;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<CapitalRelocation> _relocations;

        public CommandIssuer(
            SimClock clock, SignalLatencySolver solver, ComponentStore<SiteInfo> sites,
            ComponentStore<StarlaneEdge> lanes, ComponentStore<FleetMotion> fleets, ComponentStore<PlayerHq> players,
            ComponentStore<CapitalRelocation> relocations)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _solver = solver ?? throw new ArgumentNullException(nameof(solver));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
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
            _clock.Enqueue(command);
            executeAtTick = command.ExecuteAtTick;
            return IssueResult.Issued;
        }

        private bool IsParalysed(int playerId)
        {
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
