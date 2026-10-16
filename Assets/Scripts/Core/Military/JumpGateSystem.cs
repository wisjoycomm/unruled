using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns <see cref="JumpGate"/> and the lanes it opens. <see cref="CommandType.OpenJumpGate"/> (target: the gate's
    /// home site) works for the gate's owner once the jump-gate tech tier is reached: it charges the open cost from the
    /// player's core base, adds an STA highway of the gate length (with lane weather) to the contested cluster and
    /// rebuilds the lane graph. Invalid or unaffordable orders change nothing; an open gate never closes.
    /// </summary>
    public sealed class JumpGateSystem : ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<JumpGate> _gates;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<LaneWeather> _laneWeather;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly StarlaneGraph _graph;
        private readonly ITechGates _tech;
        private readonly JumpGateBalance _balance;

        public JumpGateSystem(
            World world, ComponentStore<JumpGate> gates, ComponentStore<StarlaneEdge> lanes,
            ComponentStore<LaneWeather> laneWeather, ComponentStore<PlayerHq> players, ComponentStore<SiteInfo> sites,
            ComponentStore<Stockpile> stockpiles, StarlaneGraph graph, ITechGates tech, JumpGateBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _gates = gates ?? throw new ArgumentNullException(nameof(gates));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _laneWeather = laneWeather ?? throw new ArgumentNullException(nameof(laneWeather));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _tech = tech ?? throw new ArgumentNullException(nameof(tech));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.OpenJumpGate)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (!_tech.Unlocks(command.PlayerId, TechGate.JumpGate))
                return;
            for (int i = 0; i < _gates.Count; i++)
            {
                ref var gate = ref _gates.At(i);
                if (gate.OwnerPlayerId != command.PlayerId || gate.HomeSite != command.Target)
                    continue;
                if (gate.IsOpen || !_sites.Has(gate.HomeSite) || !_sites.Has(gate.FarSite) ||
                    !TryCharge(command.PlayerId))
                    return;
                gate.Lane = _world.Create();
                gate.OpenedAtTick = tick;
                _lanes.Add(gate.Lane, new StarlaneEdge
                {
                    SiteA = gate.HomeSite, SiteB = gate.FarSite, Tier = StarlaneTier.StaHighway,
                    Length = _balance.LaneLength
                });
                _laneWeather.Add(gate.Lane, default);
                _graph.Rebuild(_lanes);
                return;
            }
        }

        private bool TryCharge(int playerId)
        {
            EntityId core = default;
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).PlayerId == playerId)
                    core = _players.At(i).CoreBase;
            if (core.IsNull || !_stockpiles.Has(core) || !_sites.Has(core) || _sites.Get(core).OwnerPlayerId != playerId)
                return false;
            ref var stock = ref _stockpiles.Get(core);
            for (int i = 0; i < _balance.OpenCostCount; i++)
                if (!stock.Has(_balance.OpenCost(i).Item, _balance.OpenCost(i).Quantity))
                    return false;
            for (int i = 0; i < _balance.OpenCostCount; i++)
                stock.Add(_balance.OpenCost(i).Item, -_balance.OpenCost(i).Quantity);
            return true;
        }
    }
}
