using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.AI
{
    /// <summary>ECS component on a player HQ: the player is run by the Utility AI (offline standing orders or a bot).</summary>
    public struct AiController
    {
        public byte Archetype; // tab-06 Archetype_ID
    }

    public enum AiAction : byte
    {
        VentCoolant = 1,
        Retreat = 2,
        Resupply = 3,
        RetractDrills = 4,
        RadarScan = 5,
        StressTest = 6,
        OpenGate = 7,
        LoadCargo = 8,
        StealthRaid = 9,
        Siege = 10,
        ClaimSite = 11,
        ReturnHome = 12,
        UnloadCargo = 13,
        BuildRig = 14,
        BuildConvoy = 15,
        SetConvoyRoute = 16,
        BuildRefinery = 17,
        PauseDrills = 18,
        AuditOptIn = 19,
        AssaultCitadel = 20,
        SellSurplus = 21,
        BuyShortfall = 22,
        CancelOrder = 23,
        DevelopOutpost = 24,
        Retrofit = 25,
        BuildWarships = 26,
        Mothball = 27,
        Activate = 28,
        Borrow = 29,
        Repay = 30,
        SellToSystem = 31,
        BuyFeedstock = 32,
        BuyFromSystem = 33,
        MergeFleets = 34,
        RaidConvoys = 35,
        FlankSplit = 36,
        FlankMove = 37,
        Survey = 38,
        ListChart = 39,
        BuyChart = 40
    }

    /// <summary>An order the AI tried to issue, and what <see cref="CommandIssuer"/> answered.</summary>
    public struct AiOrder
    {
        public int PlayerId;
        public AiAction Action;
        public EntityId Subject; // the fleet, deposit or lane
        public int ScoreMilli;
        public IssueResult Result;
    }

    /// <summary>
    /// Owns <see cref="AiController"/>: the Utility AI shared by offline players' standing orders and PvE bots
    /// (GDD §2.5). It is not on the sim clock: the host calls <see cref="Think"/> (Server 2), and it changes the
    /// world only through <see cref="CommandIssuer"/>, with the same latency as a player. A player hands its empire to
    /// standing orders (Auto-SOP) and takes it back with <see cref="CommandType.SetStandingOrders"/> (ParamA = tab-06
    /// archetype, 0 = back in the player's hands), which reaches it through the clock like any order.
    /// Every think interval each decision maker scores its options with Dave Mark's IAUS,
    /// Score = PresetWeight x (product of m considerations)^(1/m), all in milli-units, and issues its best one:
    /// <list type="bullet">
    /// <item>Each fleet: vent coolant (Defense_Hold weight) at or above the preset temperature while it has coolant;
    /// retreat to the core base (Evacuate_Safety) when its side's power at its site falls below the preset ratio of
    /// the hostile power there; resupply at the core base (Resupply_Logistics) when its line ships run low on ammo.</item>
    /// <item>Each drilled deposit: retract the drills (Evacuate_Safety) when the player's power at its site falls below
    /// the retreat ratio; pause them (Evacuate_Safety) while the G or S storm there is at the preset level (storms don't
    /// harm rigs, so the evacuation keeps them), and (Resupply_Logistics) while the site holds the raw stock cap of its
    /// ore; resume them under half the cap. A bare deposit
    /// with ore left, no such danger and under half the cap in stock gets its rigs back (Resupply_Logistics) when the
    /// site can pay: the new-game rigs at the core base, the outpost rigs elsewhere.</item>
    /// <item>The player joins the leaderboard audit (Resupply_Logistics) for its toll discount and grants.</item>
    /// <item>The player: one focused scan (Radar_Belt_Scan) of an unscanned bypass lane at one of its sites while the
    /// core base holds more than the preset minimum battery.</item>
    /// <item>The player: a stress test (Radar_Belt_Scan, the research weight) once telemetry is complete and the core base
    /// can pay; opening its jump gate (the higher of Stealth_Raid and Siege_Conquest) at the gate tier when the core base
    /// can pay. A preset with neither weight stays sealed at home.</item>
    /// <item>A fleet docked at the core base, even grounded for unpaid upkeep: load its oxygen and rations for the load
    /// intervals, ammo and fuel up to the load targets (leaving the core reserve of fuel and coolant), microchips for
    /// storm telemetry while the player researches, plus the claim cost when it besieges (Resupply_Logistics). Fully
    /// loaded and able to act, it picks a target it outguns with fuel for the round trip: a stealth raid
    /// (Stealth_Raid) on rival fleets at a rival site, or a siege (Siege_Conquest) of a neutral or rival site (never a
    /// core base) while it carries the claim cost. Advantage reads 500 at even power up to 1000 when undefended. A
    /// siege first prefers the most deposits of ores the player mines nowhere yet; then the best advantage wins, then the
    /// fewest lanes.</item>
    /// <item>Multi-lane flanking (GDD §2.5, PROVISIONAL): a raid or siege on a defended site goes in on two lanes whose
    /// approach directions are at least 90° apart, as each extra lane cuts the defenders' point defence. With another own
    /// fleet already bound for the target, the fleet heads for a staging site across an independent lane, on a route that
    /// skirts the target (as far along it as a plain Move there does too); else one of at least FlankMinLineShips line
    /// ships, whose half outguns the defenders and with no other own fleet docked beside it, splits in two first. A fleet
    /// ready at a quiet site within two lanes of such a target carries on the same way while the two together outgun the
    /// defenders, and from the staging site goes in along the flank lane.</item>
    /// <item>A fleet ready at a claimable site carrying the claim cost: claim it (Siege_Conquest). Ready at a site that is
    /// not its player's, with nothing to fight or claim: go home (Resupply_Logistics at half).</item>
    /// <item>A fleet ready at its own outpost (Siege_Conquest): rigs on each bare deposit with ore left, unloading the rig
    /// cost it carried from the core base first (it loads that for every deposit of a site with the claim cost); then
    /// home.</item>
    /// <item>A conquering preset's fleet ready at the core base, loaded with the claim cost, with nothing to raid, besiege
    /// or develop: chart the next frontier star that neighbours the home system (Siege_Conquest at half), so the empire
    /// grows into the galaxy instead of idling.</item>
    /// <item>A convoy-only fleet without a route (Resupply_Logistics): at the core base, a convoy route to an outpost with
    /// rigs that no convoy serves; elsewhere, home. The player builds a convoy fleet when such an outpost has no idle
    /// convoy or convoy order, and a core-base refinery for a refined item it lacks once both inputs are stocked.</item>
    /// </list>
    /// A triggered consideration reads 500 at its threshold, rising to 1000 at the extreme. Fleet orders also weigh a
    /// can-do consideration (coolant in the hold to vent, fuel to leave) that vetoes the action at 0. Power is line ships x
    /// LineAlpha plus EW ships x EwBeta (PROVISIONAL). Orders already in effect score 0; an order of the same type for
    /// the same target still in signal latency is not sent again.
    /// </summary>
    public sealed class UtilityAiEngine : ICommandHandler
    {
        private const int Full = 1000;
        private const int Threshold = 500;

        private readonly ComponentStore<AiController> _controllers;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<FleetRoute> _fleetRoutes;
        private readonly ComponentStore<FleetCombatState> _combat;
        private readonly ComponentStore<FleetReadiness> _readiness;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<SiteWeather> _weather;
        private readonly ComponentStore<MineralDeposit> _deposits;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<RadarScan> _scans;
        private readonly ComponentStore<TechProgress> _progress;
        private readonly ComponentStore<JumpGate> _gates;
        private readonly ComponentStore<ConvoyRoute> _convoyRoutes;
        private readonly ComponentStore<ShipBuildOrder> _shipOrders;
        private readonly ComponentStore<Refinery> _refineries;
        private readonly ComponentStore<PlayerStanding> _standings;
        private readonly ComponentStore<SystemCitadel> _citadels;
        private readonly ComponentStore<WtoOrder> _wtoOrders;
        private readonly ComponentStore<ItemMarket> _markets;
        private readonly ComponentStore<Loan> _loans;
        private readonly ComponentStore<SystemSanction> _sanctions;
        private readonly ComponentStore<SystemChart> _charts;
        private readonly ComponentStore<ChartListing> _chartListings;
        private readonly System.Collections.Generic.List<int> _chartNeighbours = new System.Collections.Generic.List<int>();
        private readonly Ledger _ledger;
        private readonly EntityId _systemStock;
        private readonly ITollGate _tolls;
        private readonly IMarketRates _rates;
        private readonly CommandIssuer _issuer;
        private readonly RoutePlanner _routes;
        private readonly ITechGates _tech;
        private readonly GalaxySystem _galaxy;
        private readonly AiBalance _balance;
        private readonly CombatBalance _combatBalance;
        private readonly RadarBalance _radarBalance;
        private readonly ResearchBalance _researchBalance;
        private readonly JumpGateBalance _gateBalance;
        private readonly SiteControlBalance _claimBalance;
        private readonly MiningBalance _miningBalance;
        private readonly ShipyardBalance _shipyardBalance;
        private readonly RefineryBalance _refineryBalance;
        private readonly RecipeDatabase _recipes;
        private readonly ConvoyBalance _convoyBalance;
        private readonly NewGameBalance _newGame;
        private readonly FleetBalance _fleetBalance;
        private readonly WtoBalance _wtoBalance;
        private readonly LedgerBalance _ledgerBalance;
        private readonly CitadelBalance _citadelBalance;
        private readonly SystemAuthorityBalance _authorityBalance;

        private FleetRoute _route;
        private FleetRoute _detour;

        private AiOrder[] _orders = new AiOrder[16];
        private int _orderCount;
        private readonly int _expansionRaws; // bit per raw slot of the outpost currency (see OutpostCurrency)
        private readonly int _lifelineRaws; // bit per raw slot of fuel and coolant

        public UtilityAiEngine(
            ComponentStore<AiController> controllers, ComponentStore<PlayerHq> players, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetCombatState> combat,
            ComponentStore<FleetReadiness> readiness, ComponentStore<Stockpile> stockpiles, ComponentStore<SiteInfo> sites,
            ComponentStore<SiteWeather> weather, ComponentStore<MineralDeposit> deposits, ComponentStore<StarlaneEdge> lanes,
            ComponentStore<RadarScan> scans, ComponentStore<TechProgress> progress, ComponentStore<JumpGate> gates,
            ComponentStore<ConvoyRoute> convoyRoutes, ComponentStore<ShipBuildOrder> shipOrders,
            ComponentStore<Refinery> refineries, ComponentStore<PlayerStanding> standings, ComponentStore<SystemCitadel> citadels,
            ComponentStore<WtoOrder> wtoOrders, ComponentStore<ItemMarket> markets, ComponentStore<Loan> loans,
            ComponentStore<SystemSanction> sanctions, ComponentStore<FleetRoute> fleetRoutes,
            ComponentStore<SystemChart> charts, ComponentStore<ChartListing> chartListings, Ledger ledger,
            EntityId systemStock, IMarketRates rates,
            CommandIssuer issuer,
            RoutePlanner routes, ITollGate tolls, ITechGates tech, GalaxySystem galaxy, GameBalanceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _weather = weather ?? throw new ArgumentNullException(nameof(weather));
            _deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _scans = scans ?? throw new ArgumentNullException(nameof(scans));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _gates = gates ?? throw new ArgumentNullException(nameof(gates));
            _convoyRoutes = convoyRoutes ?? throw new ArgumentNullException(nameof(convoyRoutes));
            _shipOrders = shipOrders ?? throw new ArgumentNullException(nameof(shipOrders));
            _refineries = refineries ?? throw new ArgumentNullException(nameof(refineries));
            _standings = standings ?? throw new ArgumentNullException(nameof(standings));
            _citadels = citadels ?? throw new ArgumentNullException(nameof(citadels));
            _wtoOrders = wtoOrders ?? throw new ArgumentNullException(nameof(wtoOrders));
            _markets = markets ?? throw new ArgumentNullException(nameof(markets));
            _loans = loans ?? throw new ArgumentNullException(nameof(loans));
            _sanctions = sanctions ?? throw new ArgumentNullException(nameof(sanctions));
            _charts = charts ?? throw new ArgumentNullException(nameof(charts));
            _chartListings = chartListings ?? throw new ArgumentNullException(nameof(chartListings));
            _fleetRoutes = fleetRoutes ?? throw new ArgumentNullException(nameof(fleetRoutes));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (systemStock.IsNull) throw new ArgumentException("THE SYSTEM's stock required.", nameof(systemStock));
            _systemStock = systemStock;
            _rates = rates ?? throw new ArgumentNullException(nameof(rates));
            _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _tolls = tolls ?? throw new ArgumentNullException(nameof(tolls));
            _tech = tech ?? throw new ArgumentNullException(nameof(tech));
            _galaxy = galaxy ?? throw new ArgumentNullException(nameof(galaxy));
            _balance = config.Ai;
            _combatBalance = config.Combat;
            _radarBalance = config.Radar;
            _researchBalance = config.Research;
            _gateBalance = config.JumpGates;
            _claimBalance = config.SiteControl;
            _miningBalance = config.Mining;
            _shipyardBalance = config.Shipyard;
            _refineryBalance = config.Refinery;
            _recipes = config.Recipes;
            _convoyBalance = config.Convoys;
            _newGame = config.NewGame;
            _fleetBalance = config.Fleets;
            _wtoBalance = config.Wto;
            _ledgerBalance = config.Ledger;
            _citadelBalance = config.Citadel;
            _authorityBalance = config.Authority;
            var currency = OutpostCurrency();
            if (currency != ItemId.None)
                _expansionRaws = ItemIds.IsRaw(currency) ? 1 << ItemIds.ToSlot(currency) : RecipeRaws(currency);
            _lifelineRaws = RecipeRaws(ItemId.ItemFuel) | RecipeRaws(ItemId.ItemCoolant);
        }

        // The item a new outpost takes most of: its claim cost plus the rigs on one deposit (steel).
        private ItemId OutpostCurrency()
        {
            var best = ItemId.None;
            long most = 0;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                long quantity = 0;
                for (int i = 0; i < _claimBalance.ClaimCostCount; i++)
                    if (_claimBalance.ClaimCost(i).Item == item)
                        quantity += _claimBalance.ClaimCost(i).Quantity;
                for (int i = 0; i < _miningBalance.RigCostCount; i++)
                    if (_miningBalance.RigCost(i).Item == item)
                        quantity += (long)_miningBalance.RigCost(i).Quantity * _balance.OutpostRigs;
                if (quantity <= most)
                    continue;
                most = quantity;
                best = item;
            }
            return best;
        }

        /// <summary>The orders tried by the last think that ran, in decision order.</summary>
        public int OrderCount => _orderCount;

        public AiOrder OrderAt(int i)
        {
            if (i < 0 || i >= _orderCount) throw new ArgumentOutOfRangeException(nameof(i));
            return _orders[i];
        }

        /// <summary>Host: hands a player HQ to the AI with a tab-06 archetype, or changes its archetype.</summary>
        public void Assign(EntityId hq, byte archetype)
        {
            if (!_players.Has(hq)) throw new ArgumentException("Not a player HQ.", nameof(hq));
            if (!_balance.IsValid(archetype)) throw new ArgumentOutOfRangeException(nameof(archetype));
            if (_controllers.Has(hq))
                _controllers.Get(hq).Archetype = archetype;
            else
                _controllers.Add(hq, new AiController { Archetype = archetype });
        }

        /// <summary>Host: the player is back in control.</summary>
        public void Release(EntityId hq)
        {
            if (_controllers.Has(hq))
                _controllers.Remove(hq);
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.SetStandingOrders)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (!TryFindHq(command.PlayerId, out var hq))
                return;
            if (command.ParamA == 0)
                Release(hq);
            else if (command.ParamA > 0 && command.ParamA <= byte.MaxValue && _balance.IsValid((byte)command.ParamA))
                Assign(hq, (byte)command.ParamA);
        }

        private bool TryFindHq(int playerId, out EntityId hq)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).PlayerId == playerId)
                {
                    hq = _players.EntityAt(i);
                    return true;
                }
            hq = default;
            return false;
        }

        /// <summary>Runs one think on ticks that are a multiple of the interval; returns the orders tried.</summary>
        public int Think(long tick)
        {
            if (tick % _balance.ThinkIntervalTicks != 0)
                return 0;
            _orderCount = 0;
            IndexWorld();
            for (int c = 0; c < _controllers.Count; c++)
            {
                var preset = _balance.Preset(_controllers.At(c).Archetype);
                var hq = _controllers.EntityAt(c);
                ref var player = ref _players.Get(hq);
                for (int f = FirstPlayerFleet(player.PlayerId); f >= 0; f = _playerFleetNext[f])
                {
                    if (_units.At(f).TotalShips > 0)
                        ThinkFleet(player, preset, hq, _units.EntityAt(f), tick);
                }
                for (int d = FirstPlayerDeposit(player.PlayerId); d >= 0; d = _playerDepositNext[d])
                    ThinkDeposit(player.PlayerId, player.CoreBase, preset, d);
                if (tick % SimTick.TicksPerHour < _balance.ScanWindowTicks)
                    ThinkRadar(player, preset, hq);
                ThinkProgress(player, preset, hq);
                ThinkEconomy(player, preset, hq);
                ThinkShipyard(player, preset, hq);
                ThinkFinance(player, preset, hq, tick);
                ThinkTrade(player, preset, hq, tick);
                ThinkCharts(player, preset, hq, tick);
            }
            return _orderCount;
        }

        private int _thinkStamp;
        private int[] _coreBaseStamp = Array.Empty<int>();
        private int[] _homeClusterStamp = Array.Empty<int>();
        private int[] _siteDepositStamp = Array.Empty<int>();
        private int[] _siteFirstDeposit = Array.Empty<int>();
        private int[] _siteAvailableOres = Array.Empty<int>();
        private ulong[] _siteOreCounts = Array.Empty<ulong>();
        private int[] _depositNext = Array.Empty<int>();
        private int[] _siteFleetStamp = Array.Empty<int>();
        private int[] _siteFirstFleet = Array.Empty<int>();
        private int[] _fleetNext = Array.Empty<int>();
        private int[] _armedSites = Array.Empty<int>();
        private int _armedSiteCount;
        private int[] _clusterNeutralStamp = Array.Empty<int>();
        private int[] _clusterFirstNeutralSite = Array.Empty<int>();
        private int[] _compOpenStamp = Array.Empty<int>();
        private int[] _compFirstOpenSite = Array.Empty<int>();
        private int[] _siteCandidateNext = Array.Empty<int>();
        private bool _cachedSiegeValid;
        private bool _cachedSiegeHas;
        private EntityId _cachedSiegeTarget;
        private int _cachedSiegeEdge;
        private long _cachedSiegeRtFuel;
        private int[] _siteRigsStamp = Array.Empty<int>();
        private int[] _siteOutpostRaws = Array.Empty<int>();
        private int[] _siteServedStamp = Array.Empty<int>();
        private int[] _siteRefineryStamp = Array.Empty<int>();
        private int[] _siteRefineryCount = Array.Empty<int>();
        private int[] _siteRefineryMask = Array.Empty<int>();
        private int[] _siteRefineryRaws = Array.Empty<int>();
        private int[] _playerStamp = Array.Empty<int>();
        private EntityId[] _playerHq = Array.Empty<EntityId>();
        private EntityId[] _playerCoreBase = Array.Empty<EntityId>();
        private int[] _playerFirstFleet = Array.Empty<int>();
        private int[] _playerFleetNext = Array.Empty<int>();
        private int[] _playerFirstDeposit = Array.Empty<int>();
        private int[] _playerDepositNext = Array.Empty<int>();
        private int[] _playerFirstSite = Array.Empty<int>();
        private int[] _playerSiteNext = Array.Empty<int>();
        private int[] _playerFirstWtoOrder = Array.Empty<int>();
        private int[] _playerWtoOrderNext = Array.Empty<int>();
        private int[] _wtoBuyOrders = Array.Empty<int>();
        private int _wtoBuyCount;
        private readonly int[] _bestAskIdx1 = new int[ItemIds.Count];
        private readonly long[] _bestAskPrice1 = new long[ItemIds.Count];
        private readonly int[] _bestAskRem1 = new int[ItemIds.Count];
        private readonly int[] _bestAskOwner1 = new int[ItemIds.Count];
        private readonly int[] _bestAskIdx2 = new int[ItemIds.Count];
        private readonly long[] _bestAskPrice2 = new long[ItemIds.Count];
        private readonly int[] _bestAskRem2 = new int[ItemIds.Count];
        private int[] _playerMinedOres = Array.Empty<int>();
        private int[] _playerLowCoreOres = Array.Empty<int>();
        private bool[] _playerDormantGate = Array.Empty<bool>();
        private EntityId[] _playerDormantGateSite = Array.Empty<EntityId>();
        private bool[] _playerOpenGate = Array.Empty<bool>();
        private int[] _playerOutpostCount = Array.Empty<int>();
        private int[] _playerRefineryCount = Array.Empty<int>();
        private int[] _playerShipOrders = Array.Empty<int>();
        private int[] _playerBuyOrderMask = Array.Empty<int>();
        private int[] _playerSellOrderMask = Array.Empty<int>();
        private int[] _playerStressStamp = Array.Empty<int>();
        private bool[] _playerHasStress = Array.Empty<bool>();
        private ResearchBranch[] _playerStressBranch = Array.Empty<ResearchBranch>();
        private ItemStack[][] _playerStressCost = Array.Empty<ItemStack[]>();
        private int[] _playerNeededRaws = Array.Empty<int>();
        private int _cachedBypassLaneCount = -1;
        private int[] _bypassLanes = Array.Empty<int>();
        private int _bypassLaneTotal;

        private int FirstPlayerFleet(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp ? _playerFirstFleet[playerId] : -1;

        private int FirstPlayerDeposit(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp ? _playerFirstDeposit[playerId] : -1;

        private int FirstPlayerSite(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp ? _playerFirstSite[playerId] : -1;

        private int FirstPlayerWtoOrder(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp ? _playerFirstWtoOrder[playerId] : -1;

        private int FirstSiteDeposit(EntityId site) =>
            !site.IsNull && site.Index < _siteDepositStamp.Length && _siteDepositStamp[site.Index] == _thinkStamp ? _siteFirstDeposit[site.Index] : -1;

        private void EnsurePlayerSlot(int playerId)
        {
            if (playerId <= 0)
                return;
            if (playerId >= _playerStamp.Length)
            {
                int cap = Math.Max(playerId + 1, _playerStamp.Length == 0 ? 32 : _playerStamp.Length * 2);
                Array.Resize(ref _playerStamp, cap);
                Array.Resize(ref _playerHq, cap);
                Array.Resize(ref _playerCoreBase, cap);
                Array.Resize(ref _playerFirstFleet, cap);
                Array.Resize(ref _playerFirstDeposit, cap);
                Array.Resize(ref _playerFirstSite, cap);
                Array.Resize(ref _playerFirstWtoOrder, cap);
                Array.Resize(ref _playerMinedOres, cap);
                Array.Resize(ref _playerLowCoreOres, cap);
                Array.Resize(ref _playerDormantGate, cap);
                Array.Resize(ref _playerDormantGateSite, cap);
                Array.Resize(ref _playerOpenGate, cap);
                Array.Resize(ref _playerOutpostCount, cap);
                Array.Resize(ref _playerRefineryCount, cap);
                Array.Resize(ref _playerShipOrders, cap);
                Array.Resize(ref _playerBuyOrderMask, cap);
                Array.Resize(ref _playerSellOrderMask, cap);
                Array.Resize(ref _playerStressStamp, cap);
                Array.Resize(ref _playerHasStress, cap);
                Array.Resize(ref _playerStressBranch, cap);
                Array.Resize(ref _playerStressCost, cap);
                Array.Resize(ref _playerNeededRaws, cap);
            }
            if (_playerStamp[playerId] != _thinkStamp)
            {
                _playerStamp[playerId] = _thinkStamp;
                _playerHq[playerId] = default;
                _playerCoreBase[playerId] = default;
                _playerFirstFleet[playerId] = -1;
                _playerFirstDeposit[playerId] = -1;
                _playerFirstSite[playerId] = -1;
                _playerFirstWtoOrder[playerId] = -1;
                _playerMinedOres[playerId] = 0;
                _playerLowCoreOres[playerId] = 0;
                _playerDormantGate[playerId] = false;
                _playerDormantGateSite[playerId] = default;
                _playerOpenGate[playerId] = false;
                _playerOutpostCount[playerId] = 0;
                _playerRefineryCount[playerId] = 0;
                _playerShipOrders[playerId] = 0;
                _playerBuyOrderMask[playerId] = 0;
                _playerSellOrderMask[playerId] = 0;
            }
        }

        private void IndexWorld()
        {
            if (++_thinkStamp == int.MaxValue)
            {
                Array.Clear(_coreBaseStamp, 0, _coreBaseStamp.Length);
                Array.Clear(_homeClusterStamp, 0, _homeClusterStamp.Length);
                Array.Clear(_clusterNeutralStamp, 0, _clusterNeutralStamp.Length);
                Array.Clear(_compOpenStamp, 0, _compOpenStamp.Length);
                Array.Clear(_siteDepositStamp, 0, _siteDepositStamp.Length);
                Array.Clear(_siteFleetStamp, 0, _siteFleetStamp.Length);
                Array.Clear(_siteRigsStamp, 0, _siteRigsStamp.Length);
                Array.Clear(_siteServedStamp, 0, _siteServedStamp.Length);
                Array.Clear(_siteRefineryStamp, 0, _siteRefineryStamp.Length);
                Array.Clear(_playerStamp, 0, _playerStamp.Length);
                Array.Clear(_playerStressStamp, 0, _playerStressStamp.Length);
                _thinkStamp = 1;
            }
            int maxSiteIndex = 0;
            int maxClusterId = 0;
            for (int i = 0; i < _sites.Count; i++)
            {
                int idx = _sites.EntityAt(i).Index;
                if (idx > maxSiteIndex) maxSiteIndex = idx;
                int cid = _sites.At(i).ClusterId;
                if (cid > maxClusterId) maxClusterId = cid;
            }
            int siteCap = maxSiteIndex + 1;
            if (_coreBaseStamp.Length < siteCap)
            {
                int cap = Math.Max(siteCap, _coreBaseStamp.Length == 0 ? 64 : _coreBaseStamp.Length * 2);
                Array.Resize(ref _coreBaseStamp, cap);
                Array.Resize(ref _siteDepositStamp, cap);
                Array.Resize(ref _siteFirstDeposit, cap);
                Array.Resize(ref _siteAvailableOres, cap);
                Array.Resize(ref _siteOreCounts, cap);
                Array.Resize(ref _siteFleetStamp, cap);
                Array.Resize(ref _siteFirstFleet, cap);
                Array.Resize(ref _siteRigsStamp, cap);
                Array.Resize(ref _siteOutpostRaws, cap);
                Array.Resize(ref _siteServedStamp, cap);
                Array.Resize(ref _siteRefineryStamp, cap);
                Array.Resize(ref _siteRefineryCount, cap);
                Array.Resize(ref _siteRefineryMask, cap);
                Array.Resize(ref _siteRefineryRaws, cap);
            }
            int clusterCap = maxClusterId + 1;
            if (_homeClusterStamp.Length < clusterCap)
            {
                int cap = Math.Max(clusterCap, _homeClusterStamp.Length == 0 ? 32 : _homeClusterStamp.Length * 2);
                Array.Resize(ref _homeClusterStamp, cap);
                Array.Resize(ref _clusterNeutralStamp, cap);
                Array.Resize(ref _clusterFirstNeutralSite, cap);
            }
            int compCap = _routes.Graph.ComponentCount + 1;
            if (_compOpenStamp.Length < compCap)
            {
                int cap = Math.Max(compCap, _compOpenStamp.Length == 0 ? 32 : _compOpenStamp.Length * 2);
                Array.Resize(ref _compOpenStamp, cap);
                Array.Resize(ref _compFirstOpenSite, cap);
            }
            for (int i = 0; i < _players.Count; i++)
            {
                int pid = _players.At(i).PlayerId;
                var core = _players.At(i).CoreBase;
                EnsurePlayerSlot(pid);
                if (pid > 0)
                {
                    _playerHq[pid] = _players.EntityAt(i);
                    _playerCoreBase[pid] = core;
                }
                if (!_sites.Has(core))
                    continue;
                _coreBaseStamp[core.Index] = _thinkStamp;
                int cid = _sites.Get(core).ClusterId;
                if (cid >= 0)
                    _homeClusterStamp[cid] = _thinkStamp;
            }
            if (_playerSiteNext.Length < _sites.Count)
                Array.Resize(ref _playerSiteNext, Math.Max(_sites.Count, _playerSiteNext.Length == 0 ? 64 : _playerSiteNext.Length * 2));
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                int pid = _sites.At(i).OwnerPlayerId;
                if (pid <= 0)
                {
                    _playerSiteNext[i] = -1;
                    continue;
                }
                EnsurePlayerSlot(pid);
                _playerSiteNext[i] = _playerFirstSite[pid];
                _playerFirstSite[pid] = i;
                if (_sites.EntityAt(i) != _playerCoreBase[pid])
                    _playerOutpostCount[pid]++;
            }
            if (_depositNext.Length < _deposits.Count)
            {
                int cap = Math.Max(_deposits.Count, _depositNext.Length == 0 ? 64 : _depositNext.Length * 2);
                Array.Resize(ref _depositNext, cap);
                Array.Resize(ref _playerDepositNext, cap);
            }
            for (int d = _deposits.Count - 1; d >= 0; d--)
            {
                ref var deposit = ref _deposits.At(d);
                var site = deposit.Site;
                if (!_sites.Has(site))
                {
                    _depositNext[d] = -1;
                    _playerDepositNext[d] = -1;
                    continue;
                }
                int s = site.Index;
                if (_siteDepositStamp[s] != _thinkStamp)
                {
                    _siteDepositStamp[s] = _thinkStamp;
                    _siteFirstDeposit[s] = -1;
                    _siteAvailableOres[s] = 0;
                    _siteOreCounts[s] = 0;
                }
                _depositNext[d] = _siteFirstDeposit[s];
                _siteFirstDeposit[s] = d;

                int pid = _sites.Get(site).OwnerPlayerId;
                if (pid > 0)
                {
                    EnsurePlayerSlot(pid);
                    _playerDepositNext[d] = _playerFirstDeposit[pid];
                    _playerFirstDeposit[pid] = d;
                }
                else
                {
                    _playerDepositNext[d] = -1;
                }

                bool hasOre = HasOre(in deposit);
                if (pid > 0 && IsCoreBase(site) && (!hasOre || OreLeft(in deposit) < _balance.CoreOreLowReserve))
                    _playerLowCoreOres[pid] |= 1 << ItemIds.ToSlot(deposit.RawItem);
                if (hasOre)
                {
                    int rawSlot = ItemIds.ToSlot(deposit.RawItem);
                    _siteAvailableOres[s] |= 1 << rawSlot;
                    _siteOreCounts[s] += 1UL << (rawSlot * 6);
                }

                if (deposit.RigCount > 0)
                {
                    if (_siteRigsStamp[s] != _thinkStamp)
                    {
                        _siteRigsStamp[s] = _thinkStamp;
                        _siteOutpostRaws[s] = 0;
                    }
                    if (hasOre)
                    {
                        int bit = 1 << ItemIds.ToSlot(deposit.RawItem);
                        _siteOutpostRaws[s] |= bit;
                        if (pid > 0 && (!IsCoreBase(site) || OreLeft(in deposit) >= _balance.CoreOreLowReserve))
                            _playerMinedOres[pid] |= bit;
                    }
                }
            }
            if (_fleetNext.Length < _units.Count)
            {
                int cap = Math.Max(_units.Count, _fleetNext.Length == 0 ? 64 : _fleetNext.Length * 2);
                Array.Resize(ref _fleetNext, cap);
                Array.Resize(ref _playerFleetNext, cap);
            }
            for (int f = _units.Count - 1; f >= 0; f--)
            {
                int pid = _units.At(f).OwnerPlayerId;
                if (pid > 0)
                {
                    EnsurePlayerSlot(pid);
                    _playerFleetNext[f] = _playerFirstFleet[pid];
                    _playerFirstFleet[pid] = f;
                }
                else
                {
                    _playerFleetNext[f] = -1;
                }
                ref var motion = ref _motions.Get(_units.EntityAt(f));
                if (motion.Site.IsNull || !_sites.Has(motion.Site))
                {
                    _fleetNext[f] = -1;
                    continue;
                }
                int s = motion.Site.Index;
                _fleetNext[f] = _siteFleetStamp[s] == _thinkStamp ? _siteFirstFleet[s] : -1;
                _siteFleetStamp[s] = _thinkStamp;
                _siteFirstFleet[s] = f;
            }
            if (_armedSites.Length < _sites.Count)
            {
                int cap = Math.Max(_sites.Count, _armedSites.Length == 0 ? 64 : _armedSites.Length * 2);
                Array.Resize(ref _armedSites, cap);
                Array.Resize(ref _siteCandidateNext, cap);
            }
            _armedSiteCount = 0;
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites.At(i).OwnerPlayerId <= 0)
                    continue;
                var entity = _sites.EntityAt(i);
                if (!IsCoreBase(entity) && entity.Index < _siteFleetStamp.Length && _siteFleetStamp[entity.Index] == _thinkStamp)
                    _armedSites[_armedSiteCount++] = i;
            }
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                ref var site = ref _sites.At(i);
                var entity = _sites.EntityAt(i);
                int comp = _routes.Graph.ComponentOf(entity);
                if (comp <= 0 || site.OwnerPlayerId < 0 || IsCoreBase(entity) ||
                    entity.Index >= _siteDepositStamp.Length || _siteDepositStamp[entity.Index] != _thinkStamp ||
                    _siteAvailableOres[entity.Index] == 0)
                {
                    _siteCandidateNext[i] = -1;
                    continue;
                }
                if (site.OwnerPlayerId == 0 && IsAnyHomeCluster(site.ClusterId))
                {
                    int cid = site.ClusterId;
                    _siteCandidateNext[i] = _clusterNeutralStamp[cid] == _thinkStamp ? _clusterFirstNeutralSite[cid] : -1;
                    _clusterNeutralStamp[cid] = _thinkStamp;
                    _clusterFirstNeutralSite[cid] = i;
                }
                else
                {
                    _siteCandidateNext[i] = _compOpenStamp[comp] == _thinkStamp ? _compFirstOpenSite[comp] : -1;
                    _compOpenStamp[comp] = _thinkStamp;
                    _compFirstOpenSite[comp] = i;
                }
            }
            for (int r = 0; r < _convoyRoutes.Count; r++)
            {
                var outpost = _convoyRoutes.At(r).Outpost;
                if (!outpost.IsNull && outpost.Index < _siteServedStamp.Length)
                    _siteServedStamp[outpost.Index] = _thinkStamp;
            }
            for (int r = 0; r < _refineries.Count; r++)
            {
                ref var refinery = ref _refineries.At(r);
                var site = refinery.Site;
                if (site.IsNull || site.Index >= _siteRefineryStamp.Length)
                    continue;
                int s = site.Index;
                if (_siteRefineryStamp[s] != _thinkStamp)
                {
                    _siteRefineryStamp[s] = _thinkStamp;
                    _siteRefineryCount[s] = 0;
                    _siteRefineryMask[s] = 0;
                    _siteRefineryRaws[s] = 0;
                }
                _siteRefineryCount[s]++;
                _siteRefineryMask[s] |= 1 << ItemIds.ToSlot(refinery.Output);
                _siteRefineryRaws[s] |= RecipeRaws(refinery.Output);
                if (_sites.Has(site))
                {
                    int pid = _sites.Get(site).OwnerPlayerId;
                    if (pid > 0)
                    {
                        EnsurePlayerSlot(pid);
                        _playerRefineryCount[pid]++;
                    }
                }
            }
            for (int g = 0; g < _gates.Count; g++)
            {
                ref var gate = ref _gates.At(g);
                if (gate.OwnerPlayerId <= 0)
                    continue;
                EnsurePlayerSlot(gate.OwnerPlayerId);
                if (gate.IsOpen)
                    _playerOpenGate[gate.OwnerPlayerId] = true;
                else if (!_playerDormantGate[gate.OwnerPlayerId])
                {
                    _playerDormantGate[gate.OwnerPlayerId] = true;
                    _playerDormantGateSite[gate.OwnerPlayerId] = gate.HomeSite;
                }
            }
            for (int o = 0; o < _shipOrders.Count; o++)
            {
                ref var order = ref _shipOrders.At(o);
                if (order.OwnerPlayerId <= 0)
                    continue;
                EnsurePlayerSlot(order.OwnerPlayerId);
                _playerShipOrders[order.OwnerPlayerId] |= 1 << (int)order.ShipClass;
            }
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                _bestAskIdx1[slot] = -1;
                _bestAskIdx2[slot] = -1;
            }
            if (_playerWtoOrderNext.Length < _wtoOrders.Count)
            {
                int cap = Math.Max(_wtoOrders.Count, _playerWtoOrderNext.Length == 0 ? 64 : _playerWtoOrderNext.Length * 2);
                Array.Resize(ref _playerWtoOrderNext, cap);
                Array.Resize(ref _wtoBuyOrders, cap);
            }
            _wtoBuyCount = 0;
            for (int o = _wtoOrders.Count - 1; o >= 0; o--)
            {
                int pid = _wtoOrders.At(o).OwnerPlayerId;
                if (pid <= 0)
                {
                    _playerWtoOrderNext[o] = -1;
                    continue;
                }
                EnsurePlayerSlot(pid);
                _playerWtoOrderNext[o] = _playerFirstWtoOrder[pid];
                _playerFirstWtoOrder[pid] = o;
            }
            for (int o = 0; o < _wtoOrders.Count; o++)
            {
                ref var order = ref _wtoOrders.At(o);
                int slot = ItemIds.ToSlot(order.Item);
                if (order.OwnerPlayerId > 0)
                {
                    int bit = 1 << slot;
                    if (order.Side == OrderSide.Buy)
                        _playerBuyOrderMask[order.OwnerPlayerId] |= bit;
                    else
                        _playerSellOrderMask[order.OwnerPlayerId] |= bit;
                }
                if (order.Side == OrderSide.Buy)
                {
                    _wtoBuyOrders[_wtoBuyCount++] = o;
                }
                else
                {
                    if (_bestAskIdx1[slot] < 0)
                    {
                        _bestAskIdx1[slot] = o;
                        _bestAskPrice1[slot] = order.PriceCents;
                        _bestAskRem1[slot] = order.Remaining;
                        _bestAskOwner1[slot] = order.OwnerPlayerId;
                    }
                    else if (order.PriceCents < _bestAskPrice1[slot])
                    {
                        if (order.OwnerPlayerId != _bestAskOwner1[slot])
                        {
                            _bestAskIdx2[slot] = _bestAskIdx1[slot];
                            _bestAskPrice2[slot] = _bestAskPrice1[slot];
                            _bestAskRem2[slot] = _bestAskRem1[slot];
                        }
                        _bestAskIdx1[slot] = o;
                        _bestAskPrice1[slot] = order.PriceCents;
                        _bestAskRem1[slot] = order.Remaining;
                        _bestAskOwner1[slot] = order.OwnerPlayerId;
                    }
                    else if (order.OwnerPlayerId != _bestAskOwner1[slot] &&
                             (_bestAskIdx2[slot] < 0 || order.PriceCents < _bestAskPrice2[slot]))
                    {
                        _bestAskIdx2[slot] = o;
                        _bestAskPrice2[slot] = order.PriceCents;
                        _bestAskRem2[slot] = order.Remaining;
                    }
                }
            }
            if (_cachedBypassLaneCount != _lanes.Count)
            {
                _cachedBypassLaneCount = _lanes.Count;
                _bypassLaneTotal = 0;
                for (int i = 0; i < _lanes.Count; i++)
                {
                    if (_lanes.At(i).Tier != StarlaneTier.OuterRimBypass)
                        continue;
                    if (_bypassLaneTotal == _bypassLanes.Length)
                        Array.Resize(ref _bypassLanes, _bypassLaneTotal == 0 ? 64 : _bypassLaneTotal * 2);
                    _bypassLanes[_bypassLaneTotal++] = i;
                }
            }
        }

        private void ThinkFleet(in PlayerHq player, AiPreset preset, EntityId hq, EntityId fleet, long tick)
        {
            ref var unit = ref _units.Get(fleet);
            if (unit.IsConvoyOnly)
            {
                ThinkConvoy(player, preset, fleet, tick);
                return;
            }
            ref var motion = ref _motions.Get(fleet);
            ref var state = ref _combat.Get(fleet);
            ref var hold = ref _stockpiles.Get(fleet);
            var best = default(AiAction);
            int bestScore = 0;

            if (!state.IsVenting && state.TemperatureMilli >= preset.CoolantVentTempMilli && hold.Get(ItemId.ItemCoolant) > 0)
            {
                int heat = Severity(state.TemperatureMilli - preset.CoolantVentTempMilli,
                    _combatBalance.OverheatMilli - preset.CoolantVentTempMilli);
                Consider(AiAction.VentCoolant, Score(preset.DefenseHoldWeight, heat, Full), ref best, ref bestScore);
            }

            bool raid = WantsPiracy(preset, hq, in unit, tick);
            if (state.RaidsConvoys != raid && !_issuer.IsInFlight(player.PlayerId, CommandType.RaidConvoys, fleet))
                Consider(AiAction.RaidConvoys, Score(preset.StealthRaidWeight, Threshold), ref best, ref bestScore);

            bool awayFromHome = !motion.Site.IsNull && motion.Site != player.CoreBase;
            if (awayFromHome)
            {
                int fuel = hold.Get(ItemId.ItemFuel) > 0 ? Full : 0;
                long hostile = Power(motion.Site, player.PlayerId, mine: false);
                long ratio = hostile > 0 ? Power(motion.Site, player.PlayerId, mine: true) * Full / hostile : long.MaxValue;
                if (ratio < preset.RetreatForceRatioMilli)
                {
                    int danger = Severity(preset.RetreatForceRatioMilli - ratio, preset.RetreatForceRatioMilli);
                    Consider(AiAction.Retreat, Score(preset.EvacuateSafetyWeight, danger, fuel), ref best, ref bestScore);
                }

                long need = (long)unit.LineShips * _balance.ResupplyAmmoPerLineShip;
                int ammo = hold.Get(ItemId.ItemAmmo);
                if (ammo < need)
                {
                    int shortage = Severity(need - ammo, need);
                    Consider(AiAction.Resupply, Score(preset.ResupplyLogisticsWeight, shortage, fuel), ref best, ref bestScore);
                }
            }

            bool docked = motion.Lane.IsNull && !motion.Site.IsNull && state.EngagedUntilTick <= tick;
            bool ready = docked && (!_readiness.Has(fleet) || _readiness.Get(fleet).CanAct(tick));
            EntityId raidTarget = default, siegeTarget = default, citadelTarget = default, developTarget = default,
                rigDeposit = default, flankTo = default;
            var loadItem = ItemId.None;
            int loadQuantity = 0;
            StarId surveyStar = default;
            if (docked && motion.Site == player.CoreBase)
            {
                bool telemetry = NeedsTelemetry(hq);
                if (TryPickLoad(in player, hq, preset, telemetry, in unit, ref hold, out loadItem, out loadQuantity,
                        out int shortage))
                {
                    Consider(AiAction.LoadCargo, Score(preset.ResupplyLogisticsWeight, shortage), ref best, ref bestScore);
                }
                else if (IsBattleFleet(in unit) && CitadelFallen())
                {
                    // With the Citadel down the battle line fights like any fleet, but only while it has a target: awake, it
                    // owes five times the mothballed upkeep, so with nothing to raid, besiege or develop it goes back to sleep.
                    var war = AiAction.VentCoolant;
                    int warScore = 0;
                    ConsiderRaidAndSiege(in player, preset, in unit, ref hold, ref war, ref warScore, out raidTarget,
                        out siegeTarget, out developTarget);
                    bool mothballed = _readiness.Has(fleet) && _readiness.Get(fleet).IsMothballed;
                    if (mothballed && warScore > 0 && !_issuer.IsInFlight(player.PlayerId, CommandType.Activate, fleet))
                        Consider(AiAction.Activate, warScore, ref best, ref bestScore);
                    else if (ready && warScore > 0)
                        Consider(war, warScore, ref best, ref bestScore);
                    else if (ready && !_issuer.IsInFlight(player.PlayerId, CommandType.Mothball, fleet))
                        Consider(AiAction.Mothball, Score(preset.ResupplyLogisticsWeight, Threshold), ref best, ref bestScore);
                }
                else if (IsBattleFleet(in unit))
                {
                    // A full battle line that can't take the Citadel yet waits mothballed, at a fifth of the upkeep. It
                    // only sets out with the whole siege load: run dry under the shield, it dies to the next garrison.
                    bool mothballed = _readiness.Has(fleet) && _readiness.Get(fleet).IsMothballed;
                    int citadelEdge = 0;
                    bool canGo = preset.SiegeConquestWeight > 0 &&
                                 hold.Get(ItemId.ItemAmmo) >= AmmoToLoad(preset, in unit) &&
                                 FindCitadel(player, in unit, hold.Get(ItemId.ItemFuel), out citadelTarget, out citadelEdge);
                    if (canGo && mothballed && !_issuer.IsInFlight(player.PlayerId, CommandType.Activate, fleet))
                        Consider(AiAction.Activate, Score(preset.SiegeConquestWeight, citadelEdge), ref best, ref bestScore);
                    else if (canGo && ready)
                        Consider(AiAction.AssaultCitadel, Score(preset.SiegeConquestWeight, citadelEdge), ref best, ref bestScore);
                    else if (!canGo && !mothballed && unit.LineShips >= _balance.BattleLineShips &&
                             !_issuer.IsInFlight(player.PlayerId, CommandType.Mothball, fleet))
                        Consider(AiAction.Mothball, Score(preset.ResupplyLogisticsWeight, Threshold), ref best, ref bestScore);
                }
                else if (ready)
                {
                    ConsiderRaidAndSiege(in player, preset, in unit, ref hold, ref best, ref bestScore, out raidTarget,
                        out siegeTarget, out developTarget);
                    if (siegeTarget.IsNull && developTarget.IsNull && !IsHomeSettler(preset) &&
                        hold.Get(ItemId.ItemFuel) >= _galaxy.Catalog.Balance.SurveyFuel && CarriesClaimCost(ref hold, 0) &&
                        _galaxy.TryPickSurvey(player.PlayerId, _sites.Get(motion.Site).ClusterId, out surveyStar))
                        Consider(AiAction.Survey, Score(preset.SiegeConquestWeight, Threshold), ref best, ref bestScore);
                }
            }
            else if (ready && motion.Site != player.CoreBase && FlankWeight(preset) > 0 &&
                     Power(motion.Site, player.PlayerId, mine: false) == 0 && !IsStandingCitadel(motion.Site) &&
                     FindFlankStep(player.PlayerId, fleet, in unit, motion.Site, hold.Get(ItemId.ItemFuel), out flankTo))
            {
                Consider(AiAction.FlankMove, Score(FlankWeight(preset), Full), ref best, ref bestScore);
            }
            // At a standing Citadel the fleet stays: its docked line ships bombard the shield until ammo runs low.
            else if (ready && IsAway(motion.Site, player.PlayerId) && Power(motion.Site, player.PlayerId, mine: false) == 0 &&
                     !IsStandingCitadel(motion.Site))
            {
                if (SettleWeight(preset) > 0 && IsClaimable(motion.Site, player.PlayerId) && CarriesClaimCost(ref hold, 0) &&
                    (!IsHomeSettler(preset) || IsNeutralAtHome(motion.Site, player.CoreBase)))
                    Consider(AiAction.ClaimSite, Score(SettleWeight(preset), Full), ref best, ref bestScore);
                else
                    Consider(AiAction.ReturnHome, Score(preset.ResupplyLogisticsWeight, Threshold), ref best, ref bestScore);
            }
            else if (ready && motion.Site != player.CoreBase && IsOwnSite(motion.Site, player.PlayerId) &&
                     Power(motion.Site, player.PlayerId, mine: false) == 0)
            {
                var develop = SettleWeight(preset) > 0
                    ? PickDevelopment(in player, motion.Site, ref hold, out rigDeposit, out loadItem, out loadQuantity)
                    : default(AiAction);
                if (develop != default)
                    Consider(develop, Score(SettleWeight(preset), Full), ref best, ref bestScore);
                else if (hold.Get(ItemId.ItemFuel) > 0)
                    Consider(AiAction.ReturnHome, Score(preset.ResupplyLogisticsWeight, Threshold), ref best, ref bestScore);
            }

            if (bestScore == 0)
                return;
            if ((best == AiAction.StealthRaid || best == AiAction.Siege) &&
                PlanFlank(player.PlayerId, fleet, in unit, motion.Site, best == AiAction.StealthRaid ? raidTarget : siegeTarget,
                    hold.Get(ItemId.ItemFuel), out var flank, out flankTo))
                best = flank;
            var command = new DelayedPlayerCommand { PlayerId = player.PlayerId, Target = fleet };
            switch (best)
            {
                case AiAction.VentCoolant:
                    command.CommandType = CommandType.VentCoolant;
                    break;
                case AiAction.Mothball:
                    command.CommandType = CommandType.Mothball;
                    break;
                case AiAction.Activate:
                    command.CommandType = CommandType.Activate;
                    break;
                case AiAction.LoadCargo:
                    command.CommandType = CommandType.TransferCargo;
                    command.ParamA = (int)loadItem;
                    command.ParamB = loadQuantity;
                    command.ParamC = 1; // fill up to: an order re-issued while in flight changes nothing
                    break;
                case AiAction.StealthRaid:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = raidTarget;
                    command.ParamC = TollFreeFlag(motion.Site, raidTarget, in unit, trips: 2);
                    break;
                case AiAction.Siege:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = siegeTarget;
                    command.ParamC = TollFreeFlag(motion.Site, siegeTarget, in unit, trips: 2);
                    break;
                case AiAction.AssaultCitadel:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = citadelTarget;
                    command.ParamC = TollFreeFlag(motion.Site, citadelTarget, in unit, trips: 1);
                    break;
                case AiAction.DevelopOutpost:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = developTarget;
                    command.ParamC = TollFreeFlag(motion.Site, developTarget, in unit, trips: 2);
                    break;
                case AiAction.FlankSplit:
                    command.CommandType = CommandType.SplitFleet;
                    command.ParamA = unit.LineShips / 2;
                    command.ParamB = unit.EwShips / 2;
                    command.ParamC = unit.SupplyTenders / 2;
                    break;
                case AiAction.FlankMove:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = flankTo;
                    command.ParamC = _sites.Has(flankTo) ? TollFreeFlag(motion.Site, flankTo, in unit, trips: 2) : 0;
                    break;
                case AiAction.ClaimSite:
                    command.CommandType = CommandType.ClaimSite;
                    command.Target = motion.Site;
                    break;
                case AiAction.UnloadCargo:
                    command.CommandType = CommandType.TransferCargo;
                    command.ParamA = (int)loadItem;
                    command.ParamB = -loadQuantity;
                    break;
                case AiAction.BuildRig:
                    command.CommandType = CommandType.BuildRig;
                    command.Target = rigDeposit;
                    command.ParamA = loadQuantity;
                    break;
                case AiAction.RaidConvoys:
                    command.CommandType = CommandType.RaidConvoys;
                    command.ParamA = raid ? 1 : 0;
                    break;
                case AiAction.Survey:
                    command.CommandType = CommandType.Survey;
                    StarId.Split(surveyStar.Packed, out command.ParamA, out command.ParamB);
                    break;
                default:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = player.CoreBase;
                    break;
            }
            Issue(command, best, bestScore, fleet);
        }

        // The first item the fleet is short of at the core base that the base can give: its upkeep, ammo, fuel, microchips for storm
        // telemetry while researching, then the claim cost when the preset besieges. Shortage reads 500 for any gap,
        // 1000 for an empty hold.
        private bool TryPickLoad(
            in PlayerHq player, EntityId hq, AiPreset preset, bool telemetry, in FleetUnit unit, ref Stockpile hold,
            out ItemId item, out int quantity, out int shortage)
        {
            item = ItemId.None;
            quantity = 0;
            shortage = 0;
            _cachedSiegeValid = false;
            var core = player.CoreBase;
            if (!_stockpiles.Has(core))
                return false;
            ref var stock = ref _stockpiles.Get(core);
            // Upkeep drains the hold even while docked, so it is topped up only below half the target; else the fleet
            // would reload every interval and never leave.
            var upkeep = _fleetBalance.Tier(unit.TechTier).UpkeepPerShipHour;
            for (int k = 0; k < upkeep.Length; k++)
            {
                long target = (long)FleetUpkeepSystem.Due(unit.TotalShips, upkeep[k].Milli, 0) * _balance.LoadUpkeepIntervals;
                if (hold.Get(upkeep[k].Item) * 2 < target &&
                    TryLoad(ref stock, hq, ref hold, upkeep[k].Item, target, ref item, ref quantity, ref shortage))
                    return true;
            }
            if ((CanGive(ref stock, hq, ItemId.ItemAmmo) &&
                 TryLoad(ref stock, hq, ref hold, ItemId.ItemAmmo, AmmoToLoad(preset, in unit), ref item, ref quantity,
                     ref shortage)) ||
                (unit.EwShips > 0 && CanGive(ref stock, hq, ItemId.ItemBattery) &&
                 TryLoad(ref stock, hq, ref hold, ItemId.ItemBattery,
                     EwBatteries(in unit) + (SettleWeight(preset) > 0 ? ExpansionLoad(ItemId.ItemBattery) : 0), ref item,
                     ref quantity, ref shortage)) ||
                (CanGive(ref stock, hq, ItemId.ItemFuel) &&
                 TryLoad(ref stock, hq, ref hold, ItemId.ItemFuel, FuelToLoad(in player, preset, in unit), ref item, ref quantity,
                     ref shortage)) ||
                (telemetry && TryLoad(ref stock, hq, ref hold, ItemId.ItemMicrochip, _researchBalance.TelemetryMinMicrochips,
                    ref item, ref quantity, ref shortage)))
                return true;
            if (SettleWeight(preset) == 0)
                return false;
            for (int i = 0; i < _claimBalance.ClaimCostCount; i++)
            {
                var id = _claimBalance.ClaimCost(i).Item;
                if (TryLoad(ref stock, hq, ref hold, id, ExpansionLoad(id), ref item, ref quantity, ref shortage))
                    return true;
            }
            for (int i = 0; i < _miningBalance.RigCostCount; i++)
            {
                var id = _miningBalance.RigCost(i).Item;
                if (TryLoad(ref stock, hq, ref hold, id, ExpansionLoad(id), ref item, ref quantity, ref shortage))
                    return true;
            }
            return false;
        }

        // The standard ammo load, plus for an assaulting preset's battle fleet the ammo to bombard the weakest standing
        // Citadel's shield down: a fleet that turns back with the shield up sees it regenerate.
        private long AmmoToLoad(AiPreset preset, in FleetUnit unit)
        {
            long ammo = (long)unit.LineShips * _balance.LoadAmmoPerLineShip;
            if (!IsBattleFleet(in unit) || preset.SiegeConquestWeight == 0 || unit.LineShips == 0)
                return ammo;
            long shield = long.MaxValue;
            for (int c = 0; c < _citadels.Count; c++)
                if (_citadels.At(c).FallenTick == 0)
                    shield = Math.Min(shield, _citadels.At(c).Integrity);
            if (shield == long.MaxValue)
                return ammo;
            long perCheck = (long)unit.LineShips * _citadelBalance.BombardPerLineShip(unit.TechTier);
            long checks = (shield + perCheck - 1) / perCheck;
            return ammo + checks * unit.LineShips * _citadelBalance.AmmoPerLineShip;
        }

        // The standard fuel load, or a round trip to where the fleet goes next if that is further: the site it would
        // settle, or for an assaulting preset's battle fleet the standing Citadel.
        private long FuelToLoad(in PlayerHq player, AiPreset preset, in FleetUnit unit)
        {
            long fuel = _balance.LoadFuel;
            if (IsBattleFleet(in unit))
            {
                if (preset.SiegeConquestWeight > 0)
                    for (int c = 0; c < _citadels.Count; c++)
                        if (_citadels.At(c).FallenTick == 0 &&
                            PlanTrip(player.CoreBase, _citadels.EntityAt(c), in unit, trips: 1))
                            fuel = Math.Max(fuel, 2L * RouteFuel(in unit));
            }
            else if (SettleWeight(preset) > 0)
            {
                _cachedSiegeHas = FindTarget(player, in unit, int.MaxValue, raid: false, homeOnly: IsHomeSettler(preset),
                    out _cachedSiegeTarget, out _cachedSiegeEdge, out _cachedSiegeRtFuel);
                _cachedSiegeValid = true;
                if (_cachedSiegeHas)
                    fuel = Math.Max(fuel, _cachedSiegeRtFuel);
            }
            return fuel;
        }

        // What a besieging fleet carries of an item: the claim cost plus the rigs for every deposit of a site.
        private long ExpansionLoad(ItemId item)
        {
            long total = 0;
            for (int i = 0; i < _claimBalance.ClaimCostCount; i++)
                if (_claimBalance.ClaimCost(i).Item == item)
                    total += _claimBalance.ClaimCost(i).Quantity;
            for (int i = 0; i < _miningBalance.RigCostCount; i++)
                if (_miningBalance.RigCost(i).Item == item)
                    total += (long)_miningBalance.RigCost(i).Quantity * _balance.OutpostRigs * _newGame.DepositsPerSite;
            return total;
        }

        // At an own outpost, for the bare deposit with ore left that matters most (see DepositRank; one already mined that
        // no core refinery needs isn't rigged): rigs paid from the site, or else unload the first rig-cost item the site
        // is short of, if the hold carries the gap.
        private AiAction PickDevelopment(
            in PlayerHq player, EntityId site, ref Stockpile hold, out EntityId deposit, out ItemId item, out int quantity)
        {
            deposit = default;
            item = ItemId.None;
            quantity = 0;
            int mined = MinedOres(player.PlayerId);
            int feeds = NeededRaws(in player);
            int bestRank = 0;
            for (int d = FirstSiteDeposit(site); d >= 0; d = _depositNext[d])
            {
                ref var candidate = ref _deposits.At(d);
                if (candidate.RigCount != 0 || candidate.RetractUntilTick != 0 || !HasOre(in candidate))
                    continue;
                int rank = DepositRank(candidate.RawItem, mined, feeds);
                if (rank <= bestRank)
                    continue;
                bestRank = rank;
                deposit = _deposits.EntityAt(d);
            }
            if (deposit.IsNull || !_stockpiles.Has(site))
                return default;
            int rigs = Math.Min(_balance.OutpostRigs, _miningBalance.MaxRigsPerDeposit);
            ref var stock = ref _stockpiles.Get(site);
            for (int i = 0; i < _miningBalance.RigCostCount; i++)
            {
                var cost = _miningBalance.RigCost(i);
                long gap = (long)cost.Quantity * rigs - stock.Get(cost.Item);
                if (gap <= 0)
                    continue;
                if (hold.Get(cost.Item) < gap)
                    return default;
                item = cost.Item;
                quantity = (int)gap;
                return AiAction.UnloadCargo;
            }
            quantity = rigs;
            return AiAction.BuildRig;
        }

        // A convoy-only fleet without a route: at the core base, take an outpost no convoy serves (one mining a starved raw
        // first); elsewhere go home. One docked at the core base on a route whose outpost mines nothing starved switches to
        // an unserved outpost that does, so a lone convoy never leaves the core short of the ore its next convoy is built from.
        private void ThinkConvoy(in PlayerHq player, AiPreset preset, EntityId fleet, long tick)
        {
            ref var motion = ref _motions.Get(fleet);
            if (!motion.Lane.IsNull || motion.Site.IsNull || _combat.Get(fleet).EngagedUntilTick > tick)
                return;
            var command = new DelayedPlayerCommand { PlayerId = player.PlayerId, Target = fleet };
            if (_convoyRoutes.Has(fleet))
            {
                ref var route = ref _convoyRoutes.Get(fleet);
                if (motion.Site != player.CoreBase || route.Hub != player.CoreBase)
                    return;
                int starved = StarvedRaws(in player);
                if (starved == 0 || (OutpostRaws(route.Outpost) & starved) != 0)
                    return;
                var needed = FindUnservedOutpost(in player, _units.Get(fleet).TechTier, starved);
                if (needed.IsNull)
                    return;
                command.CommandType = CommandType.SetConvoyRoute;
                command.ParamEntity = needed;
                command.ParamC = route.Batch;
                Issue(command, AiAction.SetConvoyRoute, Score(preset.ResupplyLogisticsWeight, Full), fleet);
                return;
            }
            if (motion.Site != player.CoreBase)
            {
                if (_readiness.Has(fleet) && !_readiness.Get(fleet).CanAct(tick))
                    return;
                command.CommandType = CommandType.Move;
                command.ParamEntity = player.CoreBase;
                Issue(command, AiAction.ReturnHome, Score(preset.ResupplyLogisticsWeight, Threshold), fleet);
                return;
            }
            byte tier = _units.Get(fleet).TechTier;
            var outpost = FindUnservedOutpost(in player, tier, StarvedRaws(in player));
            if (outpost.IsNull)
                outpost = FindUnservedOutpost(in player, tier, 0);
            if (outpost.IsNull)
                return;
            command.CommandType = CommandType.SetConvoyRoute;
            command.ParamEntity = outpost;
            command.ParamC = (int)Math.Min((long)_units.Get(fleet).CargoConvoys * _convoyBalance.CapacityPerConvoy, _balance.ConvoyBatch);
            Issue(command, AiAction.SetConvoyRoute, Score(preset.ResupplyLogisticsWeight, Full), fleet);
        }

        // The first own site (not the core base) with a rigged deposit, a route from the core base and no convoy route;
        // with raws set, only one whose rigs mine one of them.
        private EntityId FindUnservedOutpost(in PlayerHq player, byte tier, int raws)
        {
            for (int i = FirstPlayerSite(player.PlayerId); i >= 0; i = _playerSiteNext[i])
            {
                var site = _sites.EntityAt(i);
                if (site == player.CoreBase || !HasRigs(site) ||
                    (raws != 0 && (OutpostRaws(site) & raws) == 0) || IsServed(player.PlayerId, site) ||
                    !_routes.TryPlan(player.CoreBase, site, tier, ref _route))
                    continue;
                return site;
            }
            return default;
        }

        private bool HasRigs(EntityId site) =>
            !site.IsNull && site.Index < _siteRigsStamp.Length && _siteRigsStamp[site.Index] == _thinkStamp;

        // Bit per raw item slot the rigs at the site mine (with ore left).
        private int OutpostRaws(EntityId site) =>
            !site.IsNull && site.Index < _siteRigsStamp.Length && _siteRigsStamp[site.Index] == _thinkStamp
                ? _siteOutpostRaws[site.Index]
                : 0;

        // Bit per needed raw (see NeededRaws) the core base holds less than a convoy batch of.
        private int StarvedRaws(in PlayerHq player)
        {
            int feeds = NeededRaws(in player);
            if (feeds == 0 || !_stockpiles.Has(player.CoreBase))
                return 0;
            ref var stock = ref _stockpiles.Get(player.CoreBase);
            int mask = 0;
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                if (ItemIds.IsRaw(item) && (feeds & (1 << slot)) != 0 && stock.Get(item) < _balance.ConvoyBatch)
                    mask |= 1 << slot;
            }
            return mask;
        }

        // A convoy route runs to the outpost, or a SetConvoyRoute there waits out its latency.
        private bool IsServed(int playerId, EntityId outpost)
        {
            if (!outpost.IsNull && outpost.Index < _siteServedStamp.Length && _siteServedStamp[outpost.Index] == _thinkStamp)
                return true;
            return _issuer.IsInFlightTo(playerId, CommandType.SetConvoyRoute, outpost);
        }

        // Player economy (Resupply_Logistics): a convoy fleet for an unserved outpost, then refineries for new items.
        private void ThinkEconomy(in PlayerHq player, AiPreset preset, EntityId hq)
        {
            if (preset.ResupplyLogisticsWeight == 0 || !_stockpiles.Has(player.CoreBase))
                return;
            if (!_standings.Has(hq) || !_standings.Get(hq).IsAudited)
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.AuditOptIn, Target = player.CoreBase, ParamA = 1
                }, AiAction.AuditOptIn, Score(preset.ResupplyLogisticsWeight, Threshold));
            ref var stock = ref _stockpiles.Get(player.CoreBase);
            const byte convoyTier = 1;
            if (!FindUnservedOutpost(in player, convoyTier, 0).IsNull && !HasIdleConvoy(player.PlayerId) &&
                !HasShipOrder(player.PlayerId, TargetFocusClass.CargoConvoys) &&
                Affordable(ref stock, hq, convoyTier, 100) >= _balance.ConvoyShips)
            {
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.BuildShips, Target = player.CoreBase,
                    ParamA = (int)TargetFocusClass.CargoConvoys, ParamB = _balance.ConvoyShips, ParamC = convoyTier
                }, AiAction.BuildConvoy, Score(preset.ResupplyLogisticsWeight, Full));
                return;
            }

            int built = !player.CoreBase.IsNull && player.CoreBase.Index < _siteRefineryStamp.Length &&
                        _siteRefineryStamp[player.CoreBase.Index] == _thinkStamp
                ? _siteRefineryCount[player.CoreBase.Index]
                : 0;
            if (built >= _refineryBalance.MaxRefineriesPerSite)
                return;
            for (int i = 0; i < _refineryBalance.BuildCostCount; i++)
            {
                var cost = _refineryBalance.BuildCost(i);
                if (stock.Get(cost.Item) < cost.Quantity + ProgressReserve(hq, cost.Item))
                    return;
            }
            // What the next stress test needs comes first, once the player mines both raw inputs: another refinery may
            // eat a shared input before it piles up at the core.
            if (TryNextStressTest(hq, out _, out var stressCost))
            {
                int mined = MinedOres(player.PlayerId);
                for (int i = 0; i < stressCost.Length; i++)
                {
                    var output = stressCost[i].Item;
                    if (ItemIds.IsRaw(output) || HasRefinery(player.CoreBase, output) || stock.Has(output, stressCost[i].Quantity))
                        continue;
                    ref readonly var recipe = ref _recipes.GetRecipe(output);
                    int raws = RecipeRaws(output);
                    if (!ItemIds.IsRaw(recipe.Input1) || !ItemIds.IsRaw(recipe.Input2) || (raws & ~mined) != 0)
                        continue;
                    Issue(new DelayedPlayerCommand
                    {
                        PlayerId = player.PlayerId, CommandType = CommandType.BuildRefinery, Target = player.CoreBase,
                        ParamA = (int)output
                    }, AiAction.BuildRefinery, Score(preset.ResupplyLogisticsWeight, Full));
                    return;
                }
            }
            for (int slot = ItemIds.RawCount; slot < ItemIds.Count; slot++)
            {
                var output = ItemIds.FromSlot(slot);
                ref readonly var recipe = ref _recipes.GetRecipe(output);
                if (HasRefinery(player.CoreBase, output) || stock.Get(recipe.Input1) < _balance.RefineryInputStock ||
                    stock.Get(recipe.Input2) < _balance.RefineryInputStock)
                    continue;
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.BuildRefinery, Target = player.CoreBase,
                    ParamA = (int)output
                }, AiAction.BuildRefinery, Score(preset.ResupplyLogisticsWeight, Full));
                return;
            }
        }

        // THE SYSTEM's credit line: below the travel cash it borrows up to the cash reserve (within its dynamic credit limit),
        // repays from cash above twice it, and once the debt falls due (or is about to, within an AI think of it) repays all
        // it can, so its goods aren't seized.
        private void ThinkFinance(in PlayerHq player, AiPreset preset, EntityId hq, long tick)
        {
            if (!_loans.Has(hq))
                return;
            ref var loan = ref _loans.Get(hq);
            long cash = _ledger.Balance(hq);
            long reserve = _balance.CashReserveCredits * Wallet.CentsPerCredit;
            long repay = 0, borrow = 0;
            if (loan.DebtCents > 0 && tick + _balance.ThinkIntervalTicks >= loan.DueTick)
                repay = Math.Min(cash, loan.DebtCents);
            else if (cash < _balance.TravelCashCredits * Wallet.CentsPerCredit)
                borrow = Math.Min(reserve - cash, Math.Max(0, CreditLimitCents(player.PlayerId, hq) - loan.DebtCents));
            else if (cash > 2 * reserve)
                repay = Math.Min(cash - reserve, loan.DebtCents);
            // Commands carry whole credits: borrow rounds up, repay down.
            int credits = (int)Math.Min(int.MaxValue,
                borrow > 0 ? (borrow + Wallet.CentsPerCredit - 1) / Wallet.CentsPerCredit : repay / Wallet.CentsPerCredit);
            if (credits <= 0)
                return;
            var type = borrow > 0 ? CommandType.Borrow : CommandType.Repay;
            Issue(new DelayedPlayerCommand { PlayerId = player.PlayerId, CommandType = type, Target = player.CoreBase, ParamA = credits },
                borrow > 0 ? AiAction.Borrow : AiAction.Repay, Score(preset.ResupplyLogisticsWeight, Threshold));
        }

        private long CreditLimitCents(int playerId, EntityId hq)
        {
            int techSteps = 0;
            if (_progress.Has(hq))
            {
                ref var tech = ref _progress.Get(hq);
                techSteps = Math.Max(0, tech.TechTier - 1) + Math.Max(0, tech.IndustryTier - 1) +
                            Math.Max(0, tech.LogisticsTier - 1) + Math.Max(0, tech.SignalsTier - 1);
            }
            bool valid = playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp;
            return _ledgerBalance.DynamicCreditLimitCents(
                techSteps,
                valid && _playerOpenGate[playerId],
                valid ? _playerOutpostCount[playerId] : 0,
                valid ? _playerRefineryCount[playerId] : 0);
        }

        // WTO: cancels its orders open past the trade window; sells surplus into a rival's bid, or else, under the travel
        // cash, sells a batch to THE SYSTEM's standing bid, or else lists one batch under the reference price; bids over it
        // for the first item the next stress test still lacks, or else (nothing to bid for), debt-free with cash above
        // twice its reserve (idle money, which would otherwise repay debt), buys the cheapest rival listing of a raw it feeds
        // and holds under the sell threshold, or else a batch of one from THE SYSTEM's stock. A stress-test shortfall THE
        // SYSTEM holds is bought from it at once rather than bid for. Orders are sized to what the cash above the travel cash can lock: the seller's escrow deposit, the
        // buyer's payment from its bid budget (of the idle cash, for feedstock). One order of a kind in flight at a time.
        private void ThinkTrade(in PlayerHq player, AiPreset preset, EntityId hq, long tick)
        {
            if (!_stockpiles.Has(player.CoreBase) || !_ledger.TryFindPlayerWallet(player.PlayerId, out var wallet))
                return;
            int weight = preset.ResupplyLogisticsWeight;
            for (int o = FirstPlayerWtoOrder(player.PlayerId); o >= 0; o = _playerWtoOrderNext[o])
            {
                ref var order = ref _wtoOrders.At(o);
                bool lowFuel = order.Side == OrderSide.Sell && FirstPlayerDeposit(player.PlayerId) >= 0 &&
                               _stockpiles.Has(order.Site) && _stockpiles.Get(order.Site).Get(ItemId.ItemFuel) <= _balance.CoreSupplyReserve;
                if ((!lowFuel && tick - order.PlacedTick < _balance.TradeOrderTicks) ||
                    _issuer.IsInFlight(player.PlayerId, CommandType.CancelOrder, order.Site))
                    continue;
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.CancelOrder, Target = order.Site,
                    ParamEntity = _wtoOrders.EntityAt(o)
                }, AiAction.CancelOrder, Score(weight, Threshold));
                break;
            }

            ref var stock = ref _stockpiles.Get(player.CoreBase);
            long cash = _ledger.Balance(wallet);
            long spare = Math.Max(0, cash - _balance.TravelCashCredits * Wallet.CentsPerCredit);
            int feeds = NeededRaws(in player);
            bool listing = _issuer.IsInFlight(player.PlayerId, CommandType.PlaceSellOrder, player.CoreBase);
            if (!listing && FindBidToFill(player, hq, ref stock, feeds, out var sellItem, out long ask, out long wanted))
                IssueSell(player.PlayerId, player.CoreBase, sellItem, ask, wanted, spare, weight);
            else if (cash < _balance.TravelCashCredits * Wallet.CentsPerCredit)
            {
                if (!_issuer.IsInFlight(player.PlayerId, CommandType.SellToSystem, player.CoreBase) &&
                    FindSurplus(player.PlayerId, hq, ref stock, feeds, out var dumpItem))
                    Issue(new DelayedPlayerCommand
                    {
                        PlayerId = player.PlayerId, CommandType = CommandType.SellToSystem, Target = player.CoreBase,
                        ParamA = (int)dumpItem, ParamB = _balance.SellBatch
                    }, AiAction.SellToSystem, Score(weight, Threshold));
            }
            else if (!listing && FindSurplusToList(player.PlayerId, hq, ref stock, feeds, tick, out sellItem, out ask, out wanted))
                IssueSell(player.PlayerId, player.CoreBase, sellItem, ask, wanted, spare, weight);

            if (_issuer.IsInFlight(player.PlayerId, CommandType.PlaceBuyOrder, player.CoreBase))
                return;
            if (TryNextStressTest(hq, out _, out var cost))
            {
                for (int i = 0; i < cost.Length; i++)
                {
                    int missing = cost[i].Quantity - stock.Get(cost[i].Item);
                    if (missing <= 0 || HasOpenOrder(player.PlayerId, cost[i].Item, OrderSide.Buy))
                        continue;
                    if (BuyFromSystem(player.PlayerId, player.CoreBase, cost[i].Item, missing, spare, tick, weight))
                        return;
                    long price = TradePrice(cost[i].Item, tick, _balance.TradePricePercent);
                    long quantity = Math.Min(missing, spare * _balance.BidBudgetPercent / 100 / price);
                    if (quantity == 0)
                        return;
                    Issue(new DelayedPlayerCommand
                    {
                        PlayerId = player.PlayerId, CommandType = CommandType.PlaceBuyOrder, Target = player.CoreBase,
                        ParamA = (int)cost[i].Item, ParamB = (int)quantity, ParamC = (int)price
                    }, AiAction.BuyShortfall, Score(weight, Full));
                    return;
                }
            }

            // A raw it feeds, down to under a batch (its deposits mined out, or none it can reach), is bought even in debt,
            // from the cash above the travel cash: from THE SYSTEM, else off the cheapest rival listing. Without it the
            // core's refineries stop.
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var raw = ItemIds.FromSlot(slot);
                bool needSupply = FirstPlayerDeposit(player.PlayerId) >= 0 &&
                                  (raw == ItemId.ItemFuel || raw == ItemId.ItemCoolant) &&
                                  stock.Get(raw) < _balance.CoreSupplyReserve;
                if (((Feeds(feeds, raw) && stock.Get(raw) < _balance.SellBatch) || needSupply) &&
                    (BuyFromSystem(player.PlayerId, player.CoreBase, raw, _balance.SellBatch, spare, tick, weight) ||
                     LiftListing(player.PlayerId, player.CoreBase, raw, spare, weight)))
                    return;
            }

            long idle = cash - 2 * _balance.CashReserveCredits * Wallet.CentsPerCredit;
            if (idle <= 0 || (_loans.Has(hq) && _loans.Get(hq).DebtCents > 0))
                return;
            if (!FindListingToBuy(player.PlayerId, ref stock, feeds, out var feedItem, out long feedAsk, out int feedWanted))
            {
                for (int slot = 0; slot < ItemIds.Count; slot++)
                {
                    var raw = ItemIds.FromSlot(slot);
                    if (Feeds(feeds, raw) && stock.Get(raw) < _balance.SellAbove &&
                        BuyFromSystem(player.PlayerId, player.CoreBase, raw, _balance.SellBatch, idle, tick, weight))
                        return;
                }
                return;
            }
            long feedQuantity = Math.Min(feedWanted, idle * _balance.BidBudgetPercent / 100 / feedAsk);
            if (feedQuantity > 0)
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.PlaceBuyOrder, Target = player.CoreBase,
                    ParamA = (int)feedItem, ParamB = (int)feedQuantity, ParamC = (int)feedAsk
                }, AiAction.BuyFeedstock, Score(weight, Threshold));
        }

        private bool TryBestRivalAsk(int playerId, int slot, out long askPrice, out int askRem, out int askIdx)
        {
            if (_bestAskIdx1[slot] >= 0 && _bestAskOwner1[slot] != playerId)
            {
                askPrice = _bestAskPrice1[slot];
                askRem = _bestAskRem1[slot];
                askIdx = _bestAskIdx1[slot];
                return true;
            }
            if (_bestAskIdx2[slot] >= 0)
            {
                askPrice = _bestAskPrice2[slot];
                askRem = _bestAskRem2[slot];
                askIdx = _bestAskIdx2[slot];
                return true;
            }
            askPrice = 0;
            askRem = 0;
            askIdx = -1;
            return false;
        }

        // A bid at the cheapest rival ask for the item, for up to a sell batch of it, from the bid budget of the cash given;
        // none while the player already bids for it.
        private bool LiftListing(int playerId, EntityId coreBase, ItemId item, long cash, int weight)
        {
            if (HasOpenOrder(playerId, item, OrderSide.Buy) ||
                !TryBestRivalAsk(playerId, ItemIds.ToSlot(item), out long price, out int remaining, out _))
                return false;
            long quantity = remaining == 0 ? 0 : Math.Min(Math.Min(remaining, _balance.SellBatch), cash * _balance.BidBudgetPercent / 100 / price);
            if (quantity <= 0)
                return false;
            Issue(new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = CommandType.PlaceBuyOrder, Target = coreBase, ParamA = (int)item,
                ParamB = (int)quantity, ParamC = (int)price
            }, AiAction.BuyFeedstock, Score(weight, Threshold));
            return true;
        }

        // Up to wanted from THE SYSTEM's stock at its ask, from the bid budget of the cash given; one such order in flight.
        private bool BuyFromSystem(int playerId, EntityId coreBase, ItemId item, long wanted, long cash, long tick, int weight)
        {
            if (_issuer.IsInFlight(playerId, CommandType.BuyFromSystem, coreBase))
                return false;
            long price = TradePrice(item, tick, _wtoBalance.SystemAskPercent - 100);
            long quantity = Math.Min(Math.Min(wanted, _stockpiles.Get(_systemStock).Get(item)),
                cash * _balance.BidBudgetPercent / 100 / price);
            if (quantity <= 0)
                return false;
            Issue(new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = CommandType.BuyFromSystem, Target = coreBase, ParamA = (int)item,
                ParamB = (int)quantity
            }, AiAction.BuyFromSystem, Score(weight, Threshold));
            return true;
        }

        // A sell order sized to the escrow deposit the spare cash can lock (the engine rounds the whole deposit down, so
        // the cap is priced the same way).
        private void IssueSell(int playerId, EntityId coreBase, ItemId item, long ask, long wanted, long spare, int weight)
        {
            long depositPercent = ask * _rates.EscrowRatePercent(playerId);
            long quantity = depositPercent > 0 ? Math.Min(wanted, spare * 100 / depositPercent) : wanted;
            if (quantity > 0)
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = playerId, CommandType = CommandType.PlaceSellOrder, Target = coreBase,
                    ParamA = (int)item, ParamB = (int)quantity, ParamC = (int)ask
                }, AiAction.SellSurplus, Score(weight, Threshold));
        }

        // What it keeps of an item before selling: a raw it feeds up to the stock its drills stop at, else the threshold.
        private int Keep(int playerId, int feeds, ItemId item, int threshold) =>
            Feeds(feeds, item) || (IsCoreSupply(item) && (LowCoreOres(playerId) & RecipeRaws(item)) != 0)
                ? _balance.RawStockCap
                : threshold;

        private static bool IsCoreSupply(ItemId item) =>
            item == ItemId.ItemFuel || item == ItemId.ItemCoolant || item == ItemId.ItemOxygen || item == ItemId.ItemRations;

        // A raw a core refinery or the next stress test takes in: kept, since a deposit runs out and its fuel is made from it.
        private static bool Feeds(int feeds, ItemId item) =>
            ItemIds.IsRaw(item) && (feeds & (1 << ItemIds.ToSlot(item))) != 0;

        // Core stock above a threshold and what the next stress test and the gate opening need.
        private int Surplus(int playerId, EntityId hq, ref Stockpile stock, ItemId item, int keep) =>
            stock.Get(item) - ProgressReserve(hq, item) - GateReserve(playerId, item) - keep;

        // The best rival bid for an item the player has surplus of (see Keep) and isn't already selling,
        // at a site a contract convoy
        // can reach from the core base (else the contract is void) on fuel the core holds above its reserve (else the
        // convoy strands and the contract fails): sold into at its price.
        private bool FindBidToFill(in PlayerHq player, EntityId hq, ref Stockpile stock, int feeds, out ItemId item,
            out long price, out long wanted)
        {
            item = ItemId.None;
            price = 0;
            wanted = 0;
            for (int b = 0; b < _wtoBuyCount; b++)
            {
                ref var bid = ref _wtoOrders.At(_wtoBuyOrders[b]);
                if (bid.OwnerPlayerId == player.PlayerId || bid.PriceCents <= price)
                    continue;
                int surplus = Surplus(player.PlayerId, hq, ref stock, bid.Item, Keep(player.PlayerId, feeds, bid.Item, _balance.FillAbove));
                if (surplus <= 0 || HasOpenOrder(player.PlayerId, bid.Item, OrderSide.Sell) ||
                    !_routes.TryPlan(player.CoreBase, bid.Site, WtoContractSystem.ConvoyTier, ref _route) ||
                    stock.Get(ItemId.ItemFuel) - _balance.CoreSupplyReserve < RouteFuel(in ContractConvoy))
                    continue;
                item = bid.Item;
                price = bid.PriceCents;
                wanted = Math.Min(Math.Min(bid.Remaining, surplus), _balance.SellBatch);
            }
            return wanted > 0;
        }

        // With no listing of its own open, a batch of the first item in surplus by a whole batch (see Keep), under
        // the reference price.
        private bool FindSurplusToList(int playerId, EntityId hq, ref Stockpile stock, int feeds, long tick, out ItemId item,
            out long price, out long wanted)
        {
            item = ItemId.None;
            price = 0;
            wanted = 0;
            if (playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp &&
                _playerSellOrderMask[playerId] != 0)
                return false;
            if ((FirstPlayerDeposit(playerId) >= 0 && stock.Get(ItemId.ItemFuel) <= _balance.CoreSupplyReserve) ||
                !FindSurplus(playerId, hq, ref stock, feeds, out item))
                return false;
            price = TradePrice(item, tick, -_balance.TradePricePercent);
            wanted = _balance.SellBatch;
            return true;
        }

        // The first item in surplus by a whole batch.
        private bool FindSurplus(int playerId, EntityId hq, ref Stockpile stock, int feeds, out ItemId item)
        {
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                item = ItemIds.FromSlot(slot);
                if (Surplus(playerId, hq, ref stock, item, Keep(playerId, feeds, item, _balance.SellAbove)) >= _balance.SellBatch)
                    return true;
            }
            item = ItemId.None;
            return false;
        }

        // The cheapest rival listing of a raw the player feeds, holds under the sell threshold and isn't already bidding
        // for: up to a batch of it at its price.
        private bool FindListingToBuy(int playerId, ref Stockpile stock, int feeds, out ItemId item, out long price,
            out int wanted)
        {
            item = ItemId.None;
            price = 0;
            wanted = 0;
            int bestIdx = int.MaxValue;
            for (int slot = 0; slot < ItemIds.RawCount; slot++)
            {
                if ((feeds & (1 << slot)) == 0)
                    continue;
                var raw = ItemIds.FromSlot(slot);
                if (stock.Get(raw) >= _balance.SellAbove || HasOpenOrder(playerId, raw, OrderSide.Buy) ||
                    !TryBestRivalAsk(playerId, slot, out long askPrice, out int askRem, out int askIdx))
                    continue;
                if (wanted > 0 && (askPrice > price || (askPrice == price && askIdx >= bestIdx)))
                    continue;
                item = raw;
                price = askPrice;
                wanted = Math.Min(askRem, _balance.SellBatch);
                bestIdx = askIdx;
            }
            return wanted > 0;
        }

        private static readonly FleetUnit ContractConvoy = new FleetUnit { TechTier = WtoContractSystem.ConvoyTier, CargoConvoys = 1 };

        private bool HasOpenOrder(int playerId, ItemId item, OrderSide side)
        {
            if (playerId <= 0 || playerId >= _playerStamp.Length || _playerStamp[playerId] != _thinkStamp)
                return false;
            int bit = 1 << ItemIds.ToSlot(item);
            return ((side == OrderSide.Buy ? _playerBuyOrderMask[playerId] : _playerSellOrderMask[playerId]) & bit) != 0;
        }

        // Reference price moved by percent (within the WTO band, which is wider); at least one cent.
        private long TradePrice(ItemId item, long tick, int percent)
        {
            int slot = ItemIds.ToSlot(item);
            if (slot >= 0 && slot < _markets.Count && _markets.At(slot).Item == item)
                return Math.Max(1, _markets.At(slot).ReferencePriceCents(tick, _wtoBalance.VwapWindowTicks) * (100 + percent) / 100);
            for (int m = 0; m < _markets.Count; m++)
            {
                ref var market = ref _markets.At(m);
                if (market.Item == item)
                    return Math.Max(1, market.ReferencePriceCents(tick, _wtoBalance.VwapWindowTicks) * (100 + percent) / 100);
            }
            throw new InvalidOperationException($"No WTO market for {item}.");
        }

        private bool HasIdleConvoy(int playerId)
        {
            for (int f = FirstPlayerFleet(playerId); f >= 0; f = _playerFleetNext[f])
                if (_units.At(f).IsConvoyOnly && !_convoyRoutes.Has(_units.EntityAt(f)))
                    return true;
            return false;
        }

        private bool HasShipOrder(int playerId, TargetFocusClass shipClass) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp &&
            (_playerShipOrders[playerId] & (1 << (int)shipClass)) != 0;

        // Ships of the tier the core stock pays percent% of the build cost for, above what the next stress test needs.
        private int Affordable(ref Stockpile stock, EntityId hq, byte tier, int percent)
        {
            long ships = int.MaxValue;
            for (int i = 0; i < _shipyardBalance.CostCount(tier); i++)
            {
                var cost = _shipyardBalance.Cost(tier, i);
                long perShip = ((long)cost.Quantity * percent + 99) / 100;
                if (perShip > 0)
                    ships = Math.Min(ships, Math.Max(0, stock.Get(cost.Item) - ProgressReserve(hq, cost.Item)) / perShip);
            }
            return (int)ships;
        }

        // A fleet of the Citadel assault tier or higher is a battle fleet: built by the shipyard, it stays at the core base
        // until it can take the Citadel, leaving the fuel to the lower-tier fleets that settle and raid.
        private bool IsBattleFleet(in FleetUnit unit) => unit.TechTier >= _balance.CitadelAssaultTier;

        // Piracy is on purpose: only a preset that weighs Stealth_Raid above Siege_Conquest (the Raider) sets its escorts
        // (line ships, no cargo, not the Citadel battle line) to raid WTO contract convoys, and it stands down while one more
        // offence would bring THE SYSTEM's defence fleet to its core base.
        private bool WantsPiracy(AiPreset preset, EntityId hq, in FleetUnit unit, long tick)
        {
            if (preset.StealthRaidWeight <= preset.SiegeConquestWeight || unit.LineShips == 0 || unit.CargoConvoys > 0 ||
                IsBattleFleet(in unit))
                return false;
            if (!_sanctions.Has(hq))
                return true;
            ref var sanction = ref _sanctions.Get(hq);
            int offences = tick - sanction.LastOffenceTick > _authorityBalance.OffenceWindowTicks ? 0 : sanction.Offences;
            return offences + 1 < _authorityBalance.LayerTwoOffences && !sanction.DefencePending && sanction.DefenceUntilTick <= tick;
        }

        // Shipyard (Defense_Hold or Siege_Conquest), from the Citadel assault tier on (earlier, ships would eat the steel
        // the economy grows on): refits a battle fleet docked at the core base to the research tier, else builds line
        // ships of that tier up to the battle line. One shipyard order in flight at a time.
        // Star charts, one order in the first think of each hour: a system it charted first goes on THE SYSTEM's world shop (fee paid from cash
        // above the travel cash); any other chart it holds, but its home, goes on its own shop; debt-free with idle cash it
        // buys the cheapest listed chart, at or under its chart price, of a system next to space it knows. It never gives
        // charts away.
        private void ThinkCharts(in PlayerHq player, AiPreset preset, EntityId hq, long tick)
        {
            if (tick % SimTick.TicksPerHour >= _balance.ThinkIntervalTicks ||
                !_ledger.TryFindPlayerWallet(player.PlayerId, out var wallet) || !_sites.Has(player.CoreBase))
                return;
            int weight = preset.ResupplyLogisticsWeight;
            long cash = _ledger.Balance(wallet);
            long travel = _balance.TravelCashCredits * Wallet.CentsPerCredit;
            int home = _sites.Get(player.CoreBase).ClusterId;
            // Two passes: its world-shop registrations first (only the first surveyor may make them), then its own shop.
            for (int pass = 0; pass < 2 * _charts.Count; pass++)
            {
                int i = pass % _charts.Count;
                ref var chart = ref _charts.At(i);
                bool first = _galaxy.FirstSurveyor(chart.ClusterId) == player.PlayerId;
                if (chart.PlayerId != player.PlayerId || chart.ClusterId == home || first != pass < _charts.Count)
                    continue;
                if (_issuer.IsInFlight(player.PlayerId, CommandType.ListChart, player.CoreBase))
                    return;
                var shop = first ? ChartShop.World : ChartShop.Player;
                if (HasChartListing(player.PlayerId, chart.ClusterId, shop) ||
                    (shop == ChartShop.World && cash - _galaxy.Catalog.Balance.ChartListingFeeCents < travel))
                    continue;
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.ListChart, Target = player.CoreBase, ParamA = (int)shop,
                    ParamB = _balance.ChartPriceCredits, ParamC = chart.ClusterId
                }, AiAction.ListChart, Score(weight, Threshold));
                return;
            }

            long idle = cash - 2 * _balance.CashReserveCredits * Wallet.CentsPerCredit;
            if (idle <= 0 || (_loans.Has(hq) && _loans.Get(hq).DebtCents > 0) ||
                _issuer.IsInFlight(player.PlayerId, CommandType.BuyChart, player.CoreBase))
                return;
            long maxPrice = Math.Min(idle, _balance.ChartPriceCredits * Wallet.CentsPerCredit);
            EntityId best = default;
            long bestPrice = long.MaxValue;
            for (int i = 0; i < _chartListings.Count; i++)
            {
                ref var listing = ref _chartListings.At(i);
                if (listing.SellerPlayerId == player.PlayerId || listing.PriceCents > maxPrice ||
                    listing.PriceCents >= bestPrice || _galaxy.Knows(player.PlayerId, listing.ClusterId) ||
                    !NextToKnownSpace(player.PlayerId, listing.ClusterId))
                    continue;
                best = _chartListings.EntityAt(i);
                bestPrice = listing.PriceCents;
            }
            if (best.IsNull)
                return;
            Issue(new DelayedPlayerCommand
            {
                PlayerId = player.PlayerId, CommandType = CommandType.BuyChart, Target = player.CoreBase, ParamEntity = best
            }, AiAction.BuyChart, Score(weight, Threshold));
        }

        private bool HasChartListing(int playerId, int clusterId, ChartShop shop)
        {
            for (int i = 0; i < _chartListings.Count; i++)
            {
                ref var listing = ref _chartListings.At(i);
                if (listing.ClusterId == clusterId && listing.Shop == shop &&
                    (shop == ChartShop.World || listing.SellerPlayerId == playerId))
                    return true;
            }
            return false;
        }

        // A system whose star is a catalog hyperlane neighbour of a system the player knows.
        private bool NextToKnownSpace(int playerId, int clusterId)
        {
            if (!_galaxy.TryGetStar(clusterId, out var star))
                return false;
            _galaxy.MaterialisedNeighbours(star, _chartNeighbours);
            for (int i = 0; i < _chartNeighbours.Count; i++)
                if (_galaxy.Knows(playerId, _chartNeighbours[i]))
                    return true;
            return false;
        }

        private void ThinkShipyard(in PlayerHq player, AiPreset preset, EntityId hq)
        {
            int weight = Math.Max(preset.DefenseHoldWeight, preset.SiegeConquestWeight);
            byte tier = _tech.TechTierOf(player.PlayerId);
            if (weight == 0 || tier < _balance.CitadelAssaultTier || !_stockpiles.Has(player.CoreBase) ||
                _issuer.IsInFlight(player.PlayerId, CommandType.BuildShips, player.CoreBase))
                return;
            ref var stock = ref _stockpiles.Get(player.CoreBase);
            int lineShips = 0;
            bool behind = false;
            for (int f = FirstPlayerFleet(player.PlayerId); f >= 0; f = _playerFleetNext[f])
            {
                ref var unit = ref _units.At(f);
                if (unit.IsConvoyOnly || !IsBattleFleet(in unit))
                    continue;
                lineShips += unit.LineShips;
                behind |= unit.TechTier < tier;
                var fleet = _units.EntityAt(f);
                ref var motion = ref _motions.Get(fleet);
                if (unit.TechTier >= tier || motion.Site != player.CoreBase || !motion.Lane.IsNull ||
                    _issuer.IsInFlight(player.PlayerId, CommandType.Retrofit, fleet) ||
                    Affordable(ref stock, hq, tier, _shipyardBalance.RetrofitCostPercent) < unit.TotalShips)
                    continue;
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.Retrofit, Target = fleet
                }, AiAction.Retrofit, Score(weight, Full));
                return;
            }
            if (TryMergeBattleFleets(in player, tier, weight))
                return;
            // New ships of a higher tier would form a second fleet; one battle line only outguns the garrison whole.
            if (behind || lineShips >= _balance.BattleLineShips || HasShipOrder(player.PlayerId, TargetFocusClass.LineShips))
                return;
            int ships = Math.Min(Math.Min(_balance.BattleLineShips - lineShips, _shipyardBalance.MaxShipsPerOrder),
                Affordable(ref stock, hq, tier, 100));
            if (ships > 0)
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.BuildShips, Target = player.CoreBase,
                    ParamA = (int)TargetFocusClass.LineShips, ParamB = ships, ParamC = tier
                }, AiAction.BuildWarships, Score(weight, Threshold));
        }

        // Ships built while the battle line was away or mothballed form a second fleet: the larger of two current battle
        // fleets docked active at the core base takes the other in, as one line only outguns the garrison whole.
        private bool TryMergeBattleFleets(in PlayerHq player, byte tier, int weight)
        {
            EntityId largest = default, other = default;
            for (int f = FirstPlayerFleet(player.PlayerId); f >= 0; f = _playerFleetNext[f])
            {
                ref var unit = ref _units.At(f);
                var fleet = _units.EntityAt(f);
                ref var motion = ref _motions.Get(fleet);
                if (unit.TechTier != tier || unit.LineShips == 0 ||
                    unit.CargoConvoys > 0 || motion.Site != player.CoreBase || !motion.Lane.IsNull ||
                    (_readiness.Has(fleet) && _readiness.Get(fleet).IsMothballed))
                    continue;
                if (largest.IsNull || unit.LineShips > _units.Get(largest).LineShips)
                {
                    other = largest;
                    largest = fleet;
                }
                else if (other.IsNull)
                {
                    other = fleet;
                }
            }
            if (other.IsNull || _issuer.IsInFlight(player.PlayerId, CommandType.MergeFleets, largest))
                return false;
            var command = new DelayedPlayerCommand
            {
                PlayerId = player.PlayerId, CommandType = CommandType.MergeFleets, Target = largest
            };
            command.ParamEntity = other;
            Issue(command, AiAction.MergeFleets, Score(weight, Full));
            return true;
        }

        private bool HasRefinery(EntityId site, ItemId output) =>
            !site.IsNull && site.Index < _siteRefineryStamp.Length && _siteRefineryStamp[site.Index] == _thinkStamp &&
            (_siteRefineryMask[site.Index] & (1 << ItemIds.ToSlot(output))) != 0;

        // Fill-up-to order for the hold: never below the core reserve of fuel and coolant (or a life-support floor of oxygen
        // and rations so loading a fleet never starves the core base), or the next stress test's cost.
        private bool TryLoad(
            ref Stockpile stock, EntityId hq, ref Stockpile hold, ItemId id, long target, ref ItemId item, ref int quantity,
            ref int shortage)
        {
            long gap = target - hold.Get(id);
            long reserve = id == ItemId.ItemFuel || id == ItemId.ItemCoolant
                ? _balance.CoreSupplyReserve
                : id == ItemId.ItemOxygen || id == ItemId.ItemRations
                    ? Math.Min(_balance.CoreSupplyReserve, 50)
                    : 0;
            long available = stock.Get(id) - reserve - ProgressReserve(hq, id) - PendingCoreLoad(hq, id);
            if (gap <= 0 || available <= 0)
                return false;
            item = id;
            quantity = (int)Math.Min(Math.Min(target, hold.Get(id) + available), int.MaxValue);
            shortage = Severity(gap, target);
            return true;
        }

        // Best site this fleet outguns and has the fuel to reach and come back from: a raid hunts rival fleets at a rival
        // site; a siege takes a neutral or rival site with new ore, preferring unclaimed neutral sites over rival outposts
        // and skipping neutral sites inside another player's home system. Never a core base or THE SYSTEM's site.
        private bool CanGive(ref Stockpile stock, EntityId hq, ItemId id)
        {
            long reserve = id == ItemId.ItemFuel || id == ItemId.ItemCoolant
                ? _balance.CoreSupplyReserve
                : id == ItemId.ItemOxygen || id == ItemId.ItemRations
                    ? Math.Min(_balance.CoreSupplyReserve, 50)
                    : 0;
            return stock.Get(id) > reserve + ProgressReserve(hq, id) + PendingCoreLoad(hq, id);
        }

        private long PendingCoreLoad(EntityId hq, ItemId id)
        {
            if (!_players.Has(hq))
                return 0;
            ref var player = ref _players.Get(hq);
            return _issuer.PendingLoadAt(player.PlayerId, player.CoreBase, id, _stockpiles);
        }

        private bool FindTarget(in PlayerHq player, in FleetUnit unit, int fuel, bool raid, bool homeOnly, out EntityId target,
            out int edge) =>
            FindTarget(in player, in unit, fuel, raid, homeOnly, out target, out edge, out _);

        private bool FindTarget(in PlayerHq player, in FleetUnit unit, int fuel, bool raid, bool homeOnly, out EntityId target,
            out int edge, out long bestRoundTripFuel)
        {
            long power = FleetPower(in unit);
            target = default;
            edge = 0;
            bestRoundTripFuel = 0;
            if (power <= 0)
                return false;
            int comp = _routes.Graph.ComponentOf(player.CoreBase);
            if (comp <= 0)
                return false;
            int bestLanes = int.MaxValue;
            int bestOres = 0;
            int mined = raid ? 0 : MinedOres(player.PlayerId);
            int feeds = raid ? 0 : NeededRaws(in player);
            int urgent = raid ? 0 : _expansionRaws | (LowCoreOres(player.PlayerId) & _lifelineRaws);
            int homeCluster = _sites.Get(player.CoreBase).ClusterId;
            int raidIdx = 0;
            int iA = !raid && homeCluster >= 0 && homeCluster < _clusterNeutralStamp.Length &&
                     _clusterNeutralStamp[homeCluster] == _thinkStamp
                ? _clusterFirstNeutralSite[homeCluster]
                : -1;
            int iB = !raid && !homeOnly && comp < _compOpenStamp.Length && _compOpenStamp[comp] == _thinkStamp
                ? _compFirstOpenSite[comp]
                : -1;
            while (true)
            {
                int i;
                if (raid)
                {
                    if (raidIdx >= _armedSiteCount)
                        break;
                    i = _armedSites[raidIdx++];
                }
                else if (iA >= 0 && (iB < 0 || iA < iB))
                {
                    i = iA;
                    iA = _siteCandidateNext[iA];
                }
                else if (iB >= 0)
                {
                    i = iB;
                    iB = _siteCandidateNext[iB];
                }
                else
                {
                    break;
                }
                ref var site = ref _sites.At(i);
                var entity = _sites.EntityAt(i);
                if (!_routes.Graph.SameComponent(player.CoreBase, entity) ||
                    site.OwnerPlayerId < 0 || site.OwnerPlayerId == player.PlayerId || (raid && site.OwnerPlayerId == 0) ||
                    IsCoreBase(entity) || (homeOnly && !IsNeutralAtHome(entity, player.CoreBase)))
                    continue;
                if (!raid && site.OwnerPlayerId == 0 && site.ClusterId != homeCluster && IsAnyHomeCluster(site.ClusterId))
                    continue;
                int ores = raid ? 0 : NewOres(entity, mined, feeds, urgent);
                if (!raid && (ores == 0 || ores < bestOres))
                    continue; // a site with no new ore isn't worth the claim cost
                long hostile = Power(entity, player.PlayerId, mine: false);
                if (hostile >= power || (raid && hostile == 0))
                    continue;
                int advantage = Severity(power - hostile, power);
                bool betterOres = ores > bestOres;
                bool sameOres = ores == bestOres;
                bool preferNeutral = !raid && sameOres && !target.IsNull &&
                                     site.OwnerPlayerId == 0 && _sites.Get(target).OwnerPlayerId > 0;
                bool worseOwner = !raid && sameOres && !target.IsNull &&
                                  site.OwnerPlayerId > 0 && _sites.Get(target).OwnerPlayerId == 0;
                if (worseOwner || (!betterOres && !preferNeutral && (!sameOres || advantage < edge)))
                    continue;
                int maxLanes = betterOres || preferNeutral || advantage > edge ? FleetRoute.MaxLanes : bestLanes - 1;
                if (maxLanes < 1 || !PlanTrip(player.CoreBase, entity, in unit, trips: 2, maxLanes, out _))
                    continue;
                long rtFuel = 2L * RouteFuel(in unit);
                if (rtFuel > fuel)
                    continue;
                if (sameOres && !preferNeutral && advantage == edge && _route.Count >= bestLanes)
                    continue;
                target = entity;
                edge = advantage;
                bestLanes = _route.Count;
                bestOres = ores;
                bestRoundTripFuel = rtFuel;
            }
            return !target.IsNull;
        }

        private bool IsAnyHomeCluster(int clusterId) =>
            clusterId >= 0 && clusterId < _homeClusterStamp.Length && _homeClusterStamp[clusterId] == _thinkStamp;

        // The nearest unthreatened own outpost with a bare deposit of ore worth rigging, whose rigs the hold and the site's
        // stock can pay together, within a round trip of fuel.
        private bool FindOutpostToDevelop(in PlayerHq player, in FleetUnit unit, int fuel, ref Stockpile hold, out EntityId target)
        {
            target = default;
            int bestLanes = int.MaxValue;
            int rigs = Math.Min(_balance.OutpostRigs, _miningBalance.MaxRigsPerDeposit);
            int mined = MinedOres(player.PlayerId);
            int feeds = NeededRaws(in player);
            for (int d = FirstPlayerDeposit(player.PlayerId); d >= 0; d = _playerDepositNext[d])
            {
                ref var deposit = ref _deposits.At(d);
                var site = deposit.Site;
                if (deposit.RigCount != 0 || deposit.RetractUntilTick != 0 || !HasOre(in deposit) || site == target ||
                    DepositRank(deposit.RawItem, mined, feeds) == 0 ||
                    site == player.CoreBase || !_stockpiles.Has(site) ||
                    Power(site, player.PlayerId, mine: false) > 0)
                    continue;
                bool paid = true;
                for (int i = 0; i < _miningBalance.RigCostCount && paid; i++)
                {
                    var cost = _miningBalance.RigCost(i);
                    paid = hold.Get(cost.Item) + _stockpiles.Get(site).Get(cost.Item) >= (long)cost.Quantity * rigs;
                }
                if (!paid || !PlanTrip(player.CoreBase, site, in unit, trips: 2) ||
                    2L * RouteFuel(in unit) > fuel || _route.Count >= bestLanes)
                    continue;
                target = site;
                bestLanes = _route.Count;
            }
            return !target.IsNull;
        }

        private static int FlankWeight(AiPreset preset) => Math.Max(preset.StealthRaidWeight, preset.SiegeConquestWeight);

        // See the flanking item in the class summary. FlankSplit: split first; FlankMove: to the staging site (or straight down
        // the flank lane when the fleet already sits at its far end).
        private bool PlanFlank(int playerId, EntityId fleet, in FleetUnit unit, EntityId from, EntityId target, int fuel,
            out AiAction action, out EntityId flankTo)
        {
            action = default;
            flankTo = default;
            long hostile = Power(target, playerId, mine: false);
            if (hostile == 0)
                return false;
            if (FindPartnerArrival(playerId, fleet, target, out var partnerLane))
            {
                if (!FlankStep(in unit, from, target, partnerLane, fuel, playerId, out flankTo))
                    return false;
                action = AiAction.FlankMove;
                return true;
            }
            var half = unit;
            half.LineShips /= 2;
            half.EwShips /= 2;
            half.SupplyTenders /= 2;
            if (unit.LineShips < _balance.FlankMinLineShips || FleetPower(in half) <= hostile ||
                HasDockedPartner(playerId, fleet, from, unit.TechTier) || _issuer.IsInFlight(playerId, CommandType.SplitFleet, fleet) ||
                !PlanTrip(from, target, in unit, trips: 2))
                return false;
            var arrival = _route.LaneAt(_route.Count - 1);
            if (!FlankStep(in half, from, target, arrival, fuel / 2, playerId, out _))
                return false;
            action = AiAction.FlankSplit;
            return true;
        }

        // Another own fleet with line ships at the target, on its way there, or with a Move there in flight; lane: the one it
        // arrives (or arrived) through, null if unknown.
        private bool FindPartnerArrival(int playerId, EntityId self, EntityId target, out EntityId lane)
        {
            for (int f = FirstPlayerFleet(playerId); f >= 0; f = _playerFleetNext[f])
            {
                ref var unit = ref _units.At(f);
                var fleet = _units.EntityAt(f);
                if (fleet == self || unit.LineShips == 0 || unit.IsConvoyOnly)
                    continue;
                ref var motion = ref _motions.Get(fleet);
                if (motion.Lane.IsNull)
                {
                    if (motion.Site != target)
                        continue;
                    lane = motion.ArrivalLane;
                    return true;
                }
                if (Destination(fleet, in motion, out lane) == target)
                    return true;
            }
            lane = default;
            if (!_issuer.IsMoveInFlight(playerId, target, out var mover) || mover == self || !_motions.Has(mover) ||
                !_units.Has(mover))
                return false;
            ref var at = ref _motions.Get(mover);
            if (at.Lane.IsNull && PlanTrip(at.Site, target, in _units.Get(mover), trips: 2) && _route.Count > 0)
                lane = _route.LaneAt(_route.Count - 1);
            return true;
        }

        // Where a fleet on a lane ends its route, and the last lane it takes there.
        private EntityId Destination(EntityId fleet, in FleetMotion motion, out EntityId lastLane)
        {
            lastLane = motion.Lane;
            if (!_lanes.Has(lastLane))
                return default;
            var site = _lanes.Get(lastLane).OtherEnd(motion.FromSite);
            if (!_fleetRoutes.Has(fleet))
                return site;
            ref var route = ref _fleetRoutes.Get(fleet);
            for (int i = route.Next; i < route.Count; i++)
            {
                var lane = route.LaneAt(i);
                if (!_lanes.Has(lane))
                    return default;
                site = _lanes.Get(lane).OtherEnd(site);
                lastLane = lane;
            }
            return site;
        }

        private bool HasDockedPartner(int playerId, EntityId self, EntityId site, byte tier)
        {
            for (int f = FirstPlayerFleet(playerId); f >= 0; f = _playerFleetNext[f])
            {
                ref var unit = ref _units.At(f);
                var fleet = _units.EntityAt(f);
                if (fleet == self || unit.LineShips == 0 || unit.IsConvoyOnly ||
                    unit.TechTier != tier)
                    continue;
                ref var motion = ref _motions.Get(fleet);
                if (motion.Lane.IsNull && motion.Site == site &&
                    (!_readiness.Has(fleet) || !_readiness.Get(fleet).IsMothballed))
                    return true;
            }
            return false;
        }

        // Whether two approaches into the target, from the far ends of these lanes, are at least 90° apart.
        private bool IsIndependent(EntityId target, EntityId lane, EntityId other)
        {
            if (other.IsNull || !_lanes.Has(other))
                return lane != other;
            if (lane == other)
                return false;
            ref var at = ref _sites.Get(target);
            ref var a = ref _sites.Get(_lanes.Get(lane).OtherEnd(target));
            ref var b = ref _sites.Get(_lanes.Get(other).OtherEnd(target));
            return ((long)at.PosX - a.PosX) * ((long)at.PosX - b.PosX) + ((long)at.PosY - a.PosY) * ((long)at.PosY - b.PosY) <= 0;
        }

        // The next step of a flank: towards the nearest quiet staging site at the far end of a lane into the target that is
        // independent of the partner's, on a detour that skirts the target and fits a round trip of fuel. flankTo is the
        // furthest site on the detour a plain Move also reaches without passing the target (else its first lane), or the
        // flank lane itself once the fleet stands at the staging site.
        private bool FlankStep(in FleetUnit unit, EntityId from, EntityId target, EntityId partnerLane, int fuel, int playerId,
            out EntityId flankTo)
        {
            flankTo = default;
            int bestLanes = int.MaxValue;
            var graph = _routes.Graph;
            if (graph.SlotCount == _lanes.Count * 2)
            {
                for (int slot = graph.Start(target); slot < graph.End(target); slot++)
                {
                    var lane = graph.EdgeAt(slot);
                    var site = graph.NeighborAt(slot);
                    if (!_sites.Has(site) || !IsIndependent(target, lane, partnerLane) || Power(site, playerId, mine: false) > 0)
                        continue;
                    if (site == from)
                    {
                        flankTo = lane;
                        return true;
                    }
                    if (!_routes.TryPlan(from, site, unit.TechTier, tollFree: false, target, ref _route) ||
                        _route.Count >= bestLanes || 2L * RouteFuel(in unit) > fuel)
                        continue;
                    _detour = _route;
                    bestLanes = _route.Count;
                }
            }
            else
            {
                for (int l = 0; l < _lanes.Count; l++)
                {
                    ref var edge = ref _lanes.At(l);
                    if (edge.SiteA != target && edge.SiteB != target)
                        continue;
                    var lane = _lanes.EntityAt(l);
                    var site = edge.OtherEnd(target);
                    if (!_sites.Has(site) || !IsIndependent(target, lane, partnerLane) || Power(site, playerId, mine: false) > 0)
                        continue;
                    if (site == from)
                    {
                        flankTo = lane;
                        return true;
                    }
                    if (!_routes.TryPlan(from, site, unit.TechTier, tollFree: false, target, ref _route) ||
                        _route.Count >= bestLanes || 2L * RouteFuel(in unit) > fuel)
                        continue;
                    _detour = _route;
                    bestLanes = _route.Count;
                }
            }
            if (bestLanes == int.MaxValue)
                return false;
            var stop = from;
            flankTo = _detour.LaneAt(0);
            for (int i = 0; i < _detour.Count; i++)
            {
                stop = _lanes.Get(_detour.LaneAt(i)).OtherEnd(stop);
                if (_routes.TryPlan(from, stop, unit.TechTier, ref _route) && !RouteTouches(target))
                    flankTo = stop;
            }
            return true;
        }

        private bool RouteTouches(EntityId site)
        {
            for (int i = 0; i < _route.Count; i++)
            {
                ref var edge = ref _lanes.Get(_route.LaneAt(i));
                if (edge.SiteA == site || edge.SiteB == site)
                    return true;
            }
            return false;
        }

        // At a quiet site: the flank step towards a defended site (not a Citadel) within two lanes that another own fleet
        // is bound for or at, when the two together outgun the defenders.
        private bool FindFlankStep(int playerId, EntityId fleet, in FleetUnit unit, EntityId site, int fuel, out EntityId flankTo)
        {
            flankTo = default;
            if (unit.LineShips == 0 || fuel == 0)
                return false;
            var graph = _routes.Graph;
            if (graph.SlotCount == _lanes.Count * 2)
            {
                for (int s1 = graph.Start(site); s1 < graph.End(site); s1++)
                {
                    var lane1 = graph.EdgeAt(s1);
                    var near = graph.NeighborAt(s1);
                    if (TryFlankStepAt(playerId, fleet, in unit, site, near, fuel, out flankTo))
                        return true;
                    for (int s2 = graph.Start(near); s2 < graph.End(near); s2++)
                    {
                        var lane2 = graph.EdgeAt(s2);
                        var far = graph.NeighborAt(s2);
                        if (lane2 != lane1 && far != site &&
                            TryFlankStepAt(playerId, fleet, in unit, site, far, fuel, out flankTo))
                            return true;
                    }
                }
                return false;
            }
            for (int l = 0; l < _lanes.Count; l++)
            {
                ref var edge = ref _lanes.At(l);
                if (edge.SiteA != site && edge.SiteB != site)
                    continue;
                var near = edge.OtherEnd(site);
                if (TryFlankStepAt(playerId, fleet, in unit, site, near, fuel, out flankTo))
                    return true;
                for (int k = 0; k < _lanes.Count; k++)
                {
                    ref var next = ref _lanes.At(k);
                    if (k != l && (next.SiteA == near || next.SiteB == near) && next.OtherEnd(near) != site &&
                        TryFlankStepAt(playerId, fleet, in unit, site, next.OtherEnd(near), fuel, out flankTo))
                        return true;
                }
            }
            return false;
        }

        private bool TryFlankStepAt(int playerId, EntityId fleet, in FleetUnit unit, EntityId site, EntityId target, int fuel,
            out EntityId flankTo)
        {
            flankTo = default;
            if (!_sites.Has(target) || IsStandingCitadel(target))
                return false;
            long hostile = Power(target, playerId, mine: false);
            return hostile > 0 && FindPartnerArrival(playerId, fleet, target, out var partnerLane) &&
                   FleetPower(in unit) + Power(target, playerId, mine: true) > hostile &&
                   FlankStep(in unit, site, target, partnerLane, fuel, playerId, out flankTo);
        }

        // A standing Citadel whose garrison (if formed) the fleet outguns, within a round trip of fuel; edge as in FindTarget.
        private bool FindCitadel(in PlayerHq player, in FleetUnit unit, int fuel, out EntityId target, out int edge)
        {
            target = default;
            edge = 0;
            if (unit.TechTier < _balance.CitadelAssaultTier || unit.LineShips == 0)
                return false;
            long power = FleetPower(in unit);
            for (int c = 0; c < _citadels.Count; c++)
            {
                var site = _citadels.EntityAt(c);
                if (_citadels.At(c).FallenTick > 0)
                    continue;
                long hostile = Power(site, player.PlayerId, mine: false);
                if (hostile >= power || !PlanTrip(player.CoreBase, site, in unit, trips: 1) ||
                    2L * RouteFuel(in unit) > fuel)
                    continue;
                int advantage = Severity(power - hostile, power);
                if (advantage <= edge)
                    continue;
                target = site;
                edge = advantage;
            }
            return !target.IsNull;
        }

        // A fleet ready at the core base: a stealth raid, a siege it carries the claim kit for, or an outpost to develop.
        private void ConsiderRaidAndSiege(in PlayerHq player, AiPreset preset, in FleetUnit unit, ref Stockpile hold,
            ref AiAction best, ref int bestScore, out EntityId raidTarget, out EntityId siegeTarget, out EntityId developTarget)
        {
            siegeTarget = default;
            developTarget = default;
            int fuel = hold.Get(ItemId.ItemFuel);
            raidTarget = default;
            if (preset.StealthRaidWeight > 0 &&
                FindTarget(player, in unit, fuel, raid: true, homeOnly: false, out raidTarget, out int raidEdge))
                Consider(AiAction.StealthRaid, Score(preset.StealthRaidWeight, raidEdge), ref best, ref bestScore);
            // EW batteries ride along only while the core has batteries to give: a core out of them (no silicon to
            // make more) still sends the claim kit, or the empire never reaches the ore it lacks.
            int ewBatteries = _stockpiles.Get(player.CoreBase).Get(ItemId.ItemBattery) > 0 ? EwBatteries(in unit) : 0;
            if (SettleWeight(preset) > 0 && CarriesClaimCost(ref hold, ewBatteries))
            {
                int siegeEdge;
                bool hasSiege;
                if (_cachedSiegeValid && (!_cachedSiegeHas || _cachedSiegeRtFuel <= fuel))
                {
                    hasSiege = _cachedSiegeHas;
                    siegeTarget = _cachedSiegeTarget;
                    siegeEdge = _cachedSiegeEdge;
                }
                else
                {
                    hasSiege = FindTarget(player, in unit, fuel, raid: false, homeOnly: IsHomeSettler(preset), out siegeTarget,
                        out siegeEdge);
                }
                if (hasSiege)
                    Consider(AiAction.Siege, Score(SettleWeight(preset), siegeEdge), ref best, ref bestScore);
            }
            if (SettleWeight(preset) > 0 && FindOutpostToDevelop(player, in unit, fuel, ref hold, out developTarget))
                Consider(AiAction.DevelopOutpost, Score(SettleWeight(preset), Full), ref best, ref bestScore);
        }

        // THE SYSTEM's Citadel has fallen (and with it the reason battle lines wait mothballed at home).
        private bool CitadelFallen()
        {
            for (int c = 0; c < _citadels.Count; c++)
                if (_citadels.At(c).FallenTick == 0)
                    return false;
            return _citadels.Count > 0;
        }

        private bool IsStandingCitadel(EntityId site) => _citadels.Has(site) && _citadels.Get(site).FallenTick == 0;

        // Bit per raw item slot the player has rigs on somewhere with ore left (a mined-out deposit, or a core-base one under
        // the low reserve, sends it looking again).
        private int MinedOres(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp ? _playerMinedOres[playerId] : 0;

        private int LowCoreOres(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp ? _playerLowCoreOres[playerId] : 0;

        // Deposits at the site with ore left of a raw item outside the mined mask; one a core refinery needs counts double,
        // and one the outpost currency is made of (steel's iron) more than all the others together: every later outpost is
        // paid in it, so a player who settles elsewhere first can spend its starting steel and never claim the ore to make more.
        private int NewOres(EntityId site, int mined, int feeds, int urgent)
        {
            if (site.Index >= _siteDepositStamp.Length || _siteDepositStamp[site.Index] != _thinkStamp)
                return 0;
            int unmined = _siteAvailableOres[site.Index] & ~mined;
            if (unmined == 0)
                return 0;
            ulong counts = _siteOreCounts[site.Index];
            int count = 0;
            for (int slot = 0; slot < ItemIds.RawCount; slot++)
            {
                int bit = 1 << slot;
                if ((unmined & bit) == 0)
                    continue;
                int depCount = (int)((counts >> (slot * 6)) & 0x3F);
                count += depCount * ((urgent & bit) != 0 ? 2 * ItemIds.Count : (feeds & bit) != 0 ? 2 : 1);
            }
            return count;
        }

        // Bit per raw item slot the player needs: what a core-base refinery takes in, and what goes into the next stress
        // test's cost (refined items through their recipe).
        private int NeededRaws(in PlayerHq player)
        {
            int pid = player.PlayerId;
            if (pid > 0 && pid < _playerStressStamp.Length && _playerStressStamp[pid] == _thinkStamp)
                return _playerNeededRaws[pid];
            int mask = !player.CoreBase.IsNull && player.CoreBase.Index < _siteRefineryStamp.Length &&
                       _siteRefineryStamp[player.CoreBase.Index] == _thinkStamp
                ? _siteRefineryRaws[player.CoreBase.Index]
                : 0;
            var hq = pid > 0 && pid < _playerStamp.Length && _playerStamp[pid] == _thinkStamp ? _playerHq[pid] : default;
            if (!hq.IsNull && TryNextStressTest(hq, out _, out var cost))
            {
                for (int i = 0; i < cost.Length; i++)
                    mask |= ItemIds.IsRaw(cost[i].Item) ? 1 << ItemIds.ToSlot(cost[i].Item) : RecipeRaws(cost[i].Item);
            }
            if (pid > 0 && pid < _playerNeededRaws.Length)
                _playerNeededRaws[pid] = mask;
            return mask;
        }

        private int RecipeRaws(ItemId output)
        {
            ref readonly var recipe = ref _recipes.GetRecipe(output);
            int mask = 0;
            if (ItemIds.IsRaw(recipe.Input1))
                mask |= 1 << ItemIds.ToSlot(recipe.Input1);
            if (ItemIds.IsRaw(recipe.Input2))
                mask |= 1 << ItemIds.ToSlot(recipe.Input2);
            return mask;
        }

        // 3: not mined yet and a core refinery needs it; 2: not mined yet; 1: a core refinery needs more; 0: neither.
        private static int DepositRank(ItemId raw, int mined, int feeds)
        {
            int bit = 1 << ItemIds.ToSlot(raw);
            return ((mined & bit) == 0 ? 2 : 0) + ((feeds & bit) != 0 ? 1 : 0);
        }

        // Presets that never conquer settle only neutral sites of their own home system.
        private bool IsHomeSettler(AiPreset preset) => preset.SiegeConquestWeight == 0;

        private int SettleWeight(AiPreset preset) =>
            preset.SiegeConquestWeight > 0 ? preset.SiegeConquestWeight : _balance.HomeSettleWeight;

        private bool IsNeutralAtHome(EntityId site, EntityId coreBase) =>
            _sites.Get(site).OwnerPlayerId == 0 && _sites.Get(site).ClusterId == _sites.Get(coreBase).ClusterId;

        private long RouteFuel(in FleetUnit unit)
        {
            long fuel = 0;
            for (int i = 0; i < _route.Count; i++)
                fuel += _routes.FuelFor(unit, _lanes.Get(_route.LaneAt(i)));
            return fuel;
        }

        // Plans the trip into _route: the fastest route if the wallet covers its STA tolls for the trips (there, or there
        // and back; a Citadel assault is one way, a fallen Citadel is the fleet's to hold), else the toll-free one
        // (bypass lanes: 0 credits, more fuel). False if neither exists.
        private bool PlanTrip(EntityId from, EntityId to, in FleetUnit unit, int trips, out bool tollFree) =>
            PlanTrip(from, to, in unit, trips, FleetRoute.MaxLanes, out tollFree);

        private bool PlanTrip(EntityId from, EntityId to, in FleetUnit unit, int trips, int maxLanes, out bool tollFree)
        {
            tollFree = false;
            if (!_galaxy.KnowsSite(unit.OwnerPlayerId, to))
                return false; // fleets fly only to charted systems
            if (!_routes.TryPlan(from, to, unit.TechTier, tollFree: false, default, maxLanes, out bool hasTolls, ref _route))
                return false;
            if (!hasTolls)
                return true;
            long tolls = 0;
            for (int i = 0; i < _route.Count; i++)
                tolls += _tolls.QuoteCents(unit, _lanes.Get(_route.LaneAt(i)));
            if (tolls == 0 ||
                (_ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var wallet) && trips * tolls <= _ledger.Balance(wallet)))
                return _route.Count <= maxLanes;
            tollFree = true;
            return _routes.TryPlan(from, to, unit.TechTier, tollFree: true, default, maxLanes, out _, ref _route);
        }

        private bool PlanTrip(EntityId from, EntityId to, in FleetUnit unit, int trips) =>
            PlanTrip(from, to, in unit, trips, out _);

        // Move's ParamC for the trip PlanTrip picks: 1 = toll-free.
        private int TollFreeFlag(EntityId from, EntityId to, in FleetUnit unit, int trips) =>
            PlanTrip(from, to, in unit, trips, out bool tollFree) && tollFree ? 1 : 0;

        private void ThinkProgress(in PlayerHq player, AiPreset preset, EntityId hq)
        {
            if (!_stockpiles.Has(player.CoreBase))
                return;
            ref var stock = ref _stockpiles.Get(player.CoreBase);
            if (_progress.Has(hq) && _progress.Get(hq).Stage == ResearchStage.StressTest && preset.RadarBeltScanWeight > 0 &&
                TryNextStressTest(hq, out var branch, out var cost) && HasBranchTelemetry(in _progress.Get(hq), branch))
            {
                bool affordable = true;
                for (int i = 0; i < cost.Length; i++)
                    affordable &= stock.Has(cost[i].Item, cost[i].Quantity);
                if (affordable)
                    Issue(new DelayedPlayerCommand
                    {
                        PlayerId = player.PlayerId, CommandType = CommandType.StressTest, Target = player.CoreBase,
                        ParamA = (int)branch
                    }, AiAction.StressTest, Score(preset.RadarBeltScanWeight, Full));
            }

            // The gate carries the expansion fleets and the WTO contract convoys both ways, so traders open it too.
            int expand = Math.Max(Math.Max(preset.StealthRaidWeight, preset.SiegeConquestWeight), preset.ResupplyLogisticsWeight);
            if (expand == 0 || !HasClosedGate(player.PlayerId))
                return;
            for (int i = 0; i < _gateBalance.OpenCostCount; i++)
                if (!stock.Has(_gateBalance.OpenCost(i).Item,
                        _gateBalance.OpenCost(i).Quantity + (_gateBalance.OpenCost(i).Item == ItemId.ItemFuel && FirstPlayerDeposit(player.PlayerId) >= 0 ? _balance.CoreSupplyReserve : 0)))
                    return;
            Issue(new DelayedPlayerCommand
            {
                PlayerId = player.PlayerId, CommandType = CommandType.OpenJumpGate, Target = _playerDormantGateSite[player.PlayerId]
            }, AiAction.OpenGate, Score(expand, Full));
        }

        // Next stress test for the player's chosen branch and its cost from the core base; false once all branches are complete.
        private bool TryNextStressTest(EntityId hq, out ResearchBranch branch, out ItemStack[] cost)
        {
            int playerId = _players.Has(hq) ? _players.Get(hq).PlayerId : 0;
            if (playerId > 0 && playerId < _playerStressStamp.Length && _playerStressStamp[playerId] == _thinkStamp)
            {
                branch = _playerStressBranch[playerId];
                cost = _playerStressCost[playerId];
                return _playerHasStress[playerId];
            }
            branch = ResearchBranch.Military;
            cost = null;
            bool has = false;
            if (_progress.Has(hq))
            {
                ref var p = ref _progress.Get(hq);
                if (p.Stage <= ResearchStage.StressTest && !p.AllAtOrAbove(ResearchBalance.TopTier))
                {
                    var preset = _controllers.Has(hq) ? _balance.Preset(_controllers.Get(hq).Archetype) : _balance.Preset(1);
                    branch = PickBranch(playerId, preset, in p);
                    cost = _researchBalance.Tier((byte)(p.Tier(branch) + 1)).StressTestCost;
                    has = true;
                }
            }
            if (playerId > 0 && playerId < _playerStressStamp.Length)
            {
                _playerStressStamp[playerId] = _thinkStamp;
                _playerHasStress[playerId] = has;
                _playerStressBranch[playerId] = branch;
                _playerStressCost[playerId] = cost;
                int mask = 0;
                var core = _playerCoreBase[playerId];
                if (!core.IsNull && core.Index < _siteRefineryStamp.Length && _siteRefineryStamp[core.Index] == _thinkStamp)
                    mask = _siteRefineryRaws[core.Index];
                if (has)
                {
                    for (int i = 0; i < cost.Length; i++)
                        mask |= ItemIds.IsRaw(cost[i].Item) ? 1 << ItemIds.ToSlot(cost[i].Item) : RecipeRaws(cost[i].Item);
                }
                _playerNeededRaws[playerId] = mask;
            }
            return has;
        }

        private bool HasBranchTelemetry(in TechProgress p, ResearchBranch branch) =>
            p.TelemetryMilli == 0 ||
            p.TelemetryMilli >= _researchBalance.TierTelemetryTargetMilli((byte)(p.Tier(branch) + 1));

        private bool NeedsTelemetry(EntityId hq)
        {
            if (!_progress.Has(hq))
                return false;
            ref var p = ref _progress.Get(hq);
            if (p.Stage == ResearchStage.FieldTelemetry)
                return true;
            if (p.Stage == ResearchStage.StressTest && TryNextStressTest(hq, out var branch, out _))
                return p.TelemetryMilli < _researchBalance.TierTelemetryTargetMilli((byte)(p.Tier(branch) + 1));
            return false;
        }

        // Branch order by preset and next needed gate: the preset's leading branch first, Logistics to the jump-gate tier
        // when the player has a closed gate, Military to the Tier-4 ship and shortcut tier, then every other branch to
        // Tier 4 (laser comms, core-fracture mining: nuclear cores are scarce, and a player that can't make them would
        // otherwise retry one Tier-5 test forever), then the Tier-5 nuclear-core tests.
        private ResearchBranch PickBranch(int playerId, AiPreset preset, in TechProgress progress)
        {
            var lead = LeadingBranch(preset);
            if (progress.Tier(lead) < _researchBalance.JumpGateTier)
                return lead;
            if (progress.LogisticsTier < _researchBalance.JumpGateTier && HasDormantGate(playerId))
                return ResearchBranch.Logistics;
            byte preNuclear = _researchBalance.LaserCommsTier;
            if (progress.Tier(lead) < preNuclear)
                return lead;
            if (progress.TechTier < preNuclear)
                return ResearchBranch.Military;
            for (int b = 0; b < TechProgress.BranchCount; b++)
            {
                var branch = (ResearchBranch)b;
                if (progress.Tier(branch) < preNuclear)
                    return branch;
            }
            if (progress.TechTier < ResearchBalance.TopTier)
                return ResearchBranch.Military;
            if (progress.Tier(lead) < ResearchBalance.TopTier)
                return lead;
            for (int b = 0; b < TechProgress.BranchCount; b++)
            {
                var branch = (ResearchBranch)b;
                if (progress.Tier(branch) < ResearchBalance.TopTier)
                    return branch;
            }
            return lead;
        }

        private static ResearchBranch LeadingBranch(AiPreset preset)
        {
            var best = ResearchBranch.Military;
            int bestWeight = Math.Max(preset.DefenseHoldWeight, preset.SiegeConquestWeight);
            if (preset.ResupplyLogisticsWeight > bestWeight)
            {
                best = ResearchBranch.Industry;
                bestWeight = preset.ResupplyLogisticsWeight;
            }
            if (preset.StealthRaidWeight > bestWeight)
            {
                best = ResearchBranch.Logistics;
                bestWeight = preset.StealthRaidWeight;
            }
            if (preset.RadarBeltScanWeight > bestWeight)
                best = ResearchBranch.Signals;
            return best;
        }

        private bool HasDormantGate(int playerId) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp &&
            _playerDormantGate[playerId];

        // What the next stress test needs of an item, kept back from scans, loads, builds and sales until it is passed.
        private int ProgressReserve(EntityId hq, ItemId item)
        {
            if (!TryNextStressTest(hq, out _, out var cost))
                return 0;
            int reserve = 0;
            for (int i = 0; i < cost.Length; i++)
                if (cost[i].Item == item)
                    reserve += cost[i].Quantity;
            return reserve;
        }

        // What opening the player's unlocked, closed jump gate needs of an item, kept back from scans and sales.
        private int GateReserve(int playerId, ItemId item)
        {
            if (!HasClosedGate(playerId))
                return 0;
            int reserve = 0;
            for (int i = 0; i < _gateBalance.OpenCostCount; i++)
                if (_gateBalance.OpenCost(i).Item == item)
                    reserve += _gateBalance.OpenCost(i).Quantity;
            return reserve;
        }

        private bool HasClosedGate(int playerId) =>
            _tech.Unlocks(playerId, TechGate.JumpGate) && HasDormantGate(playerId);

        // The claim cost, plus extra batteries a siege sets out with for its EW ships' point defence: a fight on the way
        // must not burn the claim's batteries.
        private bool CarriesClaimCost(ref Stockpile hold, int extraBatteries)
        {
            for (int i = 0; i < _claimBalance.ClaimCostCount; i++)
            {
                var cost = _claimBalance.ClaimCost(i);
                if (!hold.Has(cost.Item, cost.Quantity + (cost.Item == ItemId.ItemBattery ? extraBatteries : 0)))
                    return false;
            }
            return true;
        }

        private int EwBatteries(in FleetUnit unit) => unit.EwShips * _balance.LoadBatteryPerEwShip;

        private bool IsAway(EntityId site, int playerId) => _sites.Has(site) && _sites.Get(site).OwnerPlayerId != playerId;

        private bool IsClaimable(EntityId site, int playerId)
        {
            int owner = _sites.Get(site).OwnerPlayerId;
            return owner >= 0 && owner != playerId && !IsCoreBase(site);
        }

        private bool IsCoreBase(EntityId site) =>
            site.Index < _coreBaseStamp.Length && _coreBaseStamp[site.Index] == _thinkStamp;

        private long FleetPower(in FleetUnit unit) =>
            (long)unit.LineShips * _combatBalance.LineAlpha + (long)unit.EwShips * _combatBalance.EwBeta;

        private void ThinkDeposit(int playerId, EntityId coreBase, AiPreset preset, int denseIndex)
        {
            ref var deposit = ref _deposits.At(denseIndex);
            if (deposit.RetractUntilTick > 0 || !_sites.Has(deposit.Site) || _sites.Get(deposit.Site).OwnerPlayerId != playerId)
                return;

            int storm = 0;
            if (_weather.Has(deposit.Site))
            {
                ref var weather = ref _weather.Get(deposit.Site);
                int level = Math.Max(weather.GeomagneticStormLevel, weather.SolarRadiationLevel);
                if (level >= preset.EvacuateStormLevel)
                    storm = Severity(level - preset.EvacuateStormLevel + 1, 6 - preset.EvacuateStormLevel);
            }
            int threat = 0;
            long hostile = Power(deposit.Site, playerId, mine: false);
            if (hostile > 0)
            {
                long ratio = Power(deposit.Site, playerId, mine: true) * Full / hostile;
                if (ratio < preset.RetreatForceRatioMilli)
                    threat = Severity(preset.RetreatForceRatioMilli - ratio, preset.RetreatForceRatioMilli);
            }
            int stocked = _stockpiles.Has(deposit.Site) ? _stockpiles.Get(deposit.Site).Get(deposit.RawItem) : 0;
            if (deposit.Site != coreBase && _stockpiles.Has(coreBase))
                stocked += _stockpiles.Get(coreBase).Get(deposit.RawItem); // an outpost's ore only goes to a core that lacks it
            if (deposit.RigCount == 0)
            {
                if (storm == 0 && threat == 0 && stocked < _balance.RawStockCap / 2)
                    ThinkRebuild(playerId, preset, denseIndex);
                return;
            }
            if (Score(preset.EvacuateSafetyWeight, threat) > 0)
            {
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = playerId, CommandType = CommandType.RetractDrills, Target = _deposits.EntityAt(denseIndex)
                }, AiAction.RetractDrills, Score(preset.EvacuateSafetyWeight, threat));
                return;
            }
            int score = Score(preset.EvacuateSafetyWeight, storm);
            bool pause = score > 0 || stocked >= _balance.RawStockCap || IsRationed(coreBase, in deposit);
            if (score == 0)
                score = Score(preset.ResupplyLogisticsWeight, Threshold);
            if (pause != deposit.IsPaused && (pause || stocked < _balance.RawStockCap / 2))
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = playerId, CommandType = CommandType.PauseDrills, Target = _deposits.EntityAt(denseIndex),
                    ParamA = pause ? 1 : 0
                }, AiAction.PauseDrills, score);
        }

        // While the core base holds under its drill reserve of fuel or coolant, only the drills feeding those two refineries
        // run there: the others would burn the last fuel the water drill needs to make more, and every drill would stall.
        private bool IsRationed(EntityId coreBase, in MineralDeposit deposit)
        {
            if (deposit.Site != coreBase || !_stockpiles.Has(coreBase))
                return false;
            ref var stock = ref _stockpiles.Get(coreBase);
            if (stock.Get(ItemId.ItemFuel) >= _balance.CoreSupplyReserve && stock.Get(ItemId.ItemCoolant) >= _balance.CoreSupplyReserve)
                return false;
            int lifeline = RecipeRaws(ItemId.ItemFuel) | RecipeRaws(ItemId.ItemCoolant);
            if ((lifeline & (1 << ItemIds.ToSlot(deposit.RawItem))) == 0)
                return true;
            int partner = deposit.RawItem == ItemId.RawWater
                ? stock.Get(ItemId.RawMethane) + stock.Get(ItemId.RawNitrogen)
                : stock.Get(ItemId.RawWater);
            int cap = deposit.IsPaused ? _balance.ConvoyBatch / 2 : _balance.ConvoyBatch;
            return partner == 0 && stock.Get(deposit.RawItem) >= cap;
        }

        // Rigs back on a bare deposit with ore left, up to its target, when the site can pay for them all.
        private void ThinkRebuild(int playerId, AiPreset preset, int denseIndex)
        {
            ref var deposit = ref _deposits.At(denseIndex);
            if (preset.ResupplyLogisticsWeight == 0 || !HasOre(in deposit) || !_stockpiles.Has(deposit.Site))
                return;
            int rigs = Math.Min(RigTarget(playerId, in deposit), _miningBalance.MaxRigsPerDeposit);
            if (rigs <= 0)
                return;
            ref var stock = ref _stockpiles.Get(deposit.Site);
            for (int i = 0; i < _miningBalance.RigCostCount; i++)
                if (!stock.Has(_miningBalance.RigCost(i).Item, _miningBalance.RigCost(i).Quantity * rigs))
                    return;
            Issue(new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = CommandType.BuildRig, Target = _deposits.EntityAt(denseIndex), ParamA = rigs
            }, AiAction.BuildRig, Score(preset.ResupplyLogisticsWeight, Full));
        }

        // The new-game rigs for the deposit's ore at the player's core base (none for an ore not listed); outpost rigs elsewhere.
        private int RigTarget(int playerId, in MineralDeposit deposit)
        {
            if (!IsCoreBaseOf(playerId, deposit.Site))
                return _balance.OutpostRigs;
            for (int i = 0; i < _newGame.CoreBaseDepositCount; i++)
                if (_newGame.CoreBaseDeposit(i).Item == deposit.RawItem)
                    return _newGame.CoreBaseDeposit(i).Quantity;
            return 0;
        }

        private bool IsCoreBaseOf(int playerId, EntityId site) =>
            playerId > 0 && playerId < _playerStamp.Length && _playerStamp[playerId] == _thinkStamp && _playerCoreBase[playerId] == site;

        private static long OreLeft(in MineralDeposit deposit) =>
            (long)deposit.SurfaceReserve + deposit.DeepCrustReserve +
            (deposit.IsCoreFractureUnlocked ? deposit.CoreFractureReserve : 0);

        private static bool HasOre(in MineralDeposit deposit) =>
            deposit.SurfaceReserve > 0 || deposit.DeepCrustReserve > 0 ||
            (deposit.IsCoreFractureUnlocked && deposit.CoreFractureReserve > 0);

        private void ThinkRadar(in PlayerHq player, AiPreset preset, EntityId hq)
        {
            if (!_stockpiles.Has(player.CoreBase))
                return;
            // Scans spend only what the fleets' reserve, the next stress test, the gate opening and a settler's next claim
            // leave over.
            int batteries = _stockpiles.Get(player.CoreBase).Get(ItemId.ItemBattery) - _balance.ScanBatteryReserve -
                            ProgressReserve(hq, ItemId.ItemBattery) -
                            GateReserve(player.PlayerId, ItemId.ItemBattery) -
                            (SettleWeight(preset) > 0 ? (int)ExpansionLoad(ItemId.ItemBattery) : 0);
            long full = (long)_balance.FullBatteryScans * _radarBalance.StartupBatteries;
            long percent = Math.Min(100, batteries * 100L / full);
            if (percent <= preset.MinBatteryForScanPercent || batteries < _radarBalance.StartupBatteries)
                return;

            for (int k = 0; k < _bypassLaneTotal; k++)
            {
                int i = _bypassLanes[k];
                ref var lane = ref _lanes.At(i);
                if (lane.Tier != StarlaneTier.OuterRimBypass || !(IsOwnSite(lane.SiteA, player.PlayerId) ||
                    IsOwnSite(lane.SiteB, player.PlayerId)) || IsScanning(player.PlayerId, _lanes.EntityAt(i)))
                    continue;
                int charge = Severity(percent - preset.MinBatteryForScanPercent, 100 - preset.MinBatteryForScanPercent);
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.ScanBelt, Target = _lanes.EntityAt(i)
                }, AiAction.RadarScan, Score(preset.RadarBeltScanWeight, charge));
                return;
            }
        }

        /// <summary>IAUS with one consideration: weight x consideration, in milli-units.</summary>
        public static int Score(int weightMilli, int considerationMilli)
        {
            CheckConsideration(considerationMilli);
            return (int)((long)weightMilli * considerationMilli / Full);
        }

        /// <summary>IAUS with two considerations: weight x their geometric mean, in milli-units.</summary>
        public static int Score(int weightMilli, int firstMilli, int secondMilli)
        {
            CheckConsideration(firstMilli);
            CheckConsideration(secondMilli);
            return (int)((long)weightMilli * Root((long)firstMilli * secondMilli, 2) / Full);
        }

        private static void CheckConsideration(int milli)
        {
            if (milli < 0 || milli > Full) throw new ArgumentOutOfRangeException(nameof(milli));
        }

        // Largest r with r^m <= product: the m-th root of a product of m milli-values, in milli-units.
        private static long Root(long product, int m)
        {
            long lo = 0, hi = Full;
            while (lo < hi)
            {
                long mid = (lo + hi + 1) / 2;
                long power = 1;
                for (int i = 0; i < m && power <= product; i++)
                    power *= mid;
                if (power <= product)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        // A triggered consideration: 500 at the threshold, 1000 at (or past) the extreme.
        private static int Severity(long over, long range)
        {
            if (range <= 0)
                return Full;
            return Threshold + (int)(Math.Min(Math.Max(over, 0), range) * (Full - Threshold) / range);
        }

        private static void Consider(AiAction action, int score, ref AiAction best, ref int bestScore)
        {
            if (score > bestScore)
            {
                best = action;
                bestScore = score;
            }
        }

        private long Power(EntityId site, int playerId, bool mine)
        {
            if (site.IsNull || site.Index >= _siteFleetStamp.Length || _siteFleetStamp[site.Index] != _thinkStamp)
                return 0;
            long power = 0;
            for (int i = _siteFirstFleet[site.Index]; i >= 0; i = _fleetNext[i])
            {
                ref var unit = ref _units.At(i);
                if ((unit.OwnerPlayerId == playerId) != mine)
                    continue;
                power += FleetPower(in unit);
            }
            return power;
        }

        private bool IsOwnSite(EntityId site, int playerId) => _sites.Has(site) && _sites.Get(site).OwnerPlayerId == playerId;

        private bool IsScanning(int playerId, EntityId lane)
        {
            for (int i = 0; i < _scans.Count; i++)
            {
                ref var scan = ref _scans.At(i);
                if (scan.OwnerPlayerId == playerId && scan.Lane == lane)
                    return true;
            }
            return false;
        }

        private void Issue(DelayedPlayerCommand command, AiAction action, int score) => Issue(command, action, score, command.Target);

        private void Issue(DelayedPlayerCommand command, AiAction action, int score, EntityId subject)
        {
            if (_issuer.IsInFlight(command.PlayerId, command.CommandType, command.Target))
                return;
            var result = _issuer.Issue(command, out _);
            if (_orderCount == _orders.Length)
                Array.Resize(ref _orders, _orderCount * 2);
            _orders[_orderCount++] = new AiOrder
            {
                PlayerId = command.PlayerId, Action = action, Subject = subject, ScoreMilli = score, Result = result
            };
        }
    }
}
