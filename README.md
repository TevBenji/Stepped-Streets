# Stepped Streets

<img align="right" src="src/SteppedStreets/Properties/Thumbnail.png" alt="Stepped Streets" width="260">

<a href="https://mods.paradoxplaza.com/mods/166090/Windows"><img alt="Paradox Mods: Stepped Streets" src="https://img.shields.io/badge/Paradox%20Mods-Stepped%20Streets-2ea44f"></a>
<a href="https://mods.paradoxplaza.com/mods/166090/Windows"><img alt="Version" src="https://img.shields.io/badge/version-2.6.1-1f6feb"></a>
<img alt="Game version" src="https://img.shields.io/badge/Cities%3A%20Skylines%20II-1.6-0e7490">
<a href="LICENSE"><img alt="Licence" src="https://img.shields.io/badge/licence-GPL--3.0-c2410c"></a>

A Cities: Skylines II mod that plans and builds whole hillside neighbourhoods in one go.

Building on hills is slow, fiddly work: roads that climb too steeply, lumpy lots, torn-looking ground.
**Stepped Streets does it for you.** Draw an area on a hillside and it lays out the whole neighbourhood the way a careful
builder would, previews it on the ground, and builds it when you say so. Forty layouts cover flat ground too.

<a href="#features">Features</a> ·
<a href="#how-to-use">How to use</a> ·
<a href="#settings">Settings</a> ·
<a href="#how-it-works">How it works</a> ·
<a href="#building-from-source">Building from source</a> ·
<a href="#credits">Credits</a>

<br clear="right">

<p align="center">
<img src="src/SteppedStreets/Properties/Screenshots/01.png" alt="Contour streets on a hill" width="720">
</p>

## Features

- **Streets that follow the contours**, so they stay nearly level along the hill.
- **Gentle connectors** that climb across the slope, never steeper than the grade you set (8 % by default).
- **A connection to your road network** within 400 m, or a neighbourhood of its own when there is none.
- **Level lots stepped down the hill**, graded with soft banks before anything is zoned.
- **Zoning that grows**: the game's own zone cells along the new roads, as deep as each levelled lot, in the mix of zones
  you choose. Lots are 4 cells deep by default, so every low-density house fits on level ground.
- **Service buildings in the mix**: police, fire, health, schools, parks, utilities, any unlocked roadside service building,
  each on a level site of its own, facing the road.
- **Trees if you want them**: forest on ground too steep to build and trees on the banks (off by default), and optional
  garden walls.
- **Forty layouts for hills and flat ground**: five that follow the land (contours, ridges, valleys), and for flat or
  gentle ground grids, tiled triangles and hexagons, radial rings and stars, spines, trees, loops and lollipops, a coastal
  layout, and planners' models from Radburn to Barcelona's superblocks.
- **Shape the ground, or don't**: grade roads and lots into the hill, or leave the terrain exactly as it is.
- A live preview, and **Reroll** for another layout from the same area.
- **Steep hills mode** for slopes up to 60 %, and areas up to 6.4 km across.

<table>
<tr>
<td><img src="src/SteppedStreets/Properties/Screenshots/02.png" alt="Services in the mix"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/03.png" alt="Steep hills mode"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/04.png" alt="Terraced blocks"></td>
</tr>
<tr>
<td><img src="src/SteppedStreets/Properties/Screenshots/05.png" alt="Reads any terrain"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/06.png" alt="Forest on steep ground"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/07.png" alt="Mountain lane"></td>
</tr>
<tr>
<td><img src="src/SteppedStreets/Properties/Screenshots/08.png" alt="Grids for flat ground"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/09.png" alt="Traffic-optimised grid"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/10.png" alt="Crossroads, staggered or skewed"></td>
</tr>
<tr>
<td><img src="src/SteppedStreets/Properties/Screenshots/11.png" alt="Rings and spiderwebs"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/12.png" alt="A fan from your road"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/13.png" alt="Curved and diagonal grids"></td>
</tr>
<tr>
<td><img src="src/SteppedStreets/Properties/Screenshots/14.png" alt="Loops and lollipops"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/15.png" alt="Spines, combs and fishbones"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/16.png" alt="Triangles and hexagons"></td>
</tr>
<tr>
<td><img src="src/SteppedStreets/Properties/Screenshots/17.png" alt="Along the shore"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/18.png" alt="Superblocks"></td>
<td><img src="src/SteppedStreets/Properties/Screenshots/19.png" alt="Many centres, one neighbourhood"></td>
</tr>
</table>

## Installation

Subscribe on **[Paradox Mods](https://mods.paradoxplaza.com/mods/166090/Windows)**, either on the website or in the game's
mod browser, and restart the game.

## How to use

1. Open the tool: click **Stepped Streets** in the bottom toolbar beside Zones, or press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>.
2. **Draw the area.** Click corners on the ground and click the first corner again to close it.
3. **Choose what to build.** Pick a layout, then add zones with their shares and services with how many of each.
4. **Check the preview.** The plan appears on the ground within moments and updates with every change. Press **Reroll** for
   another layout.
5. **Build.** The ground is graded, the roads are laid, the lots are zoned, and the services and trees are placed.

| Control | Action |
|---|---|
| Left-click | Place a corner; click the first corner to close the area |
| Drag a corner | Reshape the area |
| Drag an edge | Add a corner there |
| Right-click | Remove the corner under the cursor |
| <kbd>Enter</kbd> | Close the area |
| <kbd>Ctrl</kbd>+<kbd>Z</kbd> | Undo |
| <kbd>Esc</kbd> | Clear the area; press again to close the tool |

> [!IMPORTANT]
> Ground changes are permanent, like the game's own terrain tools. **Back up your save before a big build.**

The area does not have to touch a road. Groups of streets that can't reach your road network are still built, and the panel
tells you so you can join them up with a road of your own.

### Reading the preview

| Colour | Meaning |
|---|---|
| Grey road | Under 3 % grade |
| Yellow road | Under 6 % |
| Orange road | Up to the maximum grade |
| Zone colours | Lots, coloured by zone |
| Blue pad | A service building's site; the white edge faces the road |
| Brown and tan | Ground being cut and filled |

## Layouts

| Layout | What you get |
|---|---|
| **Contour suburb** (default) | Streets on the contours, linked by diagonal ramps and S-curves, every lot on its own level pad. |
| **Terraced blocks** | Straighter, level streets so each side of a block is one terrace, with plain connectors between them. |
| **Mountain lane** | One winding road that climbs with hairpins, sparse lots along it, forest everywhere else. |
| **Square grid** | Straight streets at right angles, blocks as long as they are deep. For flat or gentle ground. |
| **Rectangular grid** | Straight streets at right angles with long blocks. |
| **Irregular grid** | A grid with varied street spacing and block lengths. |
| **Skewed grid** | Long blocks whose cross streets lean, so the blocks are parallelograms. |
| **Fragmented grid** | Parallel streets with gaps: some cross streets left out and some streets broken. |
| **Diagonal avenues** | A rectangular grid crossed by two diagonal avenues. |
| **Curved grid** | A grid whose streets curve in gentle waves, the junctions still close to square. |
| **Warped grid** | Curving parallel streets with gaps, like a fragmented grid that bends. |
| **Radial grid** | A grid with four diagonal avenues radiating from a square at its centre. |
| **Star** | Streets radiating from a small ring at the centre, twice as many past a boulevard ring. |
| **Rings** | Concentric ring streets joined by staggered radial streets. |
| **Spiderweb** | Concentric rings crossed by radial streets that run straight out from the centre. |
| **Fan** | Arcs and radial streets opening like a fan from the edge of the area nearest your road. |
| **Spine** | A trunk street running in from your road, with straight side streets off both sides. |
| **Ladder** | Rails running in from your road, joined by rungs one block apart. |
| **Comb** | A trunk along one side of the area with side streets off one side only, like the teeth of a comb. |
| **Fishbone** | A trunk with side streets slanting off it in pairs, like the bones of a fish. |
| **Tree** | A trunk with limbs off it and twigs off the limbs: streets branching like a tree. |
| **Cul-de-sacs** | Trunks from your road with short dead-end streets off both sides. |
| **Loops** | Trunks with loop streets off both sides, each leaving its trunk and coming back to it. |
| **Loops and lollipops** | Trunks with loops and lollipops in turn. |
| **Lollipops on a stick** | Trunks with short sticks off both sides, each ending in a small ring street. |
| **Triangular mesh** | Streets in three directions 60° apart, so every block is a triangle. |
| **Hexagonal** | A honeycomb of streets: hexagonal blocks, every junction a three-way one at 120°. |
| **Hexagons with spokes** | Larger hexagons, each split into three diamonds by streets from its centre. |
| **Ridge** | A main road along the crest of a ridge, with streets following the contours down both sides. |
| **Valley** | A main road along the valley floor, one on each bank where a river runs there, with contour streets up both sides. |
| **Coastal** | Streets parallel to the shore, joined by streets square to it. With no water near, they line up with your road. |
| **Winding** | One road winding back and forth across the area in gentle waves, with a cross street every few blocks. |
| **Organic mesh** | An irregular mesh: streets that wave their own ways and cross streets at varied angles, so every block is different. |
| **Hybrid** | A grid on one side of the area and loops and lollipops on the other, joined up. |
| **Polycentric** | Two to four small centres, each its own neighbourhood of rings and radial streets, joined up. |
| **Superblock** | Big blocks ringed by through streets, with only dead-end streets inside them. |
| **Radburn** | Superblocks whose short cul-de-sacs leave a green space in the middle of each block. |
| **Fused grid** | A grid of through streets with loop streets inside each big block. |
| **Filtered grid** | A grid whose streets are closed in the middle of every other block, so they serve the houses but carry no through traffic. |
| **Barcelona superblock** | Superblocks of three by three blocks whose inner streets come in and turn back out by the next side, round a central square. |

The hillside layouts need a slope; all the others work on flat land too. Everything but the three hillside layouts is
new and marked **experimental** in the picker until it has been built in more cities: tell me how yours turn out. A grid lines up with your nearest road and
meets it the way a real grid does: every street that runs towards it joins it in a T. The radial layouts centre on the
roomiest part of the area (the fan on the edge nearest your road) and meet your road the same way. The linear and
branching layouts run their trunks in from your road, each meeting it in a T, with the side streets off the trunks.
Ridge and Valley need a hill, like the contour layouts; Coastal follows water in or beside the area (with none, it lines
up with your road). The planners' models build big blocks ringed by through streets (Superblock, Radburn, Fused grid,
Barcelona superblock), close streets to through traffic (Filtered grid), or split the area into parts with layouts of
their own (Hybrid, Polycentric).

**Junctions** (grids): **Crossroads** lines the cross streets up into four-way junctions; **Staggered** offsets them like
brickwork, so every junction is a T; **Traffic** runs one through road across the grid as the only road that meets
yours, with the local streets joining it in T-junctions.

## Settings

The panel shows the essentials; everything else is under **More settings**. One zone cell is 8 m.

| Setting | Range | Default | What it does |
|---|---|---|---|
| Layout | see above | Contour suburb | The kind of layout. |
| Junctions | Crossroads, Staggered, Traffic | Staggered | How a grid's cross streets meet its main streets. |
| Shape the ground | on / off | on | Grade roads and lots. Off: no terrain changes at all; roads follow the ground. |
| Zones | zones and shares | Residential Low | Each lot's zone. **Add a zone** mixes in another. |
| Services | buildings and counts (1–20) | none | Service buildings on level sites beside the new roads. **Add a service** adds one. |
| Mix mode | Random, Clumps, Even | Clumps | How a zone mix is spread: per lot, in runs of neighbouring lots, or evenly. |
| Steep hills | on / off | off | Build on slopes up to 60 % with up to 12 m of earthwork; connectors may switch back. |
| Trees | on / off | off | Forest on steep ground, trees on banks and in the gaps between lots. |

<details>
<summary><strong>More settings</strong></summary>

<br>

| Setting | Range | Default | What it does |
|---|---|---|---|
| Road type | the game's zoning roads | Small Road | The road used for every new street. |
| Block depth | 60–200 m | 90 m | The distance between neighbouring contour streets. |
| Max road grade | 4–12 % | 8 % | No new road is steeper. |
| Steepest buildable | 8–30 % (60 % with Steep hills) | 15 % | Steeper ground gets no roads or lots and becomes forest. |
| Lot width, lot depth | 2–6 cells | 3 × 4 | The size of each lot. Only the lot's depth is levelled and zoned; low-density houses are 2 or 4 cells deep. |
| Space between lots | Auto, 0, 1, 2 cells | Auto | Auto leaves planted gaps on steeper ground. |
| Level the lots | on / off | on | Grade each lot into a level pad. Off grades only the roads. |
| Most earth | 1–8 m (12 m with Steep hills) | 4 m | A lot that needs more cut or fill is left out. |
| Steepest bank | 20–50 % | 40 % | No slope the grading makes is steeper. |
| Retaining walls | on / off | off | Garden walls where a road runs through a cutting. |
| Tree density | 10–100 % | 50 % | How many trees to plant. |
| Seed | 1–99999 | 1 | Which layout. The same seed and settings always give the same plan. |

</details>

## How it works

```mermaid
flowchart LR
    A[Survey the ground] --> B[Trace contour streets]
    B --> C[Link them with gentle connectors]
    C --> D[Connect to existing roads]
    D --> E[Check every rule, drop what fails]
    E --> F[Lay out lots and service sites]
    F --> G[Grade the ground]
    G --> H[Plant trees and walls]
```

The generator follows the rules experienced players use on hills. It surveys the ground on a 4 m grid and smooths it, so small
lumps don't bend the streets. It traces contour lines a constant height apart, so neighbouring streets sit about one block
depth apart, and keeps the ones on buildable land, smoothed into gentle curves. Connectors leave and meet the streets nearly
square and climb across the slope within the maximum grade. Every road is then checked again for grade, curve radius,
junction angle, spacing and clearance, and anything that fails is dropped rather than built badly.

The ground is shaped as one field: roads and lots are held level, and everything around them eases back to the hillside no
steeper than the steepest bank. Where two levels would need a steeper bank, the lot gives way. Planning runs off the main
thread, so the game keeps running while a big area is planned.

## Known limitations

- Ground changes can't be undone by the mod.
- Areas are limited to 6.4 km on a side. A whole hillside that size can take most of a minute to plan and lays hundreds of
  kilometres of road at once.
- On true mountain slopes (40–50 %), even with Steep hills on, the plan keeps the gentler streets and the lots that fit;
  streets no connector can reach within the grade are left out rather than built as roads to nowhere.
- Every group of streets is joined to your roads. A group that no gentle road can join is left out, and the panel tells
  you so.
- Houses grow only where the city has demand for that zone; the panel tells you when a chosen zone has none yet.
- English only.

Found a problem? [Open an issue](https://github.com/TevBenji/Stepped-Streets/issues) with a screenshot and the log at
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\SteppedStreets.log`.

## Building from source

<img alt=".NET Framework 4.8" src="https://img.shields.io/badge/.NET%20Framework-4.8-512bd4?logo=dotnet&logoColor=white">
<img alt="C# 9" src="https://img.shields.io/badge/C%23-9-239120">
<img alt="Unity ECS" src="https://img.shields.io/badge/Unity-ECS-222222?logo=unity&logoColor=white">
<img alt="React" src="https://img.shields.io/badge/React-18-087ea4?logo=react&logoColor=white">
<img alt="TypeScript" src="https://img.shields.io/badge/TypeScript-5-3178c6?logo=typescript&logoColor=white">

**Requirements:** Windows, Cities: Skylines II, the .NET SDK 9 or later, and Node.js 18 or later. The mod targets
.NET Framework 4.8 with C# 9 and compiles against the game's own assemblies.

```powershell
git clone https://github.com/TevBenji/Stepped-Streets.git
cd Stepped-Streets/src/SteppedStreets.UI
npm ci
cd ../..
dotnet build SteppedStreets.sln -c Release
```

The build writes the mod, including the panel bundle, to `src/SteppedStreets/bin/Release`. If the game is not installed under
`D:\Steam`, point the build at it:
`-p:ManagedPath="<Steam library>\steamapps\common\Cities Skylines II\Cities2_Data\Managed"`.

To install a build by hand, close the game and copy everything in `src/SteppedStreets/bin/Release`, keeping the `images`
folder, into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SteppedStreets`.

### Project layout

| Folder | What's in it |
|---|---|
| `src/SteppedStreets.Core` | The planner: pure C#, no game types, so it runs and is tested outside the game |
| `src/SteppedStreets` | The mod: the tool, the preview, the build steps and the settings |
| `src/SteppedStreets.UI` | The panel: React and TypeScript, bundled for the game's UI |
| `tools` | The Paradox Mods publishing script |

## Credits

Stepped Streets is made by [TevBenji](https://github.com/TevBenji). Parts of the build code are adapted from
**[Placemaker](https://github.com/jackwrichards/placemaker)** by jackwrichards, which in turn credits ParkManager by tomkpunkt,
Grid Road Generator by Ti4goc, ParkingLotTool by KruemelGames and CS2-NetworkTools by Luca Rager. The full, file-by-file
account is in [CREDITS.md](CREDITS.md).

## Licence

Released under the [GNU General Public License v3.0](LICENSE). Cities: Skylines II is a trademark of Paradox Interactive;
this is an independent mod, not made or endorsed by Paradox Interactive or Colossal Order.
