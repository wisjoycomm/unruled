using System;
using Unruled.Core.Ecs;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Owns <see cref="FleetMotion"/>. Reverse (target: own fleet) flags the fleet as withdrawing. Invalid orders change
    /// nothing.
    /// </summary>
    public sealed class FleetMovementSystem : ISimSystem, ICommandHandler
    {
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;

        public FleetMovementSystem(ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
        }

        public void OnTick(long tick)
        {
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.Reverse:
                    Reverse(command.PlayerId, command.Target);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        private void Reverse(int playerId, EntityId fleet)
        {
            if (!_units.Has(fleet) || _units.Get(fleet).OwnerPlayerId != playerId)
                return;
            _motions.Get(fleet).IsReversing = true;
        }
    }
}
