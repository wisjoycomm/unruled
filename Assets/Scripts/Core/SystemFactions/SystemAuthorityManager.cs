using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.Core.SystemFactions
{
    /// <summary>ECS component on a player HQ: THE SYSTEM's record of the player's offences and the sanctions in force.</summary>
    public struct SystemSanction
    {
        public int Offences; // within the offence window
        public long LastOffenceTick;
        public long WtoBarredUntilTick;
        public bool DefencePending;
        public EntityId DefenceFleet;
        public long DefenceUntilTick;
    }

    /// <summary>Call-to-Arms numbers. PROVISIONAL: the GDD names the two layers but no thresholds or forces.</summary>
    public sealed class SystemAuthorityBalance
    {
        public readonly int OffenceWindowTicks;
        public readonly int LayerOneOffences;
        public readonly int WtoBarTicks;
        public readonly int LayerTwoOffences;
        public readonly byte DefenceTier;
        public readonly int DefenceLineShips;
        public readonly int DefenceAmmo;
        public readonly int DefenceTicks;
        public readonly int BulletinTicks;

        public SystemAuthorityBalance(
            int offenceWindowTicks, int layerOneOffences, int wtoBarTicks, int layerTwoOffences, byte defenceTier,
            int defenceLineShips, int defenceAmmo, int defenceTicks, int bulletinTicks)
        {
            if (offenceWindowTicks < 1) throw new ArgumentOutOfRangeException(nameof(offenceWindowTicks));
            if (layerOneOffences < 1) throw new ArgumentOutOfRangeException(nameof(layerOneOffences));
            if (wtoBarTicks < 1) throw new ArgumentOutOfRangeException(nameof(wtoBarTicks));
            if (layerTwoOffences <= layerOneOffences) throw new ArgumentOutOfRangeException(nameof(layerTwoOffences));
            if (defenceTier < 1 || defenceTier > FleetBalance.TierCount) throw new ArgumentOutOfRangeException(nameof(defenceTier));
            if (defenceLineShips < 1) throw new ArgumentOutOfRangeException(nameof(defenceLineShips));
            if (defenceAmmo < 0) throw new ArgumentOutOfRangeException(nameof(defenceAmmo));
            if (defenceTicks < 1) throw new ArgumentOutOfRangeException(nameof(defenceTicks));
            if (bulletinTicks < 1) throw new ArgumentOutOfRangeException(nameof(bulletinTicks));
            OffenceWindowTicks = offenceWindowTicks;
            LayerOneOffences = layerOneOffences;
            WtoBarTicks = wtoBarTicks;
            LayerTwoOffences = layerTwoOffences;
            DefenceTier = defenceTier;
            DefenceLineShips = defenceLineShips;
            DefenceAmmo = defenceAmmo;
            DefenceTicks = defenceTicks;
            BulletinTicks = bulletinTicks;
        }
    }

    /// <summary>
    /// Owns <see cref="SystemSanction"/> and <see cref="SystemBulletin"/>: THE SYSTEM's Call-to-Arms and public
    /// bulletins.
    /// <list type="bullet">
    /// <item>Offences (WTO contract piracy, reported by combat) count within the offence window. Layer one: the WTO
    /// bars the offender from placing orders for a while. Layer two: a WTO defence fleet forms at the offender's core
    /// base for a while and fights whoever is there; a Call-to-Arms bulletin names the offender.</item>
    /// <item>IAEA: every newly irradiated site gets a nuclear strike bulletin until its irradiation ends.</item>
    /// <item>STA: a system renamed by its owner is announced for a bulletin's time.</item>
    /// </list>
    /// Defence fleets are THE SYSTEM's: nothing can be looted from them.
    /// </summary>
    public sealed class SystemAuthorityManager : ISimSystem, IOffenceLog, IMarketAccess, IResearchIncidentLog, ISystemRenameLog
    {
        private readonly World _world;
        private readonly ComponentStore<SystemSanction> _sanctions;
        private readonly ComponentStore<SystemBulletin> _bulletins;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly Ledger _ledger;
        private readonly FleetFactory _fleets;
        private readonly FleetBalance _fleetBalance;
        private readonly SystemAuthorityBalance _balance;

        private EntityId[] _expired = new EntityId[8];

        public SystemAuthorityManager(
            World world, ComponentStore<SystemSanction> sanctions, ComponentStore<SystemBulletin> bulletins,
            ComponentStore<SiteInfo> sites, ComponentStore<PlayerHq> players, ComponentStore<Stockpile> stockpiles,
            Ledger ledger, FleetFactory fleets, FleetBalance fleetBalance, SystemAuthorityBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sanctions = sanctions ?? throw new ArgumentNullException(nameof(sanctions));
            _bulletins = bulletins ?? throw new ArgumentNullException(nameof(bulletins));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void ReportPiracy(int offenderPlayerId, long tick)
        {
            if (!_ledger.TryFindPlayerWallet(offenderPlayerId, out var hq))
                return;
            if (!_sanctions.Has(hq))
                _sanctions.Add(hq, default);
            ref var s = ref _sanctions.Get(hq);
            if (tick - s.LastOffenceTick > _balance.OffenceWindowTicks)
                s.Offences = 0;
            s.Offences++;
            s.LastOffenceTick = tick;
            if (s.Offences >= _balance.LayerOneOffences)
                s.WtoBarredUntilTick = tick + _balance.WtoBarTicks;
            if (s.Offences >= _balance.LayerTwoOffences && !_world.IsAlive(s.DefenceFleet))
                s.DefencePending = true;
        }

        /// <summary>NOAA publishes a failed stress test's blast at the player's site (GDD §2.3).</summary>
        public void ReportPrototypeBlast(int playerId, EntityId site, long tick) =>
            _bulletins.Add(_world.Create(), new SystemBulletin
            {
                Agency = SystemAgency.Noaa, Kind = BulletinKind.PrototypeBlast, PlayerId = playerId, Site = site,
                Tick = tick, ExpiresTick = tick + _balance.BulletinTicks
            });

        /// <summary>The STA announces a system's new name.</summary>
        public void ReportRename(int playerId, EntityId system, long tick) =>
            _bulletins.Add(_world.Create(), new SystemBulletin
            {
                Agency = SystemAgency.Sta, Kind = BulletinKind.SystemRenamed, PlayerId = playerId, Site = system,
                Tick = tick, ExpiresTick = tick + _balance.BulletinTicks
            });

        public bool MayTrade(int playerId, long tick) =>
            !_ledger.TryFindPlayerWallet(playerId, out var hq) || !_sanctions.Has(hq) ||
            tick >= _sanctions.Get(hq).WtoBarredUntilTick;

        public void OnTick(long tick)
        {
            ExpireBulletins(tick);

            for (int i = 0; i < _sites.Count; i++)
            {
                ref var site = ref _sites.At(i);
                if (site.IsIrradiated(tick) && !HasStrikeBulletin(_sites.EntityAt(i), site.IrradiatedUntilTick))
                    _bulletins.Add(_world.Create(), new SystemBulletin
                    {
                        Agency = SystemAgency.Iaea, Kind = BulletinKind.NuclearStrike, Site = _sites.EntityAt(i),
                        Tick = tick, ExpiresTick = site.IrradiatedUntilTick
                    });
            }

            for (int i = 0; i < _sanctions.Count; i++)
            {
                ref var s = ref _sanctions.At(i);
                var hq = _sanctions.EntityAt(i);
                if (s.DefencePending)
                {
                    s.DefencePending = false;
                    ref var player = ref _players.Get(hq);
                    if (_sites.Has(player.CoreBase))
                    {
                        s.DefenceFleet = SpawnDefence(player.CoreBase);
                        s.DefenceUntilTick = tick + _balance.DefenceTicks;
                        _bulletins.Add(_world.Create(), new SystemBulletin
                        {
                            Agency = SystemAgency.Wto, Kind = BulletinKind.CallToArms, PlayerId = player.PlayerId,
                            Site = player.CoreBase, Tick = tick, ExpiresTick = tick + _balance.BulletinTicks
                        });
                    }
                }
                else if (!s.DefenceFleet.IsNull && (!_world.IsAlive(s.DefenceFleet) || tick >= s.DefenceUntilTick))
                {
                    if (_world.IsAlive(s.DefenceFleet))
                        _world.Destroy(s.DefenceFleet);
                    s.DefenceFleet = default;
                }
            }
        }

        private EntityId SpawnDefence(EntityId site)
        {
            var unit = new FleetUnit
            {
                OwnerPlayerId = SystemAgencies.OwnerId(SystemAgency.Wto), TechTier = _balance.DefenceTier,
                LineShips = _balance.DefenceLineShips
            };
            var fleet = _fleets.Create(unit, site);
            ref var hold = ref _stockpiles.Get(fleet);
            hold.Add(ItemId.ItemAmmo, _balance.DefenceAmmo);
            int intervals = _balance.DefenceTicks / _fleetBalance.UpkeepIntervalTicks + 1;
            var upkeep = _fleetBalance.Tier(unit.TechTier).UpkeepPerShipHour;
            for (int k = 0; k < upkeep.Length; k++)
                hold.Add(upkeep[k].Item, intervals * FleetUpkeepSystem.Due(unit.TotalShips, upkeep[k].Milli, 0));
            return fleet;
        }

        private bool HasStrikeBulletin(EntityId site, long until)
        {
            for (int i = 0; i < _bulletins.Count; i++)
            {
                ref var b = ref _bulletins.At(i);
                if (b.Kind == BulletinKind.NuclearStrike && b.Site == site && b.ExpiresTick == until)
                    return true;
            }
            return false;
        }

        private void ExpireBulletins(long tick)
        {
            int count = 0;
            for (int i = 0; i < _bulletins.Count; i++)
            {
                if (_bulletins.At(i).ExpiresTick > tick)
                    continue;
                if (count == _expired.Length)
                    Array.Resize(ref _expired, count * 2);
                _expired[count++] = _bulletins.EntityAt(i);
            }
            for (int i = 0; i < count; i++)
                _world.Destroy(_expired[i]);
        }
    }
}
