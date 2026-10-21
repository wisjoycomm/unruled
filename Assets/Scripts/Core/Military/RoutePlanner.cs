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
        private const int MaxCachedStates = 4096;

        private readonly StarlaneGraph _graph;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly StarlaneBalance _laneBalance;
        private readonly FleetBalance _fleetBalance;

        private readonly SearchState _avoided = new SearchState();
        private readonly SearchState[] _states = new SearchState[MaxCachedStates];
        private int _stateCount;
        private int _evictCursor;
        private int _cacheVersion = -1;
        private int _cacheLaneCount = -1;
        private int _cacheSiteCap = -1;
        private int _cacheGen = 1;
        private int[] _keyStamp = Array.Empty<int>();
        private int[] _keySlot = Array.Empty<int>();
        private readonly EntityId[] _reversed = new EntityId[FleetRoute.MaxLanes];

        public StarlaneGraph Graph => _graph;

        private sealed class SearchState
        {
            public int CacheKey = -1;
            public EntityId From;
            public byte TechTier;
            public int Avoided = -1;
            public int GraphVersion = -1;
            public int LaneCount = -1;
            public int SiteCapacity = -1;

            public long[] Cost = Array.Empty<long>();
            public int[] CostStamp = Array.Empty<int>();
            public int[] DoneStamp = Array.Empty<int>();
            public EntityId[] ViaLane = Array.Empty<EntityId>();
            public int[] ViaSite = Array.Empty<int>();
            public int[] Hops = Array.Empty<int>();
            public bool[] HasTolls = Array.Empty<bool>();
            public long[] HeapCost = new long[32];
            public int[] HeapSite = new int[32];
            public int HeapCount;
            public int Stamp;

            public void EnsureCapacity(int n)
            {
                if (Cost.Length >= n)
                    return;
                int cap = Math.Max(n, Cost.Length == 0 ? 32 : Cost.Length * 2);
                Array.Resize(ref Cost, cap);
                Array.Resize(ref CostStamp, cap);
                Array.Resize(ref DoneStamp, cap);
                Array.Resize(ref ViaLane, cap);
                Array.Resize(ref ViaSite, cap);
                Array.Resize(ref Hops, cap);
                Array.Resize(ref HasTolls, cap);
            }

            public void Begin(EntityId from, byte techTier, int avoided, int graphVersion, int laneCount, int siteCapacity)
            {
                From = from;
                TechTier = techTier;
                Avoided = avoided;
                GraphVersion = graphVersion;
                LaneCount = laneCount;
                SiteCapacity = siteCapacity;
                if (++Stamp == int.MaxValue)
                {
                    Array.Clear(CostStamp, 0, CostStamp.Length);
                    Array.Clear(DoneStamp, 0, DoneStamp.Length);
                    Stamp = 1;
                }
                HeapCount = 0;
                SetCost(from.Index, 0);
                ViaSite[from.Index] = -1;
                Hops[from.Index] = 0;
                HasTolls[from.Index] = false;
                Push(0, from.Index);
            }

            public long CostTo(int siteIndex) =>
                CostStamp[siteIndex] == Stamp ? Cost[siteIndex] : long.MaxValue;

            public void SetCost(int siteIndex, long cost)
            {
                CostStamp[siteIndex] = Stamp;
                Cost[siteIndex] = cost;
            }

            public void Push(long cost, int siteIndex)
            {
                if (HeapCount == HeapCost.Length)
                {
                    Array.Resize(ref HeapCost, HeapCount * 2);
                    Array.Resize(ref HeapSite, HeapCount * 2);
                }
                int i = HeapCount++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(cost, siteIndex, HeapCost[parent], HeapSite[parent]))
                        break;
                    HeapCost[i] = HeapCost[parent];
                    HeapSite[i] = HeapSite[parent];
                    i = parent;
                }
                HeapCost[i] = cost;
                HeapSite[i] = siteIndex;
            }

            public void Pop(out long cost, out int siteIndex)
            {
                cost = HeapCost[0];
                siteIndex = HeapSite[0];
                long lastCost = HeapCost[--HeapCount];
                int lastSite = HeapSite[HeapCount];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= HeapCount)
                        break;
                    if (child + 1 < HeapCount && Less(HeapCost[child + 1], HeapSite[child + 1], HeapCost[child], HeapSite[child]))
                        child++;
                    if (!Less(HeapCost[child], HeapSite[child], lastCost, lastSite))
                        break;
                    HeapCost[i] = HeapCost[child];
                    HeapSite[i] = HeapSite[child];
                    i = child;
                }
                HeapCost[i] = lastCost;
                HeapSite[i] = lastSite;
            }
        }

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
        public bool TryPlan(EntityId from, EntityId to, byte techTier, bool tollFree, EntityId avoid, ref FleetRoute route) =>
            TryPlan(from, to, techTier, tollFree, avoid, FleetRoute.MaxLanes, out _, ref route);

        internal bool TryPlan(EntityId from, EntityId to, byte techTier, bool tollFree, EntityId avoid, int maxLanes,
            out bool hasTolls, ref FleetRoute route)
        {
            route.Clear();
            hasTolls = false;
            int avoided = avoid.IsNull || avoid == to ? -1 : avoid.Index;
            int n = _graph.SiteCapacity;
            if (maxLanes < 1 || from == to || from.Index >= n || to.Index >= n || !_graph.SameComponent(from, to))
                return false;
            var state = AcquireState(from, techTier, tollFree, avoided, n);

            if (state.DoneStamp[to.Index] != state.Stamp)
            {
                while (state.HeapCount > 0)
                {
                    state.Pop(out long currentCost, out int current);
                    if (state.DoneStamp[current] == state.Stamp || currentCost > state.CostTo(current))
                        continue;
                    state.DoneStamp[current] = state.Stamp;
                    int nextHops = state.Hops[current] + 1;
                    bool currentTolls = state.HasTolls[current];

                    for (int slot = _graph.Start(current); slot < _graph.End(current); slot++)
                    {
                        var lane = _graph.EdgeAt(slot);
                        ref var edge = ref _lanes.Get(lane);
                        var rules = _laneBalance.Get(edge.Tier);
                        if (techTier < rules.MinTechTier || (tollFree && rules.ChargesStaToll))
                            continue;
                        int next = _graph.NeighborAt(slot).Index;
                        if (next == avoided || state.DoneStamp[next] == state.Stamp)
                            continue;
                        long cost = currentCost + TravelTicks(edge);
                        if (cost >= state.CostTo(next))
                            continue;
                        state.SetCost(next, cost);
                        state.ViaLane[next] = lane;
                        state.ViaSite[next] = current;
                        state.Hops[next] = nextHops;
                        state.HasTolls[next] = currentTolls || rules.ChargesStaToll;
                        state.Push(cost, next);
                    }
                    if (current == to.Index)
                        break;
                }
            }
            if (state.DoneStamp[to.Index] != state.Stamp)
                return false;
            hasTolls = state.HasTolls[to.Index];
            int hops = state.Hops[to.Index];
            if (hops > FleetRoute.MaxLanes || (!hasTolls && hops > maxLanes))
                return false;

            int count = 0;
            for (int s = to.Index; s != from.Index; s = state.ViaSite[s])
            {
                if (count == FleetRoute.MaxLanes)
                    return false;
                _reversed[count++] = state.ViaLane[s];
            }
            for (int k = 0; k < count; k++)
                route.Append(_reversed[count - 1 - k]);
            return true;
        }

        private SearchState AcquireState(EntityId from, byte techTier, bool tollFree, int avoided, int n)
        {
            if (avoided >= 0)
            {
                _avoided.EnsureCapacity(n);
                _avoided.Begin(from, techTier, avoided, _graph.Version, _lanes.Count, n);
                return _avoided;
            }
            if (_cacheVersion != _graph.Version || _cacheLaneCount != _lanes.Count || _cacheSiteCap != n)
            {
                _cacheVersion = _graph.Version;
                _cacheLaneCount = _lanes.Count;
                _cacheSiteCap = n;
                _stateCount = 0;
                _evictCursor = 0;
                if (++_cacheGen == int.MaxValue)
                {
                    Array.Clear(_keyStamp, 0, _keyStamp.Length);
                    _cacheGen = 1;
                }
            }
            int tiers = FleetBalance.TierCount + 2;
            int tierSlot = techTier < tiers ? techTier : tiers - 1;
            int key = (from.Index * tiers + tierSlot) * 2 + (tollFree ? 1 : 0);
            if (key >= _keyStamp.Length)
            {
                int cap = Math.Max(key + 1, _keyStamp.Length == 0 ? 256 : _keyStamp.Length * 2);
                Array.Resize(ref _keyStamp, cap);
                Array.Resize(ref _keySlot, cap);
            }
            if (_keyStamp[key] == _cacheGen)
            {
                var cached = _states[_keySlot[key]];
                if (cached.From == from && cached.TechTier == techTier)
                    return cached;
            }
            int slot;
            if (_stateCount < MaxCachedStates)
            {
                slot = _stateCount++;
                if (_states[slot] == null)
                    _states[slot] = new SearchState();
            }
            else
            {
                slot = _evictCursor;
                _evictCursor = (_evictCursor + 1) % MaxCachedStates;
                int oldKey = _states[slot].CacheKey;
                if (oldKey >= 0 && oldKey < _keyStamp.Length && _keySlot[oldKey] == slot)
                    _keyStamp[oldKey] = 0;
            }
            var state = _states[slot];
            state.CacheKey = key;
            state.EnsureCapacity(n);
            state.Begin(from, techTier, -1, _graph.Version, _lanes.Count, n);
            _keyStamp[key] = _cacheGen;
            _keySlot[key] = slot;
            return state;
        }

        private static bool Less(long costA, int siteA, long costB, int siteB) =>
            costA < costB || (costA == costB && siteA < siteB);
    }
}
