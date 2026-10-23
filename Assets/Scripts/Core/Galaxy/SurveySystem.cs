using System;
using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// Handles <see cref="CommandType.Survey"/> (Target: an own fleet docked at a site; ParamA/ParamB: the packed
    /// <see cref="StarId"/> split by <see cref="StarId.Split"/>): the fleet burns <see cref="GalaxyBalance.SurveyFuel"/>
    /// to chart a frontier star that neighbours its system on the catalog's hyperlanes. The star is materialised as an
    /// uninhabited system with every raw item, and STA highways of <see cref="GalaxyBalance.SurveyLaneLength"/> join
    /// each materialised neighbour's outermost orbit to its orbit 0. This is how the galaxy grows past the spawn
    /// group. Invalid, already-charted or unaffordable orders change nothing.
    /// </summary>
    public sealed class SurveySystem : ICommandHandler
    {
        private readonly SimulationWorld _sim;
        private readonly GalaxyBalance _balance;
        private readonly List<int> _neighbourClusters = new List<int>();

        public SurveySystem(SimulationWorld sim, GalaxyBalance balance)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.Survey)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var fleet = command.Target;
            if (!_sim.Fleets.Has(fleet) || !_sim.FleetMotions.Has(fleet) || !_sim.Stockpiles.Has(fleet) ||
                _sim.Fleets.Get(fleet).OwnerPlayerId != command.PlayerId)
                return;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            if (!motion.Lane.IsNull || motion.Site.IsNull || !_sim.Sites.Has(motion.Site))
                return;
            if (!StarId.TryFromPacked(StarId.Join(command.ParamA, command.ParamB), out var star))
                return;
            var galaxy = _sim.Galaxy;
            if (!galaxy.CanSurvey(_sim.Sites.Get(motion.Site).ClusterId, star))
                return;
            ref var hold = ref _sim.Stockpiles.Get(fleet);
            if (!hold.Has(ItemId.ItemFuel, _balance.SurveyFuel))
                return;
            hold.Add(ItemId.ItemFuel, -_balance.SurveyFuel);

            galaxy.Catalog.TryGetStar(star, out var record);
            _sim.MaterialiseStar(star, rebuildGraph: false, out var orbits);
            NewGameSetup.StockFrontier(_sim, record.SystemSeed, orbits);

            var entry = orbits[0];
            galaxy.MaterialisedNeighbours(star, _neighbourClusters);
            for (int i = 0; i < _neighbourClusters.Count; i++)
            {
                var outer = galaxy.OuterOrbit(_neighbourClusters[i]);
                if (outer.IsNull)
                    continue;
                var lane = _sim.World.Create();
                _sim.Starlanes.Add(lane, new StarlaneEdge
                {
                    SiteA = outer, SiteB = entry, Tier = StarlaneTier.StaHighway, Length = _balance.SurveyLaneLength
                });
                _sim.LaneWeather.Add(lane, default);
            }
            _sim.Graph.Rebuild(_sim.Starlanes);
        }
    }
}
