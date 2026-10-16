# Plan: Unruled — Sprint 3 (fleets, Hughes salvo combat, mothball, drill retract, capital relocation)

Source: GDD §5 Sprint 3, §2.1 B (drill retract), §2.4 A + C, §3 (FleetUnit), §4.2; numbers from data sheet tabs 03 and 04.
Values not in the sheet are marked PROVISIONAL in `DefaultGameBalance` and the roadmap.

- [x] 1. **Fleet model** (Military): replace the `FleetPresence` placeholder with split components, each with one owner:
  `FleetUnit` (owner, tech tier, Line/EW/Supply/Cargo counts: combat owns losses), `FleetMotion` (site or lane + progress in milli, direction, holding: movement owns),
  `FleetSensors` (revealed/RWR: radar owns), `FleetCombatState` (hull milli-%, temp milli-°C, focus class, ROF stance: combat owns),
  `FleetReadiness` (mothball + warm-up: upkeep owns), plus a `Stockpile` on the fleet entity as its cargo hold. Port radar, latency (EW relay) and their tests.
  Tier 5 fleets are always revealed (IAEA, tab 03). Done: Sprint 2 tests still pass with the new components, plus an IAEA test.
- [x] 2. **FleetMovementSystem**: `Move` (target: a lane next to the fleet's site), `Hold` (stop mid-lane), `Reverse` (turn back to the origin site).
  Speed = base speed × tier speed %; fuel = length × tier fuel × ship count, paid from the cargo hold at departure (rejected if short).
  Shortcuts need tech tier ≥ 4 (lane weather is already capped by tier exposure, so storms don't block departure). Approach azimuth = direction of the arrival lane (integer vector).
  PROVISIONAL: base speed, fuel per unit length. Done: arrival tick exact, hold/reverse, fuel/tier/storm rejections, deterministic.
- [x] 3. **Mothball + upkeep** (`FleetUpkeepSystem`): hourly upkeep per ship by tier from the cargo hold (tab 03; fractions in milli), `Mothball` / `Activate` commands,
  80% off while mothballed, warm-up 60–180 ticks by tier; mothballed or warming fleets can't move or fire. Unpaid upkeep: PROVISIONAL rule (hull decay).
  Done: tab 03 sheet check, upkeep/discount/warm-up tests.
- [x] 4. **SalvoCombatEngine** (every 5 ticks): opposing fleets at the same site or on the same lane fight.
  Psi = LineShips × Alpha × ROF (needs ammo), Omega = (EwShips × Beta + fortification) × F_flank (needs batteries), damage = max(0, Psi − Omega) to the focus class.
  F_flank = max(0.20, 1 − 0.20 × extra lanes), lanes count as separate when their azimuths differ ≥ 90° (dot product ≤ 0, integer).
  Defender at own site: +35% PD and zero-distance resupply from the planet vault; supply tenders hot-resupply ammo/battery/coolant mid-battle.
  Thermal: T += dT_fire × ROF² − passive − coolant; > 90 °C locks guns and costs 5% hull per salvo until vented below 75 °C (`VentCoolant` + `SetRof` commands).
  Ammo/fuel burn × tier combat multiplier (tab 03). PROVISIONAL: Alpha, Beta, HP per ship, dT values, vent amount.
  Done: tests for the tab 04 combat rows (1v1 in transit = stalemate, 1v1 siege = defender wins, 3-lane flank breaks it), thermal lock/vent, no ammo/no battery, sheet check.
- [x] 5. **Drill retract** (Economy): `RetractDrills` on a deposit → rigs stop mining and stay unavailable for 180 ticks (rigs can't be ordered away earlier); owned by `MiningSystem`.
  Done: retract timeline test + sheet check of the 180-tick value.
- [x] 6. **CapitalRelocationSystem** (Military): `RelocateBase` (target: new site). Phase 1 teardown 900–1800 ticks (ALLOY + FUEL + BATTERY), old base −50% defence bonus;
  phase 2 capital convoy (a fleet entity) 1800–3600 ticks, latency ×2, convoy destroyed → orders paralysed 900 ticks; phase 3 bootstrap 1800 ticks (COOLANT + BATTERY), then the core base moves; cooldown 48 h.
  Locked while hostile fleets are at the core base or on its lanes (§4.2). PROVISIONAL: costs, how a duration is picked inside each range.
  Done: phase timeline, lock, paralysis, cooldown and latency-centre move tests + tab 04 sheet check.
- [x] 7. **Docs**: overview, roadmap (provisional values, component split), change_log; archive as `.docs/plan/sprint-3.md`. Done: overview matches the code.

Each step: recompile clean + EditMode tests green + architecture checklist → commit.
Out of scope (Sprint 4+): convoys/WTO escrow and piracy, STA tolls, research gates beyond tech-tier fields, AI auto-vent/auto-retract, multi-hop route planning (one lane per `Move`).
