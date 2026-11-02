# Credits

Stepped Streets is by TevBenji and is licensed under GPL-3.0.

The build setup is adapted from [Placemaker by jackwrichards](https://github.com/jackwrichards/placemaker), GPL-3.0, commit `e654b32`:

- `src/SteppedStreets/SteppedStreets.csproj`: standalone compiler settings and explicit framework/game DLL references, trimmed to remove the toolchain, UI build, git versioning, and deployment.
- `tests/SteppedStreets.Tests/SteppedStreets.Tests.csproj`: .NET 9/xUnit setup and the copy-local Unity.Mathematics reference, changed to reference the separate Core project.

The default game DLL path is shared through `build/GameReferences.props`.

The game-side zoning build is adapted from the same Placemaker commit (`e654b32`, GPL-3.0):

- `src/SteppedStreets/Build/BuildToolSystem.cs`: from `Game/Build/BuildToolSystem.cs` and the tool activation handoff in `Game/Build/BuildService.cs`. Kept the invisible tool and one-shot Apply handoff; removed apply gates and audit fields and added activation, one startup Clear, quiet frames and previous-tool restoration.
- `src/SteppedStreets/Build/ZoneBuildSystem.cs`: from the zoning emit/apply and live-cell verification in `Game/Build/BuildService.Terrain.cs:637-837`, plus `Height()` and definition ownership in `BuildService.Emit.cs`. Extracted a standalone quad API, named prefab lookup, requested-zone verification and completion report. Kept only neighbouring column depths as possible blocked-cell causes. The zoning definitions also follow **Grid Road Generator by Ti4goc (GPL-3.0)**, as credited by Placemaker: `ZoningFlags.Zone | ZoningFlags.Marquee`, without `Overwrite`. No terrain, fallback brushes or water code from that file is included.
The game terrain apply path is adapted from the same Placemaker commit (`e654b32`, GPL-3.0):

- `src/SteppedStreets/Build/TerrainBuildSystem.cs`: from `Game/Build/BuildService.Terrain.cs:30-451` and `Height()` in `Game/Build/BuildService.Emit.cs:97-106`. Kept direct Shift passes, masks, calibration and height copying; replaced plan/group types with the patch/report contract, retained the original snapshot for final curtain measurement, and removed pits, debug hooks, paint, dabs, water and zoning. No ParkManager or GridRoadGenerator code is included in this apply system.
- `src/SteppedStreets.Core/Terrain/TerraformApplyMath.cs`: extracted patch inputs (`Game/Build/BuildService.Terrain.cs:94-114`) and delta-time compensation (`:404-416`). Added patch validation and snapshot copies, and the inverse low-strength curve verified in the installed game's `TerrainSystem.ApplyToTerrain`, so reversing with a longer frame still cancels the original Shift displacement. Removed game and plan types.

The pure terrain code below is adapted from the same Placemaker commit (`e654b32`, GPL-3.0):

- `src/SteppedStreets.Core/Terrain/Sculpt.cs`: from `Core/Layers/Common/Sculpt.cs`. Changed the namespace and usings; `Emit` takes a patch list and cancellation token and returns the emitted cell count. Removed `Over`, `Resnap`, `GradeAt`, `Warn`, `CrossesRing`, the `Ground.Override` call, plan facts, and the clip path and state. Kept the shaping, interpolation, and earthwork code.
- `src/SteppedStreets.Core/Terrain/TerraformRaster.cs`: from `Core/Build/TerraformRaster.cs`, whole file with its namespace changed, including coverage and diagnostics.
- `src/SteppedStreets.Core/Terrain/TerraformSurface.cs`: from `Core/Build/TerraformSurface.cs`, whole file with its namespace changed.
- `src/SteppedStreets.Core/Terrain/TerrainMetrics.cs`: from `Core/Build/TerrainMetrics.cs`, whole file with its namespace changed.
- `src/SteppedStreets.Core/Terrain/PlannedTerraform.cs`: from `Core/Contracts.cs:849-872`. Kept `TerraformKind` and the `Ring`, `Kind`, `TargetHeight`, `Permanent`, and `Sloped` members; changed the namespace and removed the water fields and `Apron`.
- `src/SteppedStreets.Core/Terrain/PolygonHelpers.cs`: from `Core/Geometry/Poly.cs:174-204` (`Contains`/`ContainsD`) and `:243-263` (`Bounds`/`DistanceToBoundary`), `Core/Contracts.cs:363-381` (`ReservedSpace.DistSqToSegment`/`PointInPolygon`), and `Core/Build/TerraformDabs.cs:131-137` (`TerraformDabs.DistanceToBoundary`). Extracted static helpers, changed the namespace, and expanded helper parameter and local names. The original `TerraformDabs.cs` header also credits ParkManager's `ParkLakePlanner.ExcavationDabs/WaterAnchor` by tomkpunkt (GPL-3.0); that attribution is retained, although only the boundary-distance helper is included here.

The M1 spike trigger is original work. It follows Placemaker's patterns, but none of its code is copied: settings, key
bindings and Options labels (`Game/Tool/ToolRegistration.cs`), reading a hotkey action (`Game/Tool/PlacemakerToolSystem.Input.cs`),
walking a street's edges by their nodes and aggregate (`Game/Tool/PlacemakerToolSystem.Points.cs`, `Game/Suite/SuiteLab.FenceMap.cs`),
and reading the selection from `SelectedInfoUISystem` (`Game/Suite/SuiteSteps.Build.cs`). Files: `src/SteppedStreets/Settings/SteppedStreetsSettings.cs`,
`src/SteppedStreets/Spike/*.cs`, `src/SteppedStreets.Core/Planning/StepPlanPatches.cs` and `StreetChain.cs`. The idea of a
sloped strip that ramps between two levels comes from Placemaker's lot fillers (`Core/Layers/Types/Zoning/LotLevel.cs`),
and the 1 m zoning-quad inset from its `Core/Build/ZoneGuard.cs` (`QuadShrink`).

## Wave 4: low walls and documentation

The low-wall build uses the same Placemaker commit (`e654b32`, jackwrichards, GPL-3.0):

- `src/SteppedStreets/Build/WallBuildSystem.cs`: object-definition fields adapted from `Game/Build/BuildService.Emit.cs:534-569`; named prefab/entity lookup adapted from `Game/Build/SurfaceCloneSystem.cs:162-171`, using `StaticObjectPrefab` for the existing EU_Wall02 garden wall. Retained the emitter's original header and credits. Removed nets, surface clones, grouping, snapping, scale variation and audit code; added measured `ObjectGeometryData.m_Size.z` piece length, a logged 3 m fallback, fixed pad heights, end-to-end pieces and a completion report. Apply at +2 frames and definition cleanup at +4 follow this mod's `ZoneBuildSystem`, itself adapted from Placemaker's `BuildService.Terrain.cs` and invisible build tool. No new net emitter, fallback brush or water code is included.
- Placemaker's object-definition pattern credits **ParkingLotTool by KruemelGames (GPL-3.0)**. Its retained emitter header also credits **ParkManager by tomkpunkt** and **Grid Road Generator by Ti4goc (GPL-3.0)** for net definitions; this wall system uses only the object path. `SurfaceCloneSystem` also credits ParkingLotTool; this mod creates no cloned prefabs.
- `Core/Layers/Parking/Kerbs.cs:34-45` was studied for the EU_Wall02 choice, 0.2 m thickness, 0.9 m height and local-Z direction. Those asset facts inform the wall placement; its kerb planner was not copied.

`src/SteppedStreets.Core/Planning/StepPlanWalls.cs` and `tests/SteppedStreets.Tests/Planning/StepPlanWallsTests.cs` are original work by Tevin Benjamin, as are the registration block and player guide. Wall geometry uses the existing planner's corners and whole-column banks. No code is copied from `LotLevel.cs`, `PolishLookLayer.cs`, `TerraceField.LimitBanks` or `Resolve`. The full GPL-3.0 licence was already present in the root `LICENSE` and is retained unchanged.
## Wave 4: planner completion

Original work by Tevin Benjamin, 2026-11, GPL-3.0:

- `src/SteppedStreets.Core/Planning/StepPlanRequest.cs`, `StepPlan.cs`, `StepPlanner.cs` and `StepPlanPatches.cs`: bank capacity per lot width, road-height aprons, sampled earthwork and obstacle checks, hillside-aware smoothstep blend sizing, and preview summaries and messages. No additional Placemaker code was copied or adapted; the existing terrain raster and surface interpolation retain their credits above.
- `tests/SteppedStreets.Tests/Planning/StepPlannerTests.cs`, `StepPlannerSiteTests.cs` and `StepPlanPatchesTests.cs`: synthetic planner rules, apron continuity, preview wording, deterministic results, and off-game raster checks at 8%, 12% and 15%.
## Wave 4: street picker, preview and build flow (W2)

- `src/SteppedStreets/Tool/SiteSnapshot.cs` adapts the height and water grids from Placemaker
  `Game/Preview/SiteCapture.cs:153-204` and the road composition/ground-level checks at `:217-250`
  (same GPL-3.0 commit `e654b32`). It keeps fixed 4 m terrain and 8 m water grids, captures building
  lots and sampled road corridors, and fails capture rather than treating missing water data as dry.
  Building lot corners follow the API usage studied in `Game/Selection/BuildingFootprints.cs:27-38`.
  Placemaker credits Grid Road Generator's HeightSnapshot as the height-grid idea; that credit is retained.
- `Tool/StepStreetToolSystem.cs`, `Tool/StepStreetHotkeySystem.cs`, `Tool/RightClickGesture.cs` and
  `Tool/StepPreviewSystem.cs` are original work, informed by Placemaker's tool lifecycle/raycast
  (`Game/Tool/PlacemakerToolSystem.cs:165-304`), modifier/cancel input
  (`Game/Tool/PlacemakerToolSystem.Input.cs:37-155`) and overlay buffer usage
  (`Game/Overlay/PlacemakerOverlaySystem.cs:82-134`, `:625`). No code was copied from those files.
- `Tool/StreetCapture.cs` moves and extends this project's original `Spike/SelectedStreet.cs`.
  The original three failure logs and street summary remain. Shift-add validates a single unbranched
  chain, node identifiers include their version, and mixed-width chains use their widest road.
  The street-name call was studied in Placemaker's `Game/Suite/SuiteLab.FenceMap.cs:95-107`.
- `Tool/StepStreetToolSystem.Build.cs` moves this project's original spike flow into the picker,
  with settings fixed to its preview, guarded callbacks and the W4 low-wall completion step.
  The existing terrain and zoning systems retain their earlier Placemaker credits above.
- `Tool/ToolState.cs`, `Tool/SiteGeometry.cs`, `tests/SteppedStreets.Tests/Tool/*.cs`, and the
  Wave 4 contract stubs are original work. The test project links the pure geometry and gesture
  source files so those exact helpers are tested without game assemblies.
- The settings, registration and game-reference changes are original work. Options group and
  binding identifiers are preserved. The historical `Spike/*.cs` paths above now live under `Tool/`.
## Wave 4: toolbar, panel and bindings

`src/SteppedStreets.UI/tools/check-panel-json.ps1` is an original managed-assembly self-check by Tevin Benjamin, 2026-11, GPL-3.0, for the compiled JSON writers; it does not create game systems.

The UI integration is adapted from Placemaker by jackwrichards, GPL-3.0, commit `e654b32`. Original headers are retained in adapted files, followed by the adaptation note naming Tevin Benjamin and the changes.

- `src/SteppedStreets.UI/src/components/ToolbarButton.tsx`: from `UI/src/components/ToolbarButton.tsx`. Kept the vanilla ToolbarButtonStrip extension and portal into the first group; replaced the icon and toggle binding, added observation of group redraws and used a React class host to avoid a sibling marker interfering with Placemaker.
- `src/SteppedStreets.UI/src/lib/vanilla.ts`: trimmed `UI/src/lib/vanilla.ts` to the slider and focus key. Placemaker credits **Grid Road Generator by Ti4goc (GPL-3.0)** and the vanilla lookup pattern from **CS2-NetworkTools by Luca Rager (MIT, https://github.com/lucarager/CS2-NetworkTools)**; those credits are retained.
- `src/SteppedStreets.UI/src/lib/errorBoundary.tsx` and `tips.tsx`: trimmed Placemaker's matching UI helpers to null crash fallbacks and the one native toolbar tooltip. Placemaker credits Grid Road Generator by Ti4goc for both patterns.
- `src/SteppedStreets.UI/src/lib/bindings.ts`: from `UI/src/lib/bindings.ts`, trimmed to one late-safe JSON binding and guarded Stepped Streets triggers; state normalization is separate original code.
- `src/SteppedStreets.UI/src/index.tsx`: trimmed `UI/src/index.tsx` registration to the Game panel and bottom toolbar extension. Its original registration credits are **Grid Road Generator by Ti4goc (GPL-3.0)** and **CS2-NetworkTools by Luca Rager (MIT)**.
- `src/SteppedStreets.UI/types/{api,assets,bindings,cohtml,input,l10n,modding,react,ui,utils}.d.ts`: Placemaker's declarations of the game's modding API, copied with attribution and normalized whitespace; `validateTypes.ts` is excluded.
- `src/SteppedStreets.UI/tools/css-lint.js`: Placemaker's Gameface CSS lint, unchanged apart from the adaptation header; used during webpack and available as a CLI check.
- `src/SteppedStreets.UI/webpack.config.js`: from Placemaker's UI webpack setup: same window externals, module metadata banner and namespaced hashed image output; simplified to TypeScript and CSS modules, with no deployment or output cleanup. `package.json`, `mod.json` and `tsconfig.json` follow its project shape, with pinned minimal dependencies and our metadata.
- `src/SteppedStreets/Panel/PanelSystem.cs`: from the safe JSON binding/trigger pattern in `Game/UI/PlacemakerUISystem.cs`; rewritten for one state value, validated settings and a one-time localized zone catalog. Prefab query and label lookup follow `Game/Overlay/ZoneGridOpacitySystem.cs` and `Game/Assets/AssetCatalogSystem.cs` as patterns. No code was copied from either zoning planner file.

The panel (`src/SteppedStreets.UI/src/Panel.tsx`, `Panel.module.css`), defensive state reader (`src/lib/state.ts`), its tests (`tests/state.test.cjs`), the 48-unit stepped-lots SVG (`src/assets/stepped-streets.svg`) and UI README are original work by Tevin Benjamin, 2026-11, GPL-3.0. The slider API follows Placemaker's `UI/src/controls/Slider.tsx:64–88`; its drag state is implemented here for this panel. The Mod.cs registration block, optional csproj bundle target, .gitignore entries and shared Look/EarthworkLimit settings block are also original integration work. The temporary Wave4Stubs.cs contract files were removed during integration.

## Wave 4: integration review

Original integration corrections, Tevin Benjamin, 2026-11: `TerraformApplyMath.RequireClearGround` and its tests reject protected ground under the outer blend and brush margin before any brush runs; `TerrainBuildSystem.HeightAt` reads live terrain for zoning instead of substituting the target. The tool's patch log counts aprons separately, and planner advice no longer assumes narrower lots always permit a taller step. The project packages LICENSE, CREDITS.md and README.md beside its DLLs and UI assets. No additional reference code was copied for these corrections.
