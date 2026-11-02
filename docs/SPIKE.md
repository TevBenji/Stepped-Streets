# M1 spike: stepping one street by hotkey

The spike has no tool, panel or preview. You select a street with the game's own select tool and press a hotkey. The
mod plans level pads, builds the ground with a planted bank at each step (PRD option A), zones the cells that came out
level, and logs every stage. Ground changes are permanent, so work on a test save.

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
| Narrowest width (cells) | 2 | How far a pad may narrow to keep its step under the highest step. |
| Depth (cells) | 3 | Cells back from the road. |
| Minimum grade | 6% | Flatter stretches are left alone. |
| Highest step (m) | 2.0 | The tallest step allowed. A taller one narrows the pad, or leaves the lot empty. |
| Bank grade | 0.40 | Slope of the bank between two pads. Each bank takes step ÷ grade metres of frontage, half from each pad. |
| Zone | Residential Low | Zone name, matched without case: an exact match first, then the first name that contains it. |

## What the log lines mean (all start "Stepped Streets:")

- `street of N edges (M in its aggregate), L m long, R m rise, grade G%, half width W m, P points`: what was read.
  If N is less than M, part of the street did not chain (a branch). A grade far from what you drew means the wrong street.
- `the selection (Entity(…)) is not a road edge`: nothing usable is selected. Click the road itself.
- `plan: …`: pads, steps (lowest to tallest), lots left empty because they were too steep, flat stretches, and the
  settings used. A "flatter than" line means there is nothing to step.
- `terrain: N patches (… level pads, … sloped banks), bank width B m`: what goes to the terrain build. B is the blend
  back to the untouched hillside.
- `terrain: <summary>`: the terrain build's report (passes, worst error, share of texels off, curtains, time).
- `pad Left 0-24 m along, height 41.44 m: 6 of 9 cells level`: one line per pad, after the build. Only level cells get zoned.
- `zoning: N level cells as <zone>`, then `zoning: <summary>`: what was asked for, and what the zone build got.
- `still building the last street`: a build is running. Wait for its report.

## Test streets

On a flat-ish test map, draw straight two-lane streets about 200 m long at **8%, 12% and 15%**. Leave room on both sides,
away from water and buildings. Settings for each street:

- **8%:** the defaults (3-cell pads, 1.92 m steps).
- **12%:** lot width 2 (1.92 m steps). With the defaults the planner leaves every other lot empty, because a 3-cell
  pad's step (2.4–2.9 m) is over 2.0 m.
- **15%:** lot width 2 and highest step 2.5 (2.4 m steps), or highest step 3.0 (3-cell and 2-cell pads, 3.0 m steps).

## What to look at

1. **Curtains:** a screenshot at 45° along each street, on both sides. Look for stretched vertical sheets at the banks,
   the pad corners and the blend at the back. Note the curtain number in the terrain summary.
2. **Pad levelness:** the per-pad lines. Every pad should be within 0.3 m (the build's tolerance), and every step within
   the highest step.
3. **Houses:** let the zone fill (speed 3, a few in-game days). Count the lots where one house sits on one pad, out of 10.
   Success is 9 of 10.
4. **Zoned frontage:** cells zoned per street. The unit tests predict few or none on pads with a bank at both ends: each
   bank cuts 2.4–3.6 m into the end columns, and a lone middle column is wiped by the game. Count them anyway: this is
   option A's cost.

## Quay experiment (option C, by hand)

On the 8% street set lot width 6, narrowest 2 and highest step 3.0. Pads then alternate 6 and 3 cells, with steps of
about 2.9 m. At one step, place the game's 8 m pedestrian quay (QuaySmallWide) by hand along the step, running back from
the road, with its deck at the upper pad's height. Check whether the concrete wall appears on the low side, whether the
ground beside it shows a curtain, and whether houses on both pads still grow. Take a 45° screenshot.

## Baseline: Placemaker's Zoning tool (PRD section 6)

On an identical copy of each test street (same grade and length, ideally the same save before stepping), use
Placemaker's Zoning tool with "Level the lots" on and its defaults. Record the same four things: curtains, pad
levelness, houses per lot, and cells zoned. Stepped Streets must zone at least as much frontage as this.
