using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Three-phase core-base relocation (GDD §2.4 A, §4.2, data sheet tab 04). Owns <see cref="CapitalRelocation"/> and
    /// <see cref="PlayerHq"/>. RelocateBase (target: a site the player owns) needs: no relocation running, no cooldown,
    /// no hostile fleet at the core base or on a lane touching it, an own escort with line ships at the core base, and
    /// both phase costs, paid at once from the core-base stockpile.
    /// 1. Teardown (seeded within the sheet's range): the old base keeps only part of its fortification bonus.
    /// 2. Convoy (seeded): a capital convoy fleet waits at the old base and every order's latency is multiplied. If it
    ///    is destroyed the relocation is aborted and orders are refused for the paralysis time.
    /// 3. Bootstrap: then the core base (the latency centre) moves to the destination and the cooldown starts.
    /// Invalid orders change nothing.
    /// </summary>
    public sealed class CapitalRelocationSystem : ISimSystem, ICommandHandler
    {
        private const ulong RollSalt = 0x52454C4F43415445UL; // "RELOCATE"

        private readonly World _world;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<CapitalRelocation> _relocations;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly FleetFactory _fleets;
        private readonly RelocationBalance _balance;
        private readonly ulong _worldSeed;

        public CapitalRelocationSystem(
            World world, ComponentStore<PlayerHq> players, ComponentStore<CapitalRelocation> relocations,
            ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions, ComponentStore<SiteInfo> sites,
            ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles, FleetFactory fleets,
            RelocationBalance balance, ulong worldSeed)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _relocations = relocations ?? throw new ArgumentNullException(nameof(relocations));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _worldSeed = worldSeed;
        }

        public void OnTick(long tick)
        {
            for (int i = 0; i < _relocations.Count; i++)
            {
                ref var relocation = ref _relocations.At(i);
                if (relocation.Phase == RelocationPhase.None)
                    continue;
                ref var hq = ref _players.Get(_relocations.EntityAt(i));

                if (relocation.Phase == RelocationPhase.Convoy && !_world.IsAlive(relocation.Convoy))
                {
                    relocation.Phase = RelocationPhase.None;
                    relocation.Convoy = default;
                    relocation.ParalysedUntilTick = tick + _balance.ParalysisTicks;
                    hq.IsCapitalConvoyInTransit = false;
                    continue;
                }
                if (tick < relocation.PhaseEndTick)
                    continue;

                switch (relocation.Phase)
                {
                    case RelocationPhase.Teardown:
                        relocation.Phase = RelocationPhase.Convoy;
                        relocation.Convoy = _fleets.Create(
                            new FleetUnit { OwnerPlayerId = hq.PlayerId, TechTier = 1, CargoConvoys = 1 }, relocation.OldBase);
                        relocation.PhaseEndTick = tick + Roll(hq.PlayerId, tick, _balance.ConvoyMinTicks, _balance.ConvoyMaxTicks);
                        hq.IsCapitalConvoyInTransit = true;
                        break;
                    case RelocationPhase.Convoy:
                        _world.Destroy(relocation.Convoy);
                        relocation.Convoy = default;
                        relocation.Phase = RelocationPhase.Bootstrap;
                        relocation.PhaseEndTick = tick + _balance.BootstrapTicks;
                        hq.IsCapitalConvoyInTransit = false;
                        break;
                    case RelocationPhase.Bootstrap:
                        hq.CoreBase = relocation.Destination;
                        relocation.Phase = RelocationPhase.None;
                        relocation.CooldownUntilTick = tick + _balance.CooldownTicks;
                        break;
                }
            }
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.RelocateBase)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));

            int playerId = command.PlayerId;
            var destination = command.Target;
            int index = FindHq(playerId);
            if (index < 0)
                return;
            var hqEntity = _players.EntityAt(index);
            var coreBase = _players.At(index).CoreBase;

            if (_relocations.Has(hqEntity))
            {
                ref var current = ref _relocations.Get(hqEntity);
                if (current.Phase != RelocationPhase.None || tick < current.CooldownUntilTick)
                    return;
            }
            if (!_sites.Has(destination) || destination == coreBase || _sites.Get(destination).OwnerPlayerId != playerId)
                return;
            if (!_stockpiles.Has(coreBase) || IsUnderHostileLock(playerId, coreBase) || !HasEscort(playerId, coreBase))
                return;

            ref var stock = ref _stockpiles.Get(coreBase);
            if (!CanPay(ref stock, _balance.TeardownCost, _balance.BootstrapCost))
                return;
            Pay(ref stock, _balance.TeardownCost);
            Pay(ref stock, _balance.BootstrapCost);

            if (!_relocations.Has(hqEntity))
                _relocations.Add(hqEntity, default);
            ref var relocation = ref _relocations.Get(hqEntity);
            relocation.Phase = RelocationPhase.Teardown;
            relocation.OldBase = coreBase;
            relocation.Destination = destination;
            relocation.PhaseEndTick = tick + Roll(playerId, tick, _balance.TeardownMinTicks, _balance.TeardownMaxTicks);
        }

        /// <summary>§4.2: a hostile fleet at the core base, or on a lane touching it, locks the relocate button.</summary>
        public bool IsUnderHostileLock(int playerId, EntityId coreBase)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units.At(i).OwnerPlayerId == playerId)
                    continue;
                ref var motion = ref _motions.Get(_units.EntityAt(i));
                if (motion.Site == coreBase)
                    return true;
                if (!motion.Lane.IsNull)
                {
                    ref var edge = ref _lanes.Get(motion.Lane);
                    if (edge.SiteA == coreBase || edge.SiteB == coreBase)
                        return true;
                }
            }
            return false;
        }

        private bool HasEscort(int playerId, EntityId coreBase)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ref var unit = ref _units.At(i);
                if (unit.OwnerPlayerId == playerId && unit.LineShips > 0 && _motions.Get(_units.EntityAt(i)).Site == coreBase)
                    return true;
            }
            return false;
        }

        private int FindHq(int playerId)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).PlayerId == playerId)
                    return i;
            return -1;
        }

        private int Roll(int playerId, long tick, int min, int max)
        {
            var rng = new SplitMix64(_worldSeed ^ RollSalt ^ ((ulong)(uint)playerId << 32) ^ (ulong)tick);
            return rng.NextInt(min, max);
        }

        private static int Units(in ItemAmount amount) => (amount.Milli + 999) / 1000;

        private static bool CanPay(ref Stockpile stock, ItemAmount[] first, ItemAmount[] second)
        {
            for (int i = 0; i < ItemIds.Count; i++)
            {
                var item = ItemIds.FromSlot(i);
                int due = 0;
                for (int k = 0; k < first.Length; k++)
                    if (first[k].Item == item)
                        due += Units(first[k]);
                for (int k = 0; k < second.Length; k++)
                    if (second[k].Item == item)
                        due += Units(second[k]);
                if (!stock.Has(item, due))
                    return false;
            }
            return true;
        }

        private static void Pay(ref Stockpile stock, ItemAmount[] cost)
        {
            for (int k = 0; k < cost.Length; k++)
                stock.Add(cost[k].Item, -Units(cost[k]));
        }
    }
}
