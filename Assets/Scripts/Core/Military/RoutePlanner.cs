using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Shortest legal route by travel time (Dijkstra over <see cref="StarlaneGraph"/>): a lane costs its length divided
    /// by its speed, in whole ticks rounded up; lanes above the fleet's tech tier are skipped. Ties go to the lower
    /// site index, and neighbours are relaxed in ascending lane order, so the same map always gives the same route.
    /// Buffers grow only when the map grows; planning runs when an order is handled, never in a tick loop.
    /// </summary>
    public sealed class RoutePlanner
    {
        private readonly StarlaneGraph _graph;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly StarlaneBalance _laneBalance;
        private readonly FleetBalance _fleetBalance;

        private long[] _cost = Array.Empty<long>();
        private EntityId[] _viaLane = Array.Empty<EntityId>();
        private int[] _viaSite = Array.Empty<int>();
        private bool[] _done = Array.Empty<bool>();
        private readonly EntityId[] _reversed = new EntityId[FleetRoute.MaxLanes];

        public RoutePlanner(
            StarlaneGraph graph, ComponentStore<StarlaneEdge> lanes, StarlaneBalance laneBalance, FleetBalance fleetBalance)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _laneBalance = laneBalance ?? throw new ArgumentNullException(nameof(laneBalance));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
        }

        /// <summary>Ticks a fleet needs to cross a lane at full speed.</summary>
        public long TravelTicks(in StarlaneEdge edge)
        {
            long speed = (long)_fleetBalance.BaseSpeedMilliPerTick * _laneBalance.Get(edge.Tier).SpeedPercent / 100;
            return (edge.Length * 1000L + speed - 1) / speed;
        }

        /// <summary>
        /// Writes the lanes from <paramref name="from"/> to <paramref name="to"/> into <paramref name="route"/>.
        /// False (route cleared) if they are the same site, unreachable at this tech tier, or more than
        /// <see cref="FleetRoute.MaxLanes"/> lanes apart.
        /// </summary>
        public bool TryPlan(EntityId from, EntityId to, byte techTier, ref FleetRoute route)
        {
            route.Clear();
            int n = _graph.SiteCapacity;
            if (from == to || from.Index >= n || to.Index >= n)
                return false;
            EnsureCapacity(n);
            for (int s = 0; s < n; s++)
            {
                _cost[s] = long.MaxValue;
                _done[s] = false;
                _viaSite[s] = -1;
            }
            _cost[from.Index] = 0;

            while (true)
            {
                int current = -1;
                for (int s = 0; s < n; s++)
                    if (!_done[s] && _cost[s] != long.MaxValue && (current < 0 || _cost[s] < _cost[current]))
                        current = s;
                if (current < 0)
                    return false;
                if (current == to.Index)
                    break;
                _done[current] = true;

                for (int slot = _graph.Start(current); slot < _graph.End(current); slot++)
                {
                    var lane = _graph.EdgeAt(slot);
                    ref var edge = ref _lanes.Get(lane);
                    if (techTier < _laneBalance.Get(edge.Tier).MinTechTier)
                        continue;
                    int next = _graph.NeighborAt(slot).Index;
                    long cost = _cost[current] + TravelTicks(edge);
                    if (_done[next] || cost >= _cost[next])
                        continue;
                    _cost[next] = cost;
                    _viaLane[next] = lane;
                    _viaSite[next] = current;
                }
            }

            int count = 0;
            for (int s = to.Index; s != from.Index; s = _viaSite[s])
            {
                if (count == FleetRoute.MaxLanes)
                    return false;
                _reversed[count++] = _viaLane[s];
            }
            for (int k = 0; k < count; k++)
                route.Append(_reversed[count - 1 - k]);
            return true;
        }

        private void EnsureCapacity(int n)
        {
            if (_cost.Length >= n)
                return;
            _cost = new long[n];
            _viaLane = new EntityId[n];
            _viaSite = new int[n];
            _done = new bool[n];
        }
    }
}
