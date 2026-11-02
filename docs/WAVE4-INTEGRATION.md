# Wave 4 integration — 2 November 2026

All four Codex workers' changes are merged into master. The planner, street picker, preview, toolbar icon, panel, zoning and low-wall build now use the real implementations; the temporary contract stubs are removed.

| Worker | Branch | Delivered | Branch validation |
|---|---|---|---|
| W1 | `t3/complete-step-planner` | Bank limits, aprons, earthwork checks, obstacle trimming and messages | Lead rerun: Release build clean; 69 tests passed |
| W2 | `t3/street-picker-preview-build` | Picker, snapshot, preview, hotkey and terrain → zoning → walls flow | Lead rerun: Release build clean; 52 tests passed |
| W3 | `t3/toolbar-panel-ui` | Toolbar icon beside Zones, panel, settings, Build confirmation and UI packaging | Lead rerun: Release build clean; 42 .NET tests, 5 UI tests and 8 JSON checks passed; TypeScript and CSS checks clean |
| W4 | `t3/low-wall-docs` | Garden-wall geometry/emitter and player documentation | Previous lead validated and merged as `5132272`: clean build, 48 tests; its six wall tests also pass in the integrated suite |

Merge commits: W1 `e718889`, W2 `c9a8b00`, W3 `6bf867f`. Conflicts retained every registration and credit block, used W1's complete Core types and W2's current settings, and combined UI/input assembly references.

## Corrections from integration review

- Zoning reads live terrain after the build. A successful global convergence check can still leave local errors, so substituting the planned target could incorrectly zone an uneven cell.
- The terrain raster checks the outer blend and a brush margin against the captured buildings, other roads and water. A conflict rejects the entire build before the first brush; pad-only obstacle checks did not cover that area. Regression tests demonstrate rejection outside the pad, brush-edge clearance and untouched ground.
- Terrain logs distinguish pads, banks and apron strips. The flat patches deliberately use vertex interpolation, so their `Sloped` flag does not identify banks.
- Planner advice no longer promises narrower lots will help; narrowing also reduces the bank allowance.
- Release packaging includes the GPL licence, credits and player guide alongside DLLs, the UI module, CSS and the icon.

## Final verification

| Command | Result |
|---|---|
| `npm ci` in `src/SteppedStreets.UI` | 138 packages installed; audit reported zero vulnerabilities |
| `dotnet build SteppedStreets.sln -c Release` | Successful; zero warnings and zero errors; webpack built the UI beside the DLLs |
| `dotnet test SteppedStreets.sln -c Release --no-build` | 87 passed, zero failed or skipped |
| `npm test` | 5 passed |
| `npm run typecheck` | No diagnostics |
| `node tools/css-lint.js ../SteppedStreets/bin/Release` | One CSS file; zero problems |
| `node --check ../SteppedStreets/bin/Release/SteppedStreets.mjs` | Passed |
| `pwsh -File src/SteppedStreets.UI/tools/check-panel-json.ps1` | 5 string and 3 numeric/culture cases passed |
| `git diff --check` | Clean |

The user's existing npm configuration prints an `Unknown user config "store-dir"` notice. It did not affect installation, compilation or tests. All ten copied API declaration files match the reference apart from attribution and whitespace. Core still references only Unity.Mathematics and the standard library.

## Still requiring the game

Automated checks do not prove that the toolbar renders in this game session, houses stay on individual pads, walls look right, or removing the mod leaves a loadable save. Run the PRD's 8%, 12% and 15% streets and its coexistence/performance checks before publishing.

The workers identified three remaining implementation differences: the preview has geometry rather than per-pad numeric map labels (heights are logged and reasons appear in the panel); Shift-click accepts connected named streets; extreme sites can exhaust the 8 m apron or 24 m blend cap. Zoning verification counts one sampled cell per run, and wall reports count emitted definitions. These limits are recorded in README and the PRD; they are not passed acceptance criteria.

No remote was pushed, and no reference or game-install file was edited. The lead installs the Release output in the already-authorized local `Mods/SteppedStreets` folder; builds themselves never deploy.
