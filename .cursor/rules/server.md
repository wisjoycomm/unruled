---
paths:
  - "Assets/Scripts/Core/**"
---

# C# simulation & server core rules (.NET 8+, deterministic tick + sockets)

Applies to `Assets/Scripts/Core/` (`Unruled.Core`): the engine-free simulation and command
handlers run deterministically (`1 Tick = 1 Second`) in-process and on the authoritative server.

## Network & serialization performance
- Handle binary packets with `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` and
  `ReadOnlySequence<byte>`; avoid unnecessary copies (`Buffer.BlockCopy`, `Array.Copy`).
- Rent network buffers from `ArrayPool<byte>.Shared` or `MemoryPool<T>` and always
  return them in `try/finally`.
- Serialize with MessagePack-CSharp (int keys, generated resolvers) or MemoryPack.
  Client and server contracts must match `Unruled.Core` shapes exactly.

## Concurrency & determinism
- Keep tick evaluation (`SimClock`, `NoaaWeatherEngine`, `WtoMarketEngine`, `LanchesterCombat`) deterministic and free of race conditions.
- Use `ValueTask`/`ValueTask<T>` for hot-path async I/O methods that often complete synchronously.
- Prefer `ConcurrentDictionary`, `Channel<T>`, `SemaphoreSlim`,
  `ReaderWriterLockSlim`. Never `lock(this)`.
- REST JSON: `System.Text.Json` source generators.

## Checklist
- [ ] Zero `UnityEngine` references inside `Assets/Scripts/Core/`.
- [ ] No GC allocations in per-tick simulation or socket receive loops.
- [ ] Every async call takes a `CancellationToken`.
- [ ] Pooled buffers are returned.
