# Plan: 4 research branches, ChaCha8 generator, final numbers

Branches (recorded in roadmap): each has its own tier 1..5, researched through the same
3 stages (telemetry → stress test → retool), one branch at a time.
- **Military**: ship tier for build/retrofit (today's `TechTier`), nuclear strike (T5).
- **Industry**: core fracture mining (T4), terraform (T5).
- **Logistics**: jump gate (T2), storm-shortcut lanes (T4).
- **Signals**: laser comms (T4).
Costs per branch tier = today's tier costs; the AI researches by preset (Warlord: Military first; Industrialist:
Industry; all take Logistics T2 for the gate).

- [x] 1. Drop PROVISIONAL from relay/strike numbers (DefaultGameBalance, roadmap, feature-coverage). Done when: docs agree.
- [x] 2. Core: `ResearchBranch` enum; `TechProgress` gets per-branch tiers + the active branch (MessagePack keys appended, SchemaVersion 4, migration: old tier → every branch); `StressTest` ParamA = branch; `TechGates` maps each gate to its branch; ShipyardSystem/NuclearStrike read Military. Done when: research/gate/save-migration tests pass.
- [x] 3. AI: picks the branch to research by preset and next needed gate. Done when: AI tests pass, 5-seed soak: every rival reaches Military T4+ and the Citadel still falls.
- [x] 4. View: BASE tab research shows 4 branch tiers and a branch picker for the stress test. Done when: compile clean, Play Mode readback.
- [x] 5. ChaCha8: pure C# `ChaCha8` PRNG (RFC 7539 core, 8 rounds), seeded from SplitMix64, used by `SystemSeedGenerator` + `StarlaneGenerator` (GDD §2.1 A); SchemaVersion bump (old saves keep their stored map). Done when: known-answer test, determinism test, generator tests pass, soak runs.
- [x] 6. Feature-coverage doc: branches, ChaCha8 and numbers marked final; Server 2 marked out of scope by decision. Done when: no D/P rows left except Server 2.
