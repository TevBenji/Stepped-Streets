# Stepped Streets

Stepped Streets is a Cities: Skylines II code mod by Tevin Benjamin (TevBenji). Draw an area on a hillside and it lays out a
whole neighbourhood the way a careful builder would: streets that run along the contour lines, gentle connectors that climb
between them, a connection to the nearest road, level lots stepped down the hill with soft banks, a mix of zones, forest on
ground too steep to build, and trees on the banks and in the gaps.

**Status:** version 2 (the neighbourhood generator), 2 November 2026. Built against game version **1.6.2f1**. The generator is
tested off-game on eight synthetic hillsides; the in-game test is next ([docs/IN-GAME-TEST.md](docs/IN-GAME-TEST.md)).

## Play

1. Click **Stepped Streets** in the bottom toolbar beside **Zones**, or press **Ctrl+Shift+S**. The panel opens on the right.
2. **Draw the area** on the ground: left-click places corners; click the first corner (or press **Enter**, or **Close area**)
   to close it. Drag a corner to reshape; drag the middle of an edge to add a corner. **Right-click** removes the corner under
   the cursor (or the last one while drawing). **Ctrl+Z** undoes. **Esc** clears the area; Esc again closes the tool.
   The area does not have to touch a road: the mod connects to the nearest existing road within 400 m when it can, and
   otherwise builds the streets on their own. A group of streets the connection can't reach (often the hilltop) is built
   too, and the panel says so, so you can join it to the city with a road of your own.
3. **Pick a style and settings.** The plan appears a moment after the area closes and refreshes after every change: roads
   coloured by grade (grey under 3 %, yellow under 6 %, orange up to the maximum), lots coloured by zone, cut shaded brown
   and fill tan, trees as dots. The panel sums it up: road length, lots, zoned cells, earth moved, steepest road.
4. **Reroll** gives another layout from a new seed. The same seed and settings give the same layout.
5. **Build**, after **"Ground changes are permanent, like the game's terrain tools. Build?"**. The build grades the ground,
   lays the roads, waits for the game's zone blocks, zones each lot's level cells, then plants trees and adds walls.

## Styles

| Style | What you get |
|---|---|
| Contour suburb (default) | Streets on the contours, linked by diagonal ramps and S-curves, every lot on its own level pad, loops and cul-de-sacs. |
| Terraced blocks | Straighter, level contour streets so each block side is one terrace; plain connectors with no lots on them. |
| Mountain lane | One winding road that climbs with hairpins, sparse separated lots, forest everywhere else. |

## Settings

One cell is **8 m**.

| Setting | Range | Default | What it controls |
|---|---|---|---|
| Style | see above | Contour suburb | The layout. |
| Road type | the game's ground roads that zone | Small Road | The road prefab for every new street. |
| Block depth | 60–200 m | 90 m | Road spacing on the ground between neighbouring contour streets. |
| Max road grade | 4–12 % | 8 % | No new road is steeper. Connectors bend across the slope to stay under it. |
| Steep hills | on/off | off | Steep hills and mountains: buildable ground up to 60 % and earthwork up to 12 m; connectors may join at 35° and switch back. Turning it on lifts Steepest buildable to at least 50 % and Most earth to at least 8 m. |
| Steepest buildable | 8–30 % (60 % with Steep hills) | 15 % | Steeper ground (judged on the hill, not its bumps) gets no roads or lots and is planted as forest. |
| Zone mix | rows of zone and share | Residential Low | Each lot's zone; **Mix in another zone** adds a row. |
| Mix mode | Random, Clumps, Even | Clumps | Random per lot, runs of 3–5 neighbouring lots, or an even split. |
| Lot width, lot depth | 2–6 cells | 3, 3 | The lots along every road (a lot on steep ground may come out one cell shallower). |
| Level the lots | on/off | on | Grade each lot into a level pad. Off: only the roads are graded. |
| Most earth | 1–8 m (12 m with Steep hills) | 4 m | A lot that needs more cut or fill is left out. |
| Steepest bank | 20–50 % | 40 % | No slope the grading makes is steeper. |
| Space between lots | Auto, 0, 1, 2 cells | Auto | Auto leaves gaps on steeper ground; gaps are planted. |
| Retaining walls | on/off | off | The game's garden wall where a road runs in a cutting 0.8–3 m deep. |
| Trees, tree density | on/off, 10–100 % | on, 50 % | Forest on steep ground, trees on cut banks and in gaps; grass below houses keeps the view. |
| Seed | 1–99999 | 1 | Which layout; Reroll picks a new one. |

## How it plans (the short version)

The generator follows the rules from two creator videos on building on hills. It surveys the ground on a 4 m grid and smooths
it, so small lumps don't bend streets. It traces contour lines a constant height apart, so neighbouring streets sit about one
block depth apart on the ground, and keeps the ones on buildable land, smoothed to a gentle curve. It links neighbouring
streets with ramps that leave and meet them nearly square and climb across the slope at no more than the maximum grade. It
connects to the nearest road, then checks every rule again (grade, radius, junction angle and spacing, clearance) and drops
anything that fails. Lots come from the stepped-lot planner. The ground is one field: road corridors and lots are held level,
and everything around them eases back to the hillside no steeper than the steepest bank. Where two levels would need a
steeper bank, the lot gives way. Details: [the PRD](docs/Stepped_Streets_PRD.md), section 5.2.

## Known limits

- Terrain changes are permanent; nothing in the mod undoes a build.
- An area is at most 2 km on a side.
- On true mountain slopes (40–50 %), even with Steep hills on, the plan keeps the gentler streets and the lots that fit;
  contour streets that no connector can reach without breaking the grade are left out rather than built as roads to nowhere.
- Houses grow only where the game has demand for that zone; the panel says when a chosen zone has none yet.
- Zoning verification checks one game cell per run of cells, so its count is runs, not cells. Tree and wall reports count the
  definitions handed to the game, not the objects that survived its checks.
- The road build counts the pieces the game created; pieces it refuses are logged, not retried.
- English only.

## Build and test

Windows, the game's Managed DLLs, **.NET SDK 9+** (SDK 10 works) with the **.NET 9 runtime**, and **Node.js 18+**. The mod
targets .NET Framework 4.8 with C# 9; Core targets netstandard2.0 and uses only Unity.Mathematics.

```powershell
Set-Location src/SteppedStreets.UI
npm ci
Set-Location ../..
dotnet build SteppedStreets.sln -c Release   # also builds the panel bundle into src/SteppedStreets/bin/Release
dotnet test SteppedStreets.sln
Set-Location src/SteppedStreets.UI; npm test; npm run typecheck
```

`dotnet test` also renders the gallery: one PNG per synthetic hillside in `gallery/` (git-ignored), drawn like the preview.

The default `ManagedPath` is in `build/GameReferences.props`; override it with
`-p:ManagedPath="<Steam library>\steamapps\common\Cities Skylines II\Cities2_Data\Managed"`. Building never installs the mod.

## Install by hand

1. Close the game.
2. Copy **everything** in `src\SteppedStreets\bin\Release\` (DLLs, `SteppedStreets.mjs`, `SteppedStreets.css`, `images\`,
   LICENSE, CREDITS.md, README.md) into
   `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SteppedStreets\`, keeping subfolders.
3. Start the game. `Logs\SteppedStreets.log` (next to `Mods`) should say **"Stepped Streets loading"**; the panel's errors go to
   `Logs\UI.log`.

## Credits and licence

- **Placemaker by jackwrichards (GPL-3.0):** terrain plumbing (raster, apply and verify, curtain metric), the invisible build
  tool, zoning, object definitions, the net-course emitter the road build adapts, and the road-cull crash guard.
- **ParkManager by tomkpunkt** and **Grid Road Generator by Ti4goc (GPL-3.0):** the net-course and zoning patterns Placemaker
  credits, carried over with the code that follows them.
- **ParkingLotTool by KruemelGames (GPL-3.0):** the object-definition pattern credited in Placemaker's emitter.
- **CS2-NetworkTools by Luca Rager (MIT):** the vanilla UI lookup pattern credited in Placemaker's UI.

The layout generator, its grading field and the stepped-lot planner are original work. File-level provenance is in
[CREDITS.md](CREDITS.md). Stepped Streets is licensed under the [GNU GPL version 3](LICENSE).
