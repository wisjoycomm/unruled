# Plan: the Unity client plays on the server (approved 2026-10-25: "do it")

Follows the online foundation (O1–O5). Risky (network data shapes, client wiring): Opus, one step per commit, each
needing your OK first.

- [x] N1. Wire format (Core, `Unruled.Core.Net`): MessagePack frames with int keys, append-only, and their own
      `ProtocolVersion`. Client to server: Hello(version, token), Command(DelayedPlayerCommand), Ping.
      Server to client: Welcome(playerId, tick), Snapshot, Delta, Refused(reason), Bulletin.
      Done: round-trip tests and a version-mismatch test.
- [x] N2. Per-player view (Core): `PlayerView` builds what one player may see:
      - their own empire in full;
      - systems they hold a chart of: sites, owners, lanes, weather;
      - rival fleets only where the player has eyes (own site, fleet or radar);
      - market prices.
      Deltas are computed against the last view sent. Done: tests prove a rival's hidden fleet, stock and wallet never
      appear; size per player per tick is logged.
- [x] N3. Server transport (Sim Lab server): a WebSocket at /ws. A Hello logs the player in; commands go through
      `CommandIssuer` (same latency and refusals as now); every tick each online player gets its delta, plus a full
      snapshot on join. Done: a console bot client stays in sync for a game day; bytes per player per second measured.
- [x] N4. Login (temporary): a per-player token issued by the admin dashboard, checked in Hello. Steam login replaces it
      later. Done: a wrong token is refused.
- [x] N5. Unity client: a `GameLink` interface with `LocalLink` (today's in-process world: single-player and offline)
      and `ServerLink` (WebSocket, UniTask, cancellation). `GameSession` stops stepping the clock in online mode and
      applies deltas to a client-side `SimulationWorld` mirror that the views keep reading. Main menu: Play offline /
      Join server (address + token). Done: recompile clean, views unchanged.
- [x] N6. Two Unity clients plus bots on this PC for an hour: their views match the server, orders land, shields and
      governors work, bandwidth is within budget. Docs: overview, roadmap, backend API doc in `.docs/plan/`.

Open questions for later: multi-hub regions as separate processes (once one process nears its tick budget), Steam
auth, and per-platform transport checks (WebGL WebSocket).

## Result

- N1–N5 as planned, except: `ServerLink` uses `System.Net.WebSockets` + `Task` (UniTask isn't in the project; asking
  before adding it). Main menu is the game menu's Load or join / Play offline.
- N6 (2026-10-25): Unity client (player 6) + console client (player 7) + 5 bots on one server at real time. Unity's
  mirror hashed identical to the server's view at 9 of 9 five-minute checks (tick 704..3078); an order issued from
  Unity landed on the server and came back in the mirror. A game day at x120 earlier: 24/24 checks matched.
  Bandwidth: about 160 B/s per client at real time.
- Not finished: the hour was cut at ~53 min (Claude Code stopped the background runs on low memory), and a second
  Unity client wasn't run (one editor here); the console client stood in. It reported 2 drifts out of 99 unpaused
  checks: its hash request raced a command result frame (the order sat in the server queue but not yet in the client's
  view at the same tick). Fixed: the server now pushes a view right after an accepted order.
