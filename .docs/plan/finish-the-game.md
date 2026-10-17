# Finish the game (goal: all features)

R1–R5 done (shipyard, research/tech gates, site claim/rigs/refineries, jump gates + galaxy tab, cargo transfer). Play Mode checks moved to R8.

- [x] R6 AI raids, sieges, research, gate opening (18c5871)
- [x] R6b-1 Economy: core base gets a water/nitrogen/methane life-support trio; refineries run up to N batches per cycle. Done when: a soak keeps core fuel/coolant from draining to 0 in 6 h.
- [x] R6b-2 Convoy routes (GDD §1): SetConvoyRoute keeps an outpost stocked with fuel/coolant and hauls its goods home. Done when: tests show a round trip delivering raws.
- [x] R6b-3 AI development: builds rigs at claimed outposts, a convoy fleet + route, refineries when inputs exist. Done when: a 3-day soak shows rivals reach Tier 2+.
- [x] R7 SYSTEM Citadel/Directorate victory (SystemDefenseEvaluator, VictoryEvaluator); HUD shows it.
- [x] R8 View panels for new commands (research, shipyard, build, claim, gate, cargo, convoy): OperationsPanel.
- [x] R8b Play Mode capture of the new panels and Citadel tag (needs the Unity Editor open).
- [x] R9 MessagePack save/network shapes (risky).
- [x] R10 Phone layout (CompactLayout; Play Mode capture folds into R8b).

## L: System loans (user decision 2026-10-17: start at 0 credits, borrow on demand, interest-free first month, unpaid debt -> goods seized)
Numbers PROVISIONAL (GDD has none): credit limit 10,000 cr; free until tick 30 d; then 1%/day simple interest on the debt; debt due 30 d after it starts charging (or on draw if later); overdue -> SYSTEM seizes core goods at WTO reference price until paid. Loaned cents are minted, repaid principal burned, interest goes to the WTO fee pool (comes back as dividends).
- [x] L1 Core: `Loan` component on the HQ (Principal, AccruedInterest, DueTick), `LoanBalance` config, `LoanSystem` (interest + seizure, fixed SimClock slot), Borrow/Repay commands (append CommandType 30/31), StartingCents -> 0. Tests: borrow, repay, free month, interest, seizure, limit.
- [x] L2 Save: Loan store appended, SchemaVersion 2, migration test (v1 save loads with no debt).
- [x] L3 AI: borrows when cash is short of a planned cost, repays surplus above a reserve, never past the limit. Soak: AIs keep cash > 0 through day 30.
- [x] L4 View: Operations panel shows debt / due date, Borrow / Repay buttons.
- [x] L5 AI tungsten: find why site 250 (only free tungsten) is never claimed in 30 d despite 506 claim attempts; fix. Done when: a soak shows an ammo refinery and a Citadel assault order.

## F: Last GDD gaps (§1.1, §2.5)
- [x] F1 Auto-SOP (GDD §2.5): the player hands their empire to the Utility AI with a chosen preset and takes it back; Operations panel toggle through GameSession. Done when: Play Mode toggle assigns/releases the AiController.
- [x] F2 SplitFleet command (36): detach ships from a docked warship fleet into a new fleet at the same site; Split button on the fleet card. Done when: tests pass, Play Mode shows the button.
- [x] F3 AI multi-lane flanking (GDD §2.5, delta-theta >= 90 deg): a fleet big enough splits and sends the halves down two independent lanes to a siege target. Done when: tests pass and a soak shows flanked assaults.
