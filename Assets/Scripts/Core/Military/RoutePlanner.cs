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
        private int[] _costStamp = Array.Empty<int>();
        private int[] _doneStamp = Array.Empty<int>();
        private EntityId[] _viaLane = Array.Empty<EntityId>();
        private int[] _viaSite = Array.Empty<int>();
        private long[] _heapCost = new long[32];
        private int[] _heapSite = new int[32];
        private int _heapCount;
        private int _stamp;
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

        /// <summary>Fuel a fleet pays to cross a lane, in whole units: ships x fuel per 100 units x length x tier fuel multiplier, rounded up.</summary>
        public int FuelFor(in FleetUnit unit, in StarlaneEdge edge)
        {
            long milli = (long)unit.TotalShips * _fleetBalance.FuelPerShipPer100UnitsMilli * edge.Length *
                         _laneBalance.Get(edge.Tier).FuelMultiplierMilli / (100L * 1000L);
            return (int)((milli + 999) / 1000);
        }

        /// <summary>
        /// Writes the lanes from <paramref name="from"/> to <paramref name="to"/> into <paramref name="route"/>.
        /// False (route cleared) if they are the same site, unreachable at this tech tier, or more than
        /// <see cref="FleetRoute.MaxLanes"/> lanes apart.
        /// </summary>
        public bool TryPlan(EntityId from, EntityId to, byte techTier, ref FleetRoute route) =>
            TryPlan(from, to, techTier, tollFree: false, ref route);

        /// <summary>As <see cref="TryPlan(EntityId, EntityId, byte, ref FleetRoute)"/>; tollFree skips lanes that charge the STA toll.</summary>
        public bool TryPlan(EntityId from, EntityId to, byte techTier, bool tollFree, ref FleetRoute route) =>
            TryPlan(from, to, techTier, tollFree, default, ref route);

        /// <summary>As <see cref="TryPlan(EntityId, EntityId, byte, bool, ref FleetRoute)"/>, never passing through
        /// <paramref name="avoid"/> (unless it is the destination; null avoids nothing).</summary>
        public bool TryPlan(EntityId from, EntityId to, byte techTier, bool tollFree, EntityId avoid, ref FleetRoute route)
        {
            route.Clear();
            int avoided = avoid.IsNull || avoid == to ? -1 : avoid.Index;
            int n = _graph.SiteCapacity;
            if (from == to || from.Index >= n || to.Index >= n)
                return false;
            EnsureCapacity(n);
            if (++_stamp == int.MaxValue)
            {
                Array.Clear(_costStamp, 0, _costStamp.Length);
                Array.Clear(_doneStamp, 0, _doneStamp.Length);
                _stamp = 1;
            }

            _heapCount = 0;
            SetCost(from.Index, 0);
            _viaSite[from.Index] = -1;
            Push(0, from.Index);

            bool reached = false;
            while (_heapCount > 0)
            {
                Pop(out long currentCost, out int current);
                if (_doneStamp[current] == _stamp || currentCost > CostTo(current))
                    continue;
                if (current == to.Index)
                {
                    reached = true;
                    break;
                }
                _doneStamp[current] = _stamp;

                for (int slot = _graph.Start(current); slot < _graph.End(current); slot++)
                {
                    var lane = _graph.EdgeAt(slot);
                    ref var edge = ref _lanes.Get(lane);
                    var rules = _laneBalance.Get(edge.Tier);
                    if (techTier < rules.MinTechTier || (tollFree && rules.ChargesStaToll))
                        continue;
                    int next = _graph.NeighborAt(slot).Index;
                    if (next == avoided || _doneStamp[next] == _stamp)
                        continue;
                    long cost = currentCost + TravelTicks(edge);
                    if (cost >= CostTo(next))
                        continue;
                    SetCost(next, cost);
                    _viaLane[next] = lane;
                    _viaSite[next] = current;
                    Push(cost, next);
                }
            }
            if (!reached)
                return false;

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

        private long CostTo(int siteIndex) =>
            _costStamp[siteIndex] == _stamp ? _cost[siteIndex] : long.MaxValue;

        private void SetCost(int siteIndex, long cost)
        {
            _costStamp[siteIndex] = _stamp;
            _cost[siteIndex] = cost;
        }

        private void Push(long cost, int siteIndex)
        {
            if (_heapCount == _heapCost.Length)
            {
                Array.Resize(ref _heapCost, _heapCount * 2);
                Array.Resize(ref _heapSite, _heapCount * 2);
            }
            int i = _heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Less(cost, siteIndex, _heapCost[parent], _heapSite[parent]))
                    break;
                _heapCost[i] = _heapCost[parent];
                _heapSite[i] = _heapSite[parent];
                i = parent;
            }
            _heapCost[i] = cost;
            _heapSite[i] = siteIndex;
        }

        private void Pop(out long cost, out int siteIndex)
        {
            cost = _heapCost[0];
            siteIndex = _heapSite[0];
            long lastCost = _heapCost[--_heapCount];
            int lastSite = _heapSite[_heapCount];
            int i = 0;
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= _heapCount)
                    break;
                if (child + 1 < _heapCount && Less(_heapCost[child + 1], _heapSite[child + 1], _heapCost[child], _heapSite[child]))
                    child++;
                if (!Less(_heapCost[child], _heapSite[child], lastCost, lastSite))
                    break;
                _heapCost[i] = _heapCost[child];
                _heapSite[i] = _heapSite[child];
                i = child;
            }
            _heapCost[i] = lastCost;
            _heapSite[i] = lastSite;
        }

        private static bool Less(long costA, int siteA, long costB, int siteB) =>
            costA < costB || (costA == costB && siteA < siteB);

        private void EnsureCapacity(int n)
        {
            if (_cost.Length >= n)
                return;
            int cap = Math.Max(n, _cost.Length == 0 ? 32 : _cost.Length * 2);
            Array.Resize(ref _cost, cap);
            Array.Resize(ref _costStamp, cap);
            Array.Resize(ref _doneStamp, cap);
            Array.Resize(ref _viaLane, cap);
            Array.Resize(ref _viaSite, cap);
        }
    }
}
