# Hillside neighbourhoods (wave 5 spec)

> **Superseded** by the PRD Draft 4 (`docs/Stepped_Streets_PRD.md`, 2 November 2026). Kept for history; the contract below was never built.

*Lead's spec, 2 November 2026. It extends the PRD: the owner wants the mod to lay out a whole hillside neighbourhood, not
only step one existing street. Section 3's non-goal "laying out new roads or whole neighbourhoods" is lifted by the owner.*

The owner's words: draw an area on a hill, and the mod generates the road layout, respecting the terrain and following the
contour lines so it looks good, zones it with a mix of zones the player chooses, and grades the lots into stepped, level
pads. The reference is a hand-built hill town in Cities: Skylines II: winding streets on the contours, short links up the
slope, cul-de-sacs, houses on level pads with soft banks between them.

The rules below come from two creator videos the owner sent (the hillside-building tips and a mountain-houses build) and
from Placemaker's documented "Hillside terraces" method (`Core/Layers/Roads/Terraces/TerraceLayout.cs` header, `s52.md`).

## 1. Player flow

1. The toolbar button opens the panel on the **Neighbourhood** tab. The **Street** tab is today's single-street tool,
   unchanged.
2. **Draw the area.** Left-click places corners on the ground. Clicking the first corner, or the panel's **Close area**
   button once there are 3 or more corners, closes it. Dragging a corner reshapes a closed area. Right-click removes the
   last corner. Esc clears the area; Esc with no area closes the tool.
3. **The plan appears** as soon as the area is closed and refreshes after every change: new roads, lots coloured by zone,
   banks, connections to existing roads.
4. **Design** in the panel: road type, steepest road, link spacing, lot width and depth, step look, space lots on steep
   ground, the zone mix (rows of zone and share, add or remove rows) and its pattern (Random, Clumps, Even); advanced:
   steepest buildable ground, bank grade, earthwork limit, narrowest width, highest step, minimum grade.
5. **Summary:** "2.4 km of new road, 86 lots: 60 Residential Low, 26 Residential Medium Row. Most earthwork 3.1 m." plus
   messages (too steep, nothing to connect to, lots left empty and why).
6. **Build**, after the permanent-change prompt: roads, then ground, then zoning, then low walls. The result line replaces
   the summary.

## 2. Rules

| # | Rule |
|---|---|
| H1 | **Survey.** Raw ground on a 4 m grid over the area plus a 64 m margin. The layout reads a smoothed copy (Gaussian, σ = 12 m) so small lumps don't bend streets; grading and earthwork read the raw ground. |
| H2 | **Wild ground** (video tip 1). Where the smoothed slope is steeper than **Steepest buildable** (default 20 %, range 10–40 %), there are no roads and no lots. Water deeper than 0.1 m and building lots are blocked too. A message reports the share of the area left wild. |
| H3 | **Contour streets.** Streets follow contour lines of the smoothed ground. A terrace band is W = 2 × (road half width + lot depth × 8 m + 2 m). With the mean bank slope a = bank grade ÷ 1.15 and the reference slope g (the 60th percentile slope of buildable ground), streets are S = W × a ÷ (a − g) apart, clamped to 60–200 m, so the step between neighbouring terraces is ΔH = g × S (Placemaker's spacing rule, `s52.md:22-29`). Where g ≥ a the hill is too steep for terraces at this depth: a message suggests a shallower lot depth. Contours are traced with marching squares at heights ΔH apart, clipped to the area minus wild ground, smoothed (Chaikin, 3 rounds) and resampled every 8 m. A piece is dropped when it is shorter than 80 m, turns tighter than a 25 m radius anywhere after smoothing, or comes closer than 0.6 × S to an accepted street away from a junction. Along its length a contour street climbs at most 3 %. |
| H4 | **Links.** Uphill roads join each contour street to the next one up, about every **Link spacing** (default 200 m, range 100–400 m) along it, and every street gets at least one. A link is straight when that keeps it within **Steepest road** (default 10 %, range 6–15 %); otherwise it climbs diagonally across the slope at that grade; otherwise it takes one switchback. Junction angles are at least 60°, and junctions on one street are at least 45 m apart. |
| H5 | **Connection.** The new network must reach the existing roads: at least one connection road from the network to an existing road (its node or a point along its edge) within 150 m of the area, within Steepest road. Without one the plan is shown, Build stays off and the message says "No new road reaches an existing road. Draw the area so it touches a road." |
| H6 | **Street ends.** A contour street end that is not a junction becomes a dead end (cul-de-sac). An end within 40 m of another street is joined to it instead. |
| H7 | **Lots.** Both sides of every new road get lots of **Lot width** and **Lot depth** through the existing `StepPlanner`: contour streets plan with minimum grade 0, so every lot is a level pad at its street's height (the bench); links plan with the player's minimum grade, so steep links get stepped pads (R1–R10). Lots keep off every planned road (half width + 2 m), off each other, off buildings, water and existing roads (R9 shortens or empties them), and off the first 16 m from a junction. |
| H8 | **Grading** (video tips 2 and 4). Each pad is level at its street's height. The ground eases back into the hillside behind and beside it no steeper than the bank grade (`StepPlanPatches`, `BankWidth`). Between two contour streets the downhill lots of the upper street and the uphill lots of the lower street meet across one bank; H3's spacing leaves room for it. The earthwork limit empties lots that need more. |
| H9 | **Space on steep ground** (video tip 3), on by default. Where the ground across a lot is steeper than 12 %, every second lot is followed by a one-cell planted gap, so neighbouring houses don't fight over the ground. |
| H10 | **Zone mix.** The player's rows of zone and share (normalised) decide each lot's zone. **Random**: seeded per lot. **Clumps**: runs of 3–5 neighbouring lots share a zone. **Even**: weighted round robin, so the split is within one lot of the target. The seed is a hash of the area's corners, so the same area gives the same plan. |
| H11 | **Build order.** Roads first; if the game refuses them, stop and build nothing else. Then the ground (pads, aprons, banks). Then zoning, after the game has made zone blocks along the new roads (wait for them, at most 120 frames), one zone at a time. Then low walls if chosen. Each stage logs a line. |
| H12 | **Budget.** Planning 50 ha takes under 500 ms, on a worker thread with the latest request winning. No build stage freezes the game for more than 2 s. The plan is deterministic. |

Not in this wave: undo, buildings placed directly, paths and stairs, trees on banks, flat-ground grids (an area flatter
than 3 % gets the message "This area is nearly flat. Draw it on a hillside.").

## 3. Contract

Every worker codes against these exact names and signatures. A worker that needs another's members before the merge adds a
stub with the exact text in a file headed `// WAVE 5 STUB: the lead deletes this file at merge.`

### 3.1 Core layout (W1, `src/SteppedStreets.Core/Layout/`)

```csharp
namespace SteppedStreets.Core.Layout
{
    /// <summary>One existing road edge near the area, to connect to.</summary>
    public sealed class ExistingRoad
    {
        public long Id;                          // the mod's handle for the game edge; the planner only echoes it
        public float3[] Points = new float3[0];  // the edge's curve sampled at even curve parameter t = i / (n − 1)
        public float HalfWidth;
    }

    public sealed class AreaRequest
    {
        public float2[] Boundary = new float2[0];                  // the drawn area, world (x, z), either winding
        public Func<float2, float> GroundHeight;                    // raw ground
        public Func<float2, bool> Blocked;                          // buildings, water, existing road corridors; null = none
        public ExistingRoad[] ExistingRoads = new ExistingRoad[0];  // edges within 200 m of the area
        public float RoadHalfWidth = 6f;
        public float SteepestRoad = 0.10f;                          // H4, H5
        public float SteepestBuildable = 0.20f;                     // H2
        public float LinkSpacing = 200f;                            // H4
        public int LotDepthCells = 3;                               // H3's band
        public float BankGrade = 0.4f;                              // H3's spacing
        public uint Seed;
    }

    public enum RoadKind { Contour, Link, Connection }

    public sealed class PlannedNode
    {
        public float3 Position;
        public long ExistingRoadId;   // 0 for a new node; otherwise the existing edge this node joins
        public float ExistingRoadT;   // where on that edge, as its curve parameter (0 and 1 are its end nodes)
        public bool DeadEnd;
    }

    public sealed class PlannedRoad
    {
        public RoadKind Kind;
        public float3[] Points = new float3[0];  // centreline at the planned surface height, about every 8 m, StartNode to EndNode
        public int StartNode;                    // index into RoadLayout.Nodes
        public int EndNode;
    }

    public sealed class RoadLayout
    {
        public List<PlannedNode> Nodes = new List<PlannedNode>();
        public List<PlannedRoad> Roads = new List<PlannedRoad>();
        public List<string> Messages = new List<string>();
        public bool Connected;    // H5
        public float Length;      // metres of new road
        public float WildShare;   // H2: the share of the area left wild, 0 to 1
    }

    public static class ContourRoadPlanner
    {
        public static RoadLayout Plan(AreaRequest request);
    }
}
```

Roads never cross except at shared nodes. A road's first and last points equal its nodes' positions.

### 3.2 Core neighbourhood (W2, `src/SteppedStreets.Core/Neighbourhood/`)

```csharp
namespace SteppedStreets.Core.Neighbourhood
{
    public enum MixPattern { Random, Clumps, Even }

    public sealed class ZoneShare
    {
        public string Zone = "";
        public float Share = 1f;   // any positive weight; the planner normalises
    }

    public sealed class NeighbourhoodRequest
    {
        public Func<float2, float> GroundHeight;
        public Func<float2, bool> Blocked;   // buildings, water and existing roads; the planned roads are added by the planner
        public float RoadHalfWidth = 6f;
        public int LotWidthCells = 3;
        public int NarrowestWidthCells = 3;
        public int LotDepthCells = 3;
        public float MinimumGrade = 0.06f;   // links only; contour streets always plan with 0 (H7)
        public float HighestStep = 3.2f;
        public float BankGrade = 0.4f;
        public float EarthworkLimit = 4f;
        public StepLook Look = StepLook.PlantedBank;
        public bool SpaceSteepLots = true;   // H9
        public ZoneShare[] Mix = { new ZoneShare { Zone = "Residential Low", Share = 1f } };
        public MixPattern Pattern = MixPattern.Clumps;
        public uint Seed;
    }

    public sealed class PlannedLot
    {
        public Pad Pad;          // the pad in its street's StepPlan
        public int Street;       // index into NeighbourhoodPlan.Streets (= the road's index in RoadLayout.Roads)
        public string Zone = "";
    }

    public sealed class NeighbourhoodPlan
    {
        public List<StepPlan> Streets = new List<StepPlan>();                  // one per RoadLayout.Roads entry, same order
        public List<PlannedLot> Lots = new List<PlannedLot>();
        public List<PlannedTerraform> Patches = new List<PlannedTerraform>();  // every street's pads, aprons and banks
        public float BankWidth;                                               // the blend for the terrain build
        public List<WallSegment> Walls = new List<WallSegment>();             // empty unless Look is LowWall
        public string SummaryLine = "";
        public List<string> Messages = new List<string>();                    // the layout's messages first, then the lots'
        public float MostEarthwork;                                           // metres, the largest cut or fill under a pad
    }

    public static class NeighbourhoodPlanner
    {
        public static NeighbourhoodPlan Plan(RoadLayout layout, NeighbourhoodRequest request);

        /// <summary>R8 per lot after the build: the level cells' zoning quads, grouped by zone.</summary>
        public static Dictionary<string, List<float2[]>> ZoneQuads(NeighbourhoodPlan plan, Func<float2, float> builtHeight, float tolerance);
    }
}
```

### 3.3 Mod (W3: tool, settings, build; W4: panel)

Settings, `src/SteppedStreets/Settings/SteppedStreetsSettings.cs` (W3 owns it; property names are save format). After
`EarthworkLimit` add:

```csharp
        [SettingsUITextInput]
        [SettingsUISection(Section, Group)]
        public string RoadName { get; set; }                  // default "Small Road" (matched like ZoneName)

        [SettingsUISlider(min = 6, max = 15, step = 1, scalarMultiplier = 1, unit = Game.UI.Unit.kPercentage)]
        [SettingsUISection(Section, Group)]
        public int SteepestRoadPercent { get; set; }          // default 10

        [SettingsUISlider(min = 10, max = 40, step = 1, scalarMultiplier = 1, unit = Game.UI.Unit.kPercentage)]
        [SettingsUISection(Section, Group)]
        public int SteepestBuildablePercent { get; set; }     // default 20

        [SettingsUISlider(min = 100, max = 400, step = 20, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int LinkSpacing { get; set; }                  // default 200

        [SettingsUITextInput]
        [SettingsUISection(Section, Group)]
        public string ZoneMix { get; set; }                   // JSON [{"zone":"Residential Low","share":100}], the default

        [SettingsUISection(Section, Group)]
        public MixPattern MixPattern { get; set; }            // default Clumps

        [SettingsUISection(Section, Group)]
        public bool SpaceSteepLots { get; set; }              // default true
```

Tool state, `src/SteppedStreets/Tool/ToolState.cs` (W3 owns it). Add:

```csharp
    public enum ToolMode { Area, Street }
    // in ToolState:
        public ToolMode Mode = ToolMode.Area;
        public int AreaCorners;                 // corners placed so far
        public bool AreaClosed;
        public float AreaHectares;
        public string[] ZoneCounts = new string[0];   // "60 Residential Low" lines for the plan or the result
```

In Area mode `Stage` means: Picking while drawing or while the closed area has no buildable plan (messages say why),
Planned when the plan is buildable (connected, at least one lot), then Building, Built, Failed as today.

Tool methods, `StepStreetToolSystem` (W3 owns it), in addition to today's Toggle, Open, Close, ClearSelection (clears
the area too), Build and SettingsChanged:

```csharp
        public void SetMode(ToolMode mode);   // switching clears the other mode's selection
        public void CloseArea();              // the panel's Close area button; needs 3 or more corners
```

Panel bindings (W4 owns `src/SteppedStreets/Panel/PanelSystem.cs` and the React project), group "SteppedStreets".
Value "state" gains: `mode` ("Area" | "Street"), `areaCorners`, `areaClosed`, `areaHectares`, `zoneCounts` (string[]);
`settings` gains `road`, `steepestRoadPercent`, `steepestBuildablePercent`, `linkSpacing`, `zoneMix`
([{zone, share}]), `mixPattern` ("Random" | "Clumps" | "Even"), `spaceSteepLots`; and a catalogue `roads`
([{name, label}]: every ground-level, non-highway road prefab) next to `zones`. New triggers: `setMode`(string),
`closeArea`, `setRoad`(string), `setSteepestRoad`(double), `setSteepestBuildable`(double), `setLinkSpacing`(double),
`setZoneMix`(string, the JSON above), `setMixPattern`(string), `setSpaceSteepLots`(bool).

Registration: new systems register in `Mod.cs`, each in its own try/catch block at the end of `OnLoad`.

## 4. Work split and merge order

| Worker | Owns | Delivers |
|---|---|---|
| W1 | `src/SteppedStreets.Core/Layout/*`, `tests/.../Layout/*` | `ContourRoadPlanner` (H1–H6, H12) |
| W2 | `src/SteppedStreets.Core/Neighbourhood/*`, `tests/.../Neighbourhood/*` | `NeighbourhoodPlanner` (H7–H10, the summary, zone quads) |
| W3 | `src/SteppedStreets/Tool/*`, `Build/*`, `Settings/*`, `Mod.cs`, `Core/Terrain/TerraformApplyMath.cs` | area drawing, snapshot, preview, road building, build order (H11), settings |
| W4 | `src/SteppedStreets/Panel/*`, `src/SteppedStreets.UI/**`, `README.md`, `docs/Stepped_Streets_PRD.md` | the two-tab panel, the zone-mix editor, docs |

Merge order W1 → W2 → W3 → W4; at each merge the owner's files win over other workers' stubs.

## 5. What "done" means for the owner

In the game, on a hillside of 8–20 %: draw an area touching an existing road, see contour streets, links and coloured lots
in the preview within a second, press Build, and get roads that follow the hill, level stepped lots with soft banks, no
torn terrain, and the chosen mix of zones growing houses.
