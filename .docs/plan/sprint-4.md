# Plan: Unruled — Sprint 4 (THE SYSTEM, WTO escrow market, Top-10K leaderboard, UtilityAiEngine)

Source: GDD §5 Sprint 4, §1.1 macro loop, §2.2 B (VWAP + escrow), §2.5 (IAUS), §4.3–4.4 (anti-exploit); data sheet tabs 05 and 06.
Values not in the sheet are marked PROVISIONAL in `DefaultGameBalance` and the roadmap. All new state is ECS (rule 12), money is integer.

- [x] 1. **Credits ledger** (Economy): `Wallet` component (credits as `long` cents) on the player HQ, owned by `LedgerSystem`; `TaxPool` entities for the WTO fee pool and the STA toll pool.
  Every credit move is a `Transfer(from, to, amount)` that can't go negative and never creates money (except world-setup grants). Refinery `AccruedUpkeep` is now paid from the owner's wallet.
  PROVISIONAL: starting credits. Done: conservation test (total credits constant across a seeded run), refusal on overdraft.
- [x] 2. **Multi-hop routes** (Military): `Move` gains a destination site; `FleetRoute` (lane list in its own variable-size store) is solved once at departure with the existing `StarlaneGraph` (Dijkstra by travel time, tier/storm rules respected); fuel paid per lane at each departure.
  Also apply the tab-03 combat fuel-burn multiplier left from Sprint 3. Done: route = shortest legal path, re-plan refused mid-lane, fuel per lane, combat fuel test.
- [x] 3. **STA toll highways** (SystemFactions): entering an STA highway charges a toll into the STA pool, minus the owner's bracket discount (tab 05); unpaid = the fleet stops at the lane head.
  PROVISIONAL: toll = 2 credits per ship per 100 length units. Done: toll/discount/refusal tests.
- [x] 4. **WTO order book + VWAP** (Economy, `WtoMarketEngine`): limit buy/sell orders per item, matched each tick by price then time then entity id; VWAP over the last 100 ticks; orders outside ±20% of VWAP rejected; fee by bracket (default 5%) into the WTO pool.
  Seeded reference price per item before the first trade (PROVISIONAL). Done: matching determinism, VWAP window, price band, fee tests.
- [x] 5. **WTO escrow contracts + convoys** (Economy): a matched trade becomes a contract: buyer pays 100% into escrow, seller locks EscrowRate(bracket) (default 30%), seller launches a `CargoConvoys` fleet carrying the goods along a route.
  Delivery → seller gets (1 − fee)·QP + deposit; convoy destroyed → buyer refunded + compensated from the deposit; cargo taken by the attacker is tagged `Stolen_WTO_Cargo` (per-item counter) and can't be sold on the WTO. PROVISIONAL: delivery deadline and missed-deadline rule.
  Done: delivery, loss, self-piracy (stolen cargo refused), deadline tests.
- [x] 6. **Top-10K leaderboard audit** (SystemFactions): daily audit (86,400 ticks) ranks players who opted in by audited net worth; brackets from tab 05 set escrow rate, WTO fee, STA toll discount and NOAA warning lead.
  Payouts (dividends + FUEL/COOLANT/STEEL/ALLOY grants) come only from the tax pools, never from nothing (§4.4). Unrated players keep tech secrecy; "40% if evading" is applied to players who leave the audit while in a bracket (PROVISIONAL reading).
  PROVISIONAL: net worth = credits + stock at VWAP. Done: tab-05 sheet check, bracket thresholds, zero-inflation test (pools only shrink by what they pay).
- [x] 7. **NOAA forecast + IAEA bulletins + Call-to-Arms** (SystemFactions): storm forecast is the seeded schedule revealed N ticks ahead by bracket; IAEA bulletin on nuclear strikes; `SystemAuthorityManager` with **Call-to-Arms** in two thresholds:
  PROVISIONAL: layer 1 = an agency's pool or rule is attacked (toll evasion, WTO convoy piracy, nuclear strike) → that agency raises the offender's prices/locks access; layer 2 = repeated offences past a second threshold → system defence fleets spawn against the offender. Done: forecast lead tests, threshold tests.
- [x] 8. **UtilityAiEngine** (`Unruled.Core.AI`): IAUS score = weight × (Π curves)^(1/m) in fixed-point, presets from tab 06, run every 10 ticks for offline players and PvE bots; actions: retract drills on storm, vent coolant at temperature, retreat on force ratio, radar scan above min battery, resupply.
  It only issues orders through `CommandIssuer` (same latency, rule 3) and is not on the sim clock, so the host can run it on Server 2. Done: tab-06 sheet check, one test per action, same seed → same orders.
- [x] 9. **Docs**: overview, roadmap (provisional values, Call-to-Arms reading), change_log; archive as `.docs/plan/sprint-4.md`. Done: overview matches the code.

Each step: recompile clean + EditMode tests green + architecture checklist → commit.
Out of scope (Sprint 5+ or not specified): System Citadel / The Directorate victory (GDD gives no rules yet), Research/TRL module and tech gates, networking and the real Server 2 process, views.
