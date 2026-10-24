# Plan: No Man's Sky galaxy (approved 2026-10-24). Full design: C:\Users\admin\.claude\plans\enchanted-mapping-lampson.md

- [x] G1. GalaxyCatalog (Core/Galaxy): seeded sectors, spiral density, StarId, TryGetStar, StarsInRect, Neighbours + tests. Done: tests green.
- [x] G2. StarSystem/SystemName stores, GalaxySystem.Materialise, save schema 6 + migration test. Done: tests green, old save loads.
- [x] G3. Spawn rules (homes in spawn ring, Hub+Citadel per player group) replace fixed wiring; simlab --bots 20/100 soak. Done: soak reaches T2 gates and fights.
- [x] G4. Survey command + AI frontier expansion; O(n^2) generation fixes. Done: tests + soak tick time.
- [x] G5. RenameSystem command + validation tests. Done: tests green.
- [x] G6. View: zoomable camera + LOD (galaxy dust / region stars / system / planet). Done: captures of 4 levels.
- [x] G7. View: catalog labels, rename UI, system card from catalog. Done: capture of rename everywhere.
- [x] G8. Docs: roadmap (GDD deviation: hubs per player group), overview, change log.
- [x] G9. Planet count varies per star (5-12; homes and hubs at least 8). Done: tests green, soak ok.
- [x] G10. Catalog hyperlanes are routes: every charted neighbour linked (homes too); uncharted frontier neighbours shown as exits with a Survey button. Done: tests, soak, capture.
