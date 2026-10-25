# Plan (done 2026-10-25): server cost at MMO scale (player views)

Baseline (Sim Lab `profile --views 0`, 2026-10-25): views are 99% of server time. 50 empires: 389 us per player per
tick; 200 empires: 893 us per player per tick (179 ms/tick total, budget 1,000 ms). Every view scans and encodes the
whole world, so total cost grows with players x world. Simulation alone: < 0.5 ms/tick at 200 empires.
At this rate one process tops out near 300-400 players. Product target: 10,000+ players per world.

Target: views at <= 50 us per player per tick, flat as the world grows, spread over the cores: 10,000 connected
players' views in <= 250 ms/tick on 4 cores, so one world of 10,000 fits one 8-core machine with room for the sim.
Wire format and client stay as they are (no protocol bump).

- [x] P1. Load test: Sim Lab `loadtest --clients N` opens N receive-only WebSocket clients (one process, tokens issued
      in bulk), plus one full mirror client that checks its hash. Reports server ms/tick (views vs sim), CPU %, memory,
      bytes per client. Done: baseline at 100 and 300 clients recorded in the roadmap.
- [x] P2. Encode once per tick, not once per player: a shared per-tick cache (entity -> encoded bytes + hash) per
      channel; masked channels (rival fleets, orders, refineries, presences, HQs) keep two cached forms (own / other).
      Views only compare hashes and copy bytes. Done: view tests unchanged and green; profile shows the cut.
- [x] P3. Interest index: built once per tick, entities grouped by system (sites, lanes, deposits, buildings, docked
      fleets, storms) and by owner (fleets, wallets, orders...). A view walks only its charted systems, its own
      entities and public stores, never the whole world. Done: per-player cost flat from 200 to 2,000 empires
      (+-25%); a test proves a view built from the index equals one built by full scan.
- [ ] P4 (skipped: target met without it; kept for later). Only what changed: systems mark entities they write as dirty this tick; a view re-checks only dirty
      entities it can see, plus entities entering or leaving its sight. Cost follows activity, not world size.
      Done: an idle player costs < 10 us per tick; equivalence test still green.
- [x] P5. Views in parallel: after the tick the world is read-only, so player views are built on worker threads
      (one writer and scratch buffer per worker). Done: same bytes as single-threaded; near-linear speed-up to 4 cores.
- [x] P6. Send rate: deltas batched per real 1 s whatever the server speed, and public stores (market, chart
      listings, bulletins, standings) diffed once and shared by everyone. Done: bytes/s per client at x60 no higher
      than at x1.
- [x] P7. Prove it: `profile --views` at 10,000 empires and `loadtest` at 1,000+ real socket clients plus 100 bots
      for 30 min; ms/tick, cores, memory, bytes/s logged; cost per 1,000 players in the roadmap. Done: within target,
      mirror hash matched throughout. If 10,000 doesn't fit one machine, the roadmap records regions per process.

Each step: Core tests, View compile check, Unity recompile when Core changes, change log, commit.
Risks: P2-P4 touch what every player is sent (privacy). The existing "rival never sees..." tests must stay green, and
P3 adds the full-scan equivalence test. P5 is the first threading in the server: views only read; the world thread
waits for them before the next tick. Opus.

## Result

| Step | Views per player per tick |
|---|---|
| Before (200 empires) | 893 us |
| P2 shared encodings (200) | 738 us |
| P3 interest index (1,000 / 2,000) | 50 / 53 us |
| P5 worker threads, 8 (2,000 / 10,000) | 20 / 19 us |
| 1,000 real socket clients (server, 9 view threads) | 11 us, 11 ms/tick |

P6: one view per client per real second (145 B/s at x1, 480 B/s at x60). P4 (dirty entities) skipped: not needed for
the target. A product rule came out of P3: rivals' HQ, standing and shield only go to players who have met them
(hold a chart of their home system).
