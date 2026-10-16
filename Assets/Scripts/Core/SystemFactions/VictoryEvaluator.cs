using System;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.SystemFactions
{
    /// <summary>ECS component on a fallen Citadel: the game's outcome.</summary>
    public struct VictoryRecord
    {
        public long DecidedTick;
        public int Seats; // players seated on The Directorate
    }

    /// <summary>ECS component on a player HQ: the player sits on The Directorate.</summary>
    public struct DirectorateSeat
    {
        public long SinceTick;
    }

    /// <summary>
    /// Owns <see cref="VictoryRecord"/> and <see cref="DirectorateSeat"/>. When a Citadel falls, every player who
    /// bombarded it within the Directorate window before the fall takes a seat on The Directorate (GDD §1): the
    /// alliance that brought it down wins the game.
    /// </summary>
    public sealed class VictoryEvaluator : ISimSystem
    {
        private readonly ComponentStore<SystemCitadel> _citadels;
        private readonly ComponentStore<CitadelAssault> _assaults;
        private readonly ComponentStore<VictoryRecord> _records;
        private readonly ComponentStore<DirectorateSeat> _seats;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly CitadelBalance _balance;

        public VictoryEvaluator(
            ComponentStore<SystemCitadel> citadels, ComponentStore<CitadelAssault> assaults,
            ComponentStore<VictoryRecord> records, ComponentStore<DirectorateSeat> seats, ComponentStore<PlayerHq> players,
            CitadelBalance balance)
        {
            _citadels = citadels ?? throw new ArgumentNullException(nameof(citadels));
            _assaults = assaults ?? throw new ArgumentNullException(nameof(assaults));
            _records = records ?? throw new ArgumentNullException(nameof(records));
            _seats = seats ?? throw new ArgumentNullException(nameof(seats));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        /// <summary>True once a Citadel has fallen and The Directorate is seated.</summary>
        public bool IsDecided(out long tick)
        {
            tick = 0;
            if (_records.Count == 0)
                return false;
            tick = _records.At(0).DecidedTick;
            return true;
        }

        public bool HasSeat(int playerId)
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                var hq = _seats.EntityAt(i);
                if (_players.Has(hq) && _players.Get(hq).PlayerId == playerId)
                    return true;
            }
            return false;
        }

        public void OnTick(long tick)
        {
            for (int c = 0; c < _citadels.Count; c++)
            {
                ref var citadel = ref _citadels.At(c);
                var site = _citadels.EntityAt(c);
                if (citadel.FallenTick == 0 || _records.Has(site))
                    continue;
                int seats = 0;
                for (int a = 0; a < _assaults.Count; a++)
                {
                    var hq = _assaults.EntityAt(a);
                    if (_assaults.At(a).LastHitTick < citadel.FallenTick - _balance.DirectorateWindowTicks || _seats.Has(hq))
                        continue;
                    _seats.Add(hq, new DirectorateSeat { SinceTick = tick });
                    seats++;
                }
                _records.Add(site, new VictoryRecord { DecidedTick = tick, Seats = seats });
            }
        }
    }
}
