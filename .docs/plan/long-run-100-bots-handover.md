# Handover & Plan: Research Pacing, Uninhabited Star Systems & 100+ Bot Simulation

## 1. Context & User Requirements From Previous Session
1. **Research & Tech Pacing (`D1` — Completed & Verified):**
   - **Target timeline:** Tier 2 around Day 2, Tier 3 around Day 7+, Tier 4 around 1 Month (Day 30+), and **Tier 5 extremely hard / long-term** (requiring preparation, all 4 branches at Tier 4 first, and luck/planning so only ~100–1,000 players in a large universe achieve it).
   - **100% Upgrade Success, RNG in Farming & Crafting:** Once a player has gathered the required storm telemetry and materials for `StressTest`, the upgrade succeeds **100%** (`StressBaseChancePermille = 1000`).
   - **Tech-Scaled Yield Odds (Capped at 99% / `990` permille):**
     - **Storm Telemetry Sampling (`ResearchSystem` / `ResearchBalance`):** Base `20% × stormLevel` per tick (`200` permille/level) + **`+5%` (`+50` permille) per `Signals` tier above 1**, capped at **99% (`990` permille)**. Per-tier telemetry targets: `20,000 / 140,000 / 650,000 / 1,500,000` milli (deducted per tier on retool completion; retool times `2 / 8 / 24 / 72` hours).
     - **Rare-Ore Mining (`MiningSystem` / `RecipeDatabase`):** Base yield chance per rig cycle: `RawTitanium = 60%`, `RawTungsten = 25%`, `RawUranium = 10%`, plus **`+10% / +15% / +10%` per `Industry` tier above 1**, capped at **99% (`990` permille)**.
     - **High-Tier Refining (`RefinerySystem` / `RecipeDatabase`):** Base yield chance per recipe batch: `ItemAlloy = 70%`, `ItemMicrochip = 70%`, `ItemAmmo = 40%`, `ItemNuclearCore = 15%`, plus **`+10% / +10% / +15% / +10%` per `Industry` tier above 1**, capped at **99% (`990` permille)**.

2. **Uninhabited Star Systems ("những hệ hành tinh không có sự sống" — Completed & Verified):**
   - Previously, all participants jumped into a single 8-planet Contested Cluster, causing bots to crowd and steal each other's undefended outposts (`P3` took 26 sites in a 9-bot run, leaving 7/9 bots with `NO-EXPANSION`).
   - **Universe Expansion:** `NewGameBalance.UninhabitedClustersPerParticipant = 1` and `NewGameSetup.SetUpUninhabitedClusters` now generate `participants × UninhabitedClustersPerParticipant` **lifeless neutral star systems** (`Clusters = participants + 1 ..`), each with full raw ore deposits (`home: false`, including Titanium, Tungsten, and Uranium) and no native Core Base.
   - **Starlane Web:** Each participant `p`'s contested gate endpoint (`gate.FarSite`) connects via a 400-unit `StaHighway` to their uninhabited cluster's inner orbit, and adjacent uninhabited clusters connect in an outer ring via 400-unit `OuterRimBypass` lanes.
   - **AI Peaceful Expansion First (`UtilityAiEngine.FindTarget` & `TryLoad`):**
     - `FindTarget` now requires `ores > 0` for all non-raid settling (`if (!raid && ores == 0) continue;`), skips neutral sites inside other players' home clusters, and prefers unclaimed neutral sites (`OwnerPlayerId == 0`) over rival outposts (`OwnerPlayerId > 0`) when both offer the same new ore score.
     - `TryLoad` keeps a 50-unit life-support floor (`Math.Min(_balance.CoreSupplyReserve, 50)`) for `ItemOxygen` and `ItemRations` at the Core Base so large fleets loading upkeep never drain Core Base O2/Rations to `0`.

3. **100+ Bot Scaling (`--bots N` — Completed & Verified):**
   - `SimLab` (`tools/simlab/Batch.cs`, `LabGame.cs`) supports `--bots N` (`rivalCount = bots - 1`).
   - `RoutePlanner.TryPlan` now uses a stamp counter (`_stamp++`) + binary min-heap ($O(E \log V)$) instead of an $O(V^2)$ linear scan and per-call array clear, preserving identical tie-breaking (`costA < costB || (costA == costB && siteA < siteB)`).
   - `UtilityAiEngine.IndexWorld` builds per-site deposit and docked-fleet linked lists and CoreBase/HomeCluster stamp tables once per think tick, eliminating the $O(P \times S \times D)$ nested scans.

---

## 2. Verification Summary Before Handover
- **Unit Tests:** `dotnet test .temp/coretest/CoreTests.csproj` — **355/355 passed** (0 failed, 5s).
- **20-Bot 10-Day Simulation (`--bots 20 --days 10 --seed 1234`):**
  - Completed in **260s**.
  - **20/20 bots opened their Jump Gates (`open`) and expanded to 3–6 sites (`0` `NO-EXPANSION`)**.
  - **20/20 bots maintained `minO2 >= 356` and `minRat >= 228`** (no O2/Rations starvation).
  - **19/20 bots `ok`** (only 1 Raider bot flagged `STARVING` on fuel after piracy triggered WTO defence).

---

## 3. Plan Checklist & Next Steps on the New PC

### Completed Steps
- [x] **A1–A2:** SimLab long-run health metrics + headless smoke tests.
- [x] **B1–B4:** WTO contract convoy fuel check & unload-on-fail, starved hub convoy fuel reserve override, core O2/rations 50-unit reserve floor.
- [x] **D1:** 100% `StressTest` upgrade + tech-scaled RNG yield odds (capped at 99%) for storm telemetry, rare-ore mining, and high-tier refining; Tier 5 kept ultra-hard.
- [x] **D2:** Uninhabited star systems (`UninhabitedClustersPerParticipant = 1`), AI neutral-first expansion (`ores > 0`), and `RoutePlanner` + `UtilityAiEngine` indexing for 100+ bots.
- [x] **E1–E2:** All-branch Tier 4 prerequisite before Tier 5 tests; post-Citadel battle fleet wake/mothball cycle.
- [x] **F1. Run 100+ & 500-Bot Simulations in SimLab (Ryzen 9 7945HX, 16C/32T):**
  - Eliminated remaining $O(N^2)$ / $O(P \times S)$ / $O(S \times F)$ scaling bottlenecks across `StarlaneGraph` ($O(1)$ connected-component IDs), `RoutePlanner` (`4096`-slot incremental Dijkstra cache, `SameComponent` & `maxLanes` early-outs), `SignalLatencySolver` ($O(1)$ HQ cache + target early-out), `CommandIssuer` (per-player pending order buckets), `Ledger` / `TechGates` / `ResearchSystem` ($O(1)$ player caches), `NoaaWeatherEngine` (per-cluster site index + dirty-weather lane skip), `WtoMarketEngine` (dirty-item bitmask), `NewGameSetup` / `StarlaneGenerator` (single `Graph.Rebuild`), and `UtilityAiEngine` (per-player fleet/deposit/site/WTO linked lists, packed `_siteOreCounts`, `_armedSites`, `_clusterFirstNeutralSite` / `_compFirstOpenSite` siege candidate lists, $O(1)$ rival ask lookup, and `FuelToLoad` siege target reuse).
  - Verified 100% bit-for-bit deterministic equivalence on `5 bots x 5 days` (`orders = 57,649`), `100 bots x 1 day` (`orders = 296,181`, runtime `25.4s -> 7.9s`), and `100 bots x 5 days` (`orders = 1,385,390`, `58.4s`).
  - **100 bots x 90 days (`seed 1234`):** completed in **`773.6s` (`12.9 min`)** (`25,403,871` orders).
  - **500 bots x 10 days (`seed 1234`, `1,001` star clusters, `4,180` sites):** completed in **`1,663.2s` (`27.7 min`)** (`11,349,562` orders).
- [x] **F2. Analyze 100-Bot (90-Day) & 500-Bot (10-Day) Results:**
  - **Survival & Expansion (`D2`):**
    - **100 bots x 90 days:** `100/100` bots alive (`0` `NO-FLEET`, `0` `BROKE`, `0` `STALLED-T1`, `0` `NO-EXPANSION`), `91/100` Jump Gates `open`, `468` player-owned sites across the 100 home + 100 uninhabited star systems (`2–7` sites/bot).
    - **500 bots x 10 days:** `438/500` (`87.6%`) `ok`, `0/500` `BROKE`, `0/500` `NO-FLEET`, `0/500` `NO-EXPANSION`, `27/500` (`5.4%`) spent `> 1 day` at the loan limit.
  - **Tech Tier Distribution at Day 90 (`D1` / `E1`):**
    - `0/100` reached Tier 5 by Day 90 (confirming Tier 5 remains an ultra-hard long-term goal).
    - `3/100` bots (`P30 Warlord_Conqueror`, `P78 Industrialist_Trader`, `P98 Industrialist_Trader`) reached **`4/4/4/4` (Tier 4 across all 4 branches)**.
    - `55/100` bots reached Tier 4 in at least 1 branch (and built 80–86 ship Tier-4 battle lines); `62/100` reached Tier 3+.
  - **Loan / Credit Flow (`C1`):**
    - At Day 90, `0/100` bots are `BROKE` (all hold `1,002–4,315` cash), `27/100` have `0` debt, only `1/100` (`P92` at `9,963`) is near the `10,000` credit limit, and mean debt across all 100 bots is ~`4,160` credits (`57/100` briefly touched the limit for `> 24h` during mid-game expansion before paying debt down).
  - **Late-Game Supply Observation (for optional `C2`):**
    - `43/100` bots at Day 90 were flagged `STARVING` on `ItemFuel` (`minFuel = 0`) after reaching `MIL 4` (`80–86` ships) and depleting their Core Base surface/deep `RawWater` reserve before securing an active water outpost convoy or `IND 4` Core Fracture mining (`17/20` `Sentinel_Defender` bots at `80` ships stayed `ok` with `dryHrs = 0`).

### Optional Follow-Ups (Requires User OK)
- [ ] **C2:** Optional empire-size / battle-line upkeep scaling or earlier water-outpost prioritization when Core Base water drops low.
- [ ] **D3:** Post-Citadel endgame loop.
