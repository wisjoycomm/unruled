using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Hughes salvo combat (GDD §2.4 C). Owns <see cref="FleetCombatState"/> and ship losses in <see cref="FleetUnit"/>.
    /// Every salvo cycle, fleets of different owners at the same site or on the same lane fight:
    /// <list type="bullet">
    /// <item>Supply tenders first top up friendly holds; a fleet at its owner's site also draws from the site vault.</item>
    /// <item>Psi = sum of line ships x Alpha x ROF (fleets that can act, are not overheated and have ammo).</item>
    /// <item>Omega = (sum of EW ships x Beta) x (1 + fortification at own site) x F_flank (fleets with batteries).
    /// F_flank counts hostile arrival lanes at a site whose directions are at least 90° apart.</item>
    /// <item>Each player fires at the hostile player with the lowest id: damage max(0, Psi - Omega) lands on the focus
    /// class of its lowest fleet, then spills to the other classes. Damage is resolved simultaneously.</item>
    /// <item>Thermal: T += heat x ROF² - passive - vent; above the overheat line guns lock and ammo cooks off a share of
    /// hull per salvo until T drops below the release line.</item>
    /// </list>
    /// Fleets left with no ships are destroyed. SetRof, SetFocus (ParamA) and VentCoolant target an own fleet.
    /// </summary>
    public sealed class SalvoCombatEngine : ISimSystem, ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly CombatBalance _balance;
        private readonly FleetBalance _fleetBalance;

        private long[] _keys = new long[64];
        private EntityId[] _dead = new EntityId[16];
        private int _deadCount;

        // Per engagement, one slot per player present.
        private int _playerCount;
        private int[] _playerIds = new int[4];
        private long[] _psi = new long[4];
        private long[] _omegaBase = new long[4];
        private long[] _damage = new long[4];
        private int[] _target = new int[4];
        private int[] _lead = new int[4];
        private long[] _dirX = new long[4];
        private long[] _dirY = new long[4];

        public SalvoCombatEngine(
            World world, ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions,
            ComponentStore<FleetReadiness> readiness, ComponentStore<FleetCombatState> combat,
            ComponentStore<SiteInfo> sites, ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles,
            CombatBalance balance, FleetBalance fleetBalance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.SalvoCycleTicks != 0)
                return;

            int count = BuildLocationKeys();
            int start = 0;
            while (start < count)
            {
                long location = _keys[start] >> 24;
                int end = start + 1;
                while (end < count && _keys[end] >> 24 == location)
                    end++;
                if (end - start > 1)
                    Engage(start, end, tick);
                start = end;
            }

            for (int i = 0; i < _units.Count; i++)
                UpdateThermal(i, tick);

            for (int i = 0; i < _deadCount; i++)
                _world.Destroy(_dead[i]);
            _deadCount = 0;
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            var fleet = command.Target;
            bool own = _units.Has(fleet) && _units.Get(fleet).OwnerPlayerId == command.PlayerId;
            switch (command.CommandType)
            {
                case CommandType.SetRof:
                    if (own && command.ParamA >= (int)RofStance.Conservation50 && command.ParamA <= (int)RofStance.Overdrive150)
                        _combat.Get(fleet).Rof = (RofStance)command.ParamA;
                    break;
                case CommandType.SetFocus:
                    if (own && command.ParamA >= (int)TargetFocusClass.LineShips && command.ParamA <= (int)TargetFocusClass.CargoConvoys)
                        _combat.Get(fleet).Focus = (TargetFocusClass)command.ParamA;
                    break;
                case CommandType.VentCoolant:
                    if (own)
                        _combat.Get(fleet).IsVenting = true;
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        // Sort key = location (site index x 2 or lane index x 2 + 1) << 24 | dense fleet index.
        private int BuildLocationKeys()
        {
            int count = _units.Count;
            if (_keys.Length < count)
                Array.Resize(ref _keys, Math.Max(count, _keys.Length * 2));
            for (int i = 0; i < count; i++)
            {
                ref var motion = ref _motions.Get(_units.EntityAt(i));
                long location = motion.Lane.IsNull ? motion.Site.Index * 2L : motion.Lane.Index * 2L + 1;
                _keys[i] = (location << 24) | (uint)i;
            }
            Array.Sort(_keys, 0, count);
            return count;
        }

        private static int Dense(long key) => (int)(key & 0xFFFFFF);

        private void Engage(int start, int end, long tick)
        {
            _playerCount = 0;
            for (int k = start; k < end; k++)
                SlotOf(_units.At(Dense(_keys[k])).OwnerPlayerId, Dense(_keys[k]));
            if (_playerCount < 2)
                return;

            var firstMotion = _motions.Get(_units.EntityAt(Dense(_keys[start])));
            var site = firstMotion.Lane.IsNull ? firstMotion.Site : default;

            for (int k = start; k < end; k++)
                Resupply(Dense(_keys[k]), start, end, site);

            for (int p = 0; p < _playerCount; p++)
            {
                _psi[p] = 0;
                _omegaBase[p] = 0;
                _damage[p] = 0;
            }

            for (int k = start; k < end; k++)
            {
                int i = Dense(_keys[k]);
                var fleet = _units.EntityAt(i);
                ref var unit = ref _units.At(i);
                ref var state = ref _combat.Get(fleet);
                int p = SlotOf(unit.OwnerPlayerId, i);
                var tier = _fleetBalance.Tier(unit.TechTier);
                state.EngagedUntilTick = tick + _balance.SalvoCycleTicks;
                if (!_readiness.Get(fleet).CanAct(tick))
                    continue;

                if (!state.IsOverheated && unit.LineShips > 0 && HasSupply(i, ItemId.ItemAmmo, site))
                {
                    int rof = _balance.RofPercent(state.Rof);
                    _psi[p] += (long)unit.LineShips * _balance.LineAlpha * rof;
                    state.FiredAtTick = tick;
                    long ammo = (long)unit.LineShips * _balance.AmmoPerLineShipMilli * tier.BurnMilli(ItemId.ItemAmmo) / 1000 * rof / 100;
                    Consume(i, ItemId.ItemAmmo, ref state.AmmoDebtMilli, ammo, site);
                }
                if (unit.EwShips > 0 && HasSupply(i, ItemId.ItemBattery, site))
                {
                    _omegaBase[p] += (long)unit.EwShips * _balance.EwBeta * 100;
                    long battery = (long)unit.EwShips * _balance.BatteryPerEwShipMilli * tier.BurnMilli(ItemId.ItemBattery) / 1000;
                    Consume(i, ItemId.ItemBattery, ref state.BatteryDebtMilli, battery, site);
                }
            }

            for (int p = 0; p < _playerCount; p++)
            {
                int t = -1;
                for (int q = 0; q < _playerCount; q++)
                    if (q != p && (t < 0 || _playerIds[q] < _playerIds[t]))
                        t = q;
                _target[p] = t;
            }

            for (int p = 0; p < _playerCount; p++)
            {
                int t = _target[p];
                long omega = _omegaBase[t];
                if (!site.IsNull && _sites.Get(site).OwnerPlayerId == _playerIds[t])
                    omega = omega * (100 + _balance.FortificationPdPercent) / 100;
                if (!site.IsNull)
                    omega = omega * _balance.FlankPercent(ExtraLanes(start, end, site, _playerIds[t])) / 100;
                _damage[p] = Math.Max(0, _psi[p] - omega);
            }

            for (int p = 0; p < _playerCount; p++)
            {
                if (_damage[p] == 0)
                    continue;
                var focus = _combat.Get(_units.EntityAt(_lead[p])).Focus;
                ApplyDamage(start, end, _playerIds[_target[p]], _damage[p], focus);
            }
        }

        private int SlotOf(int playerId, int denseIndex)
        {
            for (int p = 0; p < _playerCount; p++)
                if (_playerIds[p] == playerId)
                    return p;
            if (_playerCount == _playerIds.Length)
            {
                int size = _playerCount * 2;
                Array.Resize(ref _playerIds, size);
                Array.Resize(ref _psi, size);
                Array.Resize(ref _omegaBase, size);
                Array.Resize(ref _damage, size);
                Array.Resize(ref _target, size);
                Array.Resize(ref _lead, size);
            }
            _playerIds[_playerCount] = playerId;
            _lead[_playerCount] = denseIndex;
            return _playerCount++;
        }

        // Independent hostile approach lanes beyond the first: arrival directions pairwise at least 90° apart (dot <= 0).
        private int ExtraLanes(int start, int end, EntityId site, int defenderId)
        {
            ref var target = ref _sites.Get(site);
            int lanes = 0;
            for (int k = start; k < end; k++)
            {
                int i = Dense(_keys[k]);
                if (_units.At(i).OwnerPlayerId == defenderId)
                    continue;
                var arrival = _motions.Get(_units.EntityAt(i)).ArrivalLane;
                if (arrival.IsNull || !_lanes.Has(arrival))
                    continue;
                ref var from = ref _sites.Get(_lanes.Get(arrival).OtherEnd(site));
                long dx = target.PosX - from.PosX, dy = target.PosY - from.PosY;

                bool independent = true;
                for (int d = 0; d < lanes && independent; d++)
                    independent = dx * _dirX[d] + dy * _dirY[d] <= 0;
                if (!independent)
                    continue;
                if (lanes == _dirX.Length)
                {
                    Array.Resize(ref _dirX, lanes * 2);
                    Array.Resize(ref _dirY, lanes * 2);
                }
                _dirX[lanes] = dx;
                _dirY[lanes] = dy;
                lanes++;
            }
            return Math.Max(0, lanes - 1);
        }

        private void Resupply(int tenderIndex, int start, int end, EntityId site)
        {
            ref var tenderUnit = ref _units.At(tenderIndex);
            if (tenderUnit.SupplyTenders <= 0)
                return;
            ref var tenderHold = ref _stockpiles.Get(_units.EntityAt(tenderIndex));
            for (int item = 0; item < 3; item++)
            {
                var id = item == 0 ? ItemId.ItemAmmo : item == 1 ? ItemId.ItemBattery : ItemId.ItemCoolant;
                int budget = tenderUnit.SupplyTenders * _balance.TenderTransferPerSalvo;
                for (int k = start; k < end && budget > 0; k++)
                {
                    int i = Dense(_keys[k]);
                    if (i == tenderIndex || _units.At(i).OwnerPlayerId != tenderUnit.OwnerPlayerId)
                        continue;
                    ref var hold = ref _stockpiles.Get(_units.EntityAt(i));
                    int give = Math.Min(Math.Min(budget, tenderHold.Get(id)), Math.Max(0, _balance.ResupplyTarget - hold.Get(id)));
                    if (give <= 0)
                        continue;
                    tenderHold.Add(id, -give);
                    hold.Add(id, give);
                    budget -= give;
                }
            }
        }

        // A fleet at its owner's site draws from the site vault once its hold is empty.
        private bool HasVault(int denseIndex, EntityId site) =>
            !site.IsNull && _sites.Get(site).OwnerPlayerId == _units.At(denseIndex).OwnerPlayerId && _stockpiles.Has(site);

        private bool HasSupply(int denseIndex, ItemId item, EntityId site) =>
            _stockpiles.Get(_units.EntityAt(denseIndex)).Get(item) > 0 ||
            (HasVault(denseIndex, site) && _stockpiles.Get(site).Get(item) > 0);

        private void Consume(int denseIndex, ItemId item, ref int debtMilli, long milli, EntityId site)
        {
            long debt = debtMilli + milli;
            int due = (int)(debt / 1000);
            debtMilli = (int)(debt % 1000);
            ref var hold = ref _stockpiles.Get(_units.EntityAt(denseIndex));
            int fromHold = Math.Min(due, hold.Get(item));
            hold.Add(item, -fromHold);
            due -= fromHold;
            if (due > 0 && HasVault(denseIndex, site))
            {
                ref var vault = ref _stockpiles.Get(site);
                vault.Add(item, -Math.Min(due, vault.Get(item)));
            }
        }

        private void ApplyDamage(int start, int end, int targetPlayerId, long damage, TargetFocusClass focus)
        {
            for (int pass = 0; pass < 4 && damage > 0; pass++)
            {
                int c = pass - 1;
                if (pass > 0 && c >= (int)focus)
                    c++;
                var shipClass = pass == 0 ? focus : (TargetFocusClass)c;
                long hp = _balance.Hp(shipClass) * 100L;

                for (int k = start; k < end && damage > 0; k++)
                {
                    int i = Dense(_keys[k]);
                    ref var unit = ref _units.At(i);
                    if (unit.OwnerPlayerId != targetPlayerId || unit.Count(shipClass) == 0)
                        continue;
                    damage = Hit(i, shipClass, damage, hp);
                }
            }
        }

        // Applies damage plus the fleet's carry to one class; returns the damage left over once that class is gone.
        private long Hit(int denseIndex, TargetFocusClass shipClass, long damage, long hp)
        {
            var fleet = _units.EntityAt(denseIndex);
            ref var unit = ref _units.At(denseIndex);
            ref var state = ref _combat.Get(fleet);
            int ships = unit.Count(shipClass);
            long total = damage + state.DamageCarry;
            int kills = (int)Math.Min(ships, total / hp);
            unit.Lose(shipClass, kills);
            total -= kills * hp;
            state.DamageCarry = 0;

            if (kills > 0 && unit.TotalShips == 0)
                MarkDead(fleet);
            if (kills < ships)
            {
                state.DamageCarry = total;
                return 0;
            }
            return total;
        }

        private void UpdateThermal(int denseIndex, long tick)
        {
            var fleet = _units.EntityAt(denseIndex);
            ref var unit = ref _units.At(denseIndex);
            ref var state = ref _combat.Get(fleet);
            var motion = _motions.Get(fleet);
            var site = motion.Lane.IsNull ? motion.Site : default;

            long temp = state.TemperatureMilli - _balance.PassiveCoolMilli;
            if (state.FiredAtTick == tick)
            {
                long rof = _balance.RofPercent(state.Rof);
                temp += _balance.FireHeatMilli * rof * rof / 10_000;
            }
            if (state.IsVenting)
            {
                if (HasSupply(denseIndex, ItemId.ItemCoolant, site))
                {
                    temp -= _balance.VentCoolMilli;
                    long coolant = (long)unit.TotalShips * _balance.VentCoolantPerShipMilli *
                                   _fleetBalance.Tier(unit.TechTier).BurnMilli(ItemId.ItemCoolant) / 1000;
                    Consume(denseIndex, ItemId.ItemCoolant, ref state.CoolantDebtMilli, coolant, site);
                }
                else
                {
                    state.IsVenting = false;
                }
            }
            state.TemperatureMilli = (int)Math.Max(_balance.AmbientMilli, temp);

            if (state.TemperatureMilli > _balance.OverheatMilli)
                state.IsOverheated = true;
            if (state.TemperatureMilli < _balance.OverheatReleaseMilli)
            {
                state.IsOverheated = false;
                state.IsVenting = false;
            }
            if (!state.IsOverheated || unit.TotalShips == 0)
                return;

            long hull = (long)unit.LineShips * _balance.LineHp + (long)unit.EwShips * _balance.EwHp +
                        (long)unit.SupplyTenders * _balance.SupplyHp + (long)unit.CargoConvoys * _balance.CargoHp;
            long cookOff = hull * 100 * _balance.CookOffHullPercent / 100;
            for (int pass = 0; pass < 4 && cookOff > 0; pass++)
            {
                var shipClass = (TargetFocusClass)pass;
                if (unit.Count(shipClass) > 0)
                    cookOff = Hit(denseIndex, shipClass, cookOff, _balance.Hp(shipClass) * 100L);
            }
        }

        private void MarkDead(EntityId fleet)
        {
            if (_deadCount == _dead.Length)
                Array.Resize(ref _dead, _deadCount * 2);
            _dead[_deadCount++] = fleet;
        }
    }
}
