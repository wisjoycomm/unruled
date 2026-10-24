# Plan (next, not started): the Unity client plays on the server

Follows the online foundation (O1–O5). Risky (network data shapes, client wiring): Opus, one step per commit, each
needing your OK first.

- [ ] N1. Wire format (Core, `Unruled.Core.Net`): MessagePack frames with int keys, append-only, and their own
      `ProtocolVersion`. Client to server: Hello(version, token), Command(DelayedPlayerCommand), Ping.
      Server to client: Welcome(playerId, tick), Snapshot, Delta, Refused(reason), Bulletin.
      Done: round-trip tests and a version-mismatch test.
- [ ] N2. Per-player view (Core): `PlayerView` builds what one player may see:
      - their own empire in full;
      - systems they hold a chart of: sites, owners, lanes, weather;
      - rival fleets only where the player has eyes (own site, fleet or radar);
      - market prices.
      Deltas are computed against the last view sent. Done: tests prove a rival's hidden fleet, stock and wallet never
      appear; size per player per tick is logged.
- [ ] N3. Server transport (Sim Lab server): a WebSocket at /ws. A Hello logs the player in; commands go through
      `CommandIssuer` (same latency and refusals as now); every tick each online player gets its delta, plus a full
      snapshot on join. Done: a console bot client stays in sync for a game day; bytes per player per second measured.
- [ ] N4. Login (temporary): a per-player token issued by the admin dashboard, checked in Hello. Steam login replaces it
      later. Done: a wrong token is refused.
- [ ] N5. Unity client: a `GameLink` interface with `LocalLink` (today's in-process world: single-player and offline)
      and `ServerLink` (WebSocket, UniTask, cancellation). `GameSession` stops stepping the clock in online mode and
      applies deltas to a client-side `SimulationWorld` mirror that the views keep reading. Main menu: Play offline /
      Join server (address + token). Done: recompile clean, views unchanged.
- [ ] N6. Two Unity clients plus bots on this PC for an hour: their views match the server, orders land, shields and
      governors work, bandwidth is within budget. Docs: overview, roadmap, backend API doc in `.docs/plan/`.

Open questions for later: multi-hub regions as separate processes (once one process nears its tick budget), Steam
auth, and per-platform transport checks (WebGL WebSocket).
