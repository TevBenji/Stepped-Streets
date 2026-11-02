# M1 spike: stepping one street by hotkey

The spike has no tool, panel or preview. You select a street with the game's own select tool and press a hotkey. The
mod plans level pads, builds the ground with a planted bank at each step (PRD option A) on whole columns taken from the
lower pad, zones the cells that came out level, and logs every stage. Ground changes are permanent, so work on a test save.

## Deploy by hand

1. `dotnet build SteppedStreets.sln -c Release` in the repo.
2. Close the game. Copy `SteppedStreets.dll` and `SteppedStreets.Core.dll` from `src\SteppedStreets\bin\Release\`
   to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SteppedStreets\` (make the folder).
3. Start the game. `Logs\SteppedStreets.log` (next to `Mods`) should say "Stepped Streets loading".

To remove it, delete that folder. The terrain and zoning it built stay.

## Hotkey and settings

**Ctrl+Shift+S**: step the selected street. To rebind it, or if the game shows a conflict, go to Options → Stepped Streets.

| Setting | Default | What it does |
|---|---|---|
| Side | Both | Which side gets pads. Left and right follow the direction the game drew the street in. |
| Lot width (cells) | 3 | Pad width along the kerb. 4 or 6 is a paired pad (two lots on one pad, PRD option C). |
| Narrowest width (cells) | 3 | How far a pad may narrow to keep its step under the highest step. At 2 the spike stops: a 2-cell pad has no room for a bank column plus two zoned columns. |
| Depth (cells) | 3 | Cells back from the road. |
| Minimum grade | 6% | Flatter stretches are left alone. |
| Highest step (m) | 3.2 | The tallest step allowed, 0.5 to 5.0 m. A taller one narrows the pad, or leaves the lot empty. The spike caps it at (narrowest width − 2) × 8 m × bank grade, so a pad of the narrowest width keeps two zoned columns beside its bank: 3.2 m for 3 cells at 0.40. |
| Bank grade | 0.40 | Slope of the bank between two pads. Each bank takes whole 8 m columns from the lower pad's uphill end, as many as the step needs at this grade (one column up to 3.2 m at 0.40, two up to 6.4 m). |
| Zone | Residential Low | Zone name, matched without case: an exact match first, then the first name that contains it. |

## What the log lines mean (all start "Stepped Streets:")

- `street of N edges (M in its aggregate), L m long, R m rise, grade G%, half width W m, P points`: what was read.
  If N is less than M, part of the street did not chain (a branch). A grade far from what you drew means the wrong street.
- `nothing is selected`, `the selection (Entity(…)) is neither a road edge nor a street`, or `the selected street
  (Entity(…)) has no road edges to step`: nothing usable is selected. Selecting a road edge or the street itself both work.
- `the narrowest width (2 cells) leaves no room for a bank column plus two zoned columns`: the spike stops. Set the lot
  width and narrowest width to 3 or more.
- `plan: …`: pads, steps (lowest to tallest), lots left empty because they were too steep, flat stretches, and the
  settings used. `highest step H m (setting S m, banks allow B m)`: H is the smaller of your setting and the cap above.
  A "flatter than" line means there is nothing to step.
- `terrain: N patches (… level pads, … sloped banks), bank width B m`: what goes to the terrain build. B is the blend
  back to the untouched hillside. A pad whose columns all went to its bank has no level patch.
- `terrain: <summary>`: the terrain build's report (passes, worst error, share of texels off, curtains, time).
- `pad Left 0-24 m along, height 41.44 m: 6 of 9 cells level, keeps 2 of 3 columns for zoning`: one line per pad, after
  the build. The columns it keeps are the ones its bank did not take. Under two, the line says the game will wipe the
  pad's zoning. Only level cells get zoned.
- `zoning: N level cells in R runs as <zone>`, then `zoning: <summary>`: what was asked for, and what the zone build got.
  The zone build counts runs, not cells (see Zoned frontage).
- `still building the last street`: a build is running. Wait for its report.

## Test streets

On a flat-ish test map, draw straight two-lane streets about 200 m long at **8%, 12% and 15%**. Leave room on both sides,
away from water and buildings. Settings for each street:

- **8%:** the defaults: 3-cell pads, 1.92 m steps, one bank column per step, two zoned columns per pad.
- **12%:** the defaults: 3-cell pads, 2.88 m steps, one bank column per step, two zoned columns per pad.
- **15%:** lot width 4, narrowest 4, highest step 5.0: 4-cell pads, 4.8 m steps, two bank columns per step, two zoned
  columns per pad. Run off-game through the build's raster, this street shows two sheets at the back corners of its
  downhill end (pad corners 2.4 m off the ground, a 9 m blend), so look there first.

The last pad on each side has no step after it and keeps all its columns.

## What to look at

1. **Curtains:** a screenshot at 45° along each street, on both sides. Look for stretched vertical sheets at the banks,
   the pad corners and the blend at the back. Note the curtain number in the terrain summary.
2. **Pad levelness:** the per-pad lines. Every pad should be within 0.3 m (the build's tolerance), and every step within
   the highest step.
3. **Houses:** let the zone fill (speed 3, a few in-game days). Count the lots where one house sits on one pad, out of 10.
   Success is 9 of 10.
4. **Zoned frontage:** cells zoned per street. Each bank takes whole columns from the lower pad, so the columns a pad
   keeps stay level and are zoned (the unit tests check this). Run off-game through the build's raster at depth 3, 102
   of 144 cells come out level on the 8% and 12% streets and 84 of 144 on the 15% street; the bank columns are option
   A's cost. Each zoning quad is a run of level cells in one
   row, and the game zones every one of its own cells whose centre lies inside it, on the road's own 8 m grid. The zone
   build's summary checks only the game cell at each run's centre, so it counts runs, not cells: count the cells by eye.

## Quay experiment (option C, by hand)

On the 8% street set lot width 6, narrowest 3 and highest step 3.0. Pads then alternate 6 and 3 cells, with steps of
about 2.9 m, each bank one column of the lower pad. At one step, place the game's 8 m pedestrian quay (QuaySmallWide)
by hand along the top of the bank (the upper pad's first edge), running back from the road, with its deck at the upper
pad's height. The bank column then lies on the quay's low side. Check whether the concrete wall appears on the low side,
whether the ground beside it shows a curtain, and whether houses on both pads still grow. Take a 45° screenshot. Off-game,
120 of 144 cells come out level on this street.

## Baseline: Placemaker's Zoning tool (PRD section 6)

On an identical copy of each test street (same grade and length, ideally the same save before stepping), use
Placemaker's Zoning tool with "Level the lots" on and its defaults. Record the same four things: curtains, pad
levelness, houses per lot, and cells zoned. Stepped Streets must zone at least as much frontage as this.
