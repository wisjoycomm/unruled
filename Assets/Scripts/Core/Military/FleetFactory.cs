using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;

namespace Unruled.Core.Military
{
    /// <summary>The one place a fleet entity is assembled: every fleet component plus an empty cargo hold.</summary>
    public sealed class FleetFactory
    {
        private readonly World _world;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetSensors> _sensors;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly CombatBalance _combatBalance;

        public FleetFactory(
            World world, ComponentStore<SiteInfo> sites, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetSensors> sensors,
            ComponentStore<FleetReadiness> readiness, ComponentStore<FleetCombatState> combat,
            ComponentStore<Stockpile> stockpiles, CombatBalance combatBalance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _combatBalance = combatBalance ?? throw new ArgumentNullException(nameof(combatBalance));
        }

        public EntityId Create(in FleetUnit unit, EntityId site)
        {
            if (unit.TechTier < 1 || unit.TechTier > FleetBalance.TierCount)
                throw new ArgumentOutOfRangeException(nameof(unit), $"Tech tier {unit.TechTier} is not 1..{FleetBalance.TierCount}.");
            if (!_sites.Has(site))
                throw new ArgumentException($"{site} is not a site.", nameof(site));

            var fleet = _world.Create();
            _units.Add(fleet, unit);
            _motions.Add(fleet, new FleetMotion { Site = site });
            _sensors.Add(fleet, default);
            _readiness.Add(fleet, default);
            _combat.Add(fleet, new FleetCombatState
            {
                Rof = RofStance.Standard100, TemperatureMilli = _combatBalance.AmbientMilli
            });
            _stockpiles.Add(fleet, default);
            return fleet;
        }
    }
}
