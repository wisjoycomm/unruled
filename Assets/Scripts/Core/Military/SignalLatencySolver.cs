using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    public enum LatencyStatus : byte
    {
        Ok = 0,
        NoHq = 1,
        Unreachable = 2,
        Blackout = 3
    }

    /// <summary>
    /// Dijkstra relay solver (GDD §2.4 A): latency = (Base + sum of hop weights) x relocation multiplier, measured from
    /// the player's core base. A hop costs <see cref="LatencyBalance.RelayedHopTicks"/> when the site it reaches has the
    /// player's relay station or EW fleet, or the lane it crosses carries the player's EW fleet; otherwise
    /// <see cref="LatencyBalance.HopTicks"/>. A target under an R-storm at or above the blackout level cannot be reached.
    /// Buffers are reused between calls; no allocations once they have grown to the map size.
    /// </summary>
    public sealed class SignalLatencySolver
    {
        private readonly StarlaneGraph _graph;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<SiteWeather> _siteWeather;
        private readonly ComponentStore<LaneWeather> _laneWeather;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<RelayStation> _relays;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly LatencyBalance _balance;

        // Stamp arrays: an entry counts only when it equals the current query's stamp, so nothing is cleared per query.
        private int _stamp;
        private int[] _distStamp = new int[64];
        private long[] _dist = new long[64];
        private int[] _relaySiteStamp = new int[64];
        private int[] _relayLaneStamp = new int[64];
        private long[] _heapDist = new long[64];
        private int[] _heapSlotSite = new int[64];
        private int _heapCount;

        public SignalLatencySolver(
            StarlaneGraph graph, ComponentStore<StarlaneEdge> lanes, ComponentStore<SiteWeather> siteWeather,
            ComponentStore<LaneWeather> laneWeather, ComponentStore<PlayerHq> players,
            ComponentStore<RelayStation> relays, ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions,
            LatencyBalance balance)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _siteWeather = siteWeather ?? throw new ArgumentNullException(nameof(siteWeather));
            _laneWeather = laneWeather ?? throw new ArgumentNullException(nameof(laneWeather));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _relays = relays ?? throw new ArgumentNullException(nameof(relays));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public LatencyStatus TryComputeToSite(int playerId, EntityId site, out int latencyTicks)
        {
            latencyTicks = 0;
            if (_siteWeather.Has(site) && _siteWeather.Get(site).RadioBlackoutLevel >= _balance.BlackoutRadioLevel)
                return LatencyStatus.Blackout;
            if (!Prepare(playerId, out var hq))
                return LatencyStatus.NoHq;

            long hops = DistanceTo(site.Index);
            if (hops == long.MaxValue)
                return LatencyStatus.Unreachable;
            latencyTicks = Total(hops, hq);
            return LatencyStatus.Ok;
        }

        /// <summary>For a target on a lane: the nearer end, plus one hop into the lane (relayed if the player's EW fleet is on it).</summary>
        public LatencyStatus TryComputeToLane(int playerId, EntityId lane, out int latencyTicks)
        {
            latencyTicks = 0;
            if (!_lanes.Has(lane))
                return LatencyStatus.Unreachable;
            if (_laneWeather.Has(lane) && _laneWeather.Get(lane).RadioBlackoutLevel >= _balance.BlackoutRadioLevel)
                return LatencyStatus.Blackout;
            if (!Prepare(playerId, out var hq))
                return LatencyStatus.NoHq;

            ref var edge = ref _lanes.Get(lane);
            long nearest = Math.Min(DistanceTo(edge.SiteA.Index), DistanceTo(edge.SiteB.Index));
            if (nearest == long.MaxValue)
                return LatencyStatus.Unreachable;
            int hop = IsStamped(_relayLaneStamp, lane.Index) ? _balance.RelayedHopTicks : _balance.HopTicks;
            latencyTicks = Total(nearest + hop, hq);
            return LatencyStatus.Ok;
        }

        private int Total(long hops, in PlayerHq hq)
        {
            long total = (_balance.BaseTicks + hops) * (hq.IsCapitalConvoyInTransit ? _balance.RelocationMultiplier : 1);
            return (int)Math.Min(int.MaxValue, total);
        }

        private bool Prepare(int playerId, out PlayerHq hq)
        {
            hq = default;
            bool found = false;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players.At(i).PlayerId != playerId)
                    continue;
                hq = _players.At(i);
                found = true;
                break;
            }
            if (!found || hq.CoreBase.IsNull)
                return false;

            _stamp++;
            int capacity = Math.Max(_graph.SiteCapacity, hq.CoreBase.Index + 1);
            Grow(ref _distStamp, capacity);
            Grow(ref _dist, capacity);
            Grow(ref _relaySiteStamp, capacity);
            Grow(ref _heapDist, _graph.SlotCount + 1);
            Grow(ref _heapSlotSite, _graph.SlotCount + 1);

            for (int i = 0; i < _relays.Count; i++)
            {
                if (_relays.At(i).OwnerPlayerId == playerId)
                    Stamp(ref _relaySiteStamp, _relays.EntityAt(i).Index);
            }
            for (int i = 0; i < _units.Count; i++)
            {
                ref var unit = ref _units.At(i);
                if (unit.OwnerPlayerId != playerId || unit.EwShips <= 0)
                    continue;
                ref var motion = ref _motions.Get(_units.EntityAt(i));
                if (!motion.Site.IsNull)
                    Stamp(ref _relaySiteStamp, motion.Site.Index);
                if (!motion.Lane.IsNull)
                    Stamp(ref _relayLaneStamp, motion.Lane.Index);
            }

            RunDijkstra(hq.CoreBase);
            return true;
        }

        private void RunDijkstra(EntityId source)
        {
            _heapCount = 0;
            SetDistance(source.Index, 0);
            Push(0, source.Index);

            while (_heapCount > 0)
            {
                Pop(out long dist, out int siteIndex);
                if (dist > _dist[siteIndex])
                    continue;
                for (int k = _graph.Start(siteIndex); k < _graph.End(siteIndex); k++)
                {
                    int next = _graph.NeighborAt(k).Index;
                    bool relayed = IsStamped(_relaySiteStamp, next) || IsStamped(_relayLaneStamp, _graph.EdgeAt(k).Index);
                    long candidate = dist + (relayed ? _balance.RelayedHopTicks : _balance.HopTicks);
                    if (candidate < DistanceTo(next))
                    {
                        SetDistance(next, candidate);
                        Push(candidate, next);
                    }
                }
            }
        }

        private long DistanceTo(int siteIndex) =>
            siteIndex < _distStamp.Length && _distStamp[siteIndex] == _stamp ? _dist[siteIndex] : long.MaxValue;

        private void SetDistance(int siteIndex, long value)
        {
            _distStamp[siteIndex] = _stamp;
            _dist[siteIndex] = value;
        }

        private bool IsStamped(int[] stamps, int index) => index < stamps.Length && stamps[index] == _stamp;

        private void Stamp(ref int[] stamps, int index)
        {
            Grow(ref stamps, index + 1);
            stamps[index] = _stamp;
        }

        private static void Grow<T>(ref T[] array, int size)
        {
            if (array.Length >= size)
                return;
            int length = array.Length;
            while (length < size) length *= 2;
            Array.Resize(ref array, length);
        }

        // Binary min-heap ordered by (distance, site index) so ties resolve the same way on every platform.
        private void Push(long dist, int siteIndex)
        {
            if (_heapCount == _heapDist.Length)
            {
                Array.Resize(ref _heapDist, _heapCount * 2);
                Array.Resize(ref _heapSlotSite, _heapCount * 2);
            }
            int i = _heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Less(dist, siteIndex, _heapDist[parent], _heapSlotSite[parent]))
                    break;
                _heapDist[i] = _heapDist[parent];
                _heapSlotSite[i] = _heapSlotSite[parent];
                i = parent;
            }
            _heapDist[i] = dist;
            _heapSlotSite[i] = siteIndex;
        }

        private void Pop(out long dist, out int siteIndex)
        {
            dist = _heapDist[0];
            siteIndex = _heapSlotSite[0];
            long lastDist = _heapDist[--_heapCount];
            int lastSite = _heapSlotSite[_heapCount];
            int i = 0;
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= _heapCount)
                    break;
                if (child + 1 < _heapCount && Less(_heapDist[child + 1], _heapSlotSite[child + 1], _heapDist[child], _heapSlotSite[child]))
                    child++;
                if (!Less(_heapDist[child], _heapSlotSite[child], lastDist, lastSite))
                    break;
                _heapDist[i] = _heapDist[child];
                _heapSlotSite[i] = _heapSlotSite[child];
                i = child;
            }
            _heapDist[i] = lastDist;
            _heapSlotSite[i] = lastSite;
        }

        private static bool Less(long distA, int siteA, long distB, int siteB) =>
            distA < distB || (distA == distB && siteA < siteB);
    }
}
