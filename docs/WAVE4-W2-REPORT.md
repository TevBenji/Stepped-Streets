# W2 implementation report

Implemented the road picker, joined-street selection, read-only overlay, site snapshot, terrain → zoning → optional low-wall build, settings and global toggle hotkey. Removed the spike trigger and `docs/SPIKE.md`. The React panel remains W3's responsibility; nothing here depends on it.

- Branch: `t3/street-picker-preview-build`
- Worktree: `C:\Users\tevin\.t3\worktrees\Stepped Streets\t3-13b333b2`
- Starting commit: `102c6f619d2e8f9952b23c2f39fbb04e5f77ae4b`
- No deployment, push, changes to the reference checkout, or changes to the game installation.

Build refreshes the selected edges and site first. If its pads or steps changed since confirmation, it leaves the new preview open for review. A failed terrain report stops before zoning/walls. Changing tools prevents later build stages from taking the tool back. Loading a city discards the old entities and snapshot. Settings used by a running build are fixed at its start.

## Validation

SDK: `dotnet --version` → `10.0.403`.

| Command | Result |
|---|---|
| `dotnet test tests/SteppedStreets.Tests/SteppedStreets.Tests.csproj --filter FullyQualifiedName~SiteGeometryTests` | Initial four geometry tests passed. |
| `dotnet build SteppedStreets.sln -c Release` | Final: Build succeeded; **0 warnings, 0 errors**. |
| `dotnet test SteppedStreets.sln` | Final: **52 passed, 0 failed, 0 skipped**. |
| `git diff --check` | No whitespace errors. |

The first Release compile found two errors from `Time` resolving to ECS's obsolete property in the overlay; qualified it as `UnityEngine.Time`. Subsequent builds passed. Ten new test cases cover bilinear heights/boundaries, rotated lots, road clearance, disconnected/branched additions, changed preview geometry, short right-clicks, camera drags, long holds, UI releases and gesture reset.

No game session was run. Main-thread capture/planning has the requested `// ponytail:` 250 ms ceiling and worker/spatial-query upgrade paths. The log measures capture plus planning. Mixed-width selections use the widest road; a `// ponytail:` comment identifies per-edge planner widths as the upgrade.

## Merge stubs

Delete both files after merging W1 and W4:

- `src/SteppedStreets.Core/Wave4Stubs.cs`: `StepLook`, `WallSegment`, `StepPlanWalls`.
- `src/SteppedStreets/Wave4Stubs.cs`: `WallBuildReport`, `WallBuildSystem`.

Take W1's complete versions of `Core/Planning/StepPlanRequest.cs` and `StepPlan.cs`; their marked stub members here only make the fixed contract compile. In this isolated branch the old planner does **not** enforce the new bank/earthwork/obstacle rules, and the wall stub places nothing. W4 supplies real wall behavior and registration. Keep W2's two pure-helper links in the test project.

## PRD differences and lead questions

- §5.1 and §5.4 still list Stone wall/Concrete wall and a wall default. The fixed contract is Planted bank/Low wall, default Planted bank.
- §5.1 says Shift-click adds a segment of the same street. The contract selects the whole named street and allows another named street when its union forms one unbranched chain.
- §5.1 mentions per-pad height/reason labels. W2's specified overlay draws outlines/fill/banks/steps; heights remain in per-pad logs, and planner messages go to the panel. Map labels would need separate scope.
- §7's worker-planning recommendation is deferred by the explicit W2 main-thread allowance. §10's old select-tool hotkey flow is replaced by the picker toggle and W3's confirmation UI.
- The fixed `ZoneBuildReport` verifies one cell per quad/run. It cannot count all actual zoned cells. The build summary therefore says `N level cells, Z/R zoning runs verified`; an exact `N cells zoned` result needs a later report/API extension.
- No implementation blocker. The lead must replace the stubs, retain the global hotkey registration, merge W3's confirmation panel, and arrange the first in-game check. Terrain failures now stop the flow rather than attempting partial zoning on failed ground.

## Game API self-review

Verified against the installed 1.6.2f1 assemblies at `D:\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed`. Every listed API also compiled against those assemblies. Placemaker paths below are relative to the read-only `reference/placemaker` checkout, commit `e654b32`.

| Used API / members | Verification |
|---|---|
| `ToolBaseSystem`, `toolID`, `GetPrefab`, `TrySetPrefab`; `OnCreate`, `OnStartRunning`, `OnStopRunning`, `OnUpdate(JobHandle)`, `InitializeRaycast` | Placemaker `Game/Tool/PlacemakerToolSystem.cs:33-39, 94-99, 191-255`; decompiled installed `Game.Tools.ToolBaseSystem` (creation disables the tool and registers it with `ToolSystem`). |
| `m_ToolSystem.activeTool`, `m_DefaultToolSystem`, `applyMode`, `ApplyMode.None` | Same reference `:165-188, 255`; installed `Game.Tools.ToolSystem.activeTool`, and this project's existing `Build/BuildToolSystem.cs`. |
| `m_ToolRaycastSystem.typeMask`, `TypeMask.Net`, `netLayerMask`, `Layer.Road`, `raycastFlags`, `RaycastFlags.SubElements` | Same reference `:226-246`. |
| `GetRaycastResult(out Entity, out RaycastHit)`, `RaycastHit.m_HitPosition` | Placemaker `Game/Tool/PlacemakerToolSystem.Input.cs:46-51`; installed `ToolBaseSystem.GetRaycastResult`. |
| `applyAction`, `secondaryApplyAction`, `cancelAction`; `IProxyAction.shouldBeEnabled`, `WasPressedThisFrame`, `IsPressed` | Same input source `:57-64, 93-101, 136-144`; installed `ToolBaseSystem` default action initialization/getters. |
| `InputManager.instance`, `controlOverWorld`, `hasInputFieldFocus`; `ProxyAction.WasPerformedThisFrame`; settings `GetAction` | Same input source `:40-44, 107-110, 129-134, 149-155`; existing spike's hotkey implementation. |
| `Mouse.current`, `leftButton/rightButton.wasPressedThisFrame`, `rightButton.isPressed`, `delta.ReadValue`; `Keyboard.current`, left/right Shift `isPressed`, `escapeKey.wasPressedThisFrame` | Same input source `:41-42, 54-65`; `get_escapeKey`, `get_current`, `ReadValue`, `WasPressedThisFrame` verified by binary `rg -a -o` in installed `Unity.InputSystem.dll`. That DLL exists; no legacy-input reference is needed. |
| `World.GetOrCreateSystemManaged<T>`, `GameSystemBase`, `Enabled`, `OnGamePreload(Purpose, GameMode)` | Placemaker tool/overlay creation; existing `Build/TerrainBuildSystem.cs` preload override; installed `Game.Tools.ToolSystem.OnGamePreload`. `GetOrCreateSystemManaged` verified in `Unity.Entities.dll`. |
| `Entity.Null`, `Index`, `Version`; `EntityManager.Exists`, `HasComponent`, `GetComponentData`, `HasBuffer`, `GetBuffer`, `CreateEntityQuery`; `ComponentType.ReadOnly/Exclude`; query `ToEntityArray`/disposal, `Allocator.Temp` | Original `Spike/SelectedStreet.cs`; Placemaker `Game/Suite/SuiteLab.FenceMap.cs:99-107`, `Game/Selection/EntityIds.cs:9-15`; query members verified by binary `rg -a -o` in installed `Unity.Entities.dll`. |
| `Road`; `Edge.m_Start/m_End`; `Curve.m_Bezier/m_Length`; `Aggregated.m_Aggregate`; `AggregateElement.m_Edge`; `Composition.m_Edge`; `NetCompositionData.m_Width`; `Temp`, `Deleted` | Original street capture; Placemaker `Game/Preview/SiteCapture.cs:230-255` and `Game/Suite/SuiteLab.FenceMap.cs:99-107`. |
| `NetCompositionData.m_Flags.m_General`; `CompositionFlags.General.Elevated/Tunnel` | Placemaker `Game/Preview/SiteCapture.cs:243-250`. |
| `NameSystem.GetRenderedLabelName(Entity)` | Placemaker `Game/Suite/SuiteLab.FenceMap.cs:95-107`; binary `rg -a -o GetRenderedLabelName Game.dll`; installed decompiled `Game.UI.NameSystem` confirms public string return. Failure logs and falls back to empty name. |
| `TerrainSystem.GetHeightData`, `TerrainUtils.SampleHeight(ref data, float3)` | Placemaker `Game/Preview/SiteCapture.cs:153-163`. No terrain writes in snapshot or preview. |
| `WaterSystem.GetSurfacesData(out dependencies)`, `WaterUtils.SampleDepth(ref data, float3)`, `JobHandle.Complete` | Same source `:165-185`; snapshot errors abort planning instead of assuming dry ground. |
| `Game.Buildings.Building`, `Game.Objects.Transform.m_Position/m_Rotation`, `PrefabRef.m_Prefab`, `BuildingData.m_LotSize`, `ObjectUtils.CalculateBaseCorners(float3, quaternion, float2)` and quad `a/b/c/d` | Placemaker `Game/Selection/BuildingFootprints.cs:23-38`; installed decompiled `Game.Prefabs.BuildingData` confirms `int2 m_LotSize`; decompiled `Game.Objects.ObjectUtils` confirms the rotated-corner overload. |
| `Bezier4x3` construction/`a/b/c/d`, `MathUtils.Position`, `MathUtils.Bounds`, `Line3.Segment` | Original capture; Placemaker `Game/Preview/SiteCapture.cs:233`, `Game/Suite/SuiteLab.FenceMap.cs:109`, `Game/Overlay/PlacemakerOverlaySystem.cs:625`. |
| `OverlayRenderSystem.GetBuffer(out JobHandle)`, buffer `DrawLine(Color, Color, float, StyleFlags, Line3.Segment, float, float2)`, `StyleFlags.Projected` | Placemaker overlay `:82-134, 625`; decompiled installed `Game.Rendering.OverlayRenderSystem` confirms overload, buffer completion and projected-list selection. Draws on main thread after completing dependencies; no scheduled writer needs registration. |
| `UnityEngine.Color` constructor/`white`/`gray`, `UnityEngine.Time.realtimeSinceStartup`, `[Preserve]` | Placemaker overlay palette and input `:43`; binary `rg -a -o` confirms `get_white`, `get_gray`, `get_realtimeSinceStartup`, `PreserveAttribute` in installed `UnityEngine.CoreModule.dll`. |
| `UpdateSystem.UpdateAt<T>`, `SystemUpdatePhase.ToolUpdate/Rendering` | Placemaker `Game/Tool/ToolRegistration.cs:43-48`; existing mod registration. Each W2 registration has its own catch. |
| Existing settings attributes, `GetOptionGroupLocaleID`, `GetEnumValueLocaleID`, label/binding locale methods, `MemorySource` | Existing settings file; Placemaker `Game/Tool/ToolRegistration.cs:77-111` (group locale at `:87`). Property/action/group identifiers are unchanged. |
| `Mod.Logger.Info/Error` and the existing terrain/zoning build APIs | Existing `Mod.cs` and `Build/*.cs`, reused directly. No new external logger or build mechanism. |

The pure helpers use only the Core project, standard collections and Unity.Mathematics. No `SystemAPI`, `IJobEntity`, aspects, source generators, or new NuGet dependencies were introduced.

## Exact tool log templates

Fields in braces are runtime values; numeric format suffixes match the source. The underlying terrain, zoning and W4 systems also write their own existing diagnostic lines.

```text
Stepped Streets: nothing is selected. Click a street with the select tool first.
Stepped Streets: the selection ({selected}) is neither a road edge nor a street. Click the road itself with the select tool.
Stepped Streets: the selected street ({selected}) has no road edges to step.
Stepped Streets: street of {order.Count} edges ({edges.Count} in its aggregate), {run:0.0} m long, {rise:0.00} m rise, grade {grade:0.0}%, half width {street.HalfWidth:0.0} m, {street.Centreline.Count} points
Stepped Streets: plan: {summary.LotCount} pads, {plan.Steps.Count} steps of {summary.LowestStep:0.00}-{summary.TallestStep:0.00} m, {summary.EmptyCount} lots left empty, {plan.FlatStretches.Count} flat stretches; settings: side {request.Side}, lots {request.LotWidthCells} cells (narrowest {request.NarrowestWidthCells}) by {request.DepthCells}, minimum grade {settings.MinimumGradePercent}%, highest step {request.HighestStep:0.0} m, bank grade {request.BankGrade:0.00}, look {request.Look}, earthwork limit {request.EarthworkLimit:0.0} m; capture and plan {milliseconds:0.0} ms
Stepped Streets: {each plan.Messages entry, verbatim}
Stepped Streets: still building the last street; wait until it is done.
Stepped Streets: terrain: {patches.Count} patches ({levelPatchCount} level pads, {slopedPatches} sloped banks), bank width {bankWidth:0.0} m
Stepped Streets: terrain: {terrainReport.Summary}
Stepped Streets: The ground did not reach the planned height. Nothing was zoned; ground changes already made remain.
Stepped Streets: pad {pad.Side} {pad.Start:0}-{pad.End:0} m along, height {pad.Height:0.00} m: {cells.Count} of {pad.WidthCells * pad.DepthCells} cells level, keeps {kept} of {pad.WidthCells} columns for zoning{warning}
Stepped Streets: zoning: no cell came out level, nothing to zone.
Stepped Streets: zoning: {levelCells} level cells in {quads.Count} runs as {zoneName}
Stepped Streets: zoning: {zoneReport.Summary}
Stepped Streets: walls: {wallReport.Summary}
Stepped Streets: Built {buildPlan.Pads.Count} pads: {levelCells} level cells, {zoneReport.Zoned}/{zoneReport.Requested} zoning runs verified, {terrainReport.Curtains} curtains, {wallPieces} wall pieces
```

`warning` is empty or exactly `; under two columns, so the game will wipe this pad's zoning`. The no-level-cell path passes `No cells came out level, nothing to zone.` as the zoning report summary. Low-wall summaries are printed only for the Low wall look. Report summaries already contain their system prefixes; the tool preserves those verbatim, as the spike did. The first two preserved selection failure lines still mention the old select tool.

Errors include the exception and one of these exact messages. Frame/raycast/name errors share a five-second limit; overlay and hotkey updates have separate five-second limits.

```text
Stepped Streets: the street picker failed to register
Stepped Streets: the street picker hotkey failed to register
Stepped Streets: the street preview failed to register
Stepped Streets: The street picker could not be created.
Stepped Streets: The street picker could not open.
Stepped Streets: The street picker could not close.
Stepped Streets: The street selection could not be cleared.
Stepped Streets: The street could not be planned with these settings.
Stepped Streets: The street picker could not start.
Stepped Streets: The street picker could not stop cleanly.
Stepped Streets: setting up the road raycast failed
Stepped Streets: street picker update failed
Stepped Streets: reading the street name failed
Stepped Streets: taking the site snapshot failed
Stepped Streets: The street picker could not reset for the next city.
Stepped Streets: The street could not be built. Ground changes already made are permanent.
Stepped Streets: Zoning the level cells failed. The ground changes remain.
Stepped Streets: Building the low walls failed. The ground and zoning remain.
Stepped Streets: The build finished, but its result could not be read. Check the log.
Stepped Streets: the street preview failed to start
Stepped Streets: drawing the street preview failed
Stepped Streets: street picker hotkey failed
Stepped Streets: removing the street picker hotkey failed
```

`That street doesn't join the selection` and `The site changed. Review the updated preview before building.` are panel messages in `State.Messages`, not logger lines.
