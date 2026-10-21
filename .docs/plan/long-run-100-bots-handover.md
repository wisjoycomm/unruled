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

### Remaining Steps (To Run on Stronger PC)
- [ ] **F1. Run 100+ Bot Simulation in SimLab:**
  ```powershell
  dotnet test .temp/coretest/CoreTests.csproj
  dotnet run --project tools/simlab -c Release -- batch --seeds 1 --days 90 --bots 100 --parallel 1 --seed 1234 --csv .temp/sim-90d-100bots.csv
  ```
- [ ] **F2. Analyze 100-Bot Results:**
  - Check completion time, memory usage, expansion across the 100 uninhabited star systems, WTO market trade volume, loan/credit flow (`C1`), and tech tier distribution at Day 30 / Day 60 / Day 90.
- [ ] **C1 / C2 / D3 (If needed after 100-bot data):**
  - `C1`: Tune AI loan repayment / spending if bots stay pinned at the loan limit in a 100-bot market.
  - `C2`: Optional empire-size upkeep scaling (requires user OK).
  - `D3`: Post-Citadel endgame loop (requires user OK).
