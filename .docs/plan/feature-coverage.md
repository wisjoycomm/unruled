# GDD feature coverage (Master GDD v3.1)

Every feature named in GDD §1, §2, §4 and §5, with the system that implements it and its tests
(`Assets/Tests/Core/<Feature>/`). Checked 2026-10-18 against `main`.

- **[x]**: built and locked.
- **[x] D**: out of scope by decision (see `roadmap.md`).

## §1.1 Three pacing loops

| Feature | Status | Where |
|---|---|---|
| 1 tick = 1 s, integer `long` ticks | [x] | `SimClock`, `GameSession` (one tick per real second) |
| Micro: NOAA R/S/G 1–5 storms sweep clusters | [x] | `NoaaWeatherEngine`, `StormFront`, `SiteWeather` / `LaneWeather` |
| Micro: 60 s focused scan costing 2 batteries | [x] | `FocusedRadarSystem` (`ScanBelt`) |
| Micro: retract drills, 180-tick countdown before R3+ | [x] | `MiningSystem` (`RetractDrills`) |
| Micro: ROF stance 50/100/150% | [x] | `SalvoCombatEngine` (`SetRof`), fleet card |
| Micro: focus target Line/EW/Supply | [x] | `SetFocus` |
| Micro: vent coolant at 85 °C, cook-off at 90 °C | [x] | `VentCoolant`, thermal model in `SalvoCombatEngine` |
| Micro: hot-drop resupply mid-battle | [x] | tender/vault resupply in `SalvoCombatEngine` |
| Meso: 10 RAW → 10 ITEM Leontief chains | [x] | `RecipeDatabase`, `RefinerySystem` (`BuildRefinery`, `SetRefineryCap`) |
| Meso: cargo convoys feed outposts and fleets | [x] | `ConvoyRouteSystem` (`SetConvoyRoute`), `CargoTransferSystem` |
| Meso: water/oxygen out for 30 ticks → WHO quarantine 300 ticks | [x] | `WhoQuarantineSystem` |
| Meso: field telemetry, stress test, retrofit | [x] | `ResearchSystem`, `ShipyardSystem` (`Retrofit`) |
| Macro: surface → deep crust | [x] | `MiningSystem`, `PlanetLifecycleSystem` |
| Macro: claim new sites; 3-phase base relocation | [x] | `SiteControlSystem` (`ClaimSite`), `CapitalRelocationSystem` (`RelocateBase`) |
| Macro: Top-10K audit, escrow 30% → 15% | [x] | `LeaderboardAuditSystem` (`AuditOptIn`), `BracketRates` |
| Macro: bring down the System Citadel, seat The Directorate | [x] | `SystemDefenseEvaluator`, `VictoryEvaluator` |

## §1.2 Four-stage progression

| Feature | Status | Where |
|---|---|---|
| Home system generated from `ulong SystemSeed` | [x] | `SystemSeedGenerator`, `NewGameSetup` |
| Heliopause cloak + dormant jump gate seal the home | [x] | `JumpGate` (dormant while its lane is null) |
| Stage 1 chains: iron → steel, water + nitrogen → oxygen; Tier 1 ships | [x] | `RecipeDatabase`, `ShipyardSystem` (`BuildShips`) |
| Home lacks tungsten and uranium | [x] | `SystemSeedGenerator` home rules |
| Tier 2 opens the gate into the contested cluster | [x] | `JumpGateSystem` (`OpenJumpGate`), `TechGate.JumpGate` (`Logistics` T2) |
| Outer-rim bypass hides 90% of the signal | [x] | `StarlaneTierRules.AutoRadarHiddenPercent` |
| EW ships / relay stations cut hops from 35 s to 8 s | [x] | `SignalLatencySolver`; relays built by `RelayStationSystem` (`BuildRelay`) |
| Tier 4 laser comms through R-scale blackouts | [x] | `TechGate.LaserComms` (`Signals` T4) |
| Tier 4 storm-shortcut lanes (G4/S4) | [x] | `HazardStormShortcut` lanes, `MinTechTier: 4` |
| Multi-lane flanking campaigns | [x] | `SalvoCombatEngine` (F_flank), `SplitFleet`, AI flanking |
| Tier 5: uranium → nuclear core; Tier 5 flagship broadcast by the IAEA | [x] | `RecipeDatabase`, `iaeaGlobalReveal` (Tier 5 fleet balance), `FocusedRadarSystem` |
| Tier 5: nuclear strike cracks a planet open | [x] | `NuclearStrikeSystem` (`NuclearStrike`, `Military` T5) → `PlanetLifecycleSystem.ApplyNuclearStrike` |
| Tier 5: all-out assault on the System Citadel | [x] | `SystemDefenseEvaluator`, Citadel assault and bombardment |

## §2.1 Universe and depletion

| Feature | Status | Where |
|---|---|---|
| SplitMix64 + ChaCha8 generator | [x] | `SplitMix64` + `ChaCha8` (RFC 7539 8-round stream), integer trig (`FixedMath`) |
| Polar anchors, golden-angle spacing, ≥ 120 units apart | [x] | `SystemSeedGenerator` |
| ESI from the habitable zone; rich and harsh ore bands | [x] | `SystemSeedGenerator` |
| Three reserve pools per deposit | [x] | `MineralDeposit` |
| ΔQ = rigs × rate × depth × ESI × storm | [x] | `MiningSystem` (plus per-item `MiningChancePermille` yield roll scaling with `Industry` tier up to 99%) |
| 1 Pristine, 2 Deep Core Stripping (×0.5, 2× burn, −0.05 ESI) | [x] | `PlanetLifecycleSystem` |
| 3A Barren Husk (−0.20 ESI, node kept as depot/relay/fort) | [x] | `PlanetLifecycleSystem`; relays on any own site |
| 3B Irradiated: biomass wiped, ESI 0, S4 radiation for 24–72 h, core fracture unlocked (4× burn, Tier 4–5 rigs) | [x] | `ApplyNuclearStrike`, `TechGate.CoreFractureMining` (`Industry` T4) |
| 4 Terraformed Recovery (water + nitrogen + biomass + core → ESI 0.60) | [x] | `PlanetLifecycleSystem` (`Terraform`), `TechGate.Terraform` (`Industry` T5) |

## §2.2 Economy and WTO

| Feature | Status | Where |
|---|---|---|
| Refinery 10-tick cycle; idle starved at 20% upkeep | [x] | `RefinerySystem` (plus per-recipe `CraftChancePermille` batch yield roll scaling with `Industry` tier up to 99%) |
| G2–G5 brownout unless batteries are burnt | [x] | `RefinerySystem` (`BatteryPerBrownoutCycle`) |
| WHO-quarantined state | [x] | `WhoQuarantineSystem` |
| 100-tick VWAP | [x] | `WtoMarketEngine`, `ItemMarket` ring |
| Price band 0.80–1.20 × VWAP | [x] | `WtoMarketEngine` |
| Escrow: buyer pays 100%, seller locks 30% (15–28% in Top 10K) | [x] | `WtoContractSystem`, `BracketRates` |
| Convoys carry the goods; 2–5% fee into the WTO pool | [x] | `WtoContractSystem` |
| Piracy compensates the buyer from escrow; `Stolen_WTO_Cargo` can't be resold | [x] | `WtoContractSystem`, `StolenGoods` |

## §2.3 Research (NASA TRL)

| Feature | Status | Where |
|---|---|---|
| 5 tiers × 4 branches × 3 stages | [x] | `ResearchBranch` (`Military`, `Industry`, `Logistics`, `Signals`), `TechProgress`, `ResearchSystem` |
| TRL 1–3 field telemetry: microchip ships inside real storms | [x] | `ResearchSystem` (per-tier telemetry target, storm level × 20% capture roll + `Signals` tier bonus up to 99%) |
| TRL 4–6 stress test: 100% upgrade once telemetry and materials are met | [x] | `ResearchSystem` (`StressTest`) |
| Failed test pulses a NOAA/IAEA bulletin | [x] | prototype blast bulletin via `SystemAuthorityManager` |
| TRL 7–9 factory retooling and ship retrofit | [x] | `ResearchSystem` retool time, `ShipyardSystem` (`Retrofit`) |

## §2.4 Military

| Feature | Status | Where |
|---|---|---|
| Every order lands at `CurrentTick + SignalLatency` | [x] | `CommandIssuer` → `SimClock` |
| Dijkstra latency (15 + Σ w) × relocation mult; w = 8 relayed, 35 not | [x] | `SignalLatencySolver` |
| Relay stations | [x] | `RelayStationSystem`: build cost and hourly battery upkeep |
| 3-phase base relocation, ×2 latency in phase 2 | [x] | `CapitalRelocationSystem` |
| Radar: 2 batteries at tick 0, RWR at tick 15, ECM jam doubles scan to 120 | [x] | `FocusedRadarSystem` (`ScanBelt`, `EcmJam`, `CancelScan`), fleet `Reverse` |
| 5-tick salvo cycle; Ψ − Ω damage to the focus class | [x] | `SalvoCombatEngine` |
| Flanking: lanes ≥ 90° apart, F_flank = max(0.20, 1 − 0.20 L) | [x] | `SalvoCombatEngine` |
| Thermal runaway: ROF² heat, lock and 5% cook-off over 90 °C until under 75 °C | [x] | `SalvoCombatEngine` |

## §2.5 Utility AI

| Feature | Status | Where |
|---|---|---|
| IAUS score, think every 10 ticks | [x] | `UtilityAiEngine` |
| Shared by offline players (Auto-SOP) and PvE bots | [x] | `SetStandingOrders`, AI rivals in `NewGameSetup` |
| Runs on Server 2 headless worker | [x] D | off the sim clock; Server 2 out of scope by decision (runs in-process in `GameSession`) |
| Auto-retract drills on storm level or enemy force ratio | [x] | `AiAction.RetractDrills`, `AiAction.Retreat` |
| Auto-vent coolant at a preset threshold (70–88 °C) | [x] | `AiAction.VentCoolant` |
| Auto-split for ≥ 90° multi-lane flanking | [x] | AI flanking (`SplitFleet` + staging site) |

## §4 Edge cases and anti-exploit rules

| Rule | Status | Where |
|---|---|---|
| Radar strobe: 2 batteries up front, nothing before 60 continuous ticks | [x] | `FocusedRadarSystem` |
| Combat capital hopping: relocation locked with a hostile at or next to the core base | [x] | `CapitalRelocationSystem` hostile lock |
| Escrow self-piracy: payout from seller escrow; stolen tag | [x] | `WtoContractSystem`, `StolenGoods` |
| Zero-inflation rewards: dividends and subsidies only from WTO/STA pools | [x] | `LeaderboardAuditSystem` |

## §5 Five sprints

| Sprint | Status | Where |
|---|---|---|
| 1: `Unruled.Core` asmdef (no engine refs), ticks, seed generator, 20 items, recipes, NUnit | [x] | `Assets/Scripts/Core`, `Assets/Tests/Core` |
| 2: site graph (5 lifecycle states, 3 depths), 3 lane tiers, NOAA, latency, radar + RWR | [x] | `Board/`, `Military/` |
| 3: salvo combat, flanking, hot resupply, mothball −80%, 180-tick retract, relocation | [x] | `Military/`, `FleetUpkeepSystem` |
| 4: THE SYSTEM (STA, WTO, WHO, IAEA, NOAA), 2-layer Call-to-Arms, Top-10K audit, Utility AI | [x] | `SystemFactions/`, `Economy/`, `AI/` |
| 5: 1-layer board (LineRenderer/Mesh), UI Toolkit 4-zone deck, opaque, no popups | [x] | `MapBoardRenderer`/`BoardMesh`, `MainScreen.uxml`, `GameSession` |
