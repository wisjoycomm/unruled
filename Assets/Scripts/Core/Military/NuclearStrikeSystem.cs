using System;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// <see cref="CommandType.NuclearStrike"/> (GDD §1.2 stage 4, §2.1 B state 3B; data sheet tab 02: a nuclear core fired
    /// in a siege). Target: an own fleet of at least <see cref="LifecycleBalance.StrikeMinFleetTier"/>, docked at a site
    /// that neither the player nor THE SYSTEM owns (its Citadel keeps its own shield rules) and that is not irradiated, carrying
    /// <see cref="LifecycleBalance.StrikeNuclearCores"/> nuclear cores. The cores leave the hold and the site becomes an
    /// irradiated dead world through <see cref="PlanetLifecycleSystem.ApplyNuclearStrike"/> (the IAEA posts the strike).
    /// Anything else changes nothing. PROVISIONAL: the GDD names the weapon but not who may fire it at what, and a strike
    /// is not an offence.
    /// </summary>
    public sealed class NuclearStrikeSystem : ICommandHandler
    {
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly PlanetLifecycleSystem _lifecycle;
        private readonly LifecycleBalance _balance;

        public NuclearStrikeSystem(
            ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions, ComponentStore<SiteInfo> sites,
            ComponentStore<Stockpile> stockpiles, PlanetLifecycleSystem lifecycle, LifecycleBalance balance)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.NuclearStrike)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var fleet = command.Target;
            if (!_units.Has(fleet) || !_motions.Has(fleet) || !_stockpiles.Has(fleet))
                return;
            ref var unit = ref _units.Get(fleet);
            ref var motion = ref _motions.Get(fleet);
            var site = motion.Site;
            if (unit.OwnerPlayerId != command.PlayerId || unit.TechTier < _balance.StrikeMinFleetTier ||
                !motion.Lane.IsNull || !_sites.Has(site))
                return;
            ref var info = ref _sites.Get(site);
            ref var hold = ref _stockpiles.Get(fleet);
            if (info.OwnerPlayerId == command.PlayerId || info.OwnerPlayerId < 0 || info.IsIrradiated(tick) ||
                !hold.Has(ItemId.ItemNuclearCore, _balance.StrikeNuclearCores))
                return;
            hold.Add(ItemId.ItemNuclearCore, -_balance.StrikeNuclearCores);
            _lifecycle.ApplyNuclearStrike(site, tick);
        }
    }
}
