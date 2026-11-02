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
Wave 4 planner completion is original work by Tevin Benjamin, 2026-11, GPL-3.0:

- `src/SteppedStreets.Core/Planning/StepPlanRequest.cs`, `StepPlan.cs`, `StepPlanner.cs` and `StepPlanPatches.cs`: bank capacity per lot width, road-height aprons, sampled earthwork and obstacle checks, hillside-aware smoothstep blend sizing, and preview summaries and messages. No additional Placemaker code was copied or adapted; the existing terrain raster and surface interpolation retain their credits above.
- `tests/SteppedStreets.Tests/Planning/StepPlannerTests.cs`, `StepPlannerSiteTests.cs` and `StepPlanPatchesTests.cs`: synthetic planner rules, apron continuity, preview wording, deterministic results, and off-game raster checks at 8%, 12% and 15%.
