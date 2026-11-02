# Stepped Streets

*Product requirements document · a Cities: Skylines II code mod*

| Item | Detail |
|---|---|
| Owner | Tevin Benjamin |
| Status | Draft 4, 2 November 2026: the hillside neighbourhood generator replaces the one-street tool (changes listed at the end) |
| Purpose | A fun side project: my own standalone mod, open source, and a portfolio piece |
| Working name | Stepped Streets (check Paradox Mods for a clash before publishing) |
| Licence | GPL-3.0, because it reuses Placemaker code (section 9) |
| Code studied | Placemaker by jackwrichards, commit `e654b32` (1 November 2026) |

## 1. Summary

Stepped Streets lays out a whole hillside neighbourhood from one drawn area. The player outlines a piece of hill that touches
an existing road. The mod plans streets that run along the contour lines, gentle connectors that climb between them, a
connection to the existing road, level lots stepped down the hill with soft banks, a mix of zones, forest on ground too steep
to build, and trees on banks and in gaps. The result should look like a careful player built it by hand: no road up the fall
line, no cliffs, no torn terrain, no houses half sunk.

The layout generator is written from scratch. It reuses Placemaker's proven terrain plumbing and its road-emitting pattern
(GPL-3.0, credited).

## 2. The problem

On a hill, the game's grid and the player's quick roads go straight up the slope. Every growable building then flattens its
own footprint at its own height, and the frontage comes out lumpy: tilted yards, raw dirt steps between houses, buildings
half sunk or floating. Good builders avoid this by hand (two build videos the owner watched, summarised in section 5.2): they
keep off steep ground, lay streets on the contours, grade each lot before zoning, leave gaps on steep ground, terrace whole
blocks, and plant the banks. That takes hours per neighbourhood. Placemaker's Hillside terraces lay contour streets but grade
whole terraces, not stepped lots, and leave zoning to a separate tool.

The first version of this mod stepped the lots along one street the player picked. The owner rejected it after the first
in-game look: the street itself was still wrong for the hill. The generator fixes the layout, not only the lots.

## 3. Goals and non-goals

**Goals**

- **It looks like a professional player designed it** (the owner's bar): smooth streets that hug the contours, tidy level
  lots, even soft banks, forest on the steep ground. Nothing lumpy: no tilted yards, no raw dirt steps, no torn ground, no
  houses half sunk or floating. Section 2 describes the lumpy result the game gives without the mod.
- Draw an area; get a buildable plan in about a second: contour streets, connectors, a connection when a road is in reach, lots, zones,
  banks, forest and trees.
- Every road within the maximum road grade; contour streets average under 3 %.
- Level lots on pads, banks no steeper than the steepest-bank setting, no curtains in the ground.
- Three styles: Contour suburb (default), Terraced blocks, Mountain lane.
- A zone mix chosen by the player (shares, Random / Clumps / Even), like Placemaker's Zoning card.
- Deterministic: the same area, settings and seed give the same plan; Reroll gives another.
- Build safely: the ground, then the roads, then zoning, then trees and walls, never freezing the game for more than 2 s.

**Non-goals (this version)**

- Undo of a build. Terrain changes are permanent, like the game's own terrain tools.
- Placing buildings directly; footpaths and stairs; flat-ground grids (a flat area gets a message instead).
- Quay retaining walls (Placemaker found them unreliable). Garden walls only, off by default.
- The old one-street mode: removed; the generator covers it.

## 4. Who it's for

Players who build on hilly maps and want a hill town that looks hand-made without hours of terraforming. The owner first:
it must look good enough for screenshots and be explainable in an interview.

## 5. How it works

### 5.1 Player flow

1. Click the Stepped Streets button beside Zones in the bottom toolbar, or press Ctrl+Shift+S. The panel opens on the right.
2. Draw the area: left-click places corners on the ground; click the first corner (or press Enter, or the panel's Close
   area button) to close it. Drag a corner to reshape. Right-click removes the corner under the cursor (or the last one while
   drawing). Ctrl+Z undoes the last change. Esc clears the area; Esc with no area closes the tool. The panel shows the area
   in ha or km². The area does not have to touch a road: the generator connects to the nearest existing road within 400 m when it can, and otherwise builds a standalone network.
3. Pick a style and settings. The preview updates live: roads coloured by grade, lots coloured by zone, cut and fill shaded,
   trees and forest as dots, and a summary: road length, lots, zoned cells, earth moved, steepest road.
4. Reroll gives another layout from a new seed. Same seed and settings give the same layout.
5. Build, after the prompt "Ground changes are permanent, like the game's terrain tools. Build?". The result line replaces
   the summary.

### 5.2 Rules (from the two build videos)

| # | Rule |
|---|---|
| G1 | **Don't build on steep ground.** Ground steeper than Steepest buildable (default 15 %, range 8–30 %) on the smoothed survey gets no roads and no lots; it is planted as forest. About 10 % is the sweet spot. |
| G2 | **Roads follow the contours.** Contour streets are contour lines of the smoothed ground (σ 12 m), at heights ΔH = g × S apart, where S is Block depth (road spacing on the ground) and g the 60th-percentile slope of buildable ground. They are clipped to buildable land, smoothed (Chaikin), held to a minimum radius, and dropped when shorter than 80 m. A level that comes closer than 0.65 S to an accepted street is clipped there; where accepted streets are more than 1.7 S apart, an in-between level fills the gap. Their profile follows the ground with at most 3 % grade (Contour suburb) or stays level (Terraced blocks). |
| G3 | **Connectors climb gently.** Neighbouring contour streets are linked about every Link spacing along their shared length (every street gets at least one link). A link leaves and meets its streets at right angles and bends across the slope (an S-curve) until its length L satisfies ΔH ÷ L ≤ Max road grade (default 8 %, at most 12 %); where no S-curve fits, it is dropped rather than made steep. Junctions are T-junctions at 60° or more and at least 40 m apart. |
| G4 | **Connection.** When an existing road lies within 400 m of the area, one road joins the network to it, within Max road grade, attached to that road's edge or node (it may run outside the area). The area need not touch a road (owner, 2 November 2026): without a reachable road the network is built standalone and the panel says it is not connected. |
| G5 | **Validate and repair.** Every road is checked for grade, radius, junction angle and spacing, clearance from other roads, water, buildings, existing roads and wild ground. A failing road is dropped; roads cut off from the connection are dropped. |
| G6 | **Grade the lots.** Lots go along both sides of every new road (StepPlanner R1–R10: chord rectangles, pads at the street height at the middle of the lot front, earthwork limit, obstacle trimming). The ground is one field: road corridors (road plus 1.5 m shoulders, level across), then pads, each held level; between and around them the ground eases back to the hillside no steeper than Steepest bank (default 40 %). Where a pad cannot keep its height without a steeper bank, the clashing part of the pad yields and becomes bank. |
| G7 | **Give space.** On steeper ground lots get gaps: Auto leaves 1 cell where the ground across a lot is steeper than half the buildable limit and 2 cells above three quarters of it; the player can force 0, 1 or 2. Gaps are planted. |
| G8 | **Terrace whole blocks.** In Terraced blocks the streets are straighter and level, so each block side is one level terrace; links are straight connectors with no lots. |
| G9 | **Be deliberate on steep stretches.** Mountain lane lays one winding road that climbs at no more than Max road grade with hairpins at the edges of the area, sparse lots with 2-cell gaps, and forest everywhere else. |
| G10 | **Retaining walls sparingly.** Optional, off by default: the game's garden wall (EU_Wall02) along road shoulders where the cut beside the road is 0.8 m or more and no lot fronts the road. |
| G11 | **Landscape.** Trees on wild ground (dense), cut banks and lot gaps (medium), fill banks only beyond 12 m from a house (the view stays open), never on lots or roads. Density is a setting. |
| G12 | **Zones.** Each lot gets one zone from the mix: Random (seeded per lot), Clumps (runs of 3–5 neighbouring lots), Even (weighted round robin within one lot of the target). Only cells that come out level (within 0.3 m) are zoned. |
| G13 | **Budget.** 1 km² plans in under 2 s on a worker thread; the latest request wins. The same request and seed give the same plan. |

### 5.3 Build order

Placemaker grades the ground before it emits nets (`Game/Build/BuildService.cs`: Terraform → EmitNets → Apply → zone blocks →
zoning), so the build follows it:

1. Re-capture the site and re-plan on the worker; if the plan changed, show it again instead of building.
2. Ground: the field becomes one terraform raster, applied with calibrated Shift brush passes (TerrainBuildSystem) and
   verified within 0.3 m. Buildings and water are protected.
3. Roads: every road becomes net courses (short cubic pieces at the planned heights) emitted in one frame through the
   invisible build tool, attached to the existing road at the connection. If the game refuses them, stop.
4. Zoning: wait for the game's zone blocks along the new roads (at most 120 frames), then zone each zone's level cells.
5. Trees in batches, then garden walls if chosen.

### 5.4 Settings

| Setting | Default | Range |
|---|---|---|
| Style | Contour suburb | Contour suburb, Terraced blocks, Mountain lane |
| Road type | the game's small two-lane road | ground-level, non-highway road prefabs |
| Block depth (road spacing) | 90 m | 60–200 m |
| Max road grade | 8 % | 4–12 % |
| Steepest buildable ground | 15 % | 8–30 % |
| Link spacing | 200 m | 100–400 m |
| Zone mix | Residential Low 100 % | rows of zone and share; "Mix in another zone" |
| Mix mode | Clumps | Random, Clumps, Even |
| Lot width, lot depth | 3 cells, 3 cells | 2–6 cells each |
| Level the lots | on | |
| Most earth | 4 m | 1–8 m |
| Steepest bank | 40 % | 20–50 % |
| Space between lots | Auto | Auto, 0, 1, 2 cells |
| Retaining walls | off | |
| Trees, density | on, 50 % | 10–100 % |
| Seed | 1 | Reroll |

Saved property names are save format: existing names (LotWidthCells, NarrowestWidthCells, DepthCells, MinimumGradePercent,
HighestStep, BankGrade, Look, EarthworkLimit, StepStreetBinding) keep their meaning; the rest are new.

### 5.5 Messages

- "Draw the area on a hillside: click corners, then click the first corner to close it."
- "This area is nearly flat. Draw it on a hillside." (median slope under 3 %)
- "Too steep: under 5 % of the area is gentler than the steepest buildable ground." 
- "Not connected: no existing road within reach. The new streets are built on their own."
- "N lots left out: too much earthwork / blocked / lost to banks." and the share of the area left as forest.

## 6. Success criteria

Off-game, on synthetic terrain (uniform slopes of 8, 12 and 18 %, a cone hill, a ridge, a valley, a saddle and a noisy hill):

- every road within Max road grade; contour streets averaging under 3 %;
- radii, junction angles (≥ 60°) and junction spacing (≥ 40 m) respected; no road through water, buildings or existing roads
  except its planned attachment;
- lots only on buildable ground; pads level within 0.3 m on the planned ground; zero curtains through the raster; banks
  never steeper than the setting where the ground was changed;
- the same layout for the same seed; 1 km² planned in under 2 s;
- the gallery PNGs look like a hillside suburb a good player would build.

In game (manual, by the owner): a 0.5–1 km² area on an 8–20 % hillside builds without curtains, the roads follow the hill,
houses grow on level pads, and no build stage stalls the game for more than 2 s.

## 7. Placemaker code study guide

`docs/STUDY-NOTES.md` maps the files with line references. For this scope the important ones are: `Core/Layers/Roads/Terraces/*`
and `docs/proposals/s52.md` (contour streets, terrace spacing S = W·a ÷ (a − g); studied, not copied), `Game/Build/BuildService.cs`
(build order), `Game/Build/BuildService.Emit.cs` (net courses, attaching to an existing edge; adapted),
`Game/Build/TerrainCullGuard.cs` (the road-cull crash guard; copied now that the mod emits nets), `Core/Build/TerraformRaster.cs`
and `TerrainMetrics.cs` (copied earlier), `Game/Preview/SiteCapture.cs` (snapshot; adapted earlier).

## 8. What's mine and what's reused

| Piece | Source | Credit |
|---|---|---|
| Layout generator: survey, contour streets, connectors, connection, validation, lots, zone mix, grading field, landscaping | **Written from scratch** | Mine |
| Stepped-lot planner (R1–R10), reused for every road's lots | Written from scratch (v1) | Mine |
| Area tool, preview, panel, gallery renderer | Written from scratch, Placemaker as a pattern | Mine |
| Net courses for the roads | Adapted from `BuildService.Emit.cs` (`EmitNets`, `MakeCoursePos`) | Placemaker (jackwrichards); its header credits ParkManager (tomkpunkt) and Grid Road Generator (Ti4goc) |
| Road-cull crash guard | Copied from `TerrainCullGuard.cs` | Placemaker (jackwrichards) |
| Terraform raster, apply and verify; curtain metric; zoning call; invisible build tool | Copied or adapted earlier (see CREDITS.md) | Placemaker (jackwrichards) |

The generator is the part explained in an interview, so it is original; `LotLevel.cs` and `PolishLookLayer.cs` were never read
for code, and `TerraceField.LimitBanks` / `Resolve` were studied but not adapted (the field uses its own cone envelopes).

## 9. Credit and licence checklist (GPL-3.0)

1. The whole mod is GPL-3.0 and ships `LICENSE`.
2. Every copied or adapted file keeps its header and gets "Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by
   Tevin Benjamin, 2026-11: <what changed>."
3. Full source on GitHub, linked from the mod page.
4. CREDITS.md and the README name Placemaker, and ParkManager and Grid Road Generator for the code paths Placemaker took from
   them.
5. No Placemaker name, logo or mod ID.

## 10. Plan

| Step | What | Done when |
|---|---|---|
| M1 Generator | Core generator, tests on synthetic terrain, PNG gallery | Section 6's off-game criteria pass and the gallery looks right |
| M2 Area tool | Area drawing, live preview on a worker, the new panel | The plan appears in game within a second of closing an area |
| M3 Build | Ground, roads, zoning | One neighbourhood built end to end in the game |
| M4 Landscape and polish | Trees, walls, panel polish | Trees and walls appear; panel complete |
| M5 Docs | README, CREDITS, in-game test checklist | The owner can test without asking |

After each milestone: build, run all tests, install to the Mods folder, commit, report.

## 11. Testing

- **Unit tests** (xUnit, .NET 9) on the pure generator over the synthetic sites in section 6, plus the existing planner and
  terrain tests.
- **Gallery:** a test renders each synthetic site to a PNG over a hillshade (roads by grade, lots by zone, cut and fill,
  forest), written to `gallery/` (git-ignored).
- **UI:** `npm test` (state parsing) and `npm run typecheck`; the CSS lint runs in the bundle build.
- **In game:** the owner's checklist in `docs/IN-GAME-TEST.md`.

## 12. Risks

| Risk | Why it matters | What to do |
|---|---|---|
| Net courses misbehave (junctions not merging, refused pieces) | Roads are new to this mod | All pieces in one frame, ends at identical positions, log refused pieces before Apply; stop the build if roads fail |
| The road-cull crash after several builds | Placemaker hit it once it emitted nets | Copy TerrainCullGuard and reserve room before emitting |
| Houses ignore the pads | Buildings choose their lot widths | Zone only level cells; gaps on steep ground |
| Heightmap smear makes cells miss 0.3 m | 3.5 m texels | Pads are held 1.5 m beyond the zoned cells; zoning re-checks the live ground |
| Large areas are slow | Field and lots scale with area | Worker thread, latest request wins, grid coarsens past 1 km² |
| Game updates break private APIs | ApplyBrush and the cull guard are undocumented | Every game path in try/catch, logged, never crashing |

## 13. Setup checklist

- Windows PC with Cities: Skylines II (1.6.2f1 checked). No modding toolchain: the mod builds against the game's DLLs
  (`ManagedPath` in `build/GameReferences.props`).
- .NET SDK 9 or newer (SDK 10 works) and the .NET 9 runtime for tests; Node.js 18+.
- Build: `npm ci` once in `src/SteppedStreets.UI`, then `dotnet build SteppedStreets.sln -c Release` (also builds the panel
  bundle into `src/SteppedStreets/bin/Release`). Tests: `dotnet test SteppedStreets.sln`, `npm test`, `npm run typecheck`.
- Install: with the game closed, copy everything in `src/SteppedStreets/bin/Release` to
  `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SteppedStreets`. Logs: `Logs\SteppedStreets.log`
  and `Logs\UI.log`.

## 14. Open questions

1. Which road prefab names exist in the owner's game? The panel lists them from the game; the default matches "Small Road".
2. Do zone blocks along curved contour streets line up with the pads well enough? The in-game test decides; the fallback is
   narrower lots on tight curves.
3. Which tree prefabs look right on banks? The build picks conifers for forest and banks and broadleaf trees for gaps, by name.
4. Final name and publishing.

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

Draft 4, 2 November 2026. The owner rejected the one-street tool after the first in-game look and asked for a generator that lays out a whole hillside neighbourhood from a drawn area (his v2 hand-off prompt, approved the same day; it supersedes the wave 5 spec in `docs/HILLSIDE-NEIGHBOURHOODS.md`).

- §1–§4 rewritten for the neighbourhood generator; the non-goal "laying out new roads or whole neighbourhoods" is lifted; the street picker is removed.
- §5.1: area drawing (corners, drag, right-click removes, Ctrl+Z, Esc), live preview, Reroll, the permanent-change prompt. The area need not touch a road (owner's follow-up the same day); the connection is best effort within 400 m.
- §5.2: rules G1–G13 from the two build videos (steep ground left wild, contour streets, gentle connectors, connection, validation, graded lots in one ground field, gaps, terraced blocks, mountain lane, sparse garden walls, landscaping, zone mix, budget). Defaults follow the owner's prompt: steepest buildable 15 %, max road grade 8 %.
- §5.3: build order ground → roads → zone blocks → zoning → trees → walls, taken from Placemaker's BuildService.
- §5.4: new settings (style, road type, block depth, max road grade, steepest buildable, link spacing, zone mix, mix mode, level the lots, lot spacing, trees and density, seed); existing property names keep their meaning.
- §6, §10, §11: off-game criteria on eight synthetic sites, a PNG gallery, milestones M1–M5 with an install after each.
- §7–§9: net courses adapted from `BuildService.Emit.cs` with its ParkManager and Grid Road Generator credits; TerrainCullGuard copied now that the mod emits nets; the generator and its grading field are original.

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
