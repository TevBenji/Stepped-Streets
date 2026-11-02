# Credits

Stepped Streets is by Tevin Benjamin and is licensed under GPL-3.0.

The build setup is adapted from [Placemaker by jackwrichards](https://github.com/jackwrichards/placemaker), GPL-3.0, commit `e654b32`:

- `src/SteppedStreets/SteppedStreets.csproj`: standalone compiler settings and explicit framework/game DLL references, trimmed to remove the toolchain, UI build, git versioning, and deployment.
- `tests/SteppedStreets.Tests/SteppedStreets.Tests.csproj`: .NET 9/xUnit setup and the copy-local Unity.Mathematics reference, changed to reference the separate Core project.

The default game DLL path is shared through `build/GameReferences.props`.

The pure terrain code below is adapted from the same Placemaker commit (`e654b32`, GPL-3.0):

- `src/SteppedStreets.Core/Terrain/Sculpt.cs`: from `Core/Layers/Common/Sculpt.cs`. Changed the namespace and usings; `Emit` takes a patch list and cancellation token and returns the emitted cell count. Removed `Over`, `Resnap`, `GradeAt`, `Warn`, `CrossesRing`, the `Ground.Override` call, plan facts, and the clip path and state. Kept the shaping, interpolation, and earthwork code.
- `src/SteppedStreets.Core/Terrain/TerraformRaster.cs`: from `Core/Build/TerraformRaster.cs`, whole file with its namespace changed, including coverage and diagnostics.
- `src/SteppedStreets.Core/Terrain/TerraformSurface.cs`: from `Core/Build/TerraformSurface.cs`, whole file with its namespace changed.
- `src/SteppedStreets.Core/Terrain/TerrainMetrics.cs`: from `Core/Build/TerrainMetrics.cs`, whole file with its namespace changed.
- `src/SteppedStreets.Core/Terrain/PlannedTerraform.cs`: from `Core/Contracts.cs:849-872`. Kept `TerraformKind` and the `Ring`, `Kind`, `TargetHeight`, `Permanent`, and `Sloped` members; changed the namespace and removed the water fields and `Apron`.
- `src/SteppedStreets.Core/Terrain/PolygonHelpers.cs`: from `Core/Geometry/Poly.cs:174-204` (`Contains`/`ContainsD`) and `:243-263` (`Bounds`/`DistanceToBoundary`), `Core/Contracts.cs:363-381` (`ReservedSpace.DistSqToSegment`/`PointInPolygon`), and `Core/Build/TerraformDabs.cs:131-137` (`TerraformDabs.DistanceToBoundary`). Extracted static helpers, changed the namespace, and expanded helper parameter and local names. The original `TerraformDabs.cs` header also credits ParkManager's `ParkLakePlanner.ExcavationDabs/WaterAnchor` by tomkpunkt (GPL-3.0); that attribution is retained, although only the boundary-distance helper is included here.
