# Credits

Stepped Streets is made by **[TevBenji](https://github.com/TevBenji)** and released under the
[GNU General Public License v3.0](LICENSE).

It stands on the shoulders of the Cities: Skylines II modding community. The projects below shaped how this mod talks to the
game, and some of their code lives on here, adapted. Thank you to every one of them.

## Projects this mod builds on

| Project | Author | Licence | What it contributed |
|---|---|---|---|
| [Placemaker](https://github.com/jackwrichards/placemaker) (commit `e654b32`) | jackwrichards | GPL-3.0 | Terrain shaping and verification, the invisible build tool, zoning, object and road-course definitions, the road-cull crash guard, the site snapshot, and the panel's UI plumbing and look |
| ParkManager | tomkpunkt | GPL-3.0 | The road-course definition pattern and a boundary-distance helper, credited through Placemaker |
| Grid Road Generator | Ti4goc | GPL-3.0 | Course positions on existing roads, the zoning flags, the height-grid idea and UI registration patterns, credited through Placemaker |
| ParkingLotTool | KruemelGames | GPL-3.0 | The object-definition pattern, credited through Placemaker |
| [CS2-NetworkTools](https://github.com/lucarager/CS2-NetworkTools) | Luca Rager | MIT | The lookup of the game's own UI components, credited through Placemaker |

Every adapted file keeps its original header and adds a line naming its source and what changed:
`Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, <date>: <changes>.`

## Original work

Original work by TevBenji (2026), GPL-3.0:

- **The neighbourhood generator** (`src/SteppedStreets.Core/Neighbourhood/`): the site survey, contour streets, connectors
  and hairpins, the connection to existing roads, the mountain lane, rule validation and pruning, lot layout, the zone and
  service mix, the grading field with its banks, trees and garden walls. Placemaker's hillside terraces were studied for two
  ideas (streets at a constant height interval and the terrace spacing rule); no code was copied or adapted from them, nor
  from its `LotLevel.cs` or `PolishLookLayer.cs`.
- **The stepped-lot planner** (`src/SteppedStreets.Core/Planning/`): lot rules, steps, aprons, bank capacity, garden-wall
  geometry and preview wording. The idea of a sloped strip between two levels comes from Placemaker's lot fillers, and the
  1 m zoning-quad inset from its `ZoneGuard`; no code was copied.
- **The tool and build flow** (`src/SteppedStreets/Tool/`, `Build/RoadCatalog.cs`, `Build/ServiceCatalog.cs`, the settings
  and the registration in `Mod.cs`). They follow the tool lifecycle, input and overlay patterns of Placemaker's tool system,
  written for this mod. The build order (ground, then roads, then zoning once the zone blocks exist) follows Placemaker's
  build service; the code is this mod's own.
- **The panel** (`src/SteppedStreets.UI/src/Panel.tsx`, `Panel.module.css`, `lib/state.ts`, its tests and the icons). The
  look follows Placemaker's panel styles (dark docked panel, small-caps sections, label-left rows, segmented choices, accent
  footer button) and its slider API; no component was copied.
- `src/SteppedStreets.UI/tools/check-panel-json.ps1` and `tools/publish-mod.ps1`.

## Adapted files

All from [Placemaker](https://github.com/jackwrichards/placemaker) by jackwrichards, GPL-3.0, commit `e654b32`.

### Building in the game

| File | Adapted from | What changed |
|---|---|---|
| `src/SteppedStreets/Build/BuildToolSystem.cs` | `Game/Build/BuildToolSystem.cs` and the tool hand-off in `BuildService.cs` | Kept the invisible tool and one-shot Apply; added activation, a start-up Clear, quiet frames and restoring the player's tool. |
| `src/SteppedStreets/Build/TerrainBuildSystem.cs` | `Game/Build/BuildService.Terrain.cs` and `Height()` in `BuildService.Emit.cs` | Kept the Shift passes, masks, calibration and height copies; takes the planner's raster ready-made and reports instead of failing; no pits, paint, water or zoning. |
| `src/SteppedStreets/Build/NetBuildSystem.cs` | The road-course path of `Game/Build/BuildService.Emit.cs` | Kept course construction and attaching ends to existing roads; builds the plan as short pieces at their planned heights, counts refused and built pieces. Credits ParkManager and Grid Road Generator through the original header. |
| `src/SteppedStreets/Build/ZoneBuildSystem.cs` | Zoning emit, apply and cell checks in `BuildService.Terrain.cs` and `BuildService.Emit.cs` | A standalone quad API with prefab lookup by name and a per-zone report. Zoning flags follow Grid Road Generator. |
| `src/SteppedStreets/Build/ObjectBuildSystem.cs` | The object-definition path of `BuildService.Emit.cs` | Trees and service buildings, 1500 a frame, on the live ground. Credits ParkingLotTool through the original header. |
| `src/SteppedStreets/Build/WallBuildSystem.cs` | The object-definition path of `BuildService.Emit.cs` and the prefab lookup in `SurfaceCloneSystem.cs` | Garden walls end to end at measured piece lengths; wall facts from Placemaker's kerb planner. |
| `src/SteppedStreets/Build/RemoveBuildSystem.cs` | The delete definitions of `Game/Suite/SuiteFixture.cs` and the clearing in `BuildService.Owner.cs` | Removes a list of road edges, objects and buildings through the invisible build tool, 1500 a frame, and counts what is gone. |
| `src/SteppedStreets/Build/TerrainCullGuard.cs` | `Game/Build/TerrainCullGuard.cs` | Copied; namespace and log lines changed. Guards the game's road-cull list once the mod builds many roads at once. |
| `src/SteppedStreets/Tool/SiteSnapshot.cs` | `Game/Preview/SiteCapture.cs` | Captures a drawn area: heights, water and buildings in one blocked grid, nearby roads and a change fingerprint. The height grid credits Grid Road Generator. |
| `src/SteppedStreets/Panel/PanelSystem.cs` | The JSON binding and trigger pattern of `Game/UI/PlacemakerUISystem.cs` | Rewritten for one state value, validated settings and the zone, road and service catalogues. |
| `src/SteppedStreets/SteppedStreets.csproj` | `Placemaker.csproj` | Standalone game references and the UI bundle step, without the toolchain or deployment. |

### Terrain maths

| File | Adapted from | What changed |
|---|---|---|
| `src/SteppedStreets.Core/Terrain/TerraformRaster.cs` | `Core/Build/TerraformRaster.cs` | Whole file; namespace changed. |
| `src/SteppedStreets.Core/Terrain/TerraformSurface.cs` | `Core/Build/TerraformSurface.cs` | Whole file; namespace changed. |
| `src/SteppedStreets.Core/Terrain/TerrainMetrics.cs` | `Core/Build/TerrainMetrics.cs` | Whole file; namespace changed. |
| `src/SteppedStreets.Core/Terrain/TerraformApplyMath.cs` | Patch inputs and frame-time compensation in `BuildService.Terrain.cs` | Added patch validation, snapshot copies and the inverse strength curve for undoing a pass. |
| `src/SteppedStreets.Core/Terrain/PlannedTerraform.cs` | `Core/Contracts.cs` | The terraform kind and ring members only. |
| `src/SteppedStreets.Core/Terrain/PolygonHelpers.cs` | `Core/Geometry/Poly.cs`, `Core/Contracts.cs`, `Core/Build/TerraformDabs.cs` | Static point-in-polygon, bounds and distance helpers. The boundary-distance helper credits ParkManager by tomkpunkt. |

### Panel

| File | Adapted from | What changed |
|---|---|---|
| `src/SteppedStreets.UI/src/components/ToolbarButton.tsx` | `UI/src/components/ToolbarButton.tsx` | New icon and toggle; watches toolbar redraws so it sits beside Placemaker without interfering. |
| `src/SteppedStreets.UI/src/lib/vanilla.ts` | `UI/src/lib/vanilla.ts` | Trimmed to the slider and focus key. Credits Grid Road Generator and CS2-NetworkTools. |
| `src/SteppedStreets.UI/src/lib/bindings.ts` | `UI/src/lib/bindings.ts` | One late-safe JSON binding and guarded triggers. |
| `src/SteppedStreets.UI/src/lib/errorBoundary.tsx`, `tips.tsx` | The matching Placemaker UI helpers | Trimmed to quiet crash fallbacks and the toolbar tooltip. Credits Grid Road Generator. |
| `src/SteppedStreets.UI/src/index.tsx` | `UI/src/index.tsx` | Registers the panel and the toolbar button only. Credits Grid Road Generator and CS2-NetworkTools. |
| `src/SteppedStreets.UI/types/*.d.ts` | Placemaker's declarations of the game's UI modding API | Copied with attribution. |
| `src/SteppedStreets.UI/webpack.config.js` | Placemaker's UI webpack setup | Same window externals, metadata banner and hashed images; simplified to TypeScript and CSS modules. |
| `src/SteppedStreets.UI/tools/css-lint.js` | `UI/tools/css-lint.js` | Unchanged apart from the header; checks CSS against what the game's UI engine supports. |
| `src/SteppedStreets.UI/tools/css-presence.js` | `UI/tools/css-presence.js` | Exports `hasCSS`, without which the game never loads the panel's stylesheet (as the official UI mod template does). |

## Game data

Cities: Skylines II, its assets and its modding API belong to Colossal Order and Paradox Interactive. Stepped Streets is an
independent mod and is not made or endorsed by them.
