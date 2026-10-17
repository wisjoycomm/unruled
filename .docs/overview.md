# Unruled — how the project works now

Unity 6000.5.7f1. Design: `.docs/unruled_master_gdd.md`; numbers: `.docs/unruled_master_data_sheets.md`;
rules: `.claude/rules/architecture.md`; decisions and exceptions: `.docs/plan/roadmap.md`.

State after Sprint 4: the engine-free simulation core (economy, board, weather, latency, radar, fleets, salvo
combat, capital relocation, credits, WTO market and escrow contracts, STA tolls, leaderboard audit, THE SYSTEM's
bulletins and Call-to-Arms, Utility AI) and its tests.
A playable local build: `Assets/Scenes/Main.unity` runs a generated game in-process (1 tick = 1 s) with the
main-screen HUD and the board renderer. No networking, research or galaxy map yet.

## Assemblies

| Assembly | Folder | Notes |
|---|---|---|
| `Unruled.Core` | `Assets/Scripts/Core/` | `noEngineReferences`, `allowUnsafeCode` (fixed buffers). All rules and state. |
| `Unruled.View` | `Assets/Scripts/View/` | References Core only. `UI/Theme/`: `Unruled.uss` (palette variables, type and component classes), `UnruledTheme.tss`, `UnruledPanelSettings`, OFL fonts + FontAssets, `DashedFrame`, `ItemIcon`, `UnruledPalette`, `ThemePreview.uxml`. `UI/`: `MainScreen.uxml/.uss`, `MainScreenView` (binds and refreshes once per tick) and its controllers `TopTelemetryHud`, `LeftInventoryDock`, `ContractAndMarketUI`, `FleetCard`, `InFlightStrip`, `OperationsPanel` (side tabs MARKET/BASE/SITE: research, audit, shipyard, jump gate; claim, drills, refineries, cargo and convoy routes for the selected site and fleet), `CompactLayout` (below 1100 px: board full width, bottom bar swaps in the dock or the side column), `UiFormat`, `HudQueries`. `MapBoardRenderer` + `BoardMesh` draw one star system (with jump-gate rings) or, on the GALAXY tab, every system with gate links, as a vertex-coloured mesh. `GameSession` hosts the game: `NewGameSetup`, binds the views, advances the clock and runs AI. |
| `Unruled.Editor` | `Assets/Editor/` | Editor-only. `ThemeAssetBuilder` (menu Unruled/Build UI Theme Assets) creates missing FontAssets and the PanelSettings; `BoardAssetBuilder` the board material; `MainSceneBuilder` (menu Unruled/Build Main Scene) rebuilds `Main.unity` and puts it first in the build list. |
| `Unruled.Tests.Core` | `Assets/Tests/Core/` | EditMode NUnit tests, one folder per Core folder. |

## Composition

`SimulationWorld(GameBalanceConfig, worldSeed)` is the composition root. It creates the `World`, every component
store, the `StarlaneGraph`, every system and the `CommandRouter`, and registers the systems on `SimClock` in the
fixed order below. `GenerateSystem(systemSeed, clusterId)` creates a star system's sites, lanes and weather
components, then rebuilds the graph. `CreatePlayer(playerId, coreBase)` creates the HQ with its `PlayerHq`,
`Wallet` (starting credits, 0 by default), an empty `Loan` and unrated `PlayerStanding`. `CreateFleet(unit, site)` delegates to `FleetFactory`,
which adds every fleet component and an empty cargo hold. Orders enter through `CommandIssuer.Issue`, which stamps
the signal latency and queues them; when due, `SimClock` sends them through `CommandRouter` to the owning system.
`UtilityAiEngine` (`Ai`) is not on the clock: the host calls `Ai.Think(tick)` (every 10 ticks, Server 2), and it
issues orders only through `CommandIssuer`. Per player it first borrows up to its cash reserve and repays debt above
twice the reserve, or all it can once the debt falls due. Per fleet it vents, retreats, resupplies, loads up at the core base
(fill-to transfers), then stealth-raids or besieges a site it outguns with round-trip fuel, claims it and rigs its
deposits; convoy fleets get routes to unserved outposts. Per deposit it pauses drills in storms or at the raw stock
cap, retracts them when outgunned and rebuilds bare ones; per player it joins the audit, builds convoys and refineries,
scans, stress-tests, opens its jump gate and trades on the WTO, keeping back the next stress test's cost; from the
Citadel assault tier its shipyard refits and builds one battle fleet that only waits at home (mothballed once full) or
assaults the Citadel with ammo aboard (every trip takes the toll-free route when the wallet can't cover the tolls), while lower-tier fleets settle and raid. An order still in signal
latency is not sent again (`CommandIssuer.IsInFlight`). `NoaaForecast` (`Forecast`) is a read-only query for the views.
`NewGameSetup.Create(sim, playerId)` lays out a new game from `NewGameBalance`: a home system per participant,
stockpiles, ESI-window deposits, the core base's rigs, refineries and stock, a starting fleet and AI rivals.

## Tick order

1. `NoaaWeatherEngine`: expires and rolls storm fronts, writes `SiteWeather` and `LaneWeather`.
2. `MiningSystem`: ore into site `Stockpile`s.
3. `PlanetLifecycleSystem`: depletion states and ESI; also nuclear strikes and the `Terraform` command.
4. `RefinerySystem`: recipes and brownouts (reads `SiteWeather`); upkeep paid from the owner's wallet.
5. `WtoMarketEngine`: limit orders, matching, VWAP; `PlaceBuyOrder`, `PlaceSellOrder`, `CancelOrder`. Matches go to
   `WtoContractSystem` through `ITradeSettlement`.
6. `WhoQuarantineSystem`: export lock when water or oxygen runs out.
7. `FleetUpkeepSystem`: hourly per-ship upkeep from the cargo hold; `Mothball` / `Activate` and warm-up.
8. `FleetMovementSystem`: routed travel with fuel and STA tolls paid at each departure (`Move` ParamC 1 routes around
   toll lanes); `Move`, `Hold`, `Reverse`.
9. `ConvoyRouteSystem`: standing supply runs between a hub and an outpost (fuel, coolant, oxygen and rations out,
   goods home; the fleet keeps its own trip fuel and upkeep, the hub keeps a supply reserve); `SetConvoyRoute`.
10. `SalvoCombatEngine`: a salvo every 5 ticks between hostile fleets at one site or lane; loot and piracy
    reports; `SetRof`, `SetFocus`, `VentCoolant`.
11. `CapitalRelocationSystem`: teardown → capital convoy → bootstrap; `RelocateBase`.
12. `FocusedRadarSystem`: scan timelines and the Tier-5 IAEA reveal; `ScanBelt`, `CancelScan`, `EcmJam`.
13. `ResearchSystem`: retooling ends, storm telemetry; `StressTest`.
14. `ShipyardSystem`: delivers finished `ShipBuildOrder`s; `BuildShips`, `Retrofit`.
15. `WtoContractSystem`: settles escrow contracts on delivery, convoy loss or deadline.
16. `LeaderboardAuditSystem`: the daily Top-10K audit and pool payouts; `AuditOptIn`.
17. `SystemAuthorityManager`: bulletins (IAEA strikes, Call-to-Arms, NOAA prototype blasts) and WTO defence fleets.
18. `SystemDefenseEvaluator`: the System Citadel's garrison, shield bombardment, regeneration and fall.
19. `VictoryEvaluator`: seats The Directorate when the Citadel falls.

`MiningSystem` also handles `RetractDrills`, `PauseDrills` and `BuildRig`, `RefinerySystem` handles `BuildRefinery`
and `SetRefineryCap`, and
`PlanetLifecycleSystem` handles `Terraform`. `SiteControlSystem` (`ClaimSite`), `JumpGateSystem` (`OpenJumpGate`) and
`CargoTransferSystem` (`TransferCargo`) are not on the clock: they only handle commands.

In each tick, due commands run before the systems. Shared components (`Stockpile`) change only through their own
methods, in this order (rule 4).

## Core folders

- **Simulation/**: `SimTick`, `SimClock`, `DelayedPlayerCommand` (target = entity index + generation),
  `CommandRouter`, `SimulationWorld`. `SplitMix64` is the only RNG; `FixedMath` provides Q16 sin/cos and an exact
  integer sqrt. `GameBalanceConfig` holds every balance object, and `DefaultGameBalance` is the one place sheet
  values are written. `NewGameSetup` + `NewGameBalance` lay out a new game.
- **Ecs/**: `EntityId`, `World` (create/destroy, owns the stores) and `ComponentStore<T>` (sorted sparse set,
  iterated in ascending entity order).
- **Board/**:
  - `SiteInfo` (position, ESI in milli-units, lifecycle, `IrradiatedUntilTick`).
  - `SystemSeedGenerator` (site layout) and `PlanetLifecycleSystem` + `LifecycleBalance`.
  - `StarlaneEdge` (3 tiers), `StarlaneBalance` (tab 04 rules), `StarlaneGenerator` (orbit-chain highways plus
    seeded bypasses and shortcuts) and `StarlaneGraph` (CSR adjacency).
  - `StormFront`, `SiteWeather`, `LaneWeather`, `NoaaWeatherEngine` + `NoaaBalance`.
- **Economy/**:
  - `ItemId`, `Stockpile`, `RecipeDatabase`, `MiningSystem` + `MineralDeposit` (drill retract: rigs freed 180
    ticks after `RetractDrills` and their build cost returned to the site; paused drills neither mine nor burn),
    `RefinerySystem` + `Refinery` (idles in `IdleFull` while its site holds its output stock cap).
  - `Wallet` (credits as `long` cents on player HQs and the three pools: WTO fees, STA tolls, WTO escrow) and
    `Ledger` (`Transfer` never overdraws or creates money; `Grant` only for world setup, `Lend` for loans).
  - `LoanSystem` + `Loan` (on the HQ): THE SYSTEM's credit line, the only new money in play. `Borrow` / `Repay`
    (ParamA credits) up to `LedgerBalance.CreditLimitCents`; daily interest after the interest-free start goes to the
    WTO fee pool on repayment; an overdue debt takes the wallet, then core-base goods at the WTO reference price.
  - `WtoMarketEngine` + `WtoOrder` / `ItemMarket` (VWAP ring) + `WtoBalance`; `IMarketRates` (bracket fees and
    escrow), `IMarketAccess` (Call-to-Arms bar), `ITradeSettlement`; `ContractCargo` and `StolenGoods` components.
- **Military/**:
  - `PlayerHq` (core base, capital convoy flag), `RelayStation`, `CapitalRelocation` (phase, convoy, cooldown,
    paralysis).
  - `ShipyardSystem` + `ShipyardBalance`: `ShipBuildOrder` queue at the core base and retrofits; it adds ships
    (`FleetUnit.Gain`) and raises fleet tiers.
  - `SiteControlSystem` + `SiteControlBalance`: the only writer of `SiteInfo.OwnerPlayerId` during play; a docked
    fleet unloads the claim cost to take a neutral site or an undefended rival site (never a core base).
  - `CargoTransferSystem`: loads or unloads a docked fleet's hold at an own site (or fills it up to a total);
    stolen tags travel with the stolen units (moved first, `CargoMover`); WTO contract convoys can't transfer.
  - `ConvoyRouteSystem` + `ConvoyBalance`: owns `ConvoyRoute`; a fleet with cargo convoys works a hub ↔ outpost run on
    its own, sending departures through `FleetMovementSystem.TryMoveTo`; a run the owner can't pay tolls for goes
    toll-free, and a convoy docked off its route heads back to the hub.
  - `JumpGateSystem`: owns `JumpGate` (Board) and the lanes it opens; at the jump-gate tech tier it charges the core
    base, adds the gate lane into the contested cluster and rebuilds `StarlaneGraph`. Dormant gates keep homes sealed.
  - Fleet components (`FleetComponents.cs`), one owner each: `FleetUnit` (ship counts by class; combat),
    `FleetMotion` (site or lane progress; movement), `FleetReadiness` (mothball, warm-up, unsupplied; upkeep),
    `FleetCombatState` (ROF, focus, temperature, damage carry; combat), `FleetSensors` (reveal, RWR; radar). A
    fleet's `Stockpile` is its cargo hold. `FleetFactory` creates them all.
  - `FleetBalance` (tab 03 per tier), `CombatBalance`, `RelocationBalance`.
  - `FleetUpkeepSystem`, `FleetMovementSystem`, `SalvoCombatEngine` (Psi − Omega with fortification, flank,
    tender/vault resupply, thermal lock and cook-off), `CapitalRelocationSystem` (hostile lock, escort, upfront
    costs, convoy loss → order paralysis, 48 h cooldown).
  - `SignalLatencySolver`: Dijkstra over lanes, (15 + hops × 35, or × 8 when relayed) × 2 during relocation; an
    R3+ storm blocks the order.
  - `CommandIssuer` (targets: site, lane, fleet or deposit; refuses every order while the player is paralysed).
  - `RoutePlanner` (shortest legal route by travel time, fuel per lane), `ITollGate`, `IOffenceLog`.
  - `FocusedRadarSystem` + `RadarScan` + `RadarBalance`.
- **SystemFactions/**:
  - `WhoQuarantineSystem` + `SiteQuarantine`.
  - `StaTollGate` + `StaBalance` (tolls into the STA pool, bracket discount).
  - `WtoContractSystem` + `WtoContract` (seller convoy, pro-rata settlement, deposit).
  - `PlayerStanding`, `LeaderboardBalance` (tab 05), `BracketRates`, `LeaderboardAuditSystem`.
  - `SystemAgency`, `SystemBulletin`, `SystemSanction`, `SystemAuthorityManager` + `SystemAuthorityBalance`,
    `NoaaForecast`.
  - `SystemDefenseEvaluator` + `CitadelBalance`: owns `SystemCitadel` (on THE SYSTEM's Citadel site, the contested
    cluster's outermost orbit) and `CitadelAssault` (on HQs); `VictoryEvaluator` owns `VictoryRecord` and
    `DirectorateSeat`. The HUD's citadel tag shows the shield and defence tier, then The Directorate.
- **Research/**: `TechProgress` (on the HQ: tech tier 1..5, stage, telemetry, failed tests, retool end), `ResearchSystem` (telemetry in storms, `StressTest` command with calibration pity, retooling) + `ResearchBalance`; blasts go to `IResearchIncidentLog` (THE SYSTEM's bulletins). `TechGates` (`ITechGates`, `sim.TechGates`) answers a player's tier and gates: laser comms (orders through R3+ blackouts) and core-fracture mining at Tier 4, terraforming at Tier 5.
- **AI/**: `UtilityAiEngine` + `AiController` (on an AI-run HQ) + `AiBalance` / `AiPreset` (tab 06).
- **Persistence/**: `SaveGame.Write(sim)` / `SaveGame.Load(balance, bytes)` save the whole world as MessagePack
  (`SchemaVersion`, seed, tick, entity allocator, queued commands, then every store in a fixed, append-only order).
  `ComponentFormatters` holds one explicit formatter per component: an array indexed by key, so missing trailing
  keys read as defaults and unknown ones are skipped. `MsgPackWriter` / `MsgPackReader` are a small in-house codec.
  Balance is not saved (it comes from config); the starlane graph is rebuilt on load. A new store or component
  field must be appended to `ComponentFormatters` / `SaveGame`, with `SchemaVersion` bumped. Schema 2 appended the
  `Loan` store; a schema-1 save loads with an empty loan on every player.
  `GameSession` continues `persistentDataPath/unruled.save` at start-up and autosaves every sim hour and on
  pause/quit.

## Tests

`Assets/Tests/Core/` covers the clock, ECS, maths, generators, graph, weather, lifecycle, mining, refinery,
quarantine, latency, radar, fleet movement/upkeep/routes, salvo combat, drill retract, relocation, ledger, WTO
market and contracts, STA tolls, leaderboard audit, Call-to-Arms, NOAA forecast, Utility AI and an end-to-end
`SimulationWorld` run (same seeds → same state) and save/load (a loaded game re-saves to the same bytes and plays on
in lockstep with the original).
`DataSheetConsistencyTests` parses the markdown data sheet (tabs 01–06) and fails if `DefaultGameBalance`
drifts from it. Run EditMode tests for `Unruled.Tests.Core`.

## Tooling

- KitWright MCP is embedded at `Packages/com.kitwright.unity.mcp` with one patched `#if` for Unity 6000.5
  (see roadmap Exceptions). Its MCP config files carry an access token and are gitignored.
- Agent rule files are generated from `.agents/templates/` + `project.config` via `python .agents/sync-rules.py`.
