# Placemaker study notes

*For the lead and the wave-2 workers: read this instead of Placemaker's 978 C# files.*

- **Code studied:** Placemaker by jackwrichards, GPL-3.0, commit `e654b32` (1 November 2026). The read-only copy is in
  `reference/placemaker` of the main checkout (git-ignored).
- **Line numbers:** every `path:line` below is relative to the Placemaker root and was read from the file, not recalled.
- **Verbs** (the PRD's terms): **Copy** means take the file, keep its header and add the "Modified by" line (rule 3).
  **Adapt** means take parts and change them. **Study** means learn the idea and copy no code. **Write your own** means
  use it only as a pattern.
- **Rule 3:** the stepped-lot planner is original work. Section 6 describes LotLevel and PolishLookLayer in prose only,
  and quotes no code from them.

## What matters most (read this first)

1. **Placemaker has given up on quay walls.** On 19 October (commit `875dbb84`) it took the retaining walls (the game's
   quays) out of road terraces. A saved "retaining" choice now builds a bank (`Core/Layers/Roads/Terraces/TerraceOptions.cs:72-73`).
   The test explains why: the user said "they never work" (`Tests/Placemaker.Tests/RoadsBlockTerraceTests.cs:205`). The
   Zoning tool still has walls, but they are off by default (`Core/Layers/Types/Zoning/PolishLookLayer.cs:189`). Step
   option C and the PRD's R7 wall depend on this mechanism.
2. **Placemaker's road plans no longer zone at all** (`Core/Layers/Roads/RoadPreZone.cs:26`, `Offered = false`, since
   26 October). The PRD's "its frontage is zoned only where it happens to lie level" is out of date. All zoning,
   including "Level the lots", lives in the Zoning category (`PolishLookLayer`).
3. **Copying Sculpt is bigger than "a few Core/Geometry helpers".** `Sculpt.Emit` takes a `LayerContext`, which pulls in
   all of `Core/Contracts.cs`. `Poly.Clean` pulls in the polygon-boolean engine (`Core/Geometry/Arrangement.cs`,
   1,006 lines). Section 9 lists the exact members and a cut that keeps the port to about four small files.
4. **Applying zoning needs an active tool.** Only the active tool's `applyMode` is honoured, so Placemaker builds through
   its own invisible `Game/Build/BuildToolSystem.cs`. That file is not on the PRD's list, but the zoning call in item 11
   does not work without something like it.
5. **The csproj installs into the game's Mods folder by default** (`Placemaker.csproj:96-105`) unless you pass
   `-p:Deploy=false`. That breaks the owner's rule 1 the first time anyone runs `dotnet build`. Remove that target when
   copying the file.

---

## 1. `AGENTS.md`, `docs/ARCHITECTURE.md`: how the mod is split

**What matters**
- The game version the notes were decompiled against is 1.6.2f1 (`AGENTS.md:3`).
- The three-way split is `Core/` (pure, xUnit on net9), `Game/` (ECS) and `UI/` (React, where `contract.ts` is
  authoritative) (`AGENTS.md:7-9`; the folder map is at `docs/ARCHITECTURE.md:127-148`).
- The code rules are at `docs/ARCHITECTURE.md:154-169`. The ones we inherit:
  - Core is pure: no `Game.*`, `Colossal.*`, `Unity.Entities` or `UnityEngine`. It must be C# 9 and net48-compatible:
    no `init`, no records, no default interface methods (`:156-157`).
  - The planner is deterministic and cancellable. It never throws for bad geometry; it returns issues instead. The
    budget is **< 250 ms for a 5 ha area on one core** (`:158-159`).
  - Control ids and layer ids are save format, so never rename them (`:165`).
  - No Harmony unless unavoidable (`:166`).
  - **No `SystemAPI`, `IJobEntity` or aspects** (`:167`). The standalone build has no Entities source generators, so use
    `EntityQuery`, `ComponentLookup`, `IJobChunk` or main-thread loops.
  - Comments explain why (`:168`).
- The runtime flow is at `docs/ARCHITECTURE.md:181-190`: the tool takes a snapshot (`SiteCapture`), plans on a worker
  where the latest request wins (`:187`), shows a preview, then builds.

**Engine and project facts worth knowing**
- Placemaker's own known bug list (`AGENTS.md:152-163`):
  - Builds freeze for **1.4–2.7 s** (`:154`). Compare our success criterion of no stall over 2 s.
  - The native `TerrainSystem.CullForCascades` crash is guarded, but the fix is "not proven" (`:159-161`).
  - Bumpy-hill builds still show **curtain jumps of about 1 m** for several types (`:162-163`).
- `-p:Deploy=false` builds without installing (`AGENTS.md:119-121`). The full test run takes more than 20 minutes
  (`:127`).
- The game runner and game queue (`AGENTS.md:52-115`) are Placemaker's own tooling and we do not have them. Our "seen in
  the game" step is manual.

**Copy or study:** Study. Copy the Core / Game / UI split and code rules 1, 2, 7, 9 and 10.

## 2. `docs/RESEARCH-NOTES-ADDENDUM.md`: the engine cheat sheet

**Numbering trap:** the file numbers some notes twice. There are two 85s (`:565` decal transparency, `:576` heightmap),
two 86s (`:580` nets, `:595` paint), two 87s (`:584`, `:600`) and two 88s (`:586`, `:603`). When the PRD says "note 85" it
means `:576`.

**Direct terraform (`:196-255`)**
- `TerrainSystem.ApplyBrush(TerraformingType type, Bounds2 area, Brush brush, Texture texture)` is public (`:204`).
- It writes the heightmap (R16) and `m_WorldMapEditable` straight away and does all the bookkeeping itself: area changed,
  CPU readback and water notification (`:205-207`).
- The texture can be any `Texture`. The game maps it onto the square `m_Position ± m_Size/2` (`:208-209`).
- Step size is `clamp(dt, 0.05, 1) · strength · toolStrength / heightScale`. Shift's tool speed is **2000**, Level and
  Slope 4000, Soften 1000. Strengths under 0.002 are bent by `pow(·, 0.055)` (`:211-213`).
- **Shift is linear and additive**, which makes it exactly reversible. Level moves toward one single target, so it cannot
  draw a bank (`:214-215`).
- CPU heights (`GetHeightData(true)`) are current a few frames after the dispatch (`:216-217`).
- What the build does, steps 1–6 (`:224-240`):
  1. A 1.75 m raster with a 4 m bank around it.
  2. One raise mask and one lower mask per tile of at most 1024 texels.
  3. Wait 6 frames, stop within 0.3 m (2 % of cells may be off), at most 4 passes and 6 s.
  4. Calibrate the brush orientation on the first pass.
  5. Fall back to dabs if that fails.
  6. Paint.
- Terraform is **permanent**: removal never restores the ground (`:241-244`).
- Unverified in game: the Shift kernel's exact linearity and which texture channel it samples (`:253-254`).

**Notes 77–79, quays (`:502-522`)**
- A quay is an ordinary net with a `Bridge` in the Quay build style (`:502-504`). Vanilla's 8 m pedestrian quay is
  QuaySmallWide01/02 (`:505-507`).
- The wall side is automatic. A side whose deck-minus-terrain is at least the elevation limit becomes `Raised` and gets
  the concrete wall (`:510-513`).
- Lay the deck at the upper level and the land behind it at deck height, and the wall appears on the low side
  (`:514-517`).
- There is no quay-specific terraform. On a Raised side the upper terrain bound is off (`:518-519`). No maximum wall
  height was found (`:522`).
- **Note 87 (`:584-585`):** quay prefabs are built straight. Emit short straight chords along a curve.
- **Placemaker's later verdict:** "they never work" (`Tests/Placemaker.Tests/RoadsBlockTerraceTests.cs:205`).

**Note 85, the coarse heightmap (`:576-579`)**
- Texels are about 3.5 m (14,336 m / 4096). A jump of "more than ~0.6 m between two texels (steeper than ~50 %)" draws as
  a vertical sheet. The two numbers in that sentence disagree: 0.6 m over 3.5 m is 17 %. The metric uses both
  (section 14).
- Patches that meet at different heights, a flat patch inside a slope, and a field cut off abruptly all make curtains.

**Note 86, nets flatten their own width (`:580-583`)**
- A net on a cross slope leaves a lip, and the game draws a concrete retaining wall where a net is cut into higher ground.
- Every walk must sit level across its width plus about 1.5 m shoulders. This applies to our street: it already exists,
  and the pads meet it at its kerb.

**Other notes a terrain-and-zoning mod needs**
- **Note 41, tool cadence (`:98-105`).** The `ClearTool` and `ApplyTool` phases run only by the **active** tool's
  `applyMode`. Definitions without `Updated` are not consumed. Switching the active tool stops the old one and leaves
  its temporaries until the new tool clears them.
- **Notes 63–66, existing buildings (`:400-423`).**
  - Zoned (spawnable) buildings are "delete when overridden". A new net or physical lot over one makes Apply delete it.
  - A building is **condemned when its cells stop being Visible with its zone** (`:410-414`).
  - Placemaker keeps off whole building lots (`SiteSafety`). R9 needs the same.
- **Notes 67–72, zone cells (`:431-468`).**
  - Cells are blocked only by ground nets with `BlockZone` or by physical or protected areas (`:431-436`).
  - `CleanBlockedCells` (`:443-446`) blocks every cell behind a blocked one, and blocks row 0 when row 1 is blocked. So
    blocking the front cell kills the whole column, and a zoned strip must be at least 2 rows deep.
  - Marquee zoning takes a cell when its **centre** is inside the quad and it is Visible. Without `Overwrite` it takes
    only empty cells (`:447-451`).
  - Cell geometry: row 0 is at the road, 8 m cells, at most 6 rows (`:449-451`).
  - Zoning runs 24 frames after Apply (`:460`).
- **Sculpted terrain (s20, `:531-538`).** Sloped patches carry their target in their vertex heights, and mean-value
  interpolation makes neighbouring quads continuous. `TerrainSurvey` grades slopes as flat < 5 %, gentle < 15 %,
  steep < 45 %, cliff above (`:537`).
- **Paint notes 86–89 (`:595-610`).** One 4-bit weight per material, and strengths under about 1/30 vanish. The map has
  four extra slots; vanilla's are Dry grass, Forest, Light dirt and Sand (`:605-607`). This is useful for a "planted
  bank" look.
- **Note 93 (`:644-650`).** Terrain heights are **not previewed**: writing and restoring them is unsafe because of
  water, re-fitting and replanning. Our preview must be an overlay, not real ground.

**Copy or study:** Study. This is the checklist for every Game-layer change.

## 3. `Mod.cs`, `Game/Tool/ToolRegistration.cs`: entry point and registration

**What matters**
- `Mod.cs:8` declares `public sealed class Mod : IMod`, and `Mod.cs:10` creates one static `ILog`. That is the logger
  the PRD's "log the game version" uses.
- `OnLoad(UpdateSystem)` (`Mod.cs:12`) wraps each area's registration in its own try/catch (`Mod.cs:17-20`, `:29-33`). A
  broken feature is logged and never takes the game down. `OnDispose` (`Mod.cs:23-27`) unregisters.
- `ToolRegistration.Register` (`Game/Tool/ToolRegistration.cs:25`):
  - settings are loaded with `AssetDatabase.global.LoadSettings` and shown in Options (`:29-36`);
  - key bindings must be registered before any `GetAction()` (`:37-38`).
- System registration:
  - `updateSystem.UpdateAt<PlacemakerToolSystem>(SystemUpdatePhase.ToolUpdate)` (`:43`);
  - the UI system at `UIUpdate` (`:44`), tooltips at `UITooltip` (`:45`), overlays at `Rendering` (`:46-48`);
  - `GetOrCreateSystemManaged` to keep a handle (`:50`).
- The options-page localisation ids come from type and property names, so Placemaker maps them to readable keys
  (`:77-111`).
- The build side registers the invisible build tool in the same phase as the tool, with the service just before it
  (`Game/Build/BuildRegistration.cs:42-43`).

**Engine rules**
- Every tool updates in `ToolUpdate`, and `ToolOutputSystem` reads `applyMode` right after
  (`Game/Build/BuildToolSystem.cs:10`).

**Copy or study:** Study, then write your own small version (an entry point and one registration with the per-area
try/catch).

## 4. `Game/Tool/PlacemakerToolSystem.cs` (+ `.Input.cs`, `.Points.cs`): a custom tool

**What matters**
- `PlacemakerToolSystem : ToolBaseSystem` (`PlacemakerToolSystem.cs:33`) overrides `toolID`, returns `null` from
  `GetPrefab` and refuses `TrySetPrefab` (`:37-39`).
- Opening and closing the tool:
  - `Toggle()` opens it with `m_ToolSystem.activeTool = this` (`:165-179`);
  - `Close()` hands back with `m_ToolSystem.activeTool = m_DefaultToolSystem` (`:181-188`);
  - `OnStartRunning` and `OnStopRunning` turn the actions on and off (`:191-224`).
- **Raycast against roads:** `InitializeRaycast()` (`:226-246`) uses `typeMask = TypeMask.Net`,
  `netLayerMask = Layer.Road` and `raycastFlags |= RaycastFlags.SubElements` (`:239-241`). Otherwise it uses
  `TypeMask.Terrain` (`:244-245`). This is the street picker's whole raycast setup.
- `OnUpdate` (`:251-304`) sets `applyMode = ApplyMode.None` (`:255`); the tool never applies. It also catches every
  exception, logs it (rate-limited) and keeps going (`:294-299`).
- `ReadInput()` (`PlacemakerToolSystem.Input.cs:37-67`):
  - `GetRaycastResult(out Entity e, out RaycastHit hit)` gives the hovered entity (`:46`);
  - Ctrl, Shift and Alt are read from `Keyboard.current` (`:54-56`);
  - clicks are the game's actions **or** the raw mouse, because CS2 masks unmodified ProxyActions while a modifier is
    held (`:57-64`). Shift-click "add the next segment" needs this.
- Right-click versus camera drag: it counts as a click only if the mouse moved under 8 px within 0.5 s (`.Input.cs:78-91`).
  Esc steps back, and right-click does not count as Esc (`.Input.cs:120-126`).
- `RoadNodeAt` (`PlacemakerToolSystem.Points.cs:32-67`) queries the road network ±40 m and walks a node's
  `ConnectedEdge` buffer, reading each `Edge`'s start and end. This is a pattern for "the next segment of the same
  street". The routing that follows (`:69-146`) is not needed.

**Engine rules**
- Only the active tool's `applyMode` is honoured (note 41). If your picker sets Clear every frame, it wipes temporaries.
- The selection mode handlers (`UpdatePerimeter` and the others) live in partials not on the list. The three files above
  are enough for a picker.

**Copy or study:** Study, then write your own street picker. Use the raycast setup, the input pattern and the try/catch
frame.

## 5. `Game/Preview/SiteCapture.cs`: the main-thread snapshot

**What matters**
- The header (`:1-7`): workers must never touch ECS or terrain data, so everything is copied into plain arrays.
- `Capture(AreaShape)` (`:91-111`) gathers heights, water, nets, obstacles and zone blocks with a margin of 60 m (`:30`).
- `HeightGrid` (`:153-163`) samples `TerrainUtils.SampleHeight(ref data, …)` from `_terrain.GetHeightData()` onto a grid
  of **max(4 m, extent / 256)** cells (`:155`, `MaxGrid = 256` at `:31`) and returns a bilinear sampler (`:193-204`).
  The planner therefore sees the hill at 4 m resolution or coarser.
- `WaterGrid` (`:165-191`) samples depth on 8 m cells from `_water.GetSurfacesData`. It is needed for R9 ("never touches
  water").
- `CaptureNets` (`:217-274`):
  - walks the net search tree; keeps entities with `Edge` and `Curve`, skipping `Temp` and `Deleted`;
  - takes the Bézier from `Curve.m_Bezier` and the width from `NetCompositionData.m_Width`;
  - **skips elevated and tunnel pieces** (`:250`);
  - marks a road by its `Road` component and reads `RoadData` flags (`:254-266`).
- `CaptureZoneBlocks` (`:318-369`) walks the zone search tree and reads each `Block`'s position, direction and size,
  plus its `Cell` buffer's Visible and Blocked bits (`:338-353`). This is what the zoning step needs.
- `CaptureObstacles` (`:371`) records building lots, so the planner keeps off them (R9).

**Copy or study:** Adapt. Take `HeightGrid` + `Bilinear`, `WaterGrid`, a roads-only `CaptureNets`, `CaptureZoneBlocks`
and `CaptureObstacles`. Drop assets, terrain palette, bike lanes and caches.

## 6. `Core/Layers/Types/Zoning/LotLevel.cs`, `PolishLookLayer.cs`: how Placemaker levels lots today

*Prose only. No code from these two files may go into the planner (rule 3).*

### How the Zoning tool lays out lots (`PolishLookLayer.PlanZoning`, `PolishLookLayer.cs:261-452`)

- **Zone settings.** Up to three zones can be mixed by share of the land; whatever the shares leave stays empty. Each
  zone has a width range and a depth range in 8 m cells. The usual sizes are at `PolishLookLayer.cs:76-86`: Residential
  Low is 2–3 wide and 2–4 deep.
- **How deep each column can go.** For each zone block in the snapshot, every column is checked from the road back. It
  stops at the first cell that is closed, outside the area, or already taken by an earlier block (`:322-334`).
- **Placing lots.** Along the block, it takes each run of usable columns (a lot never crosses a gap). For each lot it:
  - picks a zone among those allowed on that road width whose smallest lot fits;
  - picks a width at random within the zone's range, and narrows it while the land behind is not deep enough for the
    zone's smallest depth (`:353-379`);
  - deals with a leftover too narrow for any zone: either the lot absorbs it, or the lot gives up columns so the
    leftover becomes a lot of its own (`:382-388`);
  - picks a depth at random within the range, capped by the land behind (`:392-393`).
- **When "Level the lots" is on** (`levelLots`, default on, `:186`), each lot also gets:
  - a **front height for every column** (`fronts`, `:336`, worked out by `LotLevel.Fronts`, `LotLevel.cs:74-94`). It is
    the ground 1 m inside the lot's front edge, averaged with the two neighbouring columns at half weight. That is the
    ground where the lot meets the kerb, not the road net's own height.
  - a **depth limited by the earthwork setting** (`LotLevel.Depth`, `:96-128`). Row by row from the road, it measures the
    worst gap between the ground and the column's front height at five points per cell (the centre and 3.5 m each way).
    It keeps rows while that gap stays within the limit. If not even the zone's smallest depth fits, the lot is **left
    empty** and counted as "too steep" (`PolishLookLayer.cs:398-399`).
  - a "needs levelling" flag. A lot whose worst gap is under 0.2 m is left alone (`LotLevel.cs:29-30`).
- **When cells are zoned.** Levelled lots are not zoned yet; their cells are kept until the ground is laid
  (`PolishLookLayer.cs:414-415`). Afterwards only the rows and columns the lot still has are zoned, at the new ground's
  height (`:433-442`).

### The settings (`PolishLookLayer.cs:186-189`)

| Control | Default | Range |
|---|---|---|
| `levelLots` | on | |
| `levelEarth` (earthwork limit) | 4 m | 1–8 m, steps of 0.5 |
| `levelBank` (steepest bank) | 40 % | 20–50 % |
| `levelWalls` | **off** | |

`LotLevel.Options` has its own defaults: earth 4 m, bank 0.4 and walls on (`LotLevel.cs:64-72`). The panel's off
overrides the walls default (`PolishLookLayer.cs:427`).

### How the ground is laid (`LotLevel.Build`, `LotLevel.cs:134-241`)

1. **Fillers** (`:243-304`; the `Lot.Filler` flag is at `:49-50`).
   - **Where:** wherever two levelled lots on the same street front line (parallel, fronts within 1.5 m) have a gap of
     0.2–20 m between them (`:277-283`).
   - **What:** the gap becomes a "filler" strip as deep as the shallower of the two lots. Its ground **ramps** along the
     street from one lot's edge height to the other's (`:286-300`). It is never zoned.
   - **Why:** there is no ridge or trench of natural ground between two pads. This is the closest existing thing to step
     option A, but Placemaker does not make gaps on purpose: they come from the empty share and the leftovers.
2. **Walls** (`PlanWalls`, `:623-724`; only when walls are on).
   - **Sampling:** along each lot's back edge, every 2 m (`:671`), it compares the pad height just inside the edge with
     the ground behind it: the lot behind (back to back), or the hillside 6 m back (`:674-678`).
   - **Where a wall stands:** the rise must be at least `BlockField.MinWall` (2.6 m), at most twice the earthwork limit
     plus 1 m, and clear of buildings and roads (`:680-681`). The wall is a wide quay whose centre line lies 4 m behind
     the edge (`BlockField.QuayHalf`, `:635`).
   - **Merging:** pieces are merged along the street, and walls shorter than 16 m are dropped (`:695-702`).
   - **Cost:** lots behind give up the rows under the quay, and become empty if that leaves them under their smallest
     depth (`:715-721`).
3. **Interfaces** (`:726-805`).
   - **Where:** wherever a lot's back or side meets a *higher* neighbouring lot by more than 0.6 m, outside a wall
     stretch (`:771`).
   - **What:** the lower lot gives up back rows (or side columns) wide enough for a bank at the bank slope, while staying
     at least its zone's smallest depth and width (`:777-804`).
   - **Why:** a lot shrinks rather than vanishing.
4. **The field** (`Field`, `:394-516`). This is one 2 m sculpt grid over all the levelled lots plus a margin of
   *earthwork ÷ bank × 1.3 + 6 m* (`:400`, `:418`). The bank slope is clamped to 10–46 % (`:399`).
   - Road nodes are locked, out to the road's half width plus 0.5 m (`:419-440`).
   - Every node inside a lot (grown 0.5 m) is set to the lot's pad height and protected (`:445-462`). The pad height at a
     point interpolates the column fronts across the lot's width and is constant from front to back (`:825-838`).
   - Around each wall, the two sides are held at the lower pad and the upper level (`:463-507`).
5. **Settle** (`:563-602`).
   - First the bank pass: every free node is held within the bank slope of its eight neighbours, inside the earthwork
     box (`TerraceField.LimitBanks`).
   - Where two protected levels still meet too steeply, only the pad nodes right at the step are released, one ring per
     round, for up to 40 rounds.
   - Then every node is clamped to ±earthwork (`:511-512`) and the field is settled again.
   - This differs from the road terraces, which release wide areas. A lot keeps every row its bank does not take.
6. **Wall check** (`:164-184`). Walls whose face fell under the keep height after the limits are dropped and laid again
   as banks (`BlockField.Verify`).
7. **Fit** (`:309-351`). Each lot keeps the **largest rectangle** of its cells, from the road, that came out level and is
   at least its zone's smallest width and depth. Ties go to the lot's original position. Otherwise the lot becomes
   empty. A cell counts as level (`:353-369`) when:
   - 2.5 m each way from the road, its height is within about **0.1 m** (3 % of 2.5 m plus 0.03 m);
   - 2.5 m each way along the road, it is within the **road's own grade plus 4 %** (plus 0.06 m).
8. **Loop and emit.** Steps 4–7 repeat up to four times while lots keep shrinking (`:156-195`), so the bank behind a
   shortened lot is laid again. Then everything is emitted once:
   - sloped terraform quads with a two-cell skirt (`:200`);
   - quays at their deck heights (`:203-211`);
   - banks painted with the terraces' dry grass and dirt, except under quays (`:221-238`).

### Why it works this way, and what it leaves for us

- **Per-column fronts** let every lot meet its road all along its frontage, so the driveway works without a separate
  apron. The cost is that the pad slopes sideways with the street. On a 12 % street a 3-cell lot tilts 2.9 m across its
  width, and the levelness test even allows the road's grade plus 4 % along the road. That is exactly the "houses still
  make their own steps" gap the PRD describes.
- **One continuous field emitted once** avoids the curtains of separate flat patches (note 85). The bank limit is held
  over eight directions at slope ÷ 1.08 so the true gradient stays under the setting (`TerraceField.cs:452-453`). It is
  capped at 46 % because about 50 % over one texel tears (`TerraceOptions.cs:93-95`).
- **Shortening before emptying** keeps zoned frontage. The earthwork cap stops giant cuts. Walls are off by default
  because quays proved unreliable.
- **For the new planner:** keep the ideas, write the code yourself. The ideas are one height per pad (not per column),
  steps between neighbours sized by grade × width, lots narrowed or emptied with a reason, banks held under 46 %, one
  continuous field, and zoning only the cells that came out level.

**Copy or study:** Study closely. Copy nothing from these two files.

## 7. `Core/Layers/Roads/Terraces/BlockField.cs`, `TerraceField.cs`, `TerraceOptions.cs`, `docs/proposals/s52.md`

There are two terrain engines here, and the PRD mixes them up:
- **Hillside terraces** (`TerraceField`, s52, added 15 October in commit `9ba07eb6`) is a Roads *pattern*. It makes new
  contour streets, each on a level terrace.
- **Terraced blocks** (`BlockField`, added 19 October in commit `471ec0d7`) is the ground for *every* road pattern. The
  6 % rule lives here.

**BlockField** (`BlockField.cs:1-24` header)
- Each zoning street is a band source: road plus zone cells plus shoulder on each side, level across the street, tilted
  by the cross-fall.
- Bands stop short of the street behind by the width the height step needs at the bank slope (`:231-287`).
- Constants:
  - `QuayHalf = 4f`, half of the 8 m quay (`:62`);
  - **`ClimbGrade = 0.06f`** (`:63-68`);
  - **`MinWall = 2.6f`** (`:69-70`); its summary says a pathway's side is Raised from 2 m, plus a margin for the
    heightmap;
  - `KeepWall = 2.2f`, the lowest face kept after the limits (`:71-72`);
  - `MinWallLength = 16f` (`:73-74`).
- The climb test (`:208-212`) measures a street's grade **over 30 m on the smoothed survey ground**, which is blurred at
  12 m or more (`:124`). Steeper than 6 % and the segment becomes a corridor (a ramp through the terraces) with no band
  of its own (`:212-221`).
- The survey counts a site as sloped when at least 15 % of it is steeper than the flat threshold and its relief is at
  least 2.5 m (`:144`).
- Walls exist only if `o.Retaining` (`:190`), and that is now always false (`TerraceOptions.cs:72-73`).
- `Verify` (`:675-717`), `Decks` (`:720-738`) and `AddQuays` (`:765-783`) are what `LotLevel` reuses for its walls.

**TerraceField** (`TerraceField.cs:1-21` header)
- The ground is one field on a 2 m grid (`:153`).
- Each terrace street's band is a level plateau. Past the band, the edge eases back into the hillside over
  *max(4 m, fringe × |Δh| ÷ bank)*. The fringe is 1.5 for a rounded bank and 1.1 for a straight wall (`:163-164`,
  `:221-226`).
- Sources are blended with weights 1/x² (rounded) or 1/x (wall) (`:228-231`).
- Flat ground is gated out between 60 % and 100 % of the flat threshold (`:184`).
- **`Resolve`** (`:338-444`) is the clash rule: where protected ground clashes, the lesser protection gives way (a
  terrace before a walk, a walk before a road, any of them before locked ground). It runs up to 8 rounds, with a
  release radius that grows each round (`:356`, `:392`).
- **`LimitBanks`** (`:446-503`) holds every free node within slope ÷ 1.08 of its eight neighbours (`:452-453`), inside the
  earthwork box (`:474-475`). It runs up to 100 sweeps and stops once nothing moves more than 5 mm (`:460`, `:500`).
- After the limits: change dies out towards the border at 0.9 × bank slope (`CapToBorder`, `:293`), then the earthwork
  clamp (`:294-300`).

**TerraceOptions** defaults
- Cells 4 (`:15`), cross-fall 1 % (`:17`), bank 0.4 (`:23`), flat 5 % (`:28`), earthwork 8 m (`:30`), steepest car road
  10 % (`:32`).
- Shoulder 2 m (`:44`), landing 12 m (`:46`), steepest walk 7.5 % ("vanilla has no stairs", `:47-48`).
- `MeanBank = BankSlope / 1.15` for a rounded bank, because its steepest point is about 1.15× its mean (`:58-62`).
- **The bank is capped at 0.46** with the comment "CS2 draws a jump steeper than ~50 % over one 3.5 m heightmap texel as a
  torn vertical sheet" (`:93-95`).

**s52.md**
- A band is road half + cells × 8 + 2 m (`:17-20`).
- **Spacing from bank physics** (`:22-29`). On ground of slope g, streets S apart step g·S. The two bands take W, and the
  bank's mean slope is g·S ÷ (S − W). Holding that at a gives **S = W·a ÷ (a − g)**.
- Zoning on terraces covers only the level rows: at most the cross-fall plus 2.5 % (`:89-93`). Roads no longer zone
  (section 15), so this paragraph is history.
- Known limits: no stairs in vanilla (`:177-179`).

**What it means for Stepped Streets**
- The same spacing idea fits our steps. A pad of width L on grade g steps g·L. A bank strip of width B between pads holds
  the step when g·L ≤ a·B, so the strip a step needs is B ≥ g·L ÷ a (my arithmetic from `:22-26`). At a = 0.4, a 12 %
  street with 3-cell lots (step 2.88 m) needs a bank about 7.2 m wide, which is almost one zone column.

**Copy or study:** Study the maths and reuse the ideas. Whether `LimitBanks` and `Resolve` may be copied is an open
question (see the end); rule 3 bans only LotLevel and PolishLookLayer.

## 8. `Core/Layers/Parking/LotGrading.cs`, `LotHill.cs`: level tiers with foot walls (step option B)

**LotGrading**
- The header (`:1-21`): each tier of parking rows is one plane, level across within 2 %. Medians between tiers are planted
  banks a metre off both kerbs. The terraced style adds a masonry wall at the foot of each bank.
- Limits (`:53-71`):

  | Constant | Value | Line |
  |---|---|---|
  | `MaxFall` | 0.02 | `:54` |
  | `MaxRowGrade` | 0.05 | `:56` |
  | `MaxRamp` | 0.08 | `:58` |
  | `MaxBank` | 0.4 (the comment says "1 : 2.5"; the header at `:14` still says 1 : 2) | `:60` |
  | `Tolerance` | 0.15 m: below this the lot keeps its ground | `:71` |

- **`FootWall = 0.8`** (`:102-103`): the step the masonry wall takes.
- **`FootClear = 2.5, FootSmear = 1.2`** (`:104-107`). FootClear is the wall line's distance off the lower tier's kerb.
  FootSmear is how far **behind** the wall the terrain steps up, because "the game's 3.5 m height raster smears a step
  over a texel". A step at the wall itself raised the ground in front of it and buried the wall. **1.2 m is a distance,
  not a height.**
- `HasFootWall(gap)`: a wall is used only when the median is at least FootClear + FootSmear + BankClear + 1 = **5.7 m**
  (`:93`).
- `BankLines`: the foot distance is *min(2.5 m, 0.35 × gap)* (`:95-100`), so it is the full 2.5 m only on medians 7.1 m
  or wider.
- `BankCap(gap)`: the step a median can take is *0.4 × (gap − clearances − smear) + 0.8 m* (`:84-89`).
- `Bank()` (`:359-389`) makes the median's profile: level, then the wall, the smear and a straight bank. The walls are
  emitted at `:429-441`; none is made for steps under 0.3 m or runs of 3 m or less (`:436`).
- **What the wall actually is** (`Core/Layers/Parking/PlanBuilder.cs:548`): one course of the game's garden wall prop,
  `EU_Wall08` with `EU_Wall01` as fallback (`:577`). It is 0.9 m tall, and "**the game does not scale these props**": a
  scaled piece came out 3 m long and 0.9 m tall with gaps (`:556`). A taller stone wall cannot be made by scaling this
  prop.

**LotHill**
- Rows turn along the contours from 4.5 % (`:25`), and medians widen into banks from 3 % (`:27`).
- **`MaxBank = 0.4`** with the comment: "at 50 % the game's 3.5 m height raster draws a bank as a stretched sheet (…
  probe: 78 sheets on a 15 % slope at 1 : 2)" (`:30-32`). This is the evidence that 50 % is too steep in practice, not
  just in theory.
- `BankClear = 1.0` (`:34`).
- `MedianFor` (`:101-112`) is the median a step needs, with the formula in its comment (`:105-106`).

**Copy or study:** Study. Option B's numbers come from here, but the wall is a fixed 0.9 m prop.

## 9. `Core/Layers/Common/Sculpt.cs`: height field to sloped patches

**What it does**
- It is a node grid of target heights (`Base`, `Target`, `Free`; `:37-45`). The default cell is **4 m** (`:32`); callers
  pass 2 m (`TerraceField.cs:153`, `BlockField.cs:307`, `LotLevel.cs:418`).
- **Above 60,000 nodes the cell grows by ×1.2 until it fits** (`:35`, `:60`). At 2 m that is about 490 m × 490 m. A
  500 m street plus its margin fits easily.
- Nodes outside the sculptable area are locked at the original ground (`:73`).
- Shaping helpers:
  - `Shape` (`:135`), `Smooth` (`:150`) and `LimitGrade` (`:175-203`);
  - `Pad` (`:209-224`), `PadDisc` (`:227`) and `Corridor` (`:242`);
  - `FadeOut` (`:274-289`), `CapToBorder` (`:295-304`) and `LockedDistance` (`:307-333`);
  - `Measure` (`:336-348`) and `LimitEarthwork` (`:355-385`).
- `Emit` (`:414-525`) turns every cell whose corners move at least **0.12 m** (`MinChange`, `:34`) into a **sloped quad**
  carrying its four corner heights. It adds a skirt of near-zero cells around the change, so the build raster's 4 m bank
  starts on original ground (`:419-445`).
- `HeightAfter` (`:597-617`) is the same mean-value surface the build rasters (`TerraformSurface.At`), so the planned
  height equals the built height.
- **Sculpt does not prevent curtains by itself.** It emits whatever targets it is given. The bank limit (`LimitGrade`,
  or `TerraceField.LimitBanks`) is what keeps steps drawable.

**a. Exactly what the port needs.** These are the external types and members Sculpt, TerraformRaster, TerraformSurface
and TerrainMetrics use, followed transitively.

| Needed by | Type / member | File:line | Pulls in | Verdict |
|---|---|---|---|---|
| Sculpt `Emit`, `Add`, `Warn`, `Over`, `Resnap` | `LayerContext` (`H`, `Plan`, `Ground`, `G`, `P3`, `Check`, `Issue`) | `Core/Layers/Common/LayerBase.cs:144` (`H` `:155`, `Ground` `:163`, `G` `:166`, `P3` `:168`, `Check` `:199`, `Issue` `:201`) | `AreaInput`, `AreaPlan`, `SettingsBag`, `SiteContext`, `TerrainField`, `ControlSchema`, `PlannedObject`, `PlannedNet`… (most of `Core/Contracts.cs`, 1,044 lines) | **Cut.** Give `Emit` a cancellation token and a `List<patch>` sink instead |
| Sculpt `Emit` | `ctx.Ground.Override` → `TerrainField.Override` | `Core/Layers/Common/TerrainField.cs:44` | `TerrainField` | **Cut** (the planner keeps `HeightAfter` itself) |
| Sculpt `Emit`, `Warn` | `TerrainResult`; `TerrainShaper.PermanentKey`; `ctx.Plan.Facts`, `Issues`, `Terraform` | `Core/Layers/Common/TerrainShaper.cs:75`, `:87` (Sculpt `:517`, `:521-523`, `:589`, `:651-655`) | `TerrainShaper` (planner code) | **Cut.** Return the cell count and offsets; the warning text is ours |
| Sculpt `Over` | `LayerRegion` | `Core/Layers/Common/LayerRegion.cs:12` | layer regions | **Cut** (use the constructor) |
| Sculpt `GradeAt` | `TerrainSurvey.Grade` | `Core/Layers/Common/TerrainSurvey.cs:108` | `TerrainSurvey` | **Cut** (unused by the cut-down field) |
| Sculpt `Resnap` | `plan.Objects`, `Surfaces`, `Areas`, `Nets` | `Sculpt.cs:623-636` | contract types | **Cut** |
| Sculpt `Add` | `PlannedTerraform` (`Ring`, `Kind`, `TargetHeight`, `Permanent`, `Sloped`), `TerraformKind` | `Core/Contracts.cs:851`, `:849` | nothing more | Keep, or replace with a small patch struct (ring of float3 plus raise/lower) |
| Sculpt `Add`, `HeightAfter` | `TerraformSurface.At` | `Core/Build/TerraformSurface.cs:26-63` | nothing | **Keep** |
| Sculpt `Pad` | `Poly.Bounds` | `Core/Geometry/Poly.cs:243`, `:251` | nothing | Keep (or drop `Pad`) |
| Sculpt `Pad`, `Over`, `HeightAfter` (clips only) | `Poly.Contains` → `ContainsD` | `Poly.cs:174`, `:176` → `:179-190`, `:192-204` | nothing | Keep |
| Sculpt `Pad` | `Poly.DistanceToBoundary` | `Poly.cs:257-263` | `ReservedSpace.DistSqToSegment` (`Core/Contracts.cs:363`) | Keep, with the 5-line helper moved in |
| Sculpt `Emit` (clip path only) | `Poly.Clean` | `Poly.cs:434-446` | `CleanLocal` (`:482`), `IsSimple` (`:355`), `EnsureCcw` (`:92`), **`Normalize` (`Core/Geometry/Poly.Ops.cs:81`) → `Boolean` (`Poly.Ops.cs:28`) → `Arrangement` (`Core/Geometry/Arrangement.cs:26`, 1,006 lines), `Region`, `FillRule` (`Core/Geometry/Region.cs:10`, `:31`)** | **Cut.** This drags in the whole polygon-boolean engine. Call `Emit(…, clips: null)` as LotLevel does (`LotLevel.cs:200`), and the clip path, `Clean` and `Area` are never reached |
| Sculpt `Emit` (clip path only) | `Poly.Area` → `SignedArea` → `SignedAreaD` | `Poly.cs:54`, `:23`, `:26-52` | nothing | Cut with the clip path |
| Sculpt `CrossesRing` | `Poly.SegmentsIntersect` (→ `Cross` `:298`, `OnSegment` `:301`) | `Poly.cs:285-296` | nothing | **Cut.** `CrossesRing` (`Sculpt.cs:566-575`) is never called |
| TerraformRaster `Build`, `Coverage` | `ReservedSpace.PointInPolygon` | `Core/Contracts.cs:371-381` | `ReservedSpace` class (corridors, discs, biomes) | **Cut** the class and keep this 12-line static as a helper |
| TerraformRaster `Coverage` | `TerraformDabs.DistanceToBoundary` | `Core/Build/TerraformDabs.cs:131-137` | `ReservedSpace.DistSqToSegment` | **Cut.** `Coverage` is only for paint; drop it, or move the helper in |
| TerraformSurface `TargetAt`, `VertexHeights`, `BoundaryEdges` | `PlannedTerraform` | `Core/Contracts.cs:851` | nothing | Keep `At`; take the rest only if the patch type stays `PlannedTerraform` |
| TerrainMetrics | none | none | none | Copy whole |

**Suggested cut.** Port four files:
1. `Sculpt` with `LayerContext` replaced by a cancellation token and a patch sink, and without `Over`, `Resnap`,
   `GradeAt`, `Warn`, the `Ground.Override` call and the clip path.
2. `TerraformSurface.At`.
3. `TerraformRaster` without `Coverage` and `OffClusters` (diagnostics only, `TerraformRaster.cs:260-278`).
4. `TerrainMetrics`.

Add one helper with `Bounds`, `Contains`, `DistanceToBoundary`, `DistSqToSegment` and `PointInPolygon` (about 60 lines,
credited). Each ported file gets the "Modified by Tevin Benjamin, 2026-11: …" line naming the cut.

**Copy or study:** Copy, with the cut above.

## 10. `Core/Build/TerraformRaster.cs`, `TerraformSurface.cs`: patches to desired ground and masks

**TerraformSurface**
- `At(float3[] ring, float2 p)` (`:26-63`) is mean-value interpolation (Floater 2003). It is exact at vertices and linear
  on edges, so quads that share corners are continuous.
- `VertexHeights` (`:66-72`) and `BoundaryEdges` (`:78-98`) are for the preview outline.

**TerraformRaster**
- `Build(items, cell, bank, maxCells, vertexHeights, original)` (`:84-178`):
  - items are sorted stably by layer, and later items win (`:99`);
  - each texel centre inside a ring gets the patch's mean-value height (`:116-139`);
  - a smoothstep bank blends outside texels back to the original ground over `bank` metres (`:141-176`);
  - around a **sloped** patch the bank carries the *change*, not the absolute height, which stops lips on slopes
    (`:168-171`);
  - **the cell grows by ×1.25 above `maxCells`** (`:103`).
- `TrySample` (`:47-74`): the bilinear ground after terraform, for placing things on it.
- `Deltas` (`:229-254`) gives desired − current per texel, plus a `Residual`.
- `Residual.Done` (`:218-219`) means every texel is within tolerance, **or** at most a given fraction of inside texels is
  off and none is more than **2.5× the tolerance** off (`FarOff`, `:222`).
- `Tiles` (`:284-293`) and `TileWorld` (`:296-300`) cut the raster into square tiles.
- `Masks` (`:308-336`) makes a 0..1 raise mask and lower mask for a tile, scaled by the tile's largest change, with flip
  options.
- `ShiftStrength` (`:345-358`) turns metres into brush strength, keeping it at least 0.0021 and scaling the mask down
  instead (`:350`).

**b. Purity check**
- **TerrainMetrics:** pure. It uses only `System`, `System.Collections.Generic` and `Unity.Mathematics`
  (`TerrainMetrics.cs:11-13`), and no other Placemaker type.
- **TerraformRaster and TerraformSurface:** no game code; their `using`s are `System`, `System.Collections.Generic` and
  `Unity.Mathematics` (`TerraformRaster.cs:13-15`, `TerraformSurface.cs:10-12`). They are **not standalone**, though:
  - the raster calls `ReservedSpace.PointInPolygon` (`TerraformRaster.cs:132`, `:194-195`; defined at
    `Core/Contracts.cs:371`);
  - `Coverage` calls `TerraformDabs.DistanceToBoundary` (`TerraformRaster.cs:193`; `Core/Build/TerraformDabs.cs:131`);
  - the surface uses `PlannedTerraform` (`TerraformSurface.cs:19`, `:66`, `:78`; `Core/Contracts.cs:851`).

  Those dependencies are pure too (`Contracts.cs` uses only System and Unity.Mathematics, `:8-10`), so the rule-2 purity
  holds. "Copy as is" does not, without the helper from section 9.
- **The 1.75 m cell** is not inside the raster. It is the build's constant `TerraformCell = 1.75f`, "half a heightmap
  pixel" (`Game/Build/BuildService.Terrain.cs:37`), passed into `Build`.

**Copy or study:** Copy (raster minus `Coverage` and `OffClusters`; surface `At` and `VertexHeights`).

## 11. `Game/Build/BuildService.Terrain.cs`: apply, verify, zone

**The direct terraform path (`:30-451`, about 420 lines)**
- Constants (`:37-47`):

  | Constant | Value | Line |
  |---|---|---|
  | Cell | **1.75 m** | `:37` |
  | Bank | 4 m | `:38` |
  | Tolerance | **0.3 m** | `:39` |
  | Fraction of cells allowed off | **2 %** | `:40` |
  | Passes | at most **4** | `:41` |
  | Wait between passes | **6 frames** | `:42` |
  | Extra waits when the CPU copy lags | up to 3 | `:43` |
  | Tile size | 1024 texels | `:44` |
  | Raster size | 2^20 cells | `:45` |
  | Wall-clock limit | **6 s** | `:46` |
  | Shift tool speed | 2000 | `:47` |

- `BeginTerraform` (`:116-144`) builds the raster. When it can, it does so **on a worker** from a copy of the heightmap
  made with `_terrain.GetHeightData(true)` (`:155-169`, `:177-203`) and sampled with `TerrainUtils.SampleHeight`
  (`:189`). The comment gives the reason: 0.3 s for 1.5 ha and 2.7 s for 16 ha on the game thread (`:147-149`).
- `DoTerraform(frame)` (`:264-346`) runs each pass:
  - it marks the height copy stale (`:274`), so `Height()` re-reads it with `GetHeightData(true)`
    (`Game/Build/BuildService.Emit.cs:97-106`);
  - it measures the deltas (`:275`);
  - if the RMS has not moved, the CPU copy is lagging, so it waits again (`:291-298`).
- **Orientation calibration** (`:300-321`):
  - the first pass must cut the RMS to under 0.8×;
  - if it does not, the pass is reverted exactly (Shift is additive) and the next mirror is tried (`:404-416`);
  - after 4 tries it falls back to level-brush dabs (`:303-310`);
  - the orientation that works is kept in a static for the session (`:54`, `:319`).

  A failed calibration pass uses up one of the 4 passes.
- Stopping and applying:
  - it stops when `Residual.Done` holds (`:328`) or after 4 passes (`:333-338`);
  - `FinishTerraformDirect` (`:348-361`) logs and warns when the target was not reached;
  - `ApplyShiftPass` (`:364-381`) makes **up to two** `ApplyBrush` calls per tile, one with the raise mask and one with
    the lower mask (`:377-378`).
- `ApplyShift` (`:383-401`):
  - `Brush` has `m_Tool = Entity.Null`, so the Shift speed (2000) applies (`:389`);
  - `_terrain.ApplyBrush(TerraformingType.Shift, area, brush, tex)` is at `:399`;
  - the mask is an `RGBAHalf` texture with every channel equal, because which channel the kernel samples is not
    documented (`:421-440`).
- `GroundHeight` (`:447-451`): after a verified terraform, place things on the raster's height, not the lagging live
  terrain.
- The fallback dabs (`:453-578`, at most 48 brushes per frame, `:460`) and the lake water (`:582-635`) are ParkManager's
  (tomkpunkt) code (`:4-6`). Do not take them unless we credit ParkManager.

**Zoning (`:637-713`)**
- `DoZoningEmit` (`:641-697`) creates one definition entity per quad, carrying a `CreationDefinition` with the zone prefab
  and a `Zoning` whose `m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee` (`:663`, `:686`). It leaves out
  `ZoningFlags.Overwrite`, so cells the player already zoned are kept (`:685`).
- `DoZoningApply` (`:702-713`):
  - **requests Apply 2 frames later** (`_tool.Requested = ApplyMode.Apply`, `:705`);
  - destroys its own definitions at frame 4 (`:706`);
  - checks the live cells at frame 40 (`:700`, `:707`).
- The Apply goes through `Game/Build/BuildToolSystem.cs`, an invisible `ToolBaseSystem`:
  - why it is needed: "no system outside the active tool can request Apply" (`:3-8`);
  - it hands the requested mode to the game once, then resets it (`:55-63`).
- `VerifyZoning` and `BlockedCause` (`:731-837`) check, for each planned cell, whether it ended up zoned, empty, blocked
  or hidden, and why. The game's **depth rule** (`CellOverlapJobs.ReduceDepth`) cuts a column deeper than its neighbours,
  and a lone 1-column cutout entirely (`:836`).

**Copy or study:** Adapt. Extract `:30-451` (dropping the dabs fallback, pits and debug), `Height()` from `Emit.cs`, the
zoning emit and apply at `:641-713`, and a cut-down `BuildToolSystem`. The verify code is optional but worth keeping as
a debug option.

## 12. `Core/Build/ZoneGuard.cs`: zone cell rules

**What matters**
- The header (`:1-17`): every road owns zone blocks of 8 m cells up to 6 deep. A cell becomes unzonable only when the
  game blocks it.
- Constants:
  - `Cell = 8f` (`:56`);
  - `QuadShrink = 1f`: a zoning quad is shrunk 1 m because Marquee takes cells by centre (`:66-67`);
  - **`MinRows = 2`** (`:70-71`).
- **Cell geometry**, mirroring `ZoneUtils.GetCellPosition`:
  - `Right(d)` (`:99`), `CellCentre` (`:102-107`), `CellSquare` (`:110-116`), `CellRect` (`:118-128`);
  - row 0 is next to the road, and `Direction` points from the block to its road (`Core/Contracts.cs:89-94`).
- **May a cell be used?**
  - `CellOpen` and `CellOpener` (`:1443-1452`) reject a cell whose centre lies on a road (`RoadCellReach = 3.5`, `:1120`),
    or that is hidden (not Visible) or already blocked outside the build (`:1454-1465`);
  - `RoadBands` (`:1124-1149`) uses roads and tracks only.
- **The game's depth rule:** `ReduceDepths` (`:1239`) ports `CellOverlapJobs`, described at `:1229-1238`. A column deeper
  than both neighbours is cut to the deeper neighbour unless a neighbour of its own depth stands beside it, so a **lone
  1-column zoned strip is blocked entirely**. Neighbouring blocks of the same road continue the row.
- The rest (`Apply` `:140`, `KerbStrips` `:270`, `Refresh` `:479`, `Compute` `:730`) is the blocker-lot machinery for
  parks. It depends on `ComposedPlan`, `SiteContext`, `NearbyNet` and `Region`, and we do not need it.

**For Stepped Streets**
- If "a one-column gap between pads" (5.3) is left **unzoned**, it is safe.
- A zoned lot only one column wide next to empty columns is wiped by the game.
- Pads must be at least 2 rows deep.

**Copy or study:** Copy the parts you need: `Cell`, `MinRows`, `Right`, `CellCentre`, `CellSquare`, `CellRect`, the
cell-open test with `RoadBands`, and `ReduceDepths` to predict what the game will cut.

## 13. `Game/Build/TerrainCullGuard.cs`: crash guard

**What matters**
- The header (`:1-13`):
  - the cause: the terrain's road-cull list is sized at 6 sections per road but filled with `AddNoResize`, and one edge
    can write up to 18;
  - the symptom: a heap overwrite that crashes in `CullForCascades` on about the 4th build.
- `Reserve(terrain, newNets)` (`:35-69`):
  - completes the cull jobs found by reflection on private fields (`m_CullFinished`, `m_LaneCull`, `m_ClipMapCull`,
    `m_CascadeCulling`, `m_LaneHandle`; `:26-29`, `:40-49`);
  - grows the list to *2 × length + 18 × new nets + 2000* (`:52-61`).
- It is called only before the build's **nets** are emitted (`Game/Build/BuildService.Emit.cs:134`).
- Not proven: "10 of 10 builds after it, but the crash was intermittent, so not proven" (`AGENTS.md:159-161`).

**For Stepped Streets**
- A build of terrain and zoning only adds no nets, so the guard matters only if we emit quays (option C) or other nets.
- It reads private fields by reflection, which a game patch can break. It already fails safe: one warning, no exception
  (`:63-68`).

**Copy or study:** Copy, but only if the build emits nets.

## 14. `Core/Build/TerrainMetrics.cs`: the curtain metric

**What matters**
- Constants:
  - `Texel = 3.5f` (`:19-20`);
  - **`CurtainLimit = 0.6f`**: an added jump above this (m per texel) "reads as a curtain" (`:21-22`);
  - **`SheetGrade = 0.5f`**: steeper than this between two texels reads as a vertical sheet (`:23-24`).
- `Curtains(final, original, min, max, texel, mask)` (`:40-72`) samples both grounds on a texel grid. For each pair of
  neighbouring texels where the ground changed by at least 5 cm (`:64`), it computes **added = |final jump| − |original
  jump|** (`:65`).
  - `Max` is the largest added jump. The header and `ToString` call it the curtain height.
  - `Count` counts a pair only when **all three** hold (`:68`):
    - the final jump is over 0.5 × 3.5 = **1.75 m**;
    - the original jump was not;
    - the added jump is over **0.6 m**.
- `Seats` (`:93-120`) measures how badly nets sit on the ground. `OffNets` (`:123-141`) masks out the ground under nets.
- Pure (section 10b).

**What it means for the planner** (my arithmetic from `:68`)
- A step concentrated inside one texel is counted as a sheet once it is taller than 1.75 m.
- The PRD's 2.40 m and 2.88 m steps must therefore spread over at least two texels (about 7 m) of bank, or stand behind a
  real wall.
- Steps between 0.6 m and 1.75 m are not counted, but `Max` reports them. Placemaker's notes (`:5-6`) and the bumpy-hill
  failures (`AGENTS.md:162-163`, about 1 m) suggest they can still show.

**Copy or study:** Copy whole. It is our success check (section 6 of the PRD).

## 15. `Tests/Placemaker.Tests/RoadsTerraceTests.cs`, `RoadsBlockTerraceTests.cs`: testing a pure planner

**What matters**
- **Synthetic sites with no game.**
  - `Plane(settings, grade)` makes a `SiteContext` whose `HeightAt` is `100 + grade · z`, with an area ring
    (`RoadsTerraceTests.cs:70-76`).
  - The named sites (dome, valley, a 13 % hillside, flat) come from `RoadsDesignTests.SiteInput`.
- **What the build would leave:** `Built(r)` runs the plan's terraform through the real `TerraformRaster` at 1.75 m with a
  4 m bank and returns its `TrySample` (`RoadsTerraceTests.cs:79-92`). Tests judge the built ground, not the plan.
- **No curtains:** `NoTerrainCurtains` (`:162-179`) runs every preset × slope through `TerrainMetrics.Curtains(Built(r),
  original, …, OffNets(nets))` and asserts zero sheets. This is the model for our "no curtains" test.
- **Diagnostics hook:** `TerraceField.Inspect` (`TerraceField.cs:69-72`) is a `[ThreadStatic]` that tests set to capture
  the built ground. That is cheaper than reverse-engineering the plan.
- **Budget:** `TwentyHectaresPlanWithinBudget` (`:483-493`) warms up once, then asserts under `TimingBudget.Ms(2000)`
  (`:492`). `TimingBudget` (`Tests/Placemaker.Tests/TimingBudget.cs:19-29`) scales budgets by the environment variable
  `PLACEMAKER_TIMING_SCALE` and for slow machines.
- **Out of date:** the zoning tests are skipped because roads no longer zone (`RoadsTerraceTests.cs:181`,
  `RoadsBlockTerraceTests.cs:148`).
  - `NoQuaysEvenOnSteepHills` (`RoadsBlockTerraceTests.cs:202-213`) now asserts that a 15 % plane gets **no** quays.
  - The wall test is skipped (`:216`).
- **Test project:** `Tests/Placemaker.Tests/Placemaker.Tests.csproj`
  - targets net9.0 with C# 9 (`:5-6`);
  - links `..\..\Core\**\*.cs` (`:13`);
  - references only `Unity.Mathematics.dll` from the game's `ManagedPath` (`:9`, `:16`);
  - uses xUnit 2.9.2 (`:19-21`).

**Copy or study:** Study and copy the style: synthetic planes, judging through the real raster and the curtain metric, a
warm-up then a timed run, and linking Core sources.

## 16. `UI/src/index.tsx`, `UI/src/contract.ts`, `Game/UI/`: React ↔ C#

**What matters**
- `index.tsx:13-57` is the `ModRegistrar`. Each hook-up has its own try/catch:
  - `moduleRegistry.append("Game", PlacemakerRoot)` for the panel (`:24`);
  - `append("GameTopLeft", Launcher)` (`:30`);
  - `extend(TOOLBAR_STRIP, "ToolbarButtonStrip", withToolbarButton)` for the button in the game's toolbar (`:37`);
  - an override of the selected-info sections (`:44-56`), which we do not need.
- `contract.ts` sets `GROUP = "Placemaker"` (`:8`) and `PROTOCOL = 26` (`:11-15`). The game reloads the UI file from disk
  but loads the DLL only at start, so a version stamp lets the panel say "Restart the game" when they disagree.
- The JS side reads values with `bindValue<unknown>(GROUP, name, null)` from `cs2/api` (`UI/src/lib/bindings.ts:10`,
  `:20`).
- C# side, `Game/UI/PlacemakerUISystem.cs`:
  - it is a `UISystemBase` (`:20`) whose values are `ValueBinding<string>` carrying **JSON** (`:46-54`). The header warns
    never to bind `string[]`, which broke mod load in ParkingLotTool (`:3-6`);
  - every trigger is a `TriggerBinding` wrapped so a UI exception never escapes (`:148-156`).
- `PlacemakerTooltipSystem : TooltipSystemBase` (`Game/UI/PlacemakerTooltipSystem.cs:19`) shows hover text with
  `StringTooltip` + `AddMouseTooltip` (`:40-43`, `:67`). This is how "12% climb" on hover can be shown.
- `IconPaths.cs:1-8`: vanilla icons are `Media/Game/Icons/X.svg`. A missing file spams Gameface errors, so check that it
  exists.
- Placemaker is out of date with itself here: ARCHITECTURE says "6 JSON values" (`docs/ARCHITECTURE.md:190`), but the
  system now binds 9 (`PlacemakerUISystem.cs:46-54`).

**Copy or study:** Study, then build a much smaller panel. Use one JSON `ValueBinding<string>` for state, a few
`TriggerBinding`s, a tooltip system for the hover grade, and a toolbar button through `moduleRegistry.extend`.

## 17. `Placemaker.csproj`: the build

**What matters**
- **Two modes** (`:2-10`).
  - **Toolchain mode is opt-in:** `-p:UseToolchain=true`, and only if `CSII_TOOLPATH\Mod.props` exists (`:17-21`, `:25`,
    `:88`).
  - This contradicts Placemaker's own `docs/ARCHITECTURE.md:179`, which says it switches automatically.
- **Standalone mode** (`:28-46`):
  - `net48`, `LangVersion 9.0`, `NoStdLib`, references against the game's own `mscorlib`, `System` and `netstandard`;
  - `ManagedPath` defaults to `C:\Program Files (x86)\Steam\…\Cities2_Data\Managed` (`:32`). On this PC, pass
    `-p:ManagedPath="D:\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed"`.
- The game references (`:48-54`): trim this list to what we use (Game, Colossal.*, Unity.Entities, Unity.Mathematics,
  Unity.Collections, UnityEngine.CoreModule, Colossal.UI.Binding, Unity.InputSystem…).
- Harmony 2.2.2 is referenced and shipped (`:56-60`). We do not need it.
- `Compile Remove` keeps UI, Tests, docs and tools out of the DLL (`:63`).
- The git version stamp target (`:74-86`) is nice to have.
- **Traps:**
  - `BuildUserInterface` runs `npm run build` before every build, and fails without `UI\node_modules`, unless
    `-p:SkipUI=true` (`:90-94`).
  - **`DeployStandalone` runs after every build unless `-p:Deploy=false`** (`:96-105`). It runs Placemaker's own
    `tools\suite\Deploy-Mod.ps1` into `%USERPROFILE%\AppData\LocalLow\…\Mods\…`. That is forbidden by rule 1, and the
    script does not exist in our repo. **Delete this target in our copy.**
  - `PublishConfigurationPath` points at `Properties\PublishConfiguration.xml` (`:22`), which holds Placemaker's mod ID
    163638 (`Properties/PublishConfiguration.xml:3`). Give ours a new one (PRD 9.5).

**Copy or study:** Copy and trim. Keep standalone mode and `ManagedPath`. Delete Deploy, Harmony and the UI step (or keep
the UI step behind `SkipUI`).

---

## PRD fact check (sections 2, 5.3 and 7, plus the numbers the brief named)

**Verdicts:** *right* (the code agrees), *wrong* (the code disagrees, or the claim is out of date), *partly* (right
number, wrong or missing context), *can't check* (outside this code).

**Who would use the corrected value:** *planner* means the M2 pure planner (R1–R10); *build* means the M3 terrain, wall
and zoning code; *spike* means the M1 hard-coded test; *port* means whoever copies files in wave 2; *doc* means a
wording fix only, with no code effect.

| # | PRD claim (where) | Verdict | Correct value | Placemaker path:line | Who uses the corrected value |
|---|---|---|---|---|---|
| 1 | Growables flatten their own footprint; players flatten hills by hand (§2) | can't check | game behaviour, not in this code | | doc |
| 2 | Hillside terraces "added 15 October" (§2) | right | commit `9ba07eb6`, 2026-10-15 01:10 −0700 | `git log` of `Core/Layers/Roads/Terraces/TerraceField.cs` | doc |
| 3 | Hillside terraces: streets on contour lines, each on a level terrace (§2) | right | | `Core/Layers/Roads/Terraces/TerraceField.cs:1-11`; `docs/proposals/s52.md:17-20`, `:31-36` | doc |
| 4 | "A street that climbs steeper than 6% gets no terrace of its own" (§2) | partly | right for **Terraced blocks** (`BlockField`, every road pattern, added 19 Oct, `471ec0d7`), not for Hillside terraces, whose streets are contours by construction. The grade is measured over 30 m on the smoothed (12 m+ blur) survey ground | `BlockField.cs:63-68`, `:124`, `:208-221` | planner (R1 grade measure) |
| 5 | "its frontage is zoned only where it happens to lie level" (§2) | wrong (out of date) | road plans no longer zone at all (since 26 Oct); zoning is the Zoning category's. BlockField's comment is stale | `Core/Layers/Roads/RoadPreZone.cs:26`; `RoadsBlockTerraceTests.cs:148`, `:212`; `RoadsTerraceTests.cs:181`; stale comment `BlockField.cs:64-66` | doc |
| 6 | `BlockField.ClimbGrade = 0.06` in `Core/Layers/Roads/Terraces/BlockField.cs` (§2) | right | `public const float ClimbGrade = 0.06f;` | `BlockField.cs:68` | planner (R1) |
| 7 | "Level the lots": each lot levelled across its depth, following the street's slope along its width (§2) | right | | `PolishLookLayer.cs:22-23`; `LotLevel.cs:1-3`, `:825-838` | doc |
| 8 | "Each column of a lot takes the road's height at that column" (§2) | partly | it takes the **ground 1 m inside the lot's front edge**, averaged with the neighbouring columns at half weight, not the road net's height | `LotLevel.cs:74-94` | planner (R3 pad height) |
| 9 | `fronts` in `PolishLookLayer.cs` (§2) | right | `var fronts = …LotLevel.Fronts(g, site.HeightAt)` | `PolishLookLayer.cs:336` | doc |
| 10 | `LotLevel.Lot.Front` (§2) | right | pad height per column | `LotLevel.cs:43-44` | doc |
| 11 | "On a steep street the lots still slope sideways" (§2) | right | the levelness test even allows the road's grade plus 4 % along the road | `LotLevel.cs:366-368`, `:825-838` | doc |
| 12 | "No tool gives a steep street level, stepped lots" (§2) | can't check | true within Placemaker; other mods not checked | | doc |
| 13 | Step height = grade × lot width; the table's step values (§5.3) | right | arithmetic checks: 0.96, 1.44, 1.28, 1.92, 1.60, 2.40, 1.92, 2.88, 2.40, 3.60 m | | doc |
| 14 | Suburb zoning style: Residential Low lots 2–3 cells wide (§5.3) | right | Suburb (a *type*) default zone is Residential Low **2–3 wide, 3–4 deep**. Its styles differ: Leafy 3–4 wide, Tight 2 wide. The generic Residential Low size is 2–3 × 2–4 | `Core/Layers/Types/Zoning/ZoningTypes.cs:35`, `:39-40`; `PolishLookLayer.cs:80` | planner (R2 lot widths) |
| 15 | 8 m zone cells (§5.2–5.3) | right | `Cell = 8f`; at most 6 rows; at least 2 rows | `Core/Build/ZoneGuard.cs:56`, `:70-71`; notes `:451` | planner (R2) |
| 16 | Heightmap texels about 3.5 m apart (§5.3) | right | 14,336 m / 4096 = 3.5 m | `TerrainMetrics.cs:19-20`; notes `:576` | planner, build (curtain checks) |
| 17 | Notes treat a jump steeper than about 50 % across one texel as a tear (§5.3) | right | `SheetGrade = 0.5`; terraces cap banks at 0.46 | `TerrainMetrics.cs:23-24`; `TerraceOptions.cs:93-95`; notes `:576-577` | planner (R5 bank limit) |
| 18 | "…and warn that curtains can show from smaller jumps too" (§5.3) | right | `CurtainLimit = 0.6` m per texel. A pair counts as a sheet only if the final jump is over 1.75 m **and** the added jump is over 0.6 m. Car parks dropped from 1:2 to 0.4 after "78 sheets on a 15 % slope" | `TerrainMetrics.cs:5-6`, `:21-22`, `:68`; `LotHill.cs:30-32` | planner (R5, aim at 0.4 not 0.46) |
| 19 | Curtain reference "note 85" (§5.3, §7 item 2) | partly | the file has **two** notes numbered 85; the heightmap one is at `:576` (the other is decals, `:565`). Also 86–88 are duplicated | `docs/RESEARCH-NOTES-ADDENDUM.md:565`, `:576`, `:580`, `:595` | doc |
| 20 | "Quay walls … only rise where the drop is 2 m or more" (§5.3) | partly | 2 m is the game's elevation limit as Placemaker states it, but Placemaker plans walls only from **`MinWall` 2.6 m** (2 m + heightmap margin) and keeps them only above `KeepWall` 2.2 m. Placemaker has **switched quays off** for terraces ("they never work"), and the Zoning tool's walls are off by default | `BlockField.cs:13-16`, `:69-72`; `TerraceOptions.cs:72-73`; `RoadsBlockTerraceTests.cs:205`; `PolishLookLayer.cs:189`; commit `875dbb84` | planner (R7), spike (option C) |
| 21 | MinWall 2.6 m (R7) versus "2 m or more" (§5.3): which is real? | both | **2 m** = game threshold (per Placemaker's notes; the game constant itself is not in this code). **2.6 m** = Placemaker's planning threshold. Use 2.6 m | `BlockField.cs:69-70`; `LotLevel.cs:680`; notes `:510-513` | planner (R7) |
| 22 | Table column "Quay wall?" (§5.3) | wrong | the column uses 2 m. With MinWall 2.6 m: **10 %: No** (2.40 m at 3 cells); **12 %: 3 cells only** (2.88 m); **15 %: 3 cells only** (2-cell step 2.40 m < 2.6) | `BlockField.cs:70` | planner (R4 limits), spike (option C) |
| 23 | "Most steps on real steep streets fall between 1 and 2.5 m" (§5.3) | can't check | no data in this code | | doc |
| 24 | Option A: "Placemaker already leaves unzoned gaps between lots (`LotLevel.Lot.Filler`)" (§5.3) | partly | a Filler is a 0.2–20 m gap between two levelled lots on one street front, levelled as a **ramp** between their edge heights, never zoned. Placemaker does not make the gaps on purpose; they come from the empty share and leftovers | `LotLevel.cs:49-50`, `:243-304`, `:282`, `:300` | spike (option A) |
| 25 | Option B: 0.8 m wall at the foot of each bank (§5.3) | right | `FootWall = 0.8` is the step the wall takes. The wall is one course of the game's 0.9 m garden wall prop, which **cannot be scaled** | `LotGrading.cs:102-103`; `PlanBuilder.cs:548`, `:556`, `:577` | spike (option B), build (wall object) |
| 26 | Option B: 2.5 m off the lower tier's kerb (§5.3) | right | `FootClear = 2.5`, but the foot is *min(2.5, 0.35 × median)*, and a wall exists only on medians of at least 5.7 m (terraced style only) | `LotGrading.cs:93`, `:99`, `:104-107` | spike (option B) |
| 27 | Option B: "the ground stepping up 1.2 m behind the wall" (§5.3) | right | `FootSmear = 1.2` is a **distance** behind the wall's back face, not a height | `LotGrading.cs:104-107`, `:374` | spike (option B) |
| 28 | Option C: paired pads, steps of 2.6 m or more "where a quay wall works" (§5.3) | partly | 2.6 m = MinWall, right; "works" is contradicted by Placemaker removing quays because "they never work" | `BlockField.cs:70`; `RoadsBlockTerraceTests.cs:205` | spike (option C) |
| 29 | Option D, house flattening (§5.3) | can't check | | | spike (option D) |
| 30 | Nimenta2707 fork, Plop the Growables, README (§5.3) | can't check | outside this code | | doc |
| 31 | "Placemaker is about 980 C# files" (§7) | right | **978** tracked `.cs` files (531 Core, 196 Game, 249 Tests, 1 UI, 1 root) | `git ls-files '*.cs'` | doc |
| 32 | Clone URL `github.com/jackwrichards/placemaker` (§7) | right | the reference's `origin` | `git remote -v` | doc |
| 33 | Item 1: pure Core, Game ECS, UI React; code rules; tool → snapshot → plan → preview → build (§7) | right | | `docs/ARCHITECTURE.md:127-148`, `:154-169`, `:181-190` | doc |
| 34 | Item 2: direct terraform, 3.5 m heightmap, curtains, quay walls, nets flatten their width (§7) | right | | notes `:196-255`, `:502-522`, `:576-583` | doc |
| 35 | Item 3: `IMod.OnLoad`, `updateSystem.UpdateAt<…>(SystemUpdatePhase.ToolUpdate)` (§7) | right | | `Mod.cs:12`; `Game/Tool/ToolRegistration.cs:43` | doc |
| 36 | Item 4: tool on `ToolBaseSystem`: hover, click, raycast against roads (§7) | right | road raycast = `TypeMask.Net` + `Layer.Road` + `SubElements` | `PlacemakerToolSystem.cs:33`, `:239-241`; `.Input.cs:46` | build (picker) |
| 37 | Item 5: snapshot heights and nearby roads on the main thread (§7) | right | heights on a grid of at least 4 m | `SiteCapture.cs:1-7`, `:91-111`, `:155` | planner (input resolution), build |
| 38 | Item 6: `PolishLookLayer.cs` around lines 300–430 (§7) | right | the lot loop is `:315-423`, levelling `:425-445` | `PolishLookLayer.cs` | doc |
| 39 | Item 7: bank spacing S = W·a ÷ (a − g) (§7) | right | | `docs/proposals/s52.md:22-26` | planner (R4/R5 bank width) |
| 40 | Item 7: clash resolution (§7) | right | `TerraceField.Resolve` | `TerraceField.cs:338-444` | planner |
| 41 | Item 7: quay retaining walls (§7) | partly | the code exists but is switched off for road terraces | `TerraceOptions.cs:72-73`; `BlockField.cs:190` | planner (R7) |
| 42 | Item 7: the 6 % climb limit (§7) | right | in BlockField, over 30 m | `BlockField.cs:68`, `:208-212` | planner (R1) |
| 43 | Item 8: level tiers with masonry foot walls (§7) | right | terraced style only | `LotGrading.cs:1-21`, `:429-441` | spike (option B) |
| 44 | Item 9: Sculpt on "a 2 m grid" (§7) | partly | default cell **4 m**; callers pass 2 m; grows ×1.2 above 60,000 nodes | `Sculpt.cs:32`, `:35`, `:60`; `TerraceField.cs:153`; `LotLevel.cs:418` | port, planner (grid cell) |
| 45 | Item 9: Sculpt makes patches "with no curtains" (§7) | wrong | Sculpt emits what it is given. Curtains are prevented by the caller's bank limits (`LimitGrade`, `LimitBanks`) | `Sculpt.cs:175-203`, `:414-525`; `TerraceField.cs:446-503` | planner (must limit banks itself) |
| 46 | Item 9: "needs a few `Core/Geometry` helpers" (§7) | wrong | also needs `LayerContext` (most of `Contracts.cs`), `TerrainField`, `TerrainShaper`, `TerrainSurvey`, `TerraformSurface`. `Poly.Clean` pulls the polygon-boolean engine. See section 9 for the cut | `Sculpt.cs:16-17`, `:414`, `:502`; `LayerBase.cs:144`; `Poly.Ops.cs:81`; `Arrangement.cs:26` | port |
| 47 | Item 10: desired-ground grid of 1.75 m cells and raise/lower masks (§7) | right | 1.75 m is the build's constant, passed in; the raster grows ×1.25 above its cell cap | `BuildService.Terrain.cs:37`; `TerraformRaster.cs:84-178`, `:103`, `:308-336` | build, port |
| 48 | Item 10: "Pure code, only `Unity.Mathematics`. Copy as is." (§7) | partly | no game code (pure), but needs `ReservedSpace.PointInPolygon`, `TerraformDabs.DistanceToBoundary` and `PlannedTerraform` | `TerraformRaster.cs:132`, `:193-195`; `TerraformSurface.cs:19`; `Contracts.cs:371`, `:851`; `TerraformDabs.cs:131` | port |
| 49 | Item 11: `TerrainSystem.ApplyBrush(TerraformingType.Shift, …)` (§7) | right | | `BuildService.Terrain.cs:399` | build |
| 50 | Item 11: "one mask texture per tile" (§7, and "Patterns worth copying") | partly | **up to two** per tile per pass, a raise mask and a lower mask | `BuildService.Terrain.cs:370-379`; notes `:226-228` | build |
| 51 | Item 11: re-reads heights (`GetHeightData(true)`) after 6 frames (§7) | right | 6 frames, with up to 3 extra waits if the CPU copy lags. The re-read is `Height()` in `BuildService.Emit.cs`; the worker copy also uses `GetHeightData(true)` | `BuildService.Terrain.cs:42-43`, `:160`, `:274`, `:291-298`; `BuildService.Emit.cs:101` | build |
| 52 | Item 11: up to 4 passes to within 0.3 m (§7) | right | 4 passes, 0.3 m, **2 %** of inside texels may stay off (none more than 0.75 m), 6 s cap; a failed calibration pass uses up a pass | `BuildService.Terrain.cs:39-41`, `:46`, `:328-338`; `TerraformRaster.cs:218-222` | build, planner (R8) |
| 53 | Item 11: texture orientation worked out on the first pass (§7) | right | first pass must cut the RMS below 0.8×, else revert and try the next mirror; remembered for the session | `BuildService.Terrain.cs:54`, `:300-321`, `:404-416` | build |
| 54 | Item 11: zoning `ZoningFlags.Zone \| ZoningFlags.Marquee` (§7) | right | without `Overwrite`; Apply requested 2 frames later through `BuildToolSystem` (missing from the PRD's list) | `BuildService.Terrain.cs:663`, `:685-686`, `:705`; `Game/Build/BuildToolSystem.cs:3-8` | build (zoning) |
| 55 | Item 11: extract "a few hundred lines" (§7) | right | the direct path is `:30-451`, about 420 lines, plus `Height()` and `BuildToolSystem` | `BuildService.Terrain.cs:30-451` | port |
| 56 | Fallback brush and lake water from ParkManager; zoning follows Grid Road Generator (§8) | right | | `BuildService.Terrain.cs:4-8` | doc (credits) |
| 57 | Item 12: zone cell rules (8 m) and which cells may be zoned (§7) | right | | `ZoneGuard.cs:56`, `:1443-1465`, `:1239` | planner (R8), build |
| 58 | Item 13: native crash guard; "fix isn't fully proven" (§7) | right | "10 of 10 builds … intermittent, so not proven"; only used when nets are emitted | `AGENTS.md:159-161`; `TerrainCullGuard.cs:1-13`; `BuildService.Emit.cs:134` | build (only if nets) |
| 59 | Item 14: `TerrainMetrics.cs` counts curtains (§7, §6) | right | pure | `TerrainMetrics.cs:40-72` | planner tests |
| 60 | Item 15: unit-test a pure planner on synthetic slopes (§7) | right | the zoning tests in these two files are skipped now | `RoadsTerraceTests.cs:70-92`, `:162-179`, `:181`; `RoadsBlockTerraceTests.cs:148` | planner tests |
| 61 | Item 16: React ↔ C# via values and triggers (§7) | right | JSON `ValueBinding<string>` + `TriggerBinding` | `PlacemakerUISystem.cs:46-54`, `:148-156`; `UI/src/lib/bindings.ts:20` | UI (M4) |
| 62 | Item 17: two build modes, toolchain (`CSII_TOOLPATH`) or standalone (§7) | partly | toolchain mode is **opt-in** (`-p:UseToolchain=true`). The default build **deploys to the Mods folder** unless `-p:Deploy=false`, and runs npm unless `-p:SkipUI=true` | `Placemaker.csproj:17-25`, `:90-105` | port (csproj) |
| 63 | "Never fire hundreds of brush strokes … crawling at 2 FPS" (§7 patterns) | right | | notes `:198-201` | build |
| 64 | Plan on a worker; latest request wins (§7 patterns) | right | | `docs/ARCHITECTURE.md:187` | build |
| 65 | Setting ids are save format (§7 patterns) | right | | `docs/ARCHITECTURE.md:165` | build, UI |
| 66 | "Nothing is done until you have seen it in the game" (§7 patterns) | right | | `AGENTS.md:54` | doc |
| 67 | R7 retaining wall from 2.6 m (`MinWall`) (§5.2) | right | value right, but see #20: walls are off in Placemaker | `BlockField.cs:70` | planner (R7) |
| 68 | R8 "within 0.3 m, the tolerance Placemaker's terrain build checks against" (§5.2) | partly | 0.3 m is the terraform convergence tolerance (2 % may stay off, up to 0.75 m). Placemaker's own "is this zone cell level" test is much tighter: about 0.1 m over 2.5 m from the road, road grade + 4 % along it | `BuildService.Terrain.cs:39-40`; `TerraformRaster.cs:218-222`; `LotLevel.cs:353-369` | planner (R8) |
| 69 | R10 "250 ms for a 5 ha area" (§5.2) | right | "< 250 ms for a 5 ha area on one core" | `docs/ARCHITECTURE.md:159` | planner (R10) |
| 70 | "about 1,800 commits since 13 October" (§12) | right | **1,827** commits; the first is `53fbb720`, 2026-10-13 | `git rev-list --count HEAD`; `git log --reverse` | doc |
| 71 | Settings: earthwork limit 1–8 m, default 4 m (§5.4) | right | matches Placemaker's `levelEarth` (4 m, 1–8, steps of 0.5) | `PolishLookLayer.cs:187` | planner (settings) |
| 72 | Success: "Every pad level within 0.3 m" (§6) | partly | the build's own stop rule accepts 2 % of texels off by up to 0.75 m, so this needs its own measurement after the build | `TerraformRaster.cs:218-222`; `BuildService.Terrain.cs:40` | build (verify step) |

**The findings that matter most**
1. **#20, #22, #28:** quay walls. Placemaker removed them from terraces because "they never work". With the real 2.6 m
   threshold, the 10 % row gets no wall at all. Options C and R7 need rethinking before M1.
2. **#44–#46:** "Copy Sculpt" is a port with a cut, not a copy.
3. **#5 and #54:** roads no longer zone in Placemaker, and zoning needs the active-tool Apply (`BuildToolSystem`), which
   the PRD's list leaves out.

## Open questions for the lead

1. **Quay walls.** Placemaker took them out of road terraces ("they never work"), and its Zoning-tool walls are off by
   default. Should M1 still try option C and the R7 retaining wall? Or should v1 plan banks and the 0.9 m garden-wall prop
   only, keeping quays as a later experiment?
2. **May the planner use `TerraceField.LimitBanks` and `Resolve`**, copied and credited? Rule 3 bans copying only from
   LotLevel and PolishLookLayer, but these are the bank limiter LotLevel itself relies on. Or must the bank limit be
   written from scratch too?
3. **The Sculpt cut** (section 9): may Sculpt's `Emit` stop taking `LayerContext` and lose `Over`, `Resnap`, `GradeAt`,
   `Warn` and the clip path? Each copied file's "Modified by" line would name the cut.
4. **What "level" means for R8:** the build's 0.3 m convergence tolerance, or a pad-levelness test closer to
   Placemaker's (about 0.1 m over 2.5 m)? This also decides how the success criterion "every pad level within 0.3 m" is
   measured (#72).
5. **Add `Game/Build/BuildToolSystem.cs` to the wave-2 copy list?** Zoning cannot be applied without an active tool that
   requests `ApplyMode.Apply`. Our street picker could do it instead, if it stays active through the build.
6. **TerrainCullGuard** matters only when nets are emitted. If v1 emits no quays, should we skip it?
7. **The PRD wording** (§2, §5.3 table, §7 items 9–11 and 17): these are for the owner to decide (the brief says not to
   edit the PRD); the table above gives the corrected values.
