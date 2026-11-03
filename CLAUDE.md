# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Stepped Streets is a Cities: Skylines II code mod: the player draws an area on a hillside and the mod plans and builds a whole
neighbourhood (contour streets, connectors, stepped lots, grading, zones, service buildings, trees, walls). User-facing behaviour,
settings and known limits are in `README.md`; the spec is `docs/Stepped_Streets_PRD.md` (`docs/HILLSIDE-NEIGHBOURHOODS.md` is
superseded history).

## Commands

Needs Windows, the game's Managed DLLs, .NET SDK 9+ (10 works) with the .NET 9 runtime, and Node 18+.

```powershell
cd src/SteppedStreets.UI; npm ci; cd ../..          # once; without node_modules the C# build skips the UI bundle with a warning
dotnet build SteppedStreets.sln -c Release           # also runs webpack into src/SteppedStreets/bin/Release; -p:SkipUI=true skips it
dotnet test SteppedStreets.sln                       # also renders gallery/*.png (git-ignored), one per synthetic hillside
dotnet test SteppedStreets.sln --filter "FullyQualifiedName~NeighbourhoodPlannerTests"   # one class; use ~Class.Method for one test
cd src/SteppedStreets.UI; npm test; npm run typecheck; npm run lint:css   # lint:css reads build/ from `npm run build`; webpack also lints
pwsh -File src/SteppedStreets.UI/tools/check-panel-json.ps1             # after a Release build: checks the compiled C# JSON writers
```

The game DLL path defaults in `build/GameReferences.props`; override with `-p:ManagedPath="<...>\Cities2_Data\Managed"`.
Building never installs the mod; do not copy into the game's `Mods` folder unless asked. Runtime logs:
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\SteppedStreets.log` (C#) and `Logs\UI.log` (panel).

## Projects and hard constraints

- `src/SteppedStreets.Core` — netstandard2.0, C# 9, references **only Unity.Mathematics** (no game or UnityEngine types). All
  planning lives here so it can be tested off-game. `InternalsVisibleTo` the tests.
- `src/SteppedStreets` — the mod itself, **net48, C# 9**, `NoStdLib`, every framework/game DLL referenced from `ManagedPath`.
- `src/SteppedStreets.UI` — React/TypeScript panel bundled by webpack into `SteppedStreets.mjs`/`.css`. React and `cs2/*` are
  window externals supplied by the game (no bundled React). CSS targets Gameface: the lint rejects unsupported constructs; use
  the game's `rem` units.
- `tests/SteppedStreets.Tests` — net9.0 xUnit. It compiles `src/SteppedStreets/Tool/SiteGeometry.cs` and `RightClickGesture.cs`
  by link, so those two files must stay free of game types.

## Architecture

**Planning (Core, pure and deterministic — same request + seed = same plan).** `NeighbourhoodPlanner.Plan(request, cancel,
roadsReady)` runs: `SiteSurvey` (4 m grid, smoothed, buildable/wild/blocked) → streets (`ContourStreets` phases compared in
parallel, or `MountainLane`) → `RoadLinks` (connectors under max grade, joins) → `RoadNetwork` (prune, assemble, then up to four
rounds of rule `Violations` that drop failing streets) → `roadsReady` callback with a roads-only copy for the early preview →
`LotLayout` (uses the original stepped-lot planner in `Planning/StepPlanner`) → `ServiceSites` → `GroundField` (one height
field: corridors and pads held level, eased back to the hill within the bank grade) → `Landscaping` (trees, walls). Failures are
reported as `plan.Messages`, not exceptions. `Core/Terrain/*` is adapted Placemaker terrain math (raster, sculpt, apply math).

**Game side (`src/SteppedStreets`).** `Mod.OnLoad` registers every system in its own try/catch; update order matters — the
Zone/Net/Object/Wall builders `UpdateBefore` `BuildToolSystem` so they hand it their Apply in the same frame.
- `Tool/NeighbourhoodToolSystem` (partial; `.Build.cs` holds the build) owns area editing and `ToolState` (stage machine
  Closed → Drawing → Planning → Planned → Building → Built/Failed, with a `Version` the panel watches). ECS and terrain are
  main-thread only, so `SiteSnapshot.Capture` copies the world into plain arrays and the planner runs on `Task.Run` with a
  `CancellationTokenSource`; every settings change re-plans. Before building, a fresh snapshot's `Fingerprint` is compared and
  a changed site forces a re-plan.
- The build is a callback chain of one-shot systems in `Build/`: `TerrainBuildSystem` → `NetBuildSystem` → wait for the game's
  zone blocks → `ZoneBuildSystem` (one zone at a time from a queue) → `ObjectBuildSystem.BeginBuildings` (services) →
  `ObjectBuildSystem.Begin` (trees) → `WallBuildSystem` → `FinishBuild`. Each `After*` step re-checks `Stage == Building`,
  appends to the build report and catches its own exceptions; an idle-frame watchdog stops a stuck build. The policy is to keep
  going and report what was off rather than abort (roads are built even if the ground check misses).
- `Build/BuildToolSystem` is an invisible tool: CS2 applies or clears all temporary entities by the *active* tool's
  `applyMode`, so builders activate it, request Apply, and it restores the previous tool.
- `Tool/NeighbourhoodPreviewSystem` draws the plan (Rendering phase). `Build/RoadCatalog` and `ServiceCatalog` read the
  game's prefabs.
- `Panel/PanelSystem` ↔ UI: one `ValueBinding<string>` of JSON (`SteppedStreets.state`) plus triggers. The full contract
  (fields, triggers, ranges) is in `src/SteppedStreets.UI/README.md`. A change to it touches `PanelSystem.cs`,
  `src/lib/state.ts` (`readState` must never throw and clamps every field) and `tests/state.test.cjs` together; numeric
  triggers clamp in C# and save via `Mod.Settings.ApplyAndSave()` then `tool.SettingsChanged()`.
- `Settings/SteppedStreetsSettings` holds the saved settings, Options page, key bindings and English labels.

## Licensing and provenance (GPL-3.0)

- Code adapted from Placemaker (or projects it credits) carries a header line
  `Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, <yyyy-mm>: <what changed>.` and an entry in
  `CREDITS.md`. Original files say `Original work by Tevin Benjamin, <yyyy-mm>. GPL-3.0.`
- The layout generator, grading field and stepped-lot planner must stay original: nothing from Placemaker's `LotLevel.cs` or
  `PolishLookLayer.cs`.
- `reference/placemaker` (commit `e654b32`) is a git-ignored, read-only reference checkout; never edit it. It is absent from
  git worktrees. `docs/STUDY-NOTES.md` summarises it.

## Style

Full-word names; comments explain why, not what. In-game text and docs use short, plain English.
