# Plan: Unruled — Style pass (after Sprint 4)

Source: style guide artifact https://claude.ai/artifact/8zPUQcHGYvqW7m2GrD6aWd (Main = guide, Mockup = desktop main screen).
- [x] S1. Theme: `Assets/Scripts/View/UI/Theme/` USS with the palette as variables (ground/text, sides & agencies, NOAA 1–5, thermal), type classes (display Chakra Petch, UI Plex Sans, numbers Plex Mono), components (filled/outlined/danger 44px buttons, data rows, dashed in-flight rows, alert banners, NOAA chips, thermal bar); ThemeStyleSheet + PanelSettings; fonts as FontAssets. Done: a UI Builder preview sheet shows every component, matching the guide.
- [x] S2. `UnruledPalette.cs` (View): the same colours as `Color` constants for the board renderer, one source with the USS. Done: compiles, values match the guide.
- [x] S3. Item pictograms: `ItemIcon` VisualElement drawing the 20 guide paths with Painter2D (raw = outline, refined = filled); no new package. Done: 20 icons render at 16 px in the preview sheet.
- [x] S4. Main-screen UXML per the mockup: TopTelemetry, LeftInventory (20 rows), map area, fleet card, WTO market, in-flight strip. Static data. Done: Play Mode screenshot at 1440x900 lines up with the mockup.
- [x] S5. Controllers with `Bind(SimulationWorld)`, updated once per tick: TopTelemetryHud (clock, credits, NOAA), LeftInventoryDock (stockpile), ContractAndMarketUI (VWAP, best bid/ask from the order book). Done: values follow a running sim.
- [x] S6. `MapBoardRenderer`: lanes solid/dotted/zigzag, site glyphs (core, own, hostile, unclaimed, WHO, irradiated), fleet chevrons by tier, storm rings at 20% fill. Done: screenshot matches the guide's board language.
- [x] S7. Bootstrap scene `Assets/Scenes/Main.unity` + sim driver (1 tick = 1 s) on a generated universe. Done: Play Mode runs, screenshot compared with the mockup; overview/roadmap/change log updated.
