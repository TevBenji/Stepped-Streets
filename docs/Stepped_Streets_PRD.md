# Stepped Streets

*Product requirements document · a Cities: Skylines II code mod*

| Item | Detail |
|---|---|
| Owner | Tevin Benjamin |
| Status | Draft 3, 2 November 2026; wave 4 built, awaiting the in-game test (changes listed at the end) |
| Purpose | A fun side project: my own standalone mod, open source |
| Working name | Stepped Streets (check Paradox Mods for a clash before publishing) |
| Licence | GPL-3.0, because it reuses Placemaker code (section 9) |
| Code studied | Placemaker by jackwrichards, commit `e654b32` (1 November 2026) |

## 1. Summary

Stepped Streets lets a player click a steep street and get the classic hill-town frontage: every lot on its own flat pad, the lots stepping down the hill like San Francisco or Lisbon, a clean wall or planted bank at each step, and zoning on the pads so houses grow level. It does one thing, for one street at a time, and works alongside Placemaker rather than replacing it.

It reuses Placemaker's proven terrain plumbing (GPL-3.0, credited). The stepped-lot planner, the algorithm at its heart, is written from scratch.

## 2. The problem

**In the game:** on a sloped street every growable building flattens its own footprint at its own height. The frontage comes out lumpy: tilted yards, raw dirt steps between houses, buildings half sunk or floating. Players work round it by flattening whole hills by hand before they zone. That costs hours, and the hill disappears.

**What Placemaker does today** (checked in its code):

- **Hillside terraces** (added 15 October): new streets laid on the contour lines by construction, each on a level terrace.
- **Terraced blocks** (added 19 October), the ground under every road pattern: a street that climbs steeper than **6%**, measured over 30 m of smoothed ground, gets no terrace of its own (`BlockField.ClimbGrade = 0.06` in `Core/Layers/Roads/Terraces/BlockField.cs`). Road plans no longer zone at all (since 26 October); all zoning lives in Placemaker's Zoning category.
- **Zoning tool, "Level the lots"**: each lot is levelled **across** its depth but **follows the street's slope along** its width. Each column of a lot takes the ground's height 1 m inside the lot's front edge at that column, averaged with its neighbours (`fronts` in `Core/Layers/Types/Zoning/PolishLookLayer.cs`, `LotLevel.Lot.Front`). On a steep street the lots still slope sideways, so the houses still make their own steps.

No tool gives a steep street level, stepped lots. That is the gap Stepped Streets fills.

## 3. Goals and non-goals

**Goals**

1. A player can turn one steep street (6% and up) into level, stepped lots in a few clicks.
2. Every step looks built: a wall or a planted bank, never a torn terrain face.
3. Houses that grow there sit level on their pads.
4. Simple enough that anybody can use it: sensible defaults, three or four choices, a clear preview.
5. The core planner is my own code, tested without the game.

**Non-goals (version 1)**

- Laying out new roads or whole neighbourhoods (Placemaker's terraces already do that).
- Gentle streets under the chosen minimum grade (6% by default): they keep the game's normal behaviour.
- New 3D assets: walls use the game's own pieces.
- Quay walls: out of v1, by the owner's decision. The two looks are Planted bank and Low wall.
- Translations beyond English (later, if people use it).

## 4. Who it's for

- **The detailer** who wants the San Francisco look on a steep street without terraforming lot by lot.
- **The player who always flattens the hill** (me): keep the hill, click once, done.
- **The Placemaker user** who built hillside terraces and wants houses on the steep link roads its terraces skip.

## 5. How it works

### 5.1 Player flow

1. Click the Stepped Streets button in the bottom toolbar beside Zones, or press Ctrl+Shift+S to open the tool (rebindable in Options → Stepped Streets).
2. Hover a street: it highlights and shows its grade ("12% climb"). Streets under the minimum grade show grey.
3. Click to select it. Shift-click adds the next segment of the same street.
4. Pick the side (both, left, right), the zone (for example Residential Low), lot width (2–6 cells) and step look (Planted bank or Low wall). Advanced settings hold Narrowest width, Depth, Minimum grade, Highest step, Bank grade and Earthwork limit (section 5.4).
5. The preview draws each pad, its height, each step and wall, and any lot left empty with the reason. A summary line: "14 lots, steps 1.1–1.9 m, 2 lots left empty (too steep for this width)".
6. Build. The panel says first that ground changes are permanent, like the game's own terrain tools.

Clear clears the selection; Close closes the tool. Neither undoes a build. The hotkey opens or toggles the tool; it never builds by itself. Build runs terrain → zoning → walls (for Low wall).

### 5.2 Rules

Numbers marked \* are starting values; the first in-game test still needs to confirm them.

| # | Rule |
|---|---|
| R1 | Works where the selected street climbs at least the **Minimum grade** (6%\*, the same threshold Placemaker's Terraced blocks use; the planner measures it over each lot's stretch of street). Flatter stretches are left alone. |
| R2 | Lots are laid along the street from the downhill end (the lower of its two endpoints), starting at the kerb (the planner takes the road's half width), each **Lot width** cells wide (8 m cells), with **Depth** set to 2–6 cells. Left and right follow the direction of the street's centreline as the game gives it. |
| R3 | Each lot becomes **one level pad**: one height across its whole width and depth, the street's height at the middle of the lot's front\*. The planner keeps this in one function so it can change (section 14, question 1). |
| R4 | The step to the next lot is grade × the distance between the two pads' middles: grade × lot width when both lots are the same width (section 5.3). A lower pad of W cells allows a step of at most **min(Highest step, (W − 2) × 8 m × Bank grade)**, keeping two columns level beside the bank. The planner enforces this for both looks. If the step is too tall, that lot alone is narrowed down to Narrowest width; if it still fails, it is left empty with a reason. A 2-cell lower pad has no bank allowance. After an empty lot the next pad starts a fresh run. |
| R5 | Each step is a bank on one or more whole 8 m columns taken from the lower pad's uphill end. Low wall adds the game's garden-wall prop along the foot of that same bank; it does not increase the allowed step. This is original planner code: whole-column banks make a separate `TerraceField.LimitBanks` or `Resolve` limiter unnecessary. Nothing is copied from `LotLevel.cs` or `PolishLookLayer.cs`. |
| R6 | The strip between the kerb and the pad (the driveway apron) is graded so the front meets the road, over 4–8 m. The mismatch at the lot's corners is up to grade × lot width ÷ 2. Low walls start 4 m behind the kerb, matching the narrowest apron; following a wider apron exactly is a later refinement. |
| R7 | The back of each pad meets the hillside with a bank governed by Bank grade\*. Total cut or fill is capped by Earthwork limit; a lot needing too much is left empty with a reason. v1 emits no quay walls or other nets. Placemaker's quay experiments remain background research, not a release feature. |
| R8 | Zone cells are only placed on pad cells that came out level (within 0.3 m, a planner parameter). 0.3 m is the terrain build's convergence tolerance, which lets 2% of texels stay off by up to 0.75 m, so a pad's levelness needs its own measurement after the build (section 6). Placemaker's own zone-cell levelness test is about 0.1 m. |
| R9 | Never touches existing buildings, other roads or water; never goes deeper than the frontage. An obstacle may shorten a pad from the back; if the minimum usable depth cannot fit, leave it empty and say why. |
| R10 | Same input, same plan. Planning a 500 m street takes under 250 ms (Placemaker's budget for a 5 ha area). |

### 5.3 The hard part: the step between two lots

Step height = street grade × lot width, for two lots of the same width. With lots 2 or 3 cells wide (Placemaker's Suburb zoning style uses Residential Low lots 2–3 cells wide):

| Grade | 2 cells (16 m) | 3 cells (24 m) | 3-cell pad's bank at grade 0.40 |
|---|---|---|---|
| 6% | 0.96 m | 1.44 m | One bank column fits |
| 8% | 1.28 m | 1.92 m | One bank column fits |
| 10% | 1.60 m | 2.40 m | One bank column fits |
| 12% | 1.92 m | 2.88 m | One bank column fits |
| 15% | 2.40 m | 3.60 m | Exceeds the 3.2 m allowance |

These are geometric step heights before R4's limits. A 2-cell lower pad cannot keep two level columns plus a bank, so those steps cannot form a continuous run. The last pad of a run has no following bank and keeps its full width.

Pad heights sit at the middle of each lot's front (R3), so between lots of different widths the step is grade × the distance between their middles: a 3-cell lot next to a 2-cell lot steps grade × 20 m (half of 24 m plus half of 16 m), not grade × 16 m. Narrowing one lot saves only half a cell of step.

**Why this is hard.** The game's heightmap has texels about 3.5 m apart, so a sharp jump draws as a stretched vertical sheet (a "curtain"). Placemaker's notes treat a jump steeper than about 50% across one texel as a tear, and warn that curtains can show from smaller jumps too (`docs/RESEARCH-NOTES-ADDENDUM.md` note 85 at line 576, since the file numbers two notes 85; `TerraceOptions.cs`). Placemaker plans quays only from 2.6 m (`BlockField.MinWall`) and keeps them only above 2.2 m, but switched them off for road terraces on 19 October ("they never work"). The owner has ruled quays out of v1. Stepped Streets spreads the rise across whole bank columns, with an optional 0.9 m garden wall at their foot; the visual result still needs the in-game test.

**The two v1 looks, and the earlier experiments:**

- **A. Planted bank:** one or more whole 8 m columns from the lower pad's uphill end, unzoned and ramped to the next pad. A 3-cell pad keeps two level columns beside its one-column bank. Placemaker's nearest idea is `LotLevel.Lot.Filler`, a ramp in an incidental gap; this planner's geometry is original work.
- **B. Low wall:** the same bank, with unscaled `EU_Wall02` garden-wall props along its foot, from 4 m behind the kerb to the lower pad's back edge. The wall is 0.2 m thick and 0.9 m tall, running along local Z (`Core/Layers/Parking/Kerbs.cs:34-45`). Piece length comes from `ObjectGeometryData.m_Size.z`, with a logged 3 m fallback. A final piece is dropped if less than half fits. Unlike Placemaker's car-park foot-wall profile, v1 does not subtract 0.8 m from the terrain rise: the wall is decoration and the bank allowance is unchanged.
- **C. Paired pads and quays:** earlier research only. Quays are out of v1; wider pads remain available through the 2–6 cell Lot width setting.
- **D. Let the house hide it:** earlier research only. v1 zones level cells after the terrain build; it does not place houses directly.

**The second unknown: do houses use the pads?** The game picks which building grows and how wide its lot is. If one house spans two pads, the effect breaks. Options: zone each pad exactly and vary the depth or zone type between neighbours; leave a one-column gap; or place the houses directly. The Placemaker fork by Nimenta2707 places buildings itself and marks them historical so they keep their model, the way Plop the Growables does (its README says it isn't tested in the game yet).

### 5.4 Settings

| Setting | Choices | Default | Notes |
|---|---|---|---|
| Side | Both, Left, Right | Both | |
| Zone | The game's zone types | Residential Low | Row houses suit steep streets best |
| Lot width | 2–6 cells | 3 | Width along the kerb, including bank columns |
| Step look | Planted bank, Low wall | Planted bank | Same bank allowance; Low wall adds garden-wall props |
| Advanced: Narrowest width | 2–6 cells, at most Lot width | 3 | Width floor for narrowing; two cells leave no bank allowance |
| Advanced: Depth | 2–6 cells | 3 | Cells back from the kerb; obstacles may shorten it |
| Advanced: Minimum grade | 4–12% | 6%\* | |
| Advanced: Highest step | 0.5–5.0 m | 3.2 m\* | Also capped by (lower pad width − 2) × 8 m × Bank grade |
| Advanced: Bank grade | 0.20–0.50 | 0.40\* | Rise over run; whole columns hold the rise |
| Advanced: Earthwork limit | 1–8 m | 4 m\* | Cut or fill per pad |
| Opening hotkey | Rebindable in Options → Stepped Streets | Ctrl+Shift+S | Opens or toggles the tool, not Build |

### 5.5 Messages

- "This street is flatter than 6%: nothing to step here."
- "2 lots left empty: too steep for lots this wide."
- Empty lots also report too much earthwork, a blocked footprint, or an end too short for a lot. Changing lot width, Bank grade or Highest step may help; Low wall does not bypass the bank allowance.
- "Ground changes are permanent, like the game's terrain tools. Build?"

## 6. Success criteria

Measured on a test map with straight streets at 8%, 12% and 15%:

- **No curtains** inside the build. Measure with a copy of Placemaker's curtain metric (`Core/Build/TerrainMetrics.cs`) plus a 45° screenshot.
- **Every pad level** within 0.3 m, measured on the ground after the build (the build's own stop rule lets 2% of texels stay off by up to 0.75 m), and every step within Highest step and the lower pad's bank allowance.
- **Houses sit on one pad each** in at least 9 of 10 lots after the street fills up.
- **At least as much frontage zoned** as Placemaker's Zoning tool on the same street, measured in M1 as the baseline.
- **Anybody can use it:** two friends who play CS2 get a stepped street built without help on their first try.
- **No long freezes:** the build never stalls the game for more than 2 seconds.
- **Safe to remove:** unsubscribing leaves the terrain and zoning as built, and the save loads without errors.

These are manual acceptance criteria, not claims that the first in-game test has passed. Zoning verification checks one game cell at each run's centre, so its count is runs, not every zoned cell. Wall reports count emitted definitions, without a verification pass. Count actual cells and wall pieces in the game.

## 7. Placemaker code study guide

Read in this order. Placemaker is 978 C# files, so don't read it all: these files answer the questions this mod has. Get the code with `git clone https://github.com/jackwrichards/placemaker`.

| # | File | What you learn | Copy or study? |
|---|---|---|---|
| 1 | `AGENTS.md`, `docs/ARCHITECTURE.md` | How the mod is split: pure `Core/` (no game code, unit-tested), `Game/` (ECS systems), `UI/` (React). Its code rules. The runtime flow: tool → site snapshot → plan → preview → build. | Study. Copy the Core / Game / UI split. |
| 2 | `docs/RESEARCH-NOTES-ADDENDUM.md`: "Direct terraform", notes 77–79, 85–86 | Engine rules found by decompiling the game: how terrain changes, the 3.5 m heightmap, curtains, quay walls, roads flattening their own width. The file numbers notes 85–88 twice each: the heightmap note 85 is at line 576 and the nets note 86 at line 580. | Study. Your engine cheat sheet. |
| 3 | `Mod.cs`, `Game/Tool/ToolRegistration.cs` | Mod entry point (`IMod.OnLoad`) and how systems are registered (`updateSystem.UpdateAt<…>(SystemUpdatePhase.ToolUpdate)`). | Study; write your own (small). |
| 4 | `Game/Tool/PlacemakerToolSystem.cs` (+ `.Input.cs`, `.Points.cs`) | A custom tool built on the game's `ToolBaseSystem`: hover, click, raycast against roads. | Study; write your own street picker. |
| 5 | `Game/Preview/SiteCapture.cs` | Snapshotting terrain heights and nearby roads on the main thread so planning can run on a worker. | Adapt the parts you need. |
| 6 | `Core/Layers/Types/Zoning/LotLevel.cs`, `PolishLookLayer.cs` (around lines 300–430) | How Placemaker levels lots today: per-column fronts, banks, walls, earthwork limit, lots shortened or left empty. The closest thing to your feature. | Study closely. Your planner replaces per-column fronts with one height per lot. |
| 7 | `Core/Layers/Roads/Terraces/BlockField.cs`, `TerraceField.cs`, `docs/proposals/s52.md` | Bank physics (spacing S = W·a ÷ (a − g)), clash resolution, quay retaining walls (switched off for road terraces since 19 October, "they never work"), and the 6% climb limit. | Study only. Whole-column banks make the `TerraceField.LimitBanks` and `Resolve` limiter unnecessary; neither is adapted. |
| 8 | `Core/Layers/Parking/LotGrading.cs`, `LotHill.cs` | Level tiers on a hill with masonry foot walls: the closest model for step option B. | Study. |
| 9 | `Core/Layers/Common/Sculpt.cs` | Turns a height field on a grid (4 m cells by default; callers pass 2 m) into sloped terrain patches. It does not prevent curtains by itself: the caller's bank limits do. | Copy with a cut: `Emit` no longer takes `LayerContext`, and the clip path is dropped. The port needed four small files, not a few helpers (ported into `Core/Terrain` in wave 2). |
| 10 | `Core/Build/TerraformRaster.cs`, `TerraformSurface.cs` | Turns the patches into a desired-ground grid (1.75 m cells) and the raise and lower masks. Pure code, only `Unity.Mathematics`, but it also needs `ReservedSpace.PointInPolygon`, `TerraformDabs.DistanceToBoundary` and `PlannedTerraform`. | Copy as is, with those three (all ported into `Core/Terrain`). |
| 11 | `Game/Build/BuildService.Terrain.cs`, `Game/Build/BuildToolSystem.cs` | Applies the ground with `TerrainSystem.ApplyBrush(TerraformingType.Shift, …)`, up to two mask textures per tile per pass (raise and lower); re-reads heights (`GetHeightData(true)`) after 6 frames; up to 4 passes within a 6 s cap to within 0.3 m (2% of texels may stay off); works out the texture orientation on the first pass. It also holds the zoning call (`ZoningFlags.Zone \| ZoningFlags.Marquee`). Zoning needs an active tool, because only the active tool's `applyMode` is honoured, so Placemaker applies it through its invisible `BuildToolSystem.cs`. | Adapt: extract the direct path (a few hundred lines) into your own small system, plus a cut-down `BuildToolSystem`. |
| 12 | `Core/Build/ZoneGuard.cs` | Zone cell rules (8 m cells) and which cells may be zoned. | Copy the parts you need. |
| 13 | `Game/Build/TerrainCullGuard.cs` | Guards a native game crash in the terrain's road-culling list after several builds (Placemaker notes the fix isn't fully proven). | Study only; not copied. This mod emits no nets, so the guard is unnecessary. |
| 14 | `Core/Build/TerrainMetrics.cs` | Counts curtains in a height grid: your success check. | Copy (for tests). |
| 15 | `Tests/Placemaker.Tests/RoadsTerraceTests.cs`, `RoadsBlockTerraceTests.cs` | How to unit-test a pure planner on synthetic slopes, with no game. | Study; copy the style. |
| 16 | `UI/src/index.tsx`, `UI/src/contract.ts`, `Game/UI/` | How a React panel talks to C# (values and triggers). | Study; build a much smaller panel. |
| 17 | `Placemaker.csproj` | Builds in two modes: standalone against the game's `Cities2_Data\Managed` DLLs, or the official toolchain (`CSII_TOOLPATH`), which is opt-in (`-p:UseToolchain=true`). Its default build also deploys to the game's Mods folder unless `-p:Deploy=false`, and runs npm unless `-p:SkipUI=true`. | Copy and trim; drop the deploy step (ours never deploys). |

**Patterns worth copying:**

- Keep the planner pure (no game types), so you can test it in seconds without launching the game.
- Plan on a worker thread from a snapshot; let only the latest request win.
- Change terrain with one `ApplyBrush` call per mask per tile (raise and lower), then measure and correct. Never fire hundreds of brush strokes (Placemaker's notes record that crawling at 2 FPS).
- Treat setting ids as save format: never rename them.
- Nothing is done until you have seen it in the game.

## 8. What's mine and what's reused

| Piece | Source | Credit |
|---|---|---|
| Stepped-lot planner (R1–R10, whole-column banks and wall-segment geometry) | **Written from scratch** | Mine |
| Bank rule (R4–R5) | **Written from scratch**; whole columns make the `TerraceField` limiter unnecessary | Mine; no `LimitBanks` or `Resolve` code adapted |
| Street picker tool, preview overlay, UI panel | Written from scratch, Placemaker as a pattern | Mine |
| Mod entry, registration, settings | Written from scratch, Placemaker as a pattern | Mine |
| Height field → terrain patches (`Sculpt.cs` + helpers) | Adapted (copied with a cut) | Placemaker, Jack Richards |
| Terraform raster (`TerraformRaster.cs`, `TerraformSurface.cs`) | Copied | Placemaker, Jack Richards |
| Terraform apply and verify | Adapted from `BuildService.Terrain.cs` | Placemaker, Jack Richards. The fallback brush and lake water there came from ParkManager (tomkpunkt): credit too if you copy them |
| Zoning call | Adapted from `BuildService.Terrain.cs` and `BuildToolSystem.cs` | Placemaker, Jack Richards; it follows Grid Road Generator's zoning (Ti4goc) |
| Zone cell rules | Original level-cell checks, informed by `ZoneGuard`'s 8 m grid, two-row and lone-column rules | Mine; Placemaker studied |
| Curtain metric | Copied from `TerrainMetrics.cs` | Placemaker, Jack Richards |
| Crash guard | Not copied; the mod emits no nets | None |
| Low-wall object definitions and named prefab lookup | Adapted from `BuildService.Emit.cs` and `SurfaceCloneSystem.cs`; build cadence follows this mod's `ZoneBuildSystem` | Placemaker, Jack Richards; object-definition pattern credits ParkingLotTool, KruemelGames |

For a portfolio, this split matters: the planner is the part you explain in an interview, so it must be yours.

## 9. Credit and licence checklist (GPL-3.0)

Placemaker is GPL-3.0. Copying or adapting its code makes your mod a derivative work, so:

1. License the **whole mod** under GPL-3.0 and ship the `LICENSE` file.
2. Keep the licence and credit lines at the top of every copied file, and add a line saying you changed it and when (GPL-3.0 section 5a). For example: `// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: direct terraform path only.`
3. Publish the full source on GitHub and link it from the mod page.
4. Add a **Credits** section to the README and the mod page: Placemaker by jackwrichards; ParkManager by tomkpunkt and Grid Road Generator by Ti4goc for any of their code inside what you copied; Harmony (MIT) if you use it.
5. Don't use Placemaker's name, logo or mod ID. The fork by Nimenta2707 warns that `Properties/PublishConfiguration.xml` still holds the original mod's ID; give yours a new one.
6. Courtesy, not required: open a GitHub issue telling the author what you're building and thanking them.

*This is a plain-language summary, not legal advice.*

## 10. Plan

| Step | What | Done when |
|---|---|---|
| M0 Learn | The standalone mod and code study are built. Reference screenshots and the first load in the game remain to be checked. | Your mod loads in your game, and you have a folder of reference screenshots. |
| M1 Spike (go/no-go) | The hotkey spike supplied terrain and zoning plumbing. Wave 4 replaces it with the picker and panel. Its 8%, 12% and 15% map checks and the Placemaker baseline still need to run; quays are out of v1. | You know (1) which v1 look works without curtains, and (2) whether houses stay on single pads. **If neither works, stop or switch scope.** |
| M2 Planner | Done: R1–R10 implementation and pure tests, including bank allowance, aprons, earthwork and obstacles, completed in wave 4 (2 November 2026). | Tests pass; plan under 250 ms; deterministic. In-game terrain checks remain part of M1/M3. |
| M3 Tool and build | Built in wave 4 (2 November 2026): street picker, preview, permanent-change prompt, terrain → zoning → optional walls. Awaiting the in-game test. | One street built end to end in your game from clicks alone. |
| M4 Panel | Built in wave 4 (2 November 2026): React panel, settings, messages and toolbar button. Awaiting the in-game test. | A friend uses it without help. |
| M5 Ship | README and credits done in wave 4. Screenshots, a one-minute video, manual acceptance and Paradox Mods publishing pending. | Published; success criteria in section 6 met. |

**Later, if people like it:** several streets at once; an area mode; placing the houses directly; row-house styles; translations.

## 11. Testing

- **Unit tests** (xUnit, .NET 9) on the pure planner: synthetic straight and curved streets at 4–20%, checking pad heights, step heights, lots left empty, and the same output for the same input.
- **Plan pictures:** optional small renderer that saves the plan as a PNG (Placemaker's `Tests/Placemaker.Gallery` is the model).
- **In game:** a saved test map with steep streets. For each build, check curtains, pad heights, house fit, zoned frontage and the longest freeze, and take a 45° screenshot.
- **Alongside Placemaker:** build on a street next to Placemaker terraces and check neither breaks the other.

## 12. Risks

| Risk | Why it matters | What to do |
|---|---|---|
| Houses ignore the pads | The game chooses building lot widths; one house over two pads ruins the look | Spike it first (M1). Fallback: place houses directly, as the Nimenta2707 fork does |
| Steps look bad | The heightmap may smear the bank or bury a garden wall | Test Planted bank and Low wall on the 8%, 12% and 15% streets; check the whole-column allowance and wall ends beside aprons |
| Game updates break it | `TerrainSystem.ApplyBrush` is public but undocumented; patches can change it | Log the game version; fail safely with a message, never a crash |
| Copied code drags in more code | `Sculpt.cs` needs geometry helpers; the apply code sits in a large partial class | Copy pure files whole; rewrite the apply loop small |
| Placemaker adds the same thing | It moves very fast: 1,827 commits from 13 October to 2 November | Keep scope tight and ship early. It's for fun, and it can live alongside |
| Scope creep | Many other projects compete for time | M1 decides go/no-go; stop if both spikes fail |
| Licence slip | A GPL breach is avoidable embarrassment | Follow the checklist in section 9 before the first public release |

## 13. Setup checklist

- Windows PC with Cities: Skylines II.
- Modding toolchain: not needed. The mod builds standalone against the game's DLLs, so `CSII_TOOLPATH` need not be set.
- .NET SDK 9 or newer for the solution (SDK 10 works) and the .NET 9 runtime for tests; the .NET 9 SDK itself is not needed. The mod targets .NET Framework 4.8 with C# 9 against the game's framework DLLs; Core is netstandard2.0. NuGet restore needs network access on the first build.
- Node.js 18+ and npm for the React panel. Run `npm ci` and `npm run build` in `src/SteppedStreets.UI` before the final mod build; the csproj picks up the built assets.
- Visual Studio 2022 or Rider, Git and a GitHub account.
- Optional: `ilspycmd` (a dotnet tool) to decompile `Game.dll` and read the engine's own code, which is how Placemaker's author worked out its engine notes. Community API reference: ps1ke's Cities Skylines 2 Modding Guide.

The repo holds `src/SteppedStreets` (the mod), `src/SteppedStreets.Core` (the pure planner and terrain code), `src/SteppedStreets.UI` (the React panel) and `tests/SteppedStreets.Tests`. Build the panel first, then build and test the solution from the repo root. The game's DLL folder defaults to `ManagedPath` in `build/GameReferences.props`; if the game is somewhere else, override it with `-p:ManagedPath=...`:

```
cd src/SteppedStreets.UI
npm ci
npm run build
cd ../..
dotnet build SteppedStreets.sln -c Release
dotnet test SteppedStreets.sln
dotnet build SteppedStreets.sln -c Release -p:ManagedPath="D:\<Steam library>\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
```

Mods load from `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SteppedStreets\`. Close the game, create that folder if needed, and copy **everything** in `src\SteppedStreets\bin\Release\` into it, keeping subfolders, Core DLL and panel assets. The log is `Logs\SteppedStreets.log` next to `Mods`. The build never deploys there: installing is done by hand. API references have been checked against game version 1.6.2f1; nothing has run in the game yet.

## 14. Open questions

1. Pad height: the middle of the lot's front, or the uphill corner so the downhill side gets a raised front like real hill houses? Answered for v0: the middle of the front (R3), kept in one function in the planner so it can change.
2. Which step looks survive the in-game test? The owner chose Planted bank and Low wall for v1; quays are out. Check wall burial at the bank foot and the fixed 4 m setback beside wider aprons.
3. Do houses stay on single pads? v1 zones the level cells; direct house placement remains a possible later fallback.
4. Final name (check Paradox Mods for a clash).
5. A donation link on the GitHub page and mod page: check Paradox Mods' rules first (later).

## Sources

- [Placemaker source code (jackwrichards, GPL-3.0)](https://github.com/jackwrichards/placemaker)
- [Placemaker on Paradox Mods](https://mods.paradoxplaza.com/mods/163638/Windows)
- [Placemaker fork "Interchange & City Designer" (Nimenta2707)](https://github.com/Nimenta2707/Cities-Skylines2)
- [Plop the Growables source (algernon-A)](https://github.com/algernon-A/PlopTheGrowables)
- [Cities Skylines 2 Modding Guide, API reference (ps1ke)](https://github.com/ps1ke/Cities-Skylines-2-Modding-Guide)
- [GNU GPL-3.0 text](https://www.gnu.org/licenses/gpl-3.0.html)
- ["Flat terrain only?" — Steam discussion](https://steamcommunity.com/app/949230/discussions/0/3877095833476954030)
- [CS2's weird terrain quirks — PC Gamer](https://www.pcgamer.com/cities-skylines-2s-weird-terrain-quirks-mean-youll-need-to-start-landscaping-immediately)
- [Building on hills with cut-and-fill roads (Berkeley lots set back with sloping driveways) — r/CitiesSkylines mirror](https://lr.in.psf.lt/r/CitiesSkylines/comments/17jrw0r/i_found_a_neat_way_to_build_on_hills_using_the)
- [City Planner Plays — YouTube](https://www.youtube.com/@CityPlannerPlays)
- [two dollars twenty — YouTube](https://www.youtube.com/@twodollarstwenty)

## Changes

Integration review, 2 November 2026: all four wave 4 branches are merged, and temporary contract stubs are removed. Zoning now checks the live built height instead of the target raster. The build rejects the entire operation before any brush if the outer blend or sampled brush margin reaches a building, another road or water. Narrow-lot advice was corrected because narrowing also reduces bank capacity. The Release output includes the GPL licence, credits, player guide, DLLs, panel and icon.

Remaining differences from the intended player flow: the overlay draws geometry but does not yet show per-pad height labels; individual heights are logged and empty-lot reasons appear in the panel. A click captures the whole named street, and Shift-click accepts a connected street when the union remains one unbranched chain. The 8 m apron and 24 m blend caps can exceed Bank grade on extreme sites. These are implementation limits, not passed acceptance criteria.

Draft 3, 2 November 2026. Wave 4 implementation and the owner's v1 decisions; the first in-game test is still pending.

- Status, §3 and §5.1: wave 4 built but untested in game; Planted bank and Low wall are the only v1 looks, with quays explicitly out. The toolbar button sits beside Zones; Ctrl+Shift+S opens or toggles the tool. Clear and Close do not undo terrain. Build order is terrain → zoning → optional walls.
- R2, §5.1 and §5.4: numeric Lot width 2–6 cells; Depth is an explicit advanced setting. Added advanced Narrowest width and Bank grade, retained Earthwork limit and Minimum grade, and corrected Highest step to 0.5–5.0 m. Listed defaults and the rebindable opening hotkey. Gentle-street exclusion follows the chosen minimum grade.
- R4, §5.3 and §5.4: the lower pad's step ceiling is min(Highest step, (W − 2) × 8 m × Bank grade). A 2-cell lower pad has no bank allowance; the last pad has no following bank. Replaced the old quay comparison with the whole-column allowance table.
- R5, §5.3 option A, §7 item 7 and §8: whole 8 m bank columns come from the lower pad's uphill end; a 3-cell pad keeps two zoned columns beside one bank column. The planner is original work, with no `TerraceField.LimitBanks` or `Resolve` adaptation and no separate limiter needed.
- R6, R7 and R9: recorded the 4–8 m apron, hillside bank and earthwork cap, shortened back depth around obstacles, and empty-lot reasons. The wall's fixed 4 m setback matches only the narrowest apron; exact wider-apron following remains a refinement.
- §5.3 option B: Low wall decorates the unchanged bank with unscaled EU_Wall02 garden-wall pieces; documented local Z, geometry-length lookup, logged 3 m fallback and the half-piece rule. It does not use the car-park profile's 0.8 m height deduction. Options C and D are historical research, not v1 features.
- §5.5, §6 and §12: removed obsolete Narrow/wall-look advice that implied a wall permits a higher step; added earthwork, blocked and short-end reasons and the two-look visual risk check. Kept manual acceptance separate from unit checks; zoning verifies one cell per run, and wall counts are emitted definitions without an object verification pass. Step acceptance includes both Highest step and the bank allowance.
- §7 item 13 and §8: TerrainCullGuard is not copied because the mod emits no nets. Corrected ZoneGuard from a claimed copy to studied rules; kept the copied curtain metric and added low-wall emitter/lookup provenance.
- §10: M2 done, M3 and M4 built in wave 4 on 2 November 2026 and awaiting in-game testing; M1's visual/baseline checks still pending; M5 README done, publishing and media pending. Removed the outdated no-toolbar status and required quay experiment.
- §13: SDK 9+ for the solution (SDK 10 works), .NET 9 runtime for tests, game's framework DLLs for the standalone net48 mod, Core netstandard2.0, Node.js 18+, `npm ci` and panel build before packaging. Added the UI folder and whole-output-folder manual install, including panel assets and log location; no automatic deployment.
- §14: recorded the chosen v1 looks and zoning, while retaining house fit, wider-apron wall ends, final name and publishing questions for manual review.

Draft 2, 2 November 2026. Row numbers refer to the fact check in `docs/STUDY-NOTES.md`; letters are the owner's decisions of the same day.

- §2: the 6% rule belongs to Terraced blocks, measured over 30 m of smoothed ground; Hillside terraces are contours by construction (#4).
- §2: road plans no longer zone; all zoning is in the Zoning category (#5, F).
- §2: Level the lots' column height is the ground 1 m inside the front edge, averaged with its neighbours (#8).
- R1: the 6% threshold is Terraced blocks'; the planner measures grade per lot (#4).
- R2–R4, §14 question 1: planner v0 facts: road half width and narrowest width as inputs, lots from the lower endpoint, sides follow the centreline, only the offending lot narrows, fresh run after a too-steep empty lot, pad height in one function (E).
- §5.3, R4: the step between lots of different widths is grade × the distance between their middles (B).
- §5.3 table, text under it, option C, R7, §7 item 7, M1: quays only from 2.6 m, switched off in Placemaker; v1 plans banks and the 0.9 m garden-wall prop; one hand-placed quay in M1 (#20, #21, #22, #28, #41, #67, A).
- §5.3, §7 item 2: the addendum numbers note 85 (and 86–88) twice; the heightmap note is at line 576 (#19).
- §5.3 option A: a Filler is a ramp between two levelled lots, not a deliberate gap (#24).
- §5.3 option B: 0.8 m is the step the wall takes, the wall is the unscalable 0.9 m garden-wall prop, the foot is min(2.5 m, 0.35 × median) on medians of 5.7 m or more, and 1.2 m is a distance (#25, #26, #27).
- R5, §7 item 7, §8: the bank limiter may be adapted from `TerraceField.LimitBanks` and `Resolve`; new §8 row (D).
- R8, §6: 0.3 m is the build's convergence tolerance (2% of texels may stay off by up to 0.75 m); pad levelness is measured after the build; Placemaker's cell test is about 0.1 m (#52, #68, #72, C).
- §7: 978 C# files (#31).
- §7 item 9, §8: Sculpt's default cell is 4 m, it does not prevent curtains itself, and the port needed a cut and four small files (#44, #45, #46, G).
- §7 item 10: the raster also needs `PointInPolygon`, `DistanceToBoundary` and `PlannedTerraform` (#48, G).
- §7 item 11, patterns: up to two masks per tile per pass, 4 passes, 6 s cap, 2% may stay off; zoning needs `BuildToolSystem.cs` (#50, #52, #54, F, G).
- §8: zoning call adapted from `BuildToolSystem.cs` too (#54, F).
- §7 item 17: toolchain mode is opt-in; Placemaker's default build deploys unless `-p:Deploy=false` (#62, G).
- §10: M0's hello mod exists but is untested in the game; M1 runs from a hotkey on the game's select tool; M2 done for R1–R4, R8 and R10 (E, H).
- §12: 1,827 commits (#70).
- §13: repo layout, solution build and test commands, `-p:ManagedPath`, SDK needs, no `CSII_TOOLPATH`, no deploying (#62, H).
