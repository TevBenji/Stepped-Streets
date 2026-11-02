# In-game test checklist (version 2)

For Tevin, the first time the neighbourhood generator runs in the game. Tick what works; for anything that doesn't, note what
you did and send the two logs and a screenshot.

**Logs:** `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\SteppedStreets.log` and `Logs\UI.log`.
**Save first:** terrain changes are permanent. Use a throwaway save of a hilly map (8–20 % slopes), ideally with a road
running along the foot of a hill.

## 1. Loading

- [ ] The game starts; `SteppedStreets.log` has "Stepped Streets loading" and no "failed to register" lines.
- [ ] The Stepped Streets button sits beside Zones in the bottom toolbar; clicking it opens the panel on the right.
- [ ] Ctrl+Shift+S opens and closes it too.
- [ ] The panel is styled (dark panel, blue header bar), not raw text. If raw: `UI.log` says why.

## 2. Drawing the area

- [ ] Clicking the ground places corners (blue dots) joined by a line; a faint line follows the cursor.
- [ ] Clicking the first corner (it turns white once there are three) closes the area; Enter and the Close area button do too.
- [ ] The panel shows the area in ha (or km above 100 ha).
- [ ] Dragging a corner reshapes the area; dragging the middle of an edge adds a corner.
- [ ] Right-click removes the corner under the cursor; right-dragging still moves the camera.
- [ ] Ctrl+Z undoes the last change; Esc clears the area; Esc again closes the tool.
- [ ] An outline that crosses itself gets a message instead of a plan.

## 3. The plan (preview)

- [ ] Within about a second of closing the area, roads, lots and dots appear on the ground. The log line "plan in N ms" gives
      the time.
- [ ] Streets follow the hill's contours (they look level along their length); connectors climb diagonally.
- [ ] Roads are grey (under 3 %), yellow (under 6 %) or orange (up to 8 %); none is red.
- [ ] Lots sit along both sides of the streets, coloured by zone; none sits on very steep ground, water or a building.
- [ ] A magenta dot marks where the network joins an existing road (if one is within 400 m).
- [ ] Changing a setting re-plans; Reroll gives a different layout; the same seed gives the same layout again.
- [ ] Try each style: Contour suburb, Terraced blocks, Mountain lane.
- [ ] Try a zone mix of two or three zones with Clumps, Random and Even.
- [ ] Draw an area away from any road: the plan still appears and says "Not connected".

## 4. Building

Press Build and confirm. Watch the panel's progress and the log.

- [ ] Ground: the log has a "ground: Stepped Streets terrain: reached=True" line; the hillside is graded without vertical
      "curtains" (torn stretched sheets) — check at a 45° angle.
- [ ] Roads: the log's "roads:" line reports pieces built (N of N); junctions merged into real T-junctions (no gaps, no
      overlapping road ends); the connection joins the existing road.
- [ ] Roads sit on the ground (no floating or sunken stretches, no concrete retaining walls along their sides).
- [ ] Zoning: the zone colours appear on the lots; the log's "zoning:" lines show runs zoned per zone.
- [ ] Trees appear on the steep ground and banks, not on lots or roads.
- [ ] No freeze over about 2 seconds at any stage.
- [ ] After unpausing for a while: houses grow on the level pads, one per pad, not half sunk.

## 5. Things to watch (likely first fixes)

1. **Road pieces refused or not merging.** The log says "the game marked N temporary pieces with an error" or "M of N pieces
   found built". A screenshot of the junctions helps most.
2. **Zone blocks along curved streets** may not line up with the lots: some lots may zone only partly. Note how much.
3. **Ground verification** may report reached=False on very large areas (4 passes in 6 s). Try a smaller area if so.
4. **Tree prefabs:** the log line "trees: N conifer and M broadleaf prefabs" shows what it found; say if the trees look wrong.
5. **Panel layout in Gameface** (the small raised "2" in km2 and "3" in m3, buttons with an icon and text) may sit oddly.
