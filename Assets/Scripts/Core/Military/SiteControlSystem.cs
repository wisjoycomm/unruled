using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns changes to <see cref="SiteInfo.OwnerPlayerId"/> during play. <see cref="CommandType.ClaimSite"/> (target: a
    /// site) raises the player's outpost there when one of the player's fleets is docked at the site (not mothballed,
    /// not in combat, with ships) and carries the claim cost in its hold, which it unloads:
    /// <list type="bullet">
    /// <item>a neutral site is claimed outright;</item>
    /// <item>a rival player's site is taken only while none of the owner's fleets is docked there, and never a core
    /// base (core bases move only through relocation);</item>
    /// <item>THE SYSTEM's sites cannot be claimed.</item>
    /// </list>
    /// The site's stock, deposits and refineries pass to the new owner. Invalid or unaffordable orders change nothing.
    /// </summary>
    public sealed class SiteControlSystem : ICommandHandler
    {
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly SiteControlBalance _balance;

        public SiteControlSystem(
            ComponentStore<SiteInfo> sites, ComponentStore<PlayerHq> players, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetReadiness> readiness,
            ComponentStore<FleetCombatState> combat, ComponentStore<Stockpile> stockpiles, SiteControlBalance balance)
        {
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.ClaimSite)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            var site = command.Target;
            if (command.PlayerId <= 0 || !_sites.Has(site))
                return;
            int owner = _sites.Get(site).OwnerPlayerId;
            if (owner < 0 || owner == command.PlayerId)
                return;
            if (owner > 0 && (IsCoreBase(site) || HasDockedFleet(owner, site)))
                return;

            for (int i = 0; i < _units.Count; i++)
            {
                var fleet = _units.EntityAt(i);
                if (_units.At(i).OwnerPlayerId != command.PlayerId || !IsReadyAt(fleet, site, tick) ||
                    !_stockpiles.Has(fleet) || !CanPay(ref _stockpiles.Get(fleet)))
                    continue;
                ref var hold = ref _stockpiles.Get(fleet);
                for (int c = 0; c < _balance.ClaimCostCount; c++)
                    hold.Add(_balance.ClaimCost(c).Item, -_balance.ClaimCost(c).Quantity);
                _sites.Get(site).OwnerPlayerId = command.PlayerId;
                return;
            }
        }

        private bool CanPay(ref Stockpile hold)
        {
            for (int c = 0; c < _balance.ClaimCostCount; c++)
                if (!hold.Has(_balance.ClaimCost(c).Item, _balance.ClaimCost(c).Quantity))
                    return false;
            return true;
        }

        private bool HasDockedFleet(int playerId, EntityId site)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                var fleet = _units.EntityAt(i);
                if (_units.At(i).OwnerPlayerId != playerId || _units.At(i).TotalShips == 0 || !_motions.Has(fleet))
                    continue;
                ref var motion = ref _motions.Get(fleet);
                if (motion.Lane.IsNull && motion.Site == site)
                    return true;
            }
            return false;
        }

        private bool IsReadyAt(EntityId fleet, EntityId site, long tick) =>
            _units.Get(fleet).TotalShips > 0 && _motions.Has(fleet) && _motions.Get(fleet).Lane.IsNull &&
            _motions.Get(fleet).Site == site &&
            (!_combat.Has(fleet) || _combat.Get(fleet).EngagedUntilTick <= tick) &&
            (!_readiness.Has(fleet) || !_readiness.Get(fleet).IsMothballed);

        private bool IsCoreBase(EntityId site)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).CoreBase == site)
                    return true;
            return false;
        }
    }
}
