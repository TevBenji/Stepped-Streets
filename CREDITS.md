# Credits

Stepped Streets is by Tevin Benjamin and is licensed under GPL-3.0.

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
