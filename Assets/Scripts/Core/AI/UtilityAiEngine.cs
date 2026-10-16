using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;

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
        ReturnHome = 12
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
    /// <item>Each drilled deposit: retract the drills (Evacuate_Safety) when the G or S storm at its site reaches
    /// the preset level, or the player's power there falls below the retreat ratio.</item>
    /// <item>The player: one focused scan (Radar_Belt_Scan) of an unscanned bypass lane at one of its sites while the
    /// core base holds more than the preset minimum battery.</item>
    /// <item>The player: a stress test (Radar_Belt_Scan, the research weight) once telemetry is complete and the core base
    /// can pay; opening its jump gate (the higher of Stealth_Raid and Siege_Conquest) at the gate tier when the core base
    /// can pay. A preset with neither weight stays sealed at home.</item>
    /// <item>A fleet ready at the core base: load ammo and fuel up to the load targets, microchips for storm telemetry
    /// while the player researches, plus the claim cost when it
    /// besieges (Resupply_Logistics). Fully loaded, it picks a target it outguns with fuel for the round trip: a stealth raid
    /// (Stealth_Raid) on rival fleets at a rival site, or a siege (Siege_Conquest) of a neutral or rival site (never a
    /// core base) while it carries the claim cost. Advantage reads 500 at even power up to 1000 when undefended; the
    /// best advantage wins, then the fewest lanes.</item>
    /// <item>A fleet ready at a claimable site carrying the claim cost: claim it (Siege_Conquest). Ready at a site that is
    /// not its player's, with nothing to fight or claim: go home (Resupply_Logistics at half).</item>
    /// </list>
    /// A triggered consideration reads 500 at its threshold, rising to 1000 at the extreme. Fleet orders also weigh a
    /// can-do consideration (coolant in the hold to vent, fuel to leave) that vetoes the action at 0. Power is line ships x
    /// LineAlpha plus EW ships x EwBeta (PROVISIONAL). Orders already in effect score 0; orders still in flight may
    /// be issued again, which their handlers ignore.
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
        private readonly CommandIssuer _issuer;
        private readonly RoutePlanner _routes;
        private readonly ITechGates _tech;
        private readonly AiBalance _balance;
        private readonly CombatBalance _combatBalance;
        private readonly RadarBalance _radarBalance;
        private readonly ResearchBalance _researchBalance;
        private readonly JumpGateBalance _gateBalance;
        private readonly SiteControlBalance _claimBalance;

        private FleetRoute _route;

        private AiOrder[] _orders = new AiOrder[16];
        private int _orderCount;

        public UtilityAiEngine(
            ComponentStore<AiController> controllers, ComponentStore<PlayerHq> players, ComponentStore<FleetUnit> units,
            ComponentStore<FleetMotion> motions, ComponentStore<FleetCombatState> combat,
            ComponentStore<FleetReadiness> readiness, ComponentStore<Stockpile> stockpiles, ComponentStore<SiteInfo> sites,
            ComponentStore<SiteWeather> weather, ComponentStore<MineralDeposit> deposits, ComponentStore<StarlaneEdge> lanes,
            ComponentStore<RadarScan> scans, ComponentStore<TechProgress> progress, ComponentStore<JumpGate> gates,
            CommandIssuer issuer, RoutePlanner routes, ITechGates tech, AiBalance balance, CombatBalance combatBalance,
            RadarBalance radarBalance, ResearchBalance researchBalance, JumpGateBalance gateBalance,
            SiteControlBalance claimBalance)
        {
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
            _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _tech = tech ?? throw new ArgumentNullException(nameof(tech));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _combatBalance = combatBalance ?? throw new ArgumentNullException(nameof(combatBalance));
            _radarBalance = radarBalance ?? throw new ArgumentNullException(nameof(radarBalance));
            _researchBalance = researchBalance ?? throw new ArgumentNullException(nameof(researchBalance));
            _gateBalance = gateBalance ?? throw new ArgumentNullException(nameof(gateBalance));
            _claimBalance = claimBalance ?? throw new ArgumentNullException(nameof(claimBalance));
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
                ThinkRadar(player, preset);
                ThinkProgress(player, preset, _controllers.EntityAt(c));
            }
            return _orderCount;
        }

        private void ThinkFleet(in PlayerHq player, AiPreset preset, EntityId hq, EntityId fleet, long tick)
        {
            ref var unit = ref _units.Get(fleet);
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

            bool ready = motion.Lane.IsNull && !motion.Site.IsNull && state.EngagedUntilTick <= tick &&
                         (!_readiness.Has(fleet) || _readiness.Get(fleet).CanAct(tick));
            EntityId raidTarget = default, siegeTarget = default;
            var loadItem = ItemId.None;
            int loadQuantity = 0;
            if (ready && motion.Site == player.CoreBase)
            {
                bool telemetry = _progress.Has(hq) && _progress.Get(hq).Stage == ResearchStage.FieldTelemetry;
                if (TryPickLoad(player.CoreBase, preset, telemetry, in unit, ref hold, out loadItem, out loadQuantity,
                        out int shortage))
                {
                    Consider(AiAction.LoadCargo, Score(preset.ResupplyLogisticsWeight, shortage), ref best, ref bestScore);
                }
                else
                {
                    int fuel = hold.Get(ItemId.ItemFuel);
                    if (preset.StealthRaidWeight > 0 &&
                        FindTarget(player, in unit, fuel, raid: true, out raidTarget, out int raidEdge))
                        Consider(AiAction.StealthRaid, Score(preset.StealthRaidWeight, raidEdge), ref best, ref bestScore);
                    if (preset.SiegeConquestWeight > 0 && CarriesClaimCost(ref hold) &&
                        FindTarget(player, in unit, fuel, raid: false, out siegeTarget, out int siegeEdge))
                        Consider(AiAction.Siege, Score(preset.SiegeConquestWeight, siegeEdge), ref best, ref bestScore);
                }
            }
            else if (ready && IsAway(motion.Site, player.PlayerId) && Power(motion.Site, player.PlayerId, mine: false) == 0)
            {
                if (preset.SiegeConquestWeight > 0 && IsClaimable(motion.Site, player.PlayerId) && CarriesClaimCost(ref hold))
                    Consider(AiAction.ClaimSite, Score(preset.SiegeConquestWeight, Full), ref best, ref bestScore);
                else
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
                case AiAction.LoadCargo:
                    command.CommandType = CommandType.TransferCargo;
                    command.ParamA = (int)loadItem;
                    command.ParamB = loadQuantity;
                    command.ParamC = 1; // fill up to: an order re-issued while in flight changes nothing
                    break;
                case AiAction.StealthRaid:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = raidTarget;
                    break;
                case AiAction.Siege:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = siegeTarget;
                    break;
                case AiAction.ClaimSite:
                    command.CommandType = CommandType.ClaimSite;
                    command.Target = motion.Site;
                    break;
                default:
                    command.CommandType = CommandType.Move;
                    command.ParamEntity = player.CoreBase;
                    break;
            }
            Issue(command, best, bestScore, fleet);
        }

        // The first item the fleet is short of at the core base that the base can give: ammo, fuel, microchips for storm
        // telemetry while researching, then the claim cost when the preset besieges. Shortage reads 500 for any gap,
        // 1000 for an empty hold.
        private bool TryPickLoad(
            EntityId core, AiPreset preset, bool telemetry, in FleetUnit unit, ref Stockpile hold, out ItemId item,
            out int quantity, out int shortage)
        {
            item = ItemId.None;
            quantity = 0;
            shortage = 0;
            if (!_stockpiles.Has(core))
                return false;
            ref var stock = ref _stockpiles.Get(core);
            if (TryLoad(ref stock, ref hold, ItemId.ItemAmmo, (long)unit.LineShips * _balance.LoadAmmoPerLineShip,
                    ref item, ref quantity, ref shortage) ||
                TryLoad(ref stock, ref hold, ItemId.ItemFuel, _balance.LoadFuel, ref item, ref quantity, ref shortage) ||
                (telemetry && TryLoad(ref stock, ref hold, ItemId.ItemMicrochip, _researchBalance.TelemetryMinMicrochips,
                    ref item, ref quantity, ref shortage)))
                return true;
            if (preset.SiegeConquestWeight == 0)
                return false;
            for (int i = 0; i < _claimBalance.ClaimCostCount; i++)
            {
                var cost = _claimBalance.ClaimCost(i);
                if (TryLoad(ref stock, ref hold, cost.Item, cost.Quantity, ref item, ref quantity, ref shortage))
                    return true;
            }
            return false;
        }

        private static bool TryLoad(
            ref Stockpile stock, ref Stockpile hold, ItemId id, long target, ref ItemId item, ref int quantity,
            ref int shortage)
        {
            long gap = target - hold.Get(id);
            if (gap <= 0 || stock.Get(id) <= 0)
                return false;
            item = id;
            quantity = (int)Math.Min(target, int.MaxValue);
            shortage = Severity(gap, target);
            return true;
        }

        // Best site this fleet outguns and has the fuel to reach and come back from: a raid hunts rival fleets at a rival
        // site; a siege takes a neutral or rival site. Never a core base or THE SYSTEM's site.
        private bool FindTarget(in PlayerHq player, in FleetUnit unit, int fuel, bool raid, out EntityId target, out int edge)
        {
            long power = FleetPower(in unit);
            target = default;
            edge = 0;
            int bestLanes = int.MaxValue;
            for (int i = 0; i < _sites.Count; i++)
            {
                ref var site = ref _sites.At(i);
                var entity = _sites.EntityAt(i);
                if (site.OwnerPlayerId < 0 || site.OwnerPlayerId == player.PlayerId || (raid && site.OwnerPlayerId == 0) ||
                    IsCoreBase(entity))
                    continue;
                long hostile = Power(entity, player.PlayerId, mine: false);
                if (hostile >= power || (raid && hostile == 0))
                    continue;
                int advantage = Severity(power - hostile, power);
                if (advantage < edge || !_routes.TryPlan(player.CoreBase, entity, unit.TechTier, ref _route) ||
                    2L * RouteFuel(in unit) > fuel)
                    continue;
                if (advantage == edge && _route.Count >= bestLanes)
                    continue;
                target = entity;
                edge = advantage;
                bestLanes = _route.Count;
            }
            return !target.IsNull;
        }

        private long RouteFuel(in FleetUnit unit)
        {
            long fuel = 0;
            for (int i = 0; i < _route.Count; i++)
                fuel += _routes.FuelFor(unit, _lanes.Get(_route.LaneAt(i)));
            return fuel;
        }

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

            int expand = Math.Max(preset.StealthRaidWeight, preset.SiegeConquestWeight);
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

        private bool CarriesClaimCost(ref Stockpile hold)
        {
            for (int i = 0; i < _claimBalance.ClaimCostCount; i++)
                if (!hold.Has(_claimBalance.ClaimCost(i).Item, _claimBalance.ClaimCost(i).Quantity))
                    return false;
            return true;
        }

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
            if (deposit.RigCount == 0 || deposit.RetractUntilTick > 0 || !_sites.Has(deposit.Site) ||
                _sites.Get(deposit.Site).OwnerPlayerId != playerId)
                return;

            int danger = 0;
            if (_weather.Has(deposit.Site))
            {
                ref var weather = ref _weather.Get(deposit.Site);
                int storm = Math.Max(weather.GeomagneticStormLevel, weather.SolarRadiationLevel);
                if (storm >= preset.EvacuateStormLevel)
                    danger = Severity(storm - preset.EvacuateStormLevel + 1, 6 - preset.EvacuateStormLevel);
            }
            long hostile = Power(deposit.Site, playerId, mine: false);
            if (hostile > 0)
            {
                long ratio = Power(deposit.Site, playerId, mine: true) * Full / hostile;
                if (ratio < preset.RetreatForceRatioMilli)
                    danger = Math.Max(danger, Severity(preset.RetreatForceRatioMilli - ratio, preset.RetreatForceRatioMilli));
            }
            int score = Score(preset.EvacuateSafetyWeight, danger);
            if (score > 0)
                Issue(new DelayedPlayerCommand
                {
                    PlayerId = playerId, CommandType = CommandType.RetractDrills, Target = _deposits.EntityAt(denseIndex)
                }, AiAction.RetractDrills, score);
        }

        private void ThinkRadar(in PlayerHq player, AiPreset preset)
        {
            if (!_stockpiles.Has(player.CoreBase))
                return;
            int batteries = _stockpiles.Get(player.CoreBase).Get(ItemId.ItemBattery);
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
