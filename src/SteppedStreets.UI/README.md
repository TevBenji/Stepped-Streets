# Stepped Streets UI

This is the game's React panel and bottom toolbar button. The game supplies React and the `cs2/*` APIs; the bundle does not ship a second React runtime. All UI toolchain versions are pinned in `package.json`, with transitive versions in `package-lock.json`.

From this directory:

```powershell
npm ci
npm run build
npx tsc --noEmit
npm test
npm run lint:css
```

The output is `build/SteppedStreets.mjs`, `build/SteppedStreets.css` and a hashed icon under `build/images/steppedstreets/`. The module banner supplies the game's Id, Author, Version and Dependencies fields. CSS lint runs during webpack and rejects unsupported Gameface constructs. Dimensions use the game's `rem` units.

After `npm ci`, `dotnet build SteppedStreets.sln -c Release` also runs webpack and sets `STEPPED_STREETS_UI_OUT` to the C# project's output directory, normally `src/SteppedStreets/bin/Release`. The resulting folder contains the DLLs, module, CSS and `images/`. For a different UI output directory, set `STEPPED_STREETS_UI_OUT` before `npm run build`. Neither build command deploys anything.

If `node_modules` is missing or `-p:SkipUI=true` is supplied, the C# build skips webpack and reports `UI bundle skipped: run npm ci in src/SteppedStreets.UI`. Old bundle files, if present, remain in the output folder. Webpack never cleans the shared DLL output directory. The UI project is a sibling of the C# project, outside its source glob.

The toolbar extension portals one button into the first toolbar group beside Zones. It observes group redraws and removes its slot when unmounted. A class host and React 18's `findDOMNode` locate the strip without adding a sibling marker that would interfere with Placemaker's own extension. This legacy React lookup needs replacement if the game upgrades React or exposes a forwarded host ref. There is no second launcher. The panel docks on the right, folds to its header and moves by dragging the header, and is built from plain elements styled by our CSS (themed cs2/ui components lose their styling). Its vanilla slider commits on release; plus/minus buttons also work if that lookup fails. Panel and toolbar have separate error boundaries with null fallbacks. A missing binding or malformed JSON falls back to closed state and the saved-setting defaults.

Build is enabled only for Planned state. It opens the permanent-ground-change confirmation, with Build and Cancel. Changing the published state dismisses that confirmation. Building disables the controls, and the binding system also rejects setting changes during Building.

After a Release build, `pwsh -File tools/check-panel-json.ps1` checks the actual compiled C# JSON writers for escaped text and culture-independent/non-finite numbers, and that moving or folding the panel leaves a ready plan ready, without creating game systems. Use `-ManagedPath` and `-AssemblyPath` for other reference or output locations.

## Binding contract

Group: `SteppedStreets`. Value: `state`, a `ValueBinding<string>` carrying JSON, written by `src/SteppedStreets/Panel/PanelSystem.cs`
and read defensively by `src/lib/state.ts` (`readState` never throws and clamps every field):
`stage` (Closed, Drawing, Planning, Planned, Building, Built, Failed), `corners`, `areaClosed`, `confirming` (the Build dialog is
open; the tool clears it on Esc, a new plan and the build), `areaSquareMetres`, `summary`,
`messages`, `stats` (`roadKilometres`, `lots`, `zonedCells`, `earthCubicMetres`, `steepestRoadPercent`, `wildPercent`,
`connected`, `trees`), `zoneCounts` (`zone`/`lots`), `serviceCounts` (`name`/`count`, the planned buildings), `progress` (0–1),
`settings` (the trigger values below, plus `steepGround`, `shapeGround`, `existingStreets`, `junctions`, `serviceMix` as `name`/`count` rows, `crosswalks`, `streetTrees`, `panelCollapsed`, and `panelLeft`/`panelTop`: fractions of the screen, negative for the usual docked place), `discord` (the mod has a Discord link, so the panel shows its Discord buttons), and the catalogues `zones` and
`roads` (`name`/`label`, read again each time the tool opens, so Road Builder roads made since appear) and `services`
(`name`/`label`/`group`/`width`/`depth`, the unlocked roadside service buildings, read again each time the tool opens, leaving out any from content the player doesn't have). Zones and services may also carry `icon`,
the game's own menu icon path (ImageSystem.GetIconOrGroupIcon); it is left out when the game has none. While Planning and Building, `summary` is the current step; once Built,
it is the build report (the panel shows it under the status line). The state is rebuilt only when ToolState.Version, a setting or a catalogue changes.

| Trigger | Argument | Action |
|---|---|---|
| `toggleTool`, `close` | none | Open/close the tool |
| `clearArea`, `closeArea`, `undo` | none | Area editing |
| `setConfirming` | bool | Open (only when Planned) or close the Build dialog; while it is open the tool ignores the map |
| `build` | none | Build when Planned, after the UI confirmation; closes the dialog |
| `reroll` | none | A new seed |
| `setStyle` | string | ContourSuburb / TerracedBlocks / MountainLane / SquareGrid / RectangularGrid / IrregularGrid / SkewedGrid / FragmentedGrid / DiagonalGrid / CurvedGrid / WarpedGrid / RadialGrid / Star / Rings / Spiderweb / Fan / Spine / Ladder / Comb / Fishbone / Tree / CulDeSacs / Loops / LoopsAndLollipops / Lollipops / TriangularMesh / Hexagonal / HexagonalSpokes / Ridge / Valley / Coastal / Winding / OrganicMesh / Hybrid / Polycentric / Superblock / Radburn / FusedGrid / FilteredGrid / BarcelonaSuperblock |
| `setJunctions` | string | Crossroads / Staggered / TrafficOptimised (grid layouts) |
| `setRoad` | string | A road `name` from the catalogue |
| `setMainRoad` | string | A road `name` for main streets and connectors; empty: the same road for every street |
| `setCornerZone`, `setMainStreetZone` | string | A zone `name` for corner lots / lots on a main street; empty: from the mix |
| `setStreetTrees` | string | None / Grass / Trees: the game's planting upgrade on the streets built or zoned. Saved; no re-plan |
| `setMixPattern` | string | Random / Clumps / Even |
| `setZoneMix` | string | `[{"zone":"...","share":N},...]`; zones not in the catalogue are dropped (a list of only those is refused); `[]` means no zoning: lots are levelled but left unzoned |
| `setServiceMix` | string | `[{"name":"...","count":N},...]` (N 1–20); buildings not in the `services` catalogue are dropped; `[]` is allowed |
| `setBlockDepth` | double | 60–200 m, step 5 |
| `setMaxRoadGrade`, `setSteepestBuildable` | double | 4–12 %, 8–60 % (C# caps the second at 30 % unless `steepGround`) |
| `setLotWidth`, `setLotDepth` | double | 2–6 cells |
| `setEarthworkLimit` | double | 1–12 m, step 0.5 (C# caps it at 8 m unless `steepGround`) |
| `setBankGrade` | double | 20–50 %, step 5 |
| `setLotSpacing` | double | −1 (Auto) to 2 cells |
| `setTreeDensity`, `setSeed` | double | 10–100 %, 1–99999 |
| `setLevelLots`, `setRetainingWalls`, `setTrees` | bool | Switches |
| `setShapeGround` | bool | Off: no terrain changes at all (and no level lots) |
| `setExistingStreets` | bool | On: zone the empty cells along streets already in the area; no new roads |
| `setFootpaths`, `setPocketParks` | bool | Paths through long blocks; small parks on frontage too short for a lot |
| `setLevelExistingLots` | bool | Use existing: level the lots before zoning (changes the ground) |
| `setSteepGround` | bool | Steep hills; on lifts `steepestBuildablePercent` to at least 50 and `earthworkLimit` to at least 8, off caps them at 30 and 8 |
| `setCrosswalks` | bool | Off: the new streets' junctions get the game's crosswalk-removal upgrade. Saved; no re-plan; read when the roads are built |
| `setPanelCollapsed` | bool | Fold the panel to its header. Saved; no re-plan; works during a build |
| `setPanelPosition` | double, double | Left and top as fractions of the screen (above 1 is capped); negative puts the panel back in its usual place. Saved; no re-plan; works during a build |
| `openLink` | string | `bug` or `discord`, mapped in C# to a fixed address; anything else does nothing |

Numeric triggers reject NaN/infinity and clamp before saving. Valid writes call `Mod.Settings.ApplyAndSave()` and then
`tool.SettingsChanged()`, which re-plans the area. All triggers catch exceptions and log through `Mod.Logger`. The zone
catalogue is read once; labels use the active localisation's `Assets.NAME[<name>]`, falling back to the prefab name.

## Game UI API verification

All source references below refer to the supplied read-only Placemaker checkout, commit `e654b32`, under `UI/`.

| Extended, appended or retrieved path | Export / purpose | Verified source |
|---|---|---|
| `Game` | append the panel | `src/index.tsx:24` |
| `game-ui/game/components/toolbar/top/toolbar-button-strip/toolbar-button-strip.tsx` | extend `ToolbarButtonStrip` | `src/components/ToolbarButton.tsx:18`, `src/index.tsx:37` |
| `game-ui/game/components/toolbar/top/toolbar-button-strip/toolbar-button-strip.module.scss` | get `classes` | `src/components/ToolbarButton.tsx:30` |
| `game-ui/game/themes/transparent-button.module.scss` | get `classes` | `src/components/ToolbarButton.tsx:31` |
| `game-ui/common/input/slider/slider.tsx` | get `Slider` | `src/lib/vanilla.ts:17`; props at `src/controls/Slider.tsx:64–88` |
| `game-ui/common/focus/focus-key.ts` | get `FOCUS_DISABLED` | `src/lib/vanilla.ts:61–63` |

Direct imports are `cs2/api` (`bindValue`, `useValue`, `trigger`, verified in `src/lib/bindings.ts:10,20,297` and `types/api.d.ts`), `cs2/modding` (`getModule`, registration types; `src/components/ToolbarButton.tsx:8`, `src/index.tsx:4`) and `cs2/ui` (components verified in `types/ui.d.ts`: Tooltip 56, Button 424, Dropdown 454, DropdownToggle 460, DropdownItem 502/708, Panel 564). These are window externals, along with the remaining external names in Placemaker's `webpack.config.js:33–42`.

C# API names were checked against the installed game's Game.dll, Colossal.UI.Binding.dll and Unity.Entities.dll, and compiled against those assemblies. Patterns: `Game/UI/PlacemakerUISystem.cs:20,46,148–164` (UISystemBase, JSON binding and safe triggers), `Game/Overlay/ZoneGridOpacitySystem.cs:46,76–80` (zone query and prefab access), `Game/Assets/AssetCatalogSystem.cs:812–820` (localized prefab labels), and `Game/Tool/PlacemakerToolSystem.Design.cs:148` (ApplyAndSave).
