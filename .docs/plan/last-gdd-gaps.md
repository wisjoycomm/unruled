# Plan: last GDD gaps (nuke weapon, relay stations, AI lag, feature checklist)

- [x] 1. AI lag: convoys switch to starved outposts; IsServed counts in-flight routes; convoys haul goods the hub is short of; in-debt raw buys lift rival listings; settle ranks steel's ore first. Done when: tests pass, 5-seed soak has no rival stuck at T1.
- [x] 2. NuclearStrike command (38): a Tier 5 fleet docked at a site not its own (not the Citadel) spends a nuclear core from its hold → PlanetLifecycleSystem.ApplyNuclearStrike (GDD §2.1 B 3B). Done when: EditMode tests for strike/refusals pass.
- [x] 3. BuildRelay command (39) + relay upkeep: own site pays build cost; each relay burns batteries per hour from its site, lost when unpaid or the site changes hands (data sheet tab 04). Done when: tests pass, latency test shows 8-tick hops via the built relay.
- [x] 4. View: NUKE button on the fleet card, BUILD RELAY in operations, in-flight names. Done when: compile clean, buttons visible in Play Mode.
- [x] 5. Player 1 Auto-SOP soak: p1 handed to the AI by the SetStandingOrders command reaches T4+ in 30 days.
- [x] 6. GDD feature-coverage checklist `.docs/plan/feature-coverage.md`, every row marked.
