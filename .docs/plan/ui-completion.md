# Plan: finish UI for every built feature

Goal (user, 2026-10-23): every command the sim accepts has UI, plus popups. Each step: compile clean via MCP, Play Mode capture in `.temp/captures/ui/`, commit.

- [x] U0. Assets via agy: agency emblems (NOAA/IAEA/WTO/WHO/STA), action icons (scan, ECM, mothball, terraform, relocate, warning), victory/defeat/menu backdrops → `Assets/Resources/UI/`. Done when: imported as Sprite, no import errors.
- [x] U1. Modal popup (`ModalPopup`: title, body, icon, confirm/cancel); nuke, borrow, relocate, terraform ask first. Done when: Play Mode shows the confirm for a nuke.
- [x] U2. Bulletin feed: toast + log of `SimulationWorld.Bulletins` with agency emblem. Done when: a NOAA bulletin shows in Play Mode.
- [x] U3. Market tab actions: buy/sell order (item, qty, price), cancel own order, sell to / buy from THE SYSTEM. Done when: a placed order shows in the order book.
- [x] U4. Fleet card: belt scan / cancel scan / ECM jam, mothball / activate, focus Line/EW/Supply. Done when: buttons issue and show in the in-flight strip.
- [x] U5. Operations: relocate base, terraform (with confirm). Done when: buttons visible with cost and gate state.
- [x] U6. Victory/defeat screen when the Citadel falls / a player is out. Done when: forced in Play Mode via execute_code shows the screen.
- [x] U7. Pause menu: pause/resume, save, load, quit. Done when: pause stops ticks, save writes the file.
