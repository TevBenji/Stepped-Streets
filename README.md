# Stepped Streets

Stepped Streets is a Cities: Skylines II code mod by Tevin Benjamin (TevBenji). It turns a steep street's frontage into level pads that step down the hill, like San Francisco or Lisbon. Each step gets a planted bank or a low garden wall at the bank's foot, and the cells that came out level receive zoning so houses can grow on the pads. It works on one street at a time and can be used alongside Placemaker.

**Status:** wave 4 implementation, 2 November 2026. Built against game version **1.6.2f1**; the first in-game test is still pending. House fit, wall appearance and the full player flow need that test before publishing.

## Play

1. Click **Stepped Streets** in the bottom toolbar beside **Zones**, or press **Ctrl+Shift+S** to open the tool.
2. Hover a street to see its grade, for example **12% climb**. Flatter stretches are left alone.
3. Click to select the named street. **Shift-click** can add a connected street if the selection remains one chain without branches.
4. Choose **Side**, **Zone**, **Lot width** and **Step look**. Open the advanced settings to adjust the width floor, depth, grade, step height, bank grade or earthwork limit.
5. Check the pad and step preview, the summary and any reasons for empty lots.
6. Click **Build**, then accept **“Ground changes are permanent, like the game's terrain tools. Build?”** The build changes terrain, zones the level cells, then adds walls if selected.

**Clear** clears the selection; **Close** closes the tool. Neither restores built ground. Rebind the opening hotkey in **Options → Stepped Streets**.

## Settings

One cell is **8 m**. Left and right follow the street's centreline direction in the game.

| Setting | Choices or range | Default | What it controls |
|---|---|---|---|
| Side | Both, Left, Right | Both | Which frontage gets pads. |
| Zone | Game zone name | Residential Low | Zoning on the cells that came out level; existing zoning is kept. |
| Lot width | 2–6 cells | 3 | Pad width along the kerb, including any bank columns. |
| Step look | Planted bank, Low wall | Planted bank | Low wall adds the game's 0.9 m garden wall at the bank foot. Both looks use the same banks. |
| Advanced: Narrowest width | 2–6 cells, at most Lot width | 3 | How far a lot may narrow. A 2-cell pad cannot give up a bank column and still keep two zoned columns. |
| Advanced: Depth | 2–6 cells | 3 | Distance back from the kerb; obstacles can shorten a pad. |
| Advanced: Minimum grade | 4–12% | 6% | Flatter stretches are left alone. |
| Advanced: Highest step | 0.5–5.0 m | 3.2 m | Step ceiling, also limited by the lower pad's bank allowance. |
| Advanced: Bank grade | 0.20–0.50 | 0.40 | Rise divided by run; 0.40 means 40%. |
| Advanced: Earthwork limit | 1–8 m | 4 m | Maximum permitted cut or fill at a pad. |
| Opening hotkey | Rebindable in Options → Stepped Streets | Ctrl+Shift+S | Opens or toggles the tool; building is a separate action. |

## Banks and walls

Each bank takes **one or more whole 8 m columns from the lower pad's uphill end**. The remaining columns stay level. A 3-cell pad with one bank column keeps two columns to zone; the last pad in a run has no step after it and keeps its full width. The game removes isolated single-column zoning, so two level columns must remain beside a bank.

A pad **W** cells wide allows a step up to **(W − 2) × 8 m × bank grade**, or **Highest step** if that is smaller. At the default bank grade of 0.40, a 3-cell pad allows 3.2 m: the 2.88 m step on a 12% street fits in one bank column. Low wall keeps that same terrain profile and allowance; it adds decoration rather than supporting a taller step. Quays are outside v1.

Walls start 4 m behind the kerb and run to the pad's back edge. The build reads the length of `EU_Wall02` from the game's prefab and places unscaled pieces end to end; a final piece is kept when at least half fits. Its report counts emitted definitions, so it does not prove every prop survived the game's validation.

## Messages

| Message or reason | Meaning |
|---|---|
| “This street is flatter than 6%: nothing to step here.” | Nothing reaches the chosen minimum grade; the percentage follows that setting. |
| “Too steep for lots this wide” | The step exceeds Highest step or the bank allowance. Change the width or advanced settings and inspect the new preview. Low wall uses the same allowance. |
| Too much earthwork | A pad would need more cut or fill than the limit permits. |
| Blocked | Buildings, another road or water leave too little safe space for a pad. |
| “The terrain blend reaches a building, another road or water” | The outer bank or brush margin would touch protected ground. The whole build stops before changing terrain; choose a clearer frontage or reduce depth. |
| Too short for a lot | The remaining end of the street cannot fit the narrowest width. |
| “Ground changes are permanent, like the game's terrain tools. Build?” | Confirm before terrain is changed. Save first if you want to return to the original ground. |

The summary lists the planned lots, the step height range and empty lots. Build reports and errors go to `Logs\SteppedStreets.log`, next to the game's `Mods` folder.

## Known limits

- Terrain changes are permanent. Removing the mod, clearing the selection or closing the panel does not undo them.
- One street is built at a time. Shift-click extends that street; it is not an area tool.
- Zoning verification checks **one game cell per run**, rather than every requested cell. Its count is a count of runs, not total zoned cells.
- Wall placement counts emitted definitions without checking the resulting objects.
- Pad checks can shorten lots, but an obstructed outer terrain blend rejects the whole build before any brush runs. The check includes one terrain texel of clearance around the sampled brush footprint.
- The preview draws pad and step geometry; empty-lot reasons appear in the panel, and individual pad heights are in the build log.
- The apron is capped at 8 m and the outer blend at 24 m. Extreme sites can exceed the requested bank grade at those caps; the in-game terrain report is still needed.
- English only. No custom assets, quay walls or direct house placement.
- In-game appearance, houses staying on individual pads, performance and coexistence with other mods still need manual testing.

## Build and test

You need Windows, the installed game's Managed DLLs and network access for the first NuGet/npm restore. The mod targets **.NET Framework 4.8 with C# 9** and Core targets **netstandard2.0**. The solution tests target **.NET 9**: use **.NET SDK 9 or newer** (SDK 10 works) and install the **.NET 9 runtime**. The standalone mod references the game's framework DLLs; the official modding toolchain and `CSII_TOOLPATH` are not needed. The React panel needs **Node.js 18+** and npm.

From the repository root, build the panel before packaging the mod:

```powershell
Set-Location src/SteppedStreets.UI
npm ci
npm run build
Set-Location ../..
dotnet build SteppedStreets.sln -c Release
dotnet test SteppedStreets.sln
```

After `npm ci`, the mod's csproj builds the panel into the DLL output folder and includes LICENSE, credits and this guide. Build output stays under the project; **building never installs or deploys the mod**.

The default `ManagedPath` in `build/GameReferences.props` is `D:\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed`. Override it for another installation:

```powershell
dotnet build SteppedStreets.sln -c Release -p:ManagedPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
dotnet test SteppedStreets.sln -p:ManagedPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
```

## Install by hand

1. Close the game.
2. Create `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SteppedStreets\` if needed.
3. Copy **everything** in `src\SteppedStreets\bin\Release\` into that folder, keeping subfolders. Include the Core DLL and the panel assets along with the mod DLL.
4. Start the game. Check `Logs\SteppedStreets.log` next to `Mods` for **“Stepped Streets loading”** and any registration errors.

Remove the `SteppedStreets` mod folder to uninstall. Built terrain, zoning and vanilla wall props are intended to remain; save removal behaviour still needs the first in-game check.

## Credits and licence

- **Placemaker by jackwrichards (GPL-3.0):** terrain plumbing, raster and build-tool/object-definition code. The stepped-lot planner and wall-segment geometry are original work.
- **Grid Road Generator by Ti4goc (GPL-3.0):** the zoning pattern followed by Placemaker and this mod.
- **ParkingLotTool by KruemelGames (GPL-3.0):** the object-definition pattern credited in Placemaker's emitter.
- **ParkManager by tomkpunkt (GPL-3.0):** upstream attribution retained in the copied boundary-distance helper and Placemaker headers; this mod does not include its fallback terrain brushes, water or net emitter.

Full file-level provenance is in [CREDITS.md](CREDITS.md). Stepped Streets is licensed under [GNU GPL version 3](LICENSE). See the [PRD](docs/Stepped_Streets_PRD.md) for the design and manual acceptance criteria.
