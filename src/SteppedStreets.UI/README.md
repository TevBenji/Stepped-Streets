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

The toolbar extension portals one button into the first toolbar group beside Zones. It observes group redraws and removes its slot when unmounted. A class host and React 18's `findDOMNode` locate the strip without adding a sibling marker that would interfere with Placemaker's own extension. This legacy React lookup needs replacement if the game upgrades React or exposes a forwarded host ref. There is no second launcher. The panel docks under the top bar at the left and uses the game's Button, Dropdown, Tooltip and Panel. Its vanilla slider commits on release; plus/minus buttons also work if that lookup fails. Panel and toolbar have separate error boundaries with null fallbacks. A missing binding or malformed JSON falls back to closed state and the saved-setting defaults.

Build is enabled only for Planned state. It opens the permanent-ground-change confirmation, with Build and Cancel. Changing the published state dismisses that confirmation. Building disables the controls, and the binding system also rejects setting changes during Building.

After a Release build, `pwsh -File tools/check-panel-json.ps1` checks the actual compiled C# JSON writers for escaped text and culture-independent/non-finite numbers without creating game systems. Use `-ManagedPath` and `-AssemblyPath` for other reference or output locations.

## Binding contract

Group: `SteppedStreets`. Value: `state`, a `ValueBinding<string>` carrying JSON with `stage`, `streetName`, `hoverGradePercent` (number or null), `hoverTooFlat`, `summary`, `messages`, `settings`, and `zones` (`name`/`label`). State JSON is regenerated only when ToolState.Version, a displayed setting or the initial zone catalog changes. Strings are escaped manually, and numbers use invariant culture.

| Trigger | Argument | Action |
|---|---|---|
| `toggleTool` | none | Toggle the picker |
| `close` | none | Close the picker |
| `clearSelection` | none | Clear selected streets |
| `build` | none | Build when Planned, after the UI confirmation |
| `setSide` | string | Both / Left / Right |
| `setZone` | string | Exact name from the loaded zone catalog |
| `setLook` | string | PlantedBank / LowWall |
| `setLotWidth` | double | 2–6 cells, rounded to an integer |
| `setNarrowest` | double | 2–6 cells, rounded to an integer |
| `setDepth` | double | 2–6 cells, rounded to an integer |
| `setMinimumGrade` | double | 4–12 percent, rounded to an integer |
| `setHighestStep` | double | 0.5–5 m |
| `setBankGrade` | double | 0.20–0.50 |
| `setEarthworkLimit` | double | 1–8 m |

Numeric triggers reject NaN/infinity and clamp values before saving. Valid setting writes call `Mod.Settings.ApplyAndSave()` and then `tool.SettingsChanged()`, including replanning if saving throws. All triggers and game-facing lifecycle methods catch exceptions and log through `Mod.Logger`.

The zone catalog reads every `ZonePrefab` through the `ZoneData` + `PrefabData` entity query. It is cached once a nonempty catalog is available. Labels use the active localization dictionary's `Assets.NAME[<name>]` entry, falling back to the prefab name. New prefabs or locale changes after that initial catalog need a game restart.

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

## Wave 4 merge notes

Delete both contract stubs after W1/W2 are merged: `src/SteppedStreets.Core/Wave4Stubs.cs` (StepLook) and `src/SteppedStreets/Wave4Stubs.cs` (ToolStage, ToolState and the inert picker). The picker stub includes the abstract ToolBaseSystem overrides required to compile. It intentionally performs no work. Keep the shared Look/EarthworkLimit settings additions once when merging W2 and W3.

The PRD's sections 5.1/5.4 still describe Narrow/Standard widths, stone/concrete wall looks and a 3 m highest-step ceiling. This implementation follows the fixed contract: 2–6 cells, Planted bank/Low wall and a 5 m ceiling. Existing settings defaults remain Both, 3-cell widths/depth, 6%, 3.2 m and bank grade 0.40. Section 5.5's suggestion of Narrow lots or a wall is planner message text; the panel renders the supplied messages unchanged. In-game appearance, toolbar placement and input behavior still require the lead's manual game check.

## Worker verification (2026-11-02)

| Command | Result |
|---|---|
| `npm ci` | Succeeded; 138 packages installed, 0 vulnerabilities |
| `npm run build` | webpack 5.112.2 succeeded; `build/SteppedStreets.mjs`, CSS and hashed SVG emitted |
| `npx tsc --noEmit` | Exit 0, no diagnostics |
| `npm test` | 5 tests passed, 0 failed |
| `npm run lint:css` | 1 file, 0 problems |
| `node --check build/SteppedStreets.mjs` | Exit 0, no diagnostics |
| `dotnet build SteppedStreets.sln -c Release` (repo root) | Succeeded, 0 warnings, 0 errors; bundle/CSS/images beside the DLLs in `src/SteppedStreets/bin/Release` |
| `dotnet test SteppedStreets.sln` (repo root) | 42 passed, 0 failed, 0 skipped |
| `pwsh -File tools/check-panel-json.ps1` | 5 escaped-string and 3 numeric/culture cases passed |
| `dotnet build src/SteppedStreets/SteppedStreets.csproj -c Release -p:SkipUI=true --no-restore` (repo root) | Succeeded with the one expected UI-skipped warning |
| `dotnet msbuild src/SteppedStreets/SteppedStreets.csproj -getItem:Compile` (repo root) | 9 C# sources, no UI sources in the compiler glob |
| `git diff --check` (repo root) | Exit 0, no whitespace errors |

The existing user npm configuration prints an unrelated `Unknown user config "store-dir"` notice; normal webpack and MSBuild builds succeed. No deployment or game run was performed. The inert contract stubs mean the picker/build flow is available only after the lead merges W2 and removes the stubs.
