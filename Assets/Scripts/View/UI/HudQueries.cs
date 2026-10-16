using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.View.UI
{
    /// <summary>Read-only lookups the HUD controllers share. They scan small stores; call on selection or bind.</summary>
    public static class HudQueries
    {
        public static bool TryFindHq(SimulationWorld sim, int playerId, out EntityId hq)
        {
            for (int i = 0; i < sim.Players.Count; i++)
            {
                if (sim.Players.At(i).PlayerId != playerId)
                    continue;
                hq = sim.Players.EntityAt(i);
                return true;
            }
            hq = default;
            return false;
        }

        /// <summary>The player's first fleet in store order, or a null id.</summary>
        public static EntityId FirstFleet(SimulationWorld sim, int playerId)
        {
            for (int i = 0; i < sim.Fleets.Count; i++)
                if (sim.Fleets.At(i).OwnerPlayerId == playerId)
                    return sim.Fleets.EntityAt(i);
            return default;
        }

        /// <summary>Position of a fleet among its owner's fleets (by entity index), for its A/B/C name.</summary>
        public static int FleetOrdinal(SimulationWorld sim, EntityId fleet)
        {
            if (!sim.Fleets.Has(fleet))
                return 0;
            int owner = sim.Fleets.Get(fleet).OwnerPlayerId;
            int ordinal = 0;
            for (int i = 0; i < sim.Fleets.Count; i++)
            {
                var other = sim.Fleets.EntityAt(i);
                if (sim.Fleets.At(i).OwnerPlayerId == owner && other.Index < fleet.Index)
                    ordinal++;
            }
            return ordinal;
        }
    }
}
