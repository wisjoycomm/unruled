using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
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
        BuyFromSystem = 33
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
    /// world only through <see cref="CommandIssuer"/>, with the same latency as a player.
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
    /// <item>A fleet ready at a claimable site carrying the claim cost: claim it (Siege_Conquest). Ready at a site that is
    /// not its player's, with nothing to fight or claim: go home (Resupply_Logistics at half).</item>
    /// <item>A fleet ready at its own outpost (Siege_Conquest): rigs on each bare deposit with ore left, unloading the rig
    /// cost it carried from the core base first (it loads that for every deposit of a site with the claim cost); then
    /// home.</item>
    /// <item>A convoy-only fleet without a route (Resupply_Logistics): at the core base, a convoy route to an outpost with
    /// rigs that no convoy serves; elsewhere, home. The player builds a convoy fleet when such an outpost has no idle
    /// convoy or convoy order, and a core-base refinery for a refined item it lacks once both inputs are stocked.</item>
    /// </list>
    /// A triggered consideration reads 500 at its threshold, rising to 1000 at the extreme. Fleet orders also weigh a
    /// can-do consideration (coolant in the hold to vent, fuel to leave) that vetoes the action at 0. Power is line ships x
    /// LineAlpha plus EW ships x EwBeta (PROVISIONAL). Orders already in effect score 0; an order of the same type for
    /// the same target still in signal latency is not sent again.
    /// </summary>
    public sealed class UtilityAiEngine
    {
        private const int Full = 1000;
        private const int Threshold = 500;

        private readonly ComponentStore<AiController> _controllers;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<FleetUnit> _units;
        private readonly ComponentStore<FleetMotion> _motions;
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
        private readonly Ledger _ledger;
        private readonly EntityId _systemStock;
        private readonly ITollGate _tolls;
        private readonly IMarketRates _rates;
        private readonly CommandIssuer _issuer;
        private readonly RoutePlanner _routes;
        private readonly ITechGates _tech;
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

        private FleetRoute _route;

        private AiOrder[] _orders = new AiOrder[16];
        private int _orderCount;

        public UtilityAiEngine(
            ComponentStore<AiController> controllers, ComponentStore<PlayerHq> players, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetCombatState> combat,
            ComponentStore<FleetReadiness> readiness, ComponentStore<Stockpile> stockpiles, ComponentStore<SiteInfo> sites,
            ComponentStore<SiteWeather> weather, ComponentStore<MineralDeposit> deposits, ComponentStore<StarlaneEdge> lanes,
            ComponentStore<RadarScan> scans, ComponentStore<TechProgress> progress, ComponentStore<JumpGate> gates,
            ComponentStore<ConvoyRoute> convoyRoutes, ComponentStore<ShipBuildOrder> shipOrders,
            ComponentStore<Refinery> refineries, ComponentStore<PlayerStanding> standings, ComponentStore<SystemCitadel> citadels,
            ComponentStore<WtoOrder> wtoOrders, ComponentStore<ItemMarket> markets, ComponentStore<Loan> loans, Ledger ledger,
            EntityId systemStock, IMarketRates rates,
            CommandIssuer issuer,
            RoutePlanner routes, ITollGate tolls, ITechGates tech, GameBalanceConfig config)
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
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (systemStock.IsNull) throw new ArgumentException("THE SYSTEM's stock required.", nameof(systemStock));
            _systemStock = systemStock;
            _rates = rates ?? throw new ArgumentNullException(nameof(rates));
            _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _tolls = tolls ?? throw new ArgumentNullException(nameof(tolls));
            _tech = tech ?? throw new ArgumentNullException(nameof(tech));
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

        /// <summary>Runs one think on ticks that are a multiple of the interval; returns the orders tried.</summary>
        public int Think(long tick)
        {
            if (tick % _balance.ThinkIntervalTicks != 0)
                return 0;
            _orderCount = 0;
            for (int c = 0; c < _controllers.Count; c++)
            {
                var preset = _balance.Preset(_controllers.At(c).Archetype);
                ref var player = ref _players.Get(_controllers.EntityAt(c));
                for (int f = 0; f < _units.Count; f++)
                {
                    if (_units.At(f).OwnerPlayerId == player.PlayerId && _units.At(f).TotalShips > 0)
                        ThinkFleet(player, preset, _controllers.EntityAt(c), _units.EntityAt(f), tick);
                }
                for (int d = 0; d < _deposits.Count; d++)
                    ThinkDeposit(player.PlayerId, preset, d);
                if (tick % SimTick.TicksPerHour < _balance.ScanWindowTicks)
                    ThinkRadar(player, preset, _controllers.EntityAt(c));
                ThinkProgress(player, preset, _controllers.EntityAt(c));
                ThinkEconomy(player, preset, _controllers.EntityAt(c));
                ThinkShipyard(player, preset, _controllers.EntityAt(c));
                ThinkFinance(player, preset, _controllers.EntityAt(c), tick);
                ThinkTrade(player, preset, _controllers.EntityAt(c), tick);
            }
            return _orderCount;
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
                rigDeposit = default;
            var loadItem = ItemId.None;
            int loadQuantity = 0;
            if (docked && motion.Site == player.CoreBase)
            {
                bool telemetry = _progress.Has(hq) && _progress.Get(hq).Stage == ResearchStage.FieldTelemetry;
                if (TryPickLoad(in player, hq, preset, telemetry, in unit, ref hold, out loadItem, out loadQuantity,
                        out int shortage))
                {
                    Consider(AiAction.LoadCargo, Score(preset.ResupplyLogisticsWeight, shortage), ref best, ref bestScore);
                }
                else if (IsBattleFleet(in unit))
                {
                    // A full battle line that can't take the Citadel yet waits mothballed, at a fifth of the upkeep. It
                    // only sets out with the ammo it would otherwise turn back for.
                    bool mothballed = _readiness.Has(fleet) && _readiness.Get(fleet).IsMothballed;
                    int citadelEdge = 0;
                    bool canGo = preset.SiegeConquestWeight > 0 &&
                                 hold.Get(ItemId.ItemAmmo) >= (long)unit.LineShips * _balance.ResupplyAmmoPerLineShip &&
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
                    int fuel = hold.Get(ItemId.ItemFuel);
                    if (preset.StealthRaidWeight > 0 &&
                        FindTarget(player, in unit, fuel, raid: true, homeOnly: false, out raidTarget, out int raidEdge))
                        Consider(AiAction.StealthRaid, Score(preset.StealthRaidWeight, raidEdge), ref best, ref bestScore);
                    if (SettleWeight(preset) > 0 && CarriesClaimCost(ref hold, EwBatteries(in unit)) &&
                        FindTarget(player, in unit, fuel, raid: false, homeOnly: IsHomeSettler(preset), out siegeTarget,
                            out int siegeEdge))
                        Consider(AiAction.Siege, Score(SettleWeight(preset), siegeEdge), ref best, ref bestScore);
                    if (SettleWeight(preset) > 0 && FindOutpostToDevelop(player, in unit, fuel, ref hold, out developTarget))
                        Consider(AiAction.DevelopOutpost, Score(SettleWeight(preset), Full), ref best, ref bestScore);
                }
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
            if (TryLoad(ref stock, hq, ref hold, ItemId.ItemAmmo, (long)unit.LineShips * _balance.LoadAmmoPerLineShip,
                    ref item, ref quantity, ref shortage) ||
                (unit.EwShips > 0 && TryLoad(ref stock, hq, ref hold, ItemId.ItemBattery,
                    EwBatteries(in unit) + (SettleWeight(preset) > 0 ? ExpansionLoad(ItemId.ItemBattery) : 0), ref item,
                    ref quantity, ref shortage)) ||
                TryLoad(ref stock, hq, ref hold, ItemId.ItemFuel, FuelToLoad(in player, preset, in unit), ref item, ref quantity,
                    ref shortage) ||
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
            else if (SettleWeight(preset) > 0 &&
                     FindTarget(player, in unit, int.MaxValue, raid: false, homeOnly: IsHomeSettler(preset), out var target, out _) &&
                     PlanTrip(player.CoreBase, target, in unit, trips: 2))
                fuel = Math.Max(fuel, 2L * RouteFuel(in unit));
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
            for (int d = 0; d < _deposits.Count; d++)
            {
                ref var candidate = ref _deposits.At(d);
                if (candidate.Site != site || candidate.RigCount != 0 || candidate.RetractUntilTick != 0 || !HasOre(in candidate))
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

        // A convoy-only fleet without a route: at the core base, take an outpost no convoy serves; elsewhere go home.
        private void ThinkConvoy(in PlayerHq player, AiPreset preset, EntityId fleet, long tick)
        {
            if (_convoyRoutes.Has(fleet))
                return;
            ref var motion = ref _motions.Get(fleet);
            if (!motion.Lane.IsNull || motion.Site.IsNull || _combat.Get(fleet).EngagedUntilTick > tick)
                return;
            var command = new DelayedPlayerCommand { PlayerId = player.PlayerId, Target = fleet };
            if (motion.Site != player.CoreBase)
            {
                if (_readiness.Has(fleet) && !_readiness.Get(fleet).CanAct(tick))
                    return;
                command.CommandType = CommandType.Move;
                command.ParamEntity = player.CoreBase;
                Issue(command, AiAction.ReturnHome, Score(preset.ResupplyLogisticsWeight, Threshold), fleet);
                return;
            }
            var outpost = FindUnservedOutpost(in player, _units.Get(fleet).TechTier);
            if (outpost.IsNull)
                return;
            command.CommandType = CommandType.SetConvoyRoute;
            command.ParamEntity = outpost;
            command.ParamC = (int)Math.Min((long)_units.Get(fleet).CargoConvoys * _convoyBalance.CapacityPerConvoy, _balance.ConvoyBatch);
            Issue(command, AiAction.SetConvoyRoute, Score(preset.ResupplyLogisticsWeight, Full), fleet);
        }

        // The first own site (not the core base) with a rigged deposit, a route from the core base and no convoy route.
        private EntityId FindUnservedOutpost(in PlayerHq player, byte tier)
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                var site = _sites.EntityAt(i);
                if (_sites.At(i).OwnerPlayerId != player.PlayerId || site == player.CoreBase || !HasRigs(site) ||
                    IsServed(site) || !_routes.TryPlan(player.CoreBase, site, tier, ref _route))
                    continue;
                return site;
            }
            return default;
        }

        private bool HasRigs(EntityId site)
        {
            for (int d = 0; d < _deposits.Count; d++)
                if (_deposits.At(d).Site == site && _deposits.At(d).RigCount > 0)
                    return true;
            return false;
        }

        private bool IsServed(EntityId outpost)
        {
            for (int r = 0; r < _convoyRoutes.Count; r++)
                if (_convoyRoutes.At(r).Outpost == outpost)
                    return true;
            return false;
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
            if (!FindUnservedOutpost(in player, convoyTier).IsNull && !HasIdleConvoy(player.PlayerId) &&
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

            int built = 0;
            for (int r = 0; r < _refineries.Count; r++)
                if (_refineries.At(r).Site == player.CoreBase)
                    built++;
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
            if (_progress.Has(hq) && _progress.Get(hq).Stage <= ResearchStage.StressTest)
            {
                var cost = _researchBalance.Tier((byte)(_progress.Get(hq).TechTier + 1)).StressTestCost;
                int mined = MinedOres(player.PlayerId);
                for (int i = 0; i < cost.Length; i++)
                {
                    var output = cost[i].Item;
                    if (ItemIds.IsRaw(output) || HasRefinery(player.CoreBase, output) || stock.Has(output, cost[i].Quantity))
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

        // THE SYSTEM's credit line: below the travel cash it borrows up to the cash reserve, repays from cash above twice it,
        // and once the debt falls due (or is about to, within an AI think of it) repays all it can, so its goods aren't seized.
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
                borrow = Math.Min(reserve - cash, Math.Max(0, _ledgerBalance.CreditLimitCents - loan.DebtCents));
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
            for (int o = 0; o < _wtoOrders.Count; o++)
            {
                ref var order = ref _wtoOrders.At(o);
                if (order.OwnerPlayerId != player.PlayerId || tick - order.PlacedTick < _balance.TradeOrderTicks ||
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
            if (_progress.Has(hq) && _progress.Get(hq).Stage <= ResearchStage.StressTest)
            {
                var cost = _researchBalance.Tier((byte)(_progress.Get(hq).TechTier + 1)).StressTestCost;
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
        private int Keep(int feeds, ItemId item, int threshold) => Feeds(feeds, item) ? _balance.RawStockCap : threshold;

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
            for (int o = 0; o < _wtoOrders.Count; o++)
            {
                ref var bid = ref _wtoOrders.At(o);
                if (bid.Side != OrderSide.Buy || bid.OwnerPlayerId == player.PlayerId || bid.PriceCents <= price)
                    continue;
                int surplus = Surplus(player.PlayerId, hq, ref stock, bid.Item, Keep(feeds, bid.Item, _balance.FillAbove));
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
            for (int o = 0; o < _wtoOrders.Count; o++)
                if (_wtoOrders.At(o).OwnerPlayerId == playerId && _wtoOrders.At(o).Side == OrderSide.Sell)
                    return false;
            if (!FindSurplus(playerId, hq, ref stock, feeds, out item))
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
                if (Surplus(playerId, hq, ref stock, item, Keep(feeds, item, _balance.SellAbove)) >= _balance.SellBatch)
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
            for (int o = 0; o < _wtoOrders.Count; o++)
            {
                ref var ask = ref _wtoOrders.At(o);
                if (ask.Side != OrderSide.Sell || ask.OwnerPlayerId == playerId || !Feeds(feeds, ask.Item) ||
                    stock.Get(ask.Item) >= _balance.SellAbove || (wanted > 0 && ask.PriceCents >= price) ||
                    HasOpenOrder(playerId, ask.Item, OrderSide.Buy))
                    continue;
                item = ask.Item;
                price = ask.PriceCents;
                wanted = Math.Min(ask.Remaining, _balance.SellBatch);
            }
            return wanted > 0;
        }

        private static readonly FleetUnit ContractConvoy = new FleetUnit { TechTier = WtoContractSystem.ConvoyTier, CargoConvoys = 1 };

        private bool HasOpenOrder(int playerId, ItemId item, OrderSide side)
        {
            for (int o = 0; o < _wtoOrders.Count; o++)
            {
                ref var order = ref _wtoOrders.At(o);
                if (order.OwnerPlayerId == playerId && order.Item == item && order.Side == side)
                    return true;
            }
            return false;
        }

        // Reference price moved by percent (within the WTO band, which is wider); at least one cent.
        private long TradePrice(ItemId item, long tick, int percent)
        {
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
            for (int f = 0; f < _units.Count; f++)
                if (_units.At(f).OwnerPlayerId == playerId && _units.At(f).IsConvoyOnly && !_convoyRoutes.Has(_units.EntityAt(f)))
                    return true;
            return false;
        }

        private bool HasShipOrder(int playerId, TargetFocusClass shipClass)
        {
            for (int o = 0; o < _shipOrders.Count; o++)
                if (_shipOrders.At(o).OwnerPlayerId == playerId && _shipOrders.At(o).ShipClass == shipClass)
                    return true;
            return false;
        }

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

        // Shipyard (Defense_Hold or Siege_Conquest), from the Citadel assault tier on (earlier, ships would eat the steel
        // the economy grows on): refits a battle fleet docked at the core base to the research tier, else builds line
        // ships of that tier up to the battle line. One shipyard order in flight at a time.
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
            for (int f = 0; f < _units.Count; f++)
            {
                ref var unit = ref _units.At(f);
                if (unit.OwnerPlayerId != player.PlayerId || unit.IsConvoyOnly || !IsBattleFleet(in unit))
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

        private bool HasRefinery(EntityId site, ItemId output)
        {
            for (int r = 0; r < _refineries.Count; r++)
                if (_refineries.At(r).Site == site && _refineries.At(r).Output == output)
                    return true;
            return false;
        }

        // Fill-up-to order for the hold: never below the core reserve of fuel and coolant, or the next stress test's cost.
        private bool TryLoad(
            ref Stockpile stock, EntityId hq, ref Stockpile hold, ItemId id, long target, ref ItemId item, ref int quantity,
            ref int shortage)
        {
            long gap = target - hold.Get(id);
            long reserve = id == ItemId.ItemFuel || id == ItemId.ItemCoolant ? _balance.CoreSupplyReserve : 0;
            long available = stock.Get(id) - reserve - ProgressReserve(hq, id);
            if (gap <= 0 || available <= 0)
                return false;
            item = id;
            quantity = (int)Math.Min(Math.Min(target, hold.Get(id) + available), int.MaxValue);
            shortage = Severity(gap, target);
            return true;
        }

        // Best site this fleet outguns and has the fuel to reach and come back from: a raid hunts rival fleets at a rival
        // site; a siege takes a neutral or rival site, preferring the most ores the player mines nowhere yet. Never a
        // core base or THE SYSTEM's site.
        private bool FindTarget(in PlayerHq player, in FleetUnit unit, int fuel, bool raid, bool homeOnly, out EntityId target,
            out int edge)
        {
            long power = FleetPower(in unit);
            target = default;
            edge = 0;
            int bestLanes = int.MaxValue;
            int bestOres = 0;
            int mined = raid ? 0 : MinedOres(player.PlayerId);
            int feeds = raid ? 0 : NeededRaws(in player);
            for (int i = 0; i < _sites.Count; i++)
            {
                ref var site = ref _sites.At(i);
                var entity = _sites.EntityAt(i);
                if (site.OwnerPlayerId < 0 || site.OwnerPlayerId == player.PlayerId || (raid && site.OwnerPlayerId == 0) ||
                    IsCoreBase(entity) || (homeOnly && !IsNeutralAtHome(entity, player.CoreBase)))
                    continue;
                long hostile = Power(entity, player.PlayerId, mine: false);
                if (hostile >= power || (raid && hostile == 0))
                    continue;
                int advantage = Severity(power - hostile, power);
                int ores = raid ? 0 : NewOres(entity, mined, feeds);
                if (!raid && site.OwnerPlayerId == 0 && ores == 0)
                    continue; // a neutral site with no new ore isn't worth the claim cost
                if (ores < bestOres || (ores == bestOres && advantage < edge) ||
                    !PlanTrip(player.CoreBase, entity, in unit, trips: 2) || 2L * RouteFuel(in unit) > fuel)
                    continue;
                if (ores == bestOres && advantage == edge && _route.Count >= bestLanes)
                    continue;
                target = entity;
                edge = advantage;
                bestLanes = _route.Count;
                bestOres = ores;
            }
            return !target.IsNull;
        }

        // The nearest unthreatened own outpost with a bare deposit of ore worth rigging, whose rigs the hold and the site's
        // stock can pay together, within a round trip of fuel.
        private bool FindOutpostToDevelop(in PlayerHq player, in FleetUnit unit, int fuel, ref Stockpile hold, out EntityId target)
        {
            target = default;
            int bestLanes = int.MaxValue;
            int rigs = Math.Min(_balance.OutpostRigs, _miningBalance.MaxRigsPerDeposit);
            int mined = MinedOres(player.PlayerId);
            int feeds = NeededRaws(in player);
            for (int d = 0; d < _deposits.Count; d++)
            {
                ref var deposit = ref _deposits.At(d);
                var site = deposit.Site;
                if (deposit.RigCount != 0 || deposit.RetractUntilTick != 0 || !HasOre(in deposit) || site == target ||
                    DepositRank(deposit.RawItem, mined, feeds) == 0 ||
                    site == player.CoreBase || !IsOwnSite(site, player.PlayerId) || !_stockpiles.Has(site) ||
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

        private bool IsStandingCitadel(EntityId site) => _citadels.Has(site) && _citadels.Get(site).FallenTick == 0;

        // Bit per raw item slot the player has rigs on somewhere with ore left (a mined-out deposit sends it looking again).
        private int MinedOres(int playerId)
        {
            int mask = 0;
            for (int d = 0; d < _deposits.Count; d++)
            {
                ref var deposit = ref _deposits.At(d);
                if (deposit.RigCount > 0 && HasOre(in deposit) && _sites.Has(deposit.Site) &&
                    _sites.Get(deposit.Site).OwnerPlayerId == playerId)
                    mask |= 1 << ItemIds.ToSlot(deposit.RawItem);
            }
            return mask;
        }

        // Deposits at the site with ore left of a raw item outside the mined mask; one a core refinery needs counts double.
        private int NewOres(EntityId site, int mined, int feeds)
        {
            int count = 0;
            for (int d = 0; d < _deposits.Count; d++)
            {
                ref var deposit = ref _deposits.At(d);
                int bit = 1 << ItemIds.ToSlot(deposit.RawItem);
                if (deposit.Site == site && HasOre(in deposit) && (mined & bit) == 0)
                    count += (feeds & bit) != 0 ? 2 : 1;
            }
            return count;
        }

        // Bit per raw item slot the player needs: what a core-base refinery takes in, and what goes into the next stress
        // test's cost (refined items through their recipe).
        private int NeededRaws(in PlayerHq player)
        {
            int mask = 0;
            for (int r = 0; r < _refineries.Count; r++)
                if (_refineries.At(r).Site == player.CoreBase)
                    mask |= RecipeRaws(_refineries.At(r).Output);
            for (int h = 0; h < _players.Count; h++)
            {
                if (_players.At(h).PlayerId != player.PlayerId)
                    continue;
                var hq = _players.EntityAt(h);
                if (!_progress.Has(hq) || _progress.Get(hq).Stage > ResearchStage.StressTest)
                    break;
                var cost = _researchBalance.Tier((byte)(_progress.Get(hq).TechTier + 1)).StressTestCost;
                for (int i = 0; i < cost.Length; i++)
                    mask |= ItemIds.IsRaw(cost[i].Item) ? 1 << ItemIds.ToSlot(cost[i].Item) : RecipeRaws(cost[i].Item);
                break;
            }
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
        private bool PlanTrip(EntityId from, EntityId to, in FleetUnit unit, int trips, out bool tollFree)
        {
            tollFree = false;
            if (!_routes.TryPlan(from, to, unit.TechTier, ref _route))
                return false;
            long tolls = 0;
            for (int i = 0; i < _route.Count; i++)
                tolls += _tolls.QuoteCents(unit, _lanes.Get(_route.LaneAt(i)));
            if (tolls == 0 ||
                (_ledger.TryFindPlayerWallet(unit.OwnerPlayerId, out var wallet) && trips * tolls <= _ledger.Balance(wallet)))
                return true;
            tollFree = true;
            return _routes.TryPlan(from, to, unit.TechTier, tollFree: true, ref _route);
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
            if (_progress.Has(hq) && _progress.Get(hq).Stage == ResearchStage.StressTest && preset.RadarBeltScanWeight > 0)
            {
                var cost = _researchBalance.Tier((byte)(_progress.Get(hq).TechTier + 1)).StressTestCost;
                bool affordable = true;
                for (int i = 0; i < cost.Length; i++)
                    affordable &= stock.Has(cost[i].Item, cost[i].Quantity);
                if (affordable)
                    Issue(new DelayedPlayerCommand
                    {
                        PlayerId = player.PlayerId, CommandType = CommandType.StressTest, Target = player.CoreBase
                    }, AiAction.StressTest, Score(preset.RadarBeltScanWeight, Full));
            }

            // The gate carries the expansion fleets and the WTO contract convoys both ways, so traders open it too.
            int expand = Math.Max(Math.Max(preset.StealthRaidWeight, preset.SiegeConquestWeight), preset.ResupplyLogisticsWeight);
            if (expand == 0 || !_tech.Unlocks(player.PlayerId, TechGate.JumpGate))
                return;
            for (int g = 0; g < _gates.Count; g++)
            {
                ref var gate = ref _gates.At(g);
                if (gate.OwnerPlayerId != player.PlayerId || gate.IsOpen)
                    continue;
                for (int i = 0; i < _gateBalance.OpenCostCount; i++)
                    if (!stock.Has(_gateBalance.OpenCost(i).Item, _gateBalance.OpenCost(i).Quantity))
                        return;
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = player.PlayerId, CommandType = CommandType.OpenJumpGate, Target = gate.HomeSite
                }, AiAction.OpenGate, Score(expand, Full));
                return;
            }
        }

        // What the next stress test needs of an item, kept back from scans, loads, builds and sales until it is passed.
        private int ProgressReserve(EntityId hq, ItemId item)
        {
            if (!_progress.Has(hq) || _progress.Get(hq).Stage > ResearchStage.StressTest)
                return 0;
            var cost = _researchBalance.Tier((byte)(_progress.Get(hq).TechTier + 1)).StressTestCost;
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

        private bool HasClosedGate(int playerId)
        {
            if (!_tech.Unlocks(playerId, TechGate.JumpGate))
                return false;
            for (int g = 0; g < _gates.Count; g++)
                if (_gates.At(g).OwnerPlayerId == playerId && !_gates.At(g).IsOpen)
                    return true;
            return false;
        }

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

        private bool IsCoreBase(EntityId site)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).CoreBase == site)
                    return true;
            return false;
        }

        private long FleetPower(in FleetUnit unit) =>
            (long)unit.LineShips * _combatBalance.LineAlpha + (long)unit.EwShips * _combatBalance.EwBeta;

        private void ThinkDeposit(int playerId, AiPreset preset, int denseIndex)
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
            bool pause = score > 0 || stocked >= _balance.RawStockCap;
            if (score == 0)
                score = Score(preset.ResupplyLogisticsWeight, Threshold);
            if (pause != deposit.IsPaused && (pause || stocked < _balance.RawStockCap / 2))
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = playerId, CommandType = CommandType.PauseDrills, Target = _deposits.EntityAt(denseIndex),
                    ParamA = pause ? 1 : 0
                }, AiAction.PauseDrills, score);
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

        private bool IsCoreBaseOf(int playerId, EntityId site)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players.At(i).PlayerId == playerId)
                    return _players.At(i).CoreBase == site;
            return false;
        }

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

            for (int i = 0; i < _lanes.Count; i++)
            {
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
            long power = 0;
            for (int i = 0; i < _units.Count; i++)
            {
                ref var unit = ref _units.At(i);
                if ((unit.OwnerPlayerId == playerId) != mine || _motions.Get(_units.EntityAt(i)).Site != site)
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
