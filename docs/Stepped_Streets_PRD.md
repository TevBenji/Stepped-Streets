# Stepped Streets

*Product requirements document · a Cities: Skylines II code mod*

| Item | Detail |
|---|---|
| Owner | Tevin Benjamin |
| Status | Draft 2, 2 November 2026 (changes listed at the end) |
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
- Gentle streets under 6%: they keep the game's normal behaviour.
- New 3D assets: walls use the game's own pieces.
- Translations beyond English (later, if people use it).

## 4. Who it's for

- **The detailer** who wants the San Francisco look on a steep street without terraforming lot by lot.
- **The player who always flattens the hill** (me): keep the hill, click once, done.
- **The Placemaker user** who built hillside terraces and wants houses on the steep link roads its terraces skip.

## 5. How it works

### 5.1 Player flow

1. Click the Stepped Streets button in the toolbar.
2. Hover a street: it highlights and shows its grade ("12% climb"). Streets under the minimum grade show grey.
3. Click to select it. Shift-click adds the next segment of the same street.
4. Pick the side (both, left, right), the zone (for example Residential Low), lot width (Narrow or Standard) and step look (Planted bank, Stone wall, Concrete wall).
5. The preview draws each pad, its height, each step and wall, and any lot left empty with the reason. A summary line: "14 lots, steps 1.1–1.9 m, 2 lots left empty (too steep for this width)".
6. Build. The panel says first that ground changes are permanent, like the game's own terrain tools.

### 5.2 Rules

Numbers marked \* are starting values; the spike (milestone M1) sets the real ones.

| # | Rule |
|---|---|
| R1 | Works where the selected street climbs at least the **Minimum grade** (6%\*, the same threshold Placemaker's Terraced blocks use; the planner measures it over each lot's stretch of street). Flatter stretches are left alone. |
| R2 | Lots are laid along the street from the downhill end (the lower of its two endpoints), starting at the kerb (the planner takes the road's half width), each **Lot width** cells wide (8 m cells) and the zone's usual depth (2 to 6 cells). Left and right follow the direction of the street's centreline as the game gives it. |
| R3 | Each lot becomes **one level pad**: one height across its whole width and depth, the street's height at the middle of the lot's front\*. The planner keeps this in one function so it can change (section 14, question 1). |
| R4 | The step to the next lot is grade × the distance between the two pads' middles: grade × lot width when both lots are the same width (section 5.3). If it is taller than the step look allows, that lot alone is narrowed, down to the narrowest width the zone permits (a planner input), so widths may alternate (3, 2, 3, 2); otherwise it is left empty and the preview says why. After a lot left empty as too steep, the next pad starts a fresh run, and the empty strip holds the drop as a future bank. |
| R5 | Each step is built in the chosen look (bank, stone wall, concrete wall) inside the side clearance, and the ground never jumps steeper than the engine can draw (section 5.3). The bank limiter may be adapted, with credit, from Placemaker's `TerraceField.LimitBanks` and `Resolve`; only `LotLevel.cs` and `PolishLookLayer.cs` are off limits. |
| R6 | The strip between the kerb and the pad (the driveway apron) is graded so the front meets the road. The mismatch at the lot's corners is up to grade × lot width ÷ 2. |
| R7 | The back of each pad meets the hillside with a bank no steeper than the bank slope\*. v1 plans no retaining walls: Placemaker plans quay walls only where the drop is 2.6 m or more (`MinWall`) and has switched them off for road terraces because "they never work"; quays are only a hand-placed experiment in M1. Total cut or fill is capped by an earthwork limit. |
| R8 | Zone cells are only placed on pad cells that came out level (within 0.3 m, a planner parameter). 0.3 m is the terrain build's convergence tolerance, which lets 2% of texels stay off by up to 0.75 m, so a pad's levelness needs its own measurement after the build (section 6). Placemaker's own zone-cell levelness test is about 0.1 m. |
| R9 | Never touches existing buildings, other roads or water; never goes deeper than the frontage. |
| R10 | Same input, same plan. Planning a 500 m street takes under 250 ms (Placemaker's budget for a 5 ha area). |

### 5.3 The hard part: the step between two lots

Step height = street grade × lot width, for two lots of the same width. With lots 2 or 3 cells wide (Placemaker's Suburb zoning style uses Residential Low lots 2–3 cells wide):

| Grade | 2 cells (16 m) | 3 cells (24 m) | Ground alone? | Quay wall? |
|---|---|---|---|---|
| 6% | 0.96 m | 1.44 m | Probably | No (under 2.6 m) |
| 8% | 1.28 m | 1.92 m | Borderline | No |
| 10% | 1.60 m | 2.40 m | Unlikely | No |
| 12% | 1.92 m | 2.88 m | No | 3 cells only |
| 15% | 2.40 m | 3.60 m | No | 3 cells only |

Pad heights sit at the middle of each lot's front (R3), so between lots of different widths the step is grade × the distance between their middles: a 3-cell lot next to a 2-cell lot steps grade × 20 m (half of 24 m plus half of 16 m), not grade × 16 m. Narrowing one lot saves only half a cell of step.

**Why this is hard.** The game's heightmap has texels about 3.5 m apart, so a sharp jump draws as a stretched vertical sheet (a "curtain"). Placemaker's notes treat a jump steeper than about 50% across one texel as a tear, and warn that curtains can show from smaller jumps too (`docs/RESEARCH-NOTES-ADDENDUM.md` note 85 at line 576, since the file numbers two notes 85; `TerraceOptions.cs`). Quay walls, Placemaker's retaining wall, only rise where the drop is 2 m or more (the game's own limit), and Placemaker plans them only from 2.6 m (`BlockField.MinWall`, the threshold the table uses) and keeps them only above 2.2 m. Placemaker switched them off for road terraces on 19 October ("they never work"), and its Zoning tool's walls are off by default, so v1 is designed around planted banks and the game's 0.9 m garden-wall prop. Most steps on real steep streets fall between 1 and 2.5 m: too tall for bare ground, too short for a quay wall.

**Options to test in the spike:**

- **A. Planted bank strip:** a narrow unzoned strip between two lots holding a planted bank. Costs some lot width. Placemaker's nearest thing is `LotLevel.Lot.Filler`: an unzoned gap between two levelled lots, graded as a ramp between them; its gaps come from empty shares and leftovers, not by design.
- **B. Low masonry wall plus short bank:** Placemaker's terraced car parks put a wall at the foot of each bank that takes 0.8 m of the step. The wall is the game's 0.9 m garden-wall prop, which cannot be scaled. The foot sits min(2.5 m, 0.35 × median) off the lower tier's kerb, walls exist only on medians of 5.7 m or more, and the ground steps up 1.2 m behind the wall (a distance, not a height) so the heightmap's smear doesn't bury it (`FootWall`, `FootClear`, `FootSmear` in `Core/Layers/Parking/LotGrading.cs`; the prop is in `PlanBuilder.cs`).
- **C. Paired pads:** two lots share a pad so steps come every other lot at 2.6 m or more, tall enough for a quay wall. Since Placemaker found quays unreliable, this is only a hand-placed experiment in M1: the owner places one quay by hand at a paired-pad step to judge the look.
- **D. Let the house hide it:** pre-grade the pads and leave the last half metre or so to the building's own flattening.

**The second unknown: do houses use the pads?** The game picks which building grows and how wide its lot is. If one house spans two pads, the effect breaks. Options: zone each pad exactly and vary the depth or zone type between neighbours; leave a one-column gap; or place the houses directly. The Placemaker fork by Nimenta2707 places buildings itself and marks them historical so they keep their model, the way Plop the Growables does (its README says it isn't tested in the game yet).

### 5.4 Settings

| Setting | Choices | Default | Notes |
|---|---|---|---|
| Side | Both, Left, Right | Both | |
| Zone | The game's zone types | Residential Low | Row houses suit steep streets best |
| Lot width | Narrow, Standard | Narrow | Narrow = the zone's smallest width |
| Step look | Planted bank, Stone wall, Concrete wall | Stone wall\* | Final list depends on the spike |
| Advanced: Minimum grade | 4–12% | 6%\* | |
| Advanced: Highest step | 0.5–3.0 m | set by the spike | Per look |
| Advanced: Earthwork limit | 1–8 m | 4 m\* | Cut or fill per pad |

### 5.5 Messages

- "This street is flatter than 6%: nothing to step here."
- "2 lots left empty: too steep for lots this wide. Try Narrow lots or a wall look."
- "Ground changes are permanent, like the game's terrain tools. Build?"

## 6. Success criteria

Measured on a test map with straight streets at 8%, 12% and 15%:

- **No curtains** inside the build. Measure with a copy of Placemaker's curtain metric (`Core/Build/TerrainMetrics.cs`) plus a 45° screenshot.
- **Every pad level** within 0.3 m, measured on the ground after the build (the build's own stop rule lets 2% of texels stay off by up to 0.75 m), and every step within the look's Highest step.
- **Houses sit on one pad each** in at least 9 of 10 lots after the street fills up.
- **At least as much frontage zoned** as Placemaker's Zoning tool on the same street, measured in M1 as the baseline.
- **Anybody can use it:** two friends who play CS2 get a stepped street built without help on their first try.
- **No long freezes:** the build never stalls the game for more than 2 seconds.
- **Safe to remove:** unsubscribing leaves the terrain and zoning as built, and the save loads without errors.

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
| 7 | `Core/Layers/Roads/Terraces/BlockField.cs`, `TerraceField.cs`, `docs/proposals/s52.md` | Bank physics (spacing S = W·a ÷ (a − g)), clash resolution, quay retaining walls (switched off for road terraces since 19 October, "they never work"), and the 6% climb limit. | Study the maths; reuse the ideas. The bank limiter (`TerraceField.LimitBanks`, `Resolve`) may be adapted with credit (R5). |
| 8 | `Core/Layers/Parking/LotGrading.cs`, `LotHill.cs` | Level tiers on a hill with masonry foot walls: the closest model for step option B. | Study. |
| 9 | `Core/Layers/Common/Sculpt.cs` | Turns a height field on a grid (4 m cells by default; callers pass 2 m) into sloped terrain patches. It does not prevent curtains by itself: the caller's bank limits do. | Copy with a cut: `Emit` no longer takes `LayerContext`, and the clip path is dropped. The port needed four small files, not a few helpers (ported into `Core/Terrain` in wave 2). |
| 10 | `Core/Build/TerraformRaster.cs`, `TerraformSurface.cs` | Turns the patches into a desired-ground grid (1.75 m cells) and the raise and lower masks. Pure code, only `Unity.Mathematics`, but it also needs `ReservedSpace.PointInPolygon`, `TerraformDabs.DistanceToBoundary` and `PlannedTerraform`. | Copy as is, with those three (all ported into `Core/Terrain`). |
| 11 | `Game/Build/BuildService.Terrain.cs`, `Game/Build/BuildToolSystem.cs` | Applies the ground with `TerrainSystem.ApplyBrush(TerraformingType.Shift, …)`, up to two mask textures per tile per pass (raise and lower); re-reads heights (`GetHeightData(true)`) after 6 frames; up to 4 passes within a 6 s cap to within 0.3 m (2% of texels may stay off); works out the texture orientation on the first pass. It also holds the zoning call (`ZoningFlags.Zone \| ZoningFlags.Marquee`). Zoning needs an active tool, because only the active tool's `applyMode` is honoured, so Placemaker applies it through its invisible `BuildToolSystem.cs`. | Adapt: extract the direct path (a few hundred lines) into your own small system, plus a cut-down `BuildToolSystem`. |
| 12 | `Core/Build/ZoneGuard.cs` | Zone cell rules (8 m cells) and which cells may be zoned. | Copy the parts you need. |
| 13 | `Game/Build/TerrainCullGuard.cs` | Guards a native game crash in the terrain's road-culling list after several builds (Placemaker notes the fix isn't fully proven). | Copy. |
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
| Stepped-lot planner (R1–R10 except the bank limiter, step options) | **Written from scratch** | Mine |
| Bank limiter (R5) | Adapted from `TerraceField.LimitBanks` and `Resolve` | Placemaker, Jack Richards |
| Street picker tool, preview overlay, UI panel | Written from scratch, Placemaker as a pattern | Mine |
| Mod entry, registration, settings | Written from scratch, Placemaker as a pattern | Mine |
| Height field → terrain patches (`Sculpt.cs` + helpers) | Adapted (copied with a cut) | Placemaker, Jack Richards |
| Terraform raster (`TerraformRaster.cs`, `TerraformSurface.cs`) | Copied | Placemaker, Jack Richards |
| Terraform apply and verify | Adapted from `BuildService.Terrain.cs` | Placemaker, Jack Richards. The fallback brush and lake water there came from ParkManager (tomkpunkt): credit too if you copy them |
| Zoning call | Adapted from `BuildService.Terrain.cs` and `BuildToolSystem.cs` | Placemaker, Jack Richards; it follows Grid Road Generator's zoning (Ti4goc) |
| Zone cell rules, crash guard, curtain metric | Copied | Placemaker, Jack Richards |

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
| M0 Learn | Build a "hello" mod that logs on load and shows a toolbar button. Read study-guide items 1–4. Watch two or three hill builds by City Planner Plays and two dollars twenty and screenshot how they handle steep frontages: lot widths, step heights, wall types. The "hello" mod exists (it logs on load; no toolbar button yet) but has not been loaded in the game yet. | Your empty mod loads in your game, and you have a folder of reference screenshots. |
| M1 Spike (go/no-go) | On a test map with 8%, 12% and 15% streets: hard-code stepped pads using the copied raster and apply code (no UI), triggered by a hotkey on the street selected with the game's own select tool (no custom tool yet; M3 adds the picker). Try step options A–D; for C, place one quay by hand at a paired-pad step to judge the look. Zone the pads and let houses grow. Record Placemaker's Zoning tool on the same streets as the baseline. | You know (1) which step look works without curtains and its highest step, and (2) which way gets houses onto single pads. **If neither works, stop or switch scope.** |
| M2 Planner | Write the pure planner for R1–R10 with unit tests on synthetic slopes. Done for R1–R4, R8 and R10 (2 November 2026); R5–R7 and R9 remain. | Tests pass; plan under 250 ms; deterministic. |
| M3 Tool and build | Street picker, preview, build (terrain, walls, zoning), permanent-change prompt. | One street built end to end in your game from clicks alone. |
| M4 Panel | Small React panel with the settings in 5.4 and the messages in 5.5. | A friend uses it without help. |
| M5 Ship | Before/after screenshots and a one-minute video, README with credits, Paradox Mods page. | Published; success criteria in section 6 met. |

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
| Steps of 1–2.5 m look bad | Too tall for bare ground, too short for quay walls | Test options A–D in M1; narrow lots on steeper streets |
| Game updates break it | `TerrainSystem.ApplyBrush` is public but undocumented; patches can change it | Log the game version; fail safely with a message, never a crash |
| Copied code drags in more code | `Sculpt.cs` needs geometry helpers; the apply code sits in a large partial class | Copy pure files whole; rewrite the apply loop small |
| Placemaker adds the same thing | It moves very fast: 1,827 commits from 13 October to 2 November | Keep scope tight and ship early. It's for fun, and it can live alongside |
| Scope creep | Many other projects compete for time | M1 decides go/no-go; stop if both spikes fail |
| Licence slip | A GPL breach is avoidable embarrassment | Follow the checklist in section 9 before the first public release |

## 13. Setup checklist

- Windows PC with Cities: Skylines II.
- Modding toolchain: not needed. The mod builds standalone against the game's DLLs, so `CSII_TOOLPATH` need not be set.
- .NET SDK 8 or newer for the mod (it targets .NET Framework 4.8 with C# 9, like Placemaker). The unit tests target .NET 9, so they need SDK 9 or newer (SDK 10 works) and the .NET 9 runtime; the .NET 9 SDK itself is not needed.
- Node.js 18+ for the React panel.
- Visual Studio 2022 or Rider, Git and a GitHub account.
- Optional: `ilspycmd` (a dotnet tool) to decompile `Game.dll` and read the engine's own code, which is how Placemaker's author worked out its engine notes. Community API reference: ps1ke's Cities Skylines 2 Modding Guide.

The repo holds `src/SteppedStreets` (the mod), `src/SteppedStreets.Core` (the pure planner and terrain code) and `tests/SteppedStreets.Tests`. Build and test from the repo root. The game's DLL folder defaults to `ManagedPath` in `build/GameReferences.props`; if the game is somewhere else, override it with `-p:ManagedPath=...`:

```
git clone https://github.com/jackwrichards/placemaker   # reference copy
dotnet build SteppedStreets.sln
dotnet test SteppedStreets.sln
dotnet build SteppedStreets.sln -p:ManagedPath="D:\<Steam library>\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
```

Mods load from `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\<mod name>`, and logs go to the `Logs` folder next to it. The build never deploys there: installing is done by hand.

## 14. Open questions

1. Pad height: the middle of the lot's front, or the uphill corner so the downhill side gets a raised front like real hill houses? Answered for v0: the middle of the front (R3), kept in one function in the planner so it can change.
2. Which step looks survive the spike?
3. Zone the pads, or place the houses directly?
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
