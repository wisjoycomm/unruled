using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.SystemFactions
{
    /// <summary>ECS component on THE SYSTEM's Citadel site: its shield and the garrison holding it.</summary>
    public struct SystemCitadel
    {
        public int Integrity; // 0..MaxIntegrity; 0 once fallen
        public byte DefenceTier; // the tier a garrison forms at, rising as the shield falls
        public EntityId Garrison; // THE SYSTEM's fleet at the site; null while it re-forms
        public long GarrisonDueTick; // when the next garrison forms
        public long LastHitTick;
        public long FallenTick; // 0 while standing
    }

    /// <summary>ECS component on a player HQ: the player's part in the assault on the Citadel.</summary>
    public struct CitadelAssault
    {
        public long Damage; // shield removed in total
        public long LastHitTick;
    }

    /// <summary>
    /// The System Citadel (GDD §1: an alliance that brings it down forms The Directorate). PROVISIONAL: the GDD names
    /// only the goal and that Tier-5 nuclear ships are the weapon for it.
    /// </summary>
    public sealed class CitadelBalance
    {
        public readonly int MaxIntegrity;
        public readonly int CheckIntervalTicks;
        public readonly int AmmoPerLineShip; // per check, from the bombarding fleet's hold
        public readonly int RegenPerCheck; // at each check nobody bombards it
        public readonly byte StartTier;
        public readonly int[] TierStepPercents; // the garrison tier rises by one below each of these shield percents
        public readonly int GarrisonLineShips;
        public readonly int GarrisonEwShips;
        public readonly int GarrisonAmmo;
        public readonly int GarrisonBatteries;
        public readonly int GarrisonRespawnTicks;
        public readonly int DirectorateWindowTicks; // bombarded within this long of the fall = a Directorate seat
        private readonly int[] _bombardPerLineShip; // by fleet tier 1..5

        public CitadelBalance(
            int maxIntegrity, int checkIntervalTicks, int[] bombardPerLineShip, int ammoPerLineShip, int regenPerCheck,
            byte startTier, int[] tierStepPercents, int garrisonLineShips, int garrisonEwShips, int garrisonAmmo,
            int garrisonBatteries, int garrisonRespawnTicks, int directorateWindowTicks)
        {
            if (maxIntegrity < 1) throw new ArgumentOutOfRangeException(nameof(maxIntegrity));
            if (checkIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(checkIntervalTicks));
            if (bombardPerLineShip == null || bombardPerLineShip.Length != FleetBalance.TierCount)
                throw new ArgumentException($"Needs one bombard value per tier 1..{FleetBalance.TierCount}.", nameof(bombardPerLineShip));
            for (int i = 0; i < bombardPerLineShip.Length; i++)
                if (bombardPerLineShip[i] < 0) throw new ArgumentOutOfRangeException(nameof(bombardPerLineShip));
            if (ammoPerLineShip < 0) throw new ArgumentOutOfRangeException(nameof(ammoPerLineShip));
            if (regenPerCheck < 0) throw new ArgumentOutOfRangeException(nameof(regenPerCheck));
            if (tierStepPercents == null) throw new ArgumentNullException(nameof(tierStepPercents));
            if (startTier < 1 || startTier + tierStepPercents.Length > FleetBalance.TierCount)
                throw new ArgumentOutOfRangeException(nameof(startTier));
            for (int i = 0; i < tierStepPercents.Length; i++)
                if (tierStepPercents[i] <= 0 || tierStepPercents[i] >= 100 || (i > 0 && tierStepPercents[i] >= tierStepPercents[i - 1]))
                    throw new ArgumentOutOfRangeException(nameof(tierStepPercents), "Steps fall strictly within 1..99.");
            if (garrisonLineShips < 1) throw new ArgumentOutOfRangeException(nameof(garrisonLineShips));
            if (garrisonEwShips < 0) throw new ArgumentOutOfRangeException(nameof(garrisonEwShips));
            if (garrisonAmmo < 0) throw new ArgumentOutOfRangeException(nameof(garrisonAmmo));
            if (garrisonBatteries < 0) throw new ArgumentOutOfRangeException(nameof(garrisonBatteries));
            if (garrisonRespawnTicks < 1) throw new ArgumentOutOfRangeException(nameof(garrisonRespawnTicks));
            if (directorateWindowTicks < 1) throw new ArgumentOutOfRangeException(nameof(directorateWindowTicks));
            MaxIntegrity = maxIntegrity;
            CheckIntervalTicks = checkIntervalTicks;
            _bombardPerLineShip = (int[])bombardPerLineShip.Clone();
            AmmoPerLineShip = ammoPerLineShip;
            RegenPerCheck = regenPerCheck;
            StartTier = startTier;
            TierStepPercents = (int[])tierStepPercents.Clone();
            GarrisonLineShips = garrisonLineShips;
            GarrisonEwShips = garrisonEwShips;
            GarrisonAmmo = garrisonAmmo;
            GarrisonBatteries = garrisonBatteries;
            GarrisonRespawnTicks = garrisonRespawnTicks;
            DirectorateWindowTicks = directorateWindowTicks;
        }

        public int BombardPerLineShip(byte tier) => _bombardPerLineShip[tier - 1];

        /// <summary>The garrison tier at this shield.</summary>
        public byte DefenceTier(int integrity)
        {
            int tier = StartTier;
            for (int i = 0; i < TierStepPercents.Length; i++)
                if ((long)integrity * 100 < (long)MaxIntegrity * TierStepPercents[i])
                    tier++;
            return (byte)tier;
        }
    }

    /// <summary>
    /// Owns <see cref="SystemCitadel"/> and <see cref="CitadelAssault"/>: THE SYSTEM's defence of its Citadel.
    /// Every check interval, at a standing Citadel:
    /// <list type="bullet">
    /// <item>The garrison is THE SYSTEM's fleet at the site; it fights every player there in salvo combat and is
    /// resupplied each check. Once it is destroyed a new one forms after the respawn time, at the defence tier of the
    /// shield then: the lower the shield, the higher the tier.</item>
    /// <item>While no garrison holds the site, every player fleet docked there that can act and carries the ammo
    /// bombards the shield: line ships x the bombard value of its tier (Tier-5 nuclear ships hit hardest), paying
    /// ammo per line ship from its hold. The damage is recorded on the player's HQ; every fleet firing at the check
    /// that breaks the shield counts as part of the assault.</item>
    /// <item>A shield nobody bombards at a check regenerates.</item>
    /// <item>At 0 the Citadel falls for good and its garrison is gone.</item>
    /// </list>
    /// </summary>
    public sealed class SystemDefenseEvaluator : ISimSystem
    {
        /// <summary>The owner id of the Citadel site and its garrison.</summary>
        public static readonly int OwnerId = SystemAgencies.OwnerId(SystemAgency.Sta);

        private readonly World _world;
        private readonly ComponentStore<SystemCitadel> _citadels;
        private readonly ComponentStore<CitadelAssault> _assaults;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly Ledger _ledger;
        private readonly FleetFactory _fleets;
        private readonly FleetBalance _fleetBalance;
        private readonly CitadelBalance _balance;

        public SystemDefenseEvaluator(
            World world, ComponentStore<SystemCitadel> citadels, ComponentStore<CitadelAssault> assaults,
            ComponentStore<SiteInfo> sites, ComponentStore<FleetUnit> units, ComponentStore<FleetMotion> motions,
            ComponentStore<FleetReadiness> readiness, ComponentStore<Stockpile> stockpiles, Ledger ledger, FleetFactory fleets,
            FleetBalance fleetBalance, CitadelBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _citadels = citadels ?? throw new ArgumentNullException(nameof(citadels));
            _assaults = assaults ?? throw new ArgumentNullException(nameof(assaults));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        /// <summary>World setup: THE SYSTEM takes the site as its Citadel, at full shield with a garrison.</summary>
        public void CreateCitadel(EntityId site)
        {
            if (!_sites.Has(site))
                throw new ArgumentException($"{site} is not a site.", nameof(site));
            if (_citadels.Has(site))
                throw new InvalidOperationException($"{site} is already a Citadel.");
            _sites.Get(site).OwnerPlayerId = OwnerId;
            if (!_stockpiles.Has(site))
                _stockpiles.Add(site, default);
            var tier = _balance.DefenceTier(_balance.MaxIntegrity);
            _citadels.Add(site, new SystemCitadel
            {
                Integrity = _balance.MaxIntegrity, DefenceTier = tier, Garrison = SpawnGarrison(site, tier)
            });
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.CheckIntervalTicks != 0)
                return;
            for (int c = 0; c < _citadels.Count; c++)
            {
                ref var citadel = ref _citadels.At(c);
                if (citadel.FallenTick > 0)
                    continue;
                var site = _citadels.EntityAt(c);
                if (HoldsSite(citadel.Garrison))
                {
                    Resupply(citadel.Garrison);
                    Regenerate(ref citadel, tick);
                    continue;
                }
                if (!citadel.Garrison.IsNull)
                {
                    if (_world.IsAlive(citadel.Garrison))
                        _world.Destroy(citadel.Garrison);
                    citadel.Garrison = default;
                    citadel.GarrisonDueTick = tick + _balance.GarrisonRespawnTicks;
                }

                Bombard(ref citadel, site, tick);
                if (citadel.Integrity == 0)
                {
                    citadel.FallenTick = tick;
                    continue;
                }
                Regenerate(ref citadel, tick);
                citadel.DefenceTier = _balance.DefenceTier(citadel.Integrity);
                if (tick >= citadel.GarrisonDueTick)
                    citadel.Garrison = SpawnGarrison(site, citadel.DefenceTier);
            }
        }

        private bool HoldsSite(EntityId garrison) =>
            !garrison.IsNull && _world.IsAlive(garrison) && _units.Has(garrison) && _units.Get(garrison).TotalShips > 0;

        private void Bombard(ref SystemCitadel citadel, EntityId site, long tick)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ref var unit = ref _units.At(i);
                var fleet = _units.EntityAt(i);
                long salvo = (long)unit.LineShips * _balance.BombardPerLineShip(unit.TechTier);
                if (unit.OwnerPlayerId <= 0 || salvo == 0)
                    continue;
                ref var motion = ref _motions.Get(fleet);
                if (!motion.Lane.IsNull || motion.Site != site || !_readiness.Get(fleet).CanAct(tick))
                    continue;
                ref var hold = ref _stockpiles.Get(fleet);
                long ammo = (long)unit.LineShips * _balance.AmmoPerLineShip;
                if (hold.Get(ItemId.ItemAmmo) < ammo)
                    continue;
                hold.Add(ItemId.ItemAmmo, (int)-ammo);
                int damage = (int)Math.Min(citadel.Integrity, salvo);
                citadel.Integrity -= damage;
                citadel.LastHitTick = tick;
                if (!_ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var hq))
                    continue;
                if (!_assaults.Has(hq))
                    _assaults.Add(hq, default);
                ref var assault = ref _assaults.Get(hq);
                assault.Damage += damage;
                assault.LastHitTick = tick;
            }
        }

        private void Regenerate(ref SystemCitadel citadel, long tick)
        {
            if (citadel.LastHitTick != tick)
                citadel.Integrity = Math.Min(_balance.MaxIntegrity, citadel.Integrity + _balance.RegenPerCheck);
        }

        private EntityId SpawnGarrison(EntityId site, byte tier)
        {
            var fleet = _fleets.Create(new FleetUnit
            {
                OwnerPlayerId = OwnerId, TechTier = tier, LineShips = _balance.GarrisonLineShips,
                EwShips = _balance.GarrisonEwShips
            }, site);
            Resupply(fleet);
            return fleet;
        }

        // THE SYSTEM keeps its garrison stocked: ammo, batteries and two upkeep intervals.
        private void Resupply(EntityId fleet)
        {
            ref var unit = ref _units.Get(fleet);
            ref var hold = ref _stockpiles.Get(fleet);
            TopUp(ref hold, ItemId.ItemAmmo, _balance.GarrisonAmmo);
            TopUp(ref hold, ItemId.ItemBattery, _balance.GarrisonBatteries);
            var upkeep = _fleetBalance.Tier(unit.TechTier).UpkeepPerShipHour;
            for (int k = 0; k < upkeep.Length; k++)
                TopUp(ref hold, upkeep[k].Item, 2 * FleetUpkeepSystem.Due(unit.TotalShips, upkeep[k].Milli, 0));
        }

        private static void TopUp(ref Stockpile hold, ItemId item, int target)
        {
            int gap = target - hold.Get(item);
            if (gap > 0)
                hold.Add(item, gap);
        }
    }
}
