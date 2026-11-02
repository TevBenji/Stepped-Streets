// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Linq;
using SteppedStreets.Build;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;
using static System.FormattableString;

namespace SteppedStreets.Tool
{
    public sealed partial class StepStreetToolSystem
    {
        private const float LevelTolerance = 0.3f;

        public void Build()
        {
            try
            {
                var terrainBuild = World.GetOrCreateSystemManaged<TerrainBuildSystem>();
                var zoneBuild = World.GetOrCreateSystemManaged<ZoneBuildSystem>();
                var wallBuild = World.GetOrCreateSystemManaged<WallBuildSystem>();
                if (State.Stage == ToolStage.Building || terrainBuild.Busy || zoneBuild.Busy || wallBuild.Busy)
                {
                    Message("Still building the last street. Wait until it is done.");
                    Log("still building the last street; wait until it is done.");
                    return;
                }
                if (!IsActive || State.Stage != ToolStage.Planned || plan == null || plan.Pads.Count == 0)
                {
                    Message("Select a street with planned pads before building.");
                    return;
                }

                // Buildings, water and terrain can change while the preview is open.
                var approvedPlan = plan;
                selection = selection.Refresh(EntityManager);
                Replan();
                if (!SiteGeometry.SamePlan(approvedPlan, plan))
                {
                    Message("The site changed. Review the updated preview before building.");
                    return;
                }
                var buildPlan = plan;
                float bankGrade = plannedBankGrade;
                string zoneName = plannedZoneName;
                StepLook look = plannedLook;
                var patches = StepPlanPatches.ToPatches(buildPlan, bankGrade);
                float bankWidth = StepPlanPatches.BankWidth(buildPlan, snapshot.GroundHeight, bankGrade);
                int slopedPatches = patches.Count(patch => patch.Sloped);
                Log(Invariant($"terrain: {patches.Count} patches ({patches.Count - slopedPatches} level pads, {slopedPatches} sloped banks), bank width {bankWidth:0.0} m"));
                State.Stage = ToolStage.Building;
                State.Summary = "Building the ground, then zoning the level cells...";
                State.Messages = buildPlan.Messages.ToArray();
                Touch();
                terrainBuild.Begin(patches, bankWidth, report => ZoneLevelCells(buildPlan, zoneName, bankGrade, look, report));
            }
            catch (Exception exception) { Fail(exception, "The street could not be built. Ground changes already made are permanent."); }
        }

        private void ZoneLevelCells(StepPlan buildPlan, string zoneName, float bankGrade, StepLook look, TerrainBuildReport terrainReport)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (!IsActive) throw new InvalidOperationException("The active tool changed during the terrain build.");
                Log("terrain: " + terrainReport.Summary);
                Message(terrainReport.Summary);
                if (!terrainReport.Reached)
                {
                    State.Stage = ToolStage.Failed;
                    stageBeforeClose = ToolStage.Failed;
                    State.Summary = "The ground did not reach the planned height. Nothing was zoned; ground changes already made remain.";
                    Message(State.Summary);
                    Log(State.Summary);
                    Touch();
                    return;
                }
                var terrainBuild = World.GetOrCreateSystemManaged<TerrainBuildSystem>();
                var quads = new List<float2[]>();
                int levelCells = 0;
                foreach (var pad in buildPlan.Pads)
                {
                    var cells = StepPlanner.LevelCells(pad, terrainBuild.HeightAt, LevelTolerance);
                    var stepAfter = buildPlan.Steps.FirstOrDefault(step => step.Earlier == pad);
                    int kept = pad.WidthCells - (stepAfter == null ? 0 : StepPlanPatches.BankColumns(stepAfter, bankGrade));
                    string warning = kept < StepPlanner.MinimumWidthCells
                        ? "; under two columns, so the game will wipe this pad's zoning" : "";
                    Log(Invariant($"pad {pad.Side} {pad.Start:0}-{pad.End:0} m along, height {pad.Height:0.00} m: {cells.Count} of {pad.WidthCells * pad.DepthCells} cells level, keeps {kept} of {pad.WidthCells} columns for zoning{warning}"));
                    levelCells += cells.Count;
                    quads.AddRange(StepPlanPatches.CellQuads(pad, cells));
                }
                if (quads.Count == 0)
                {
                    Log("zoning: no cell came out level, nothing to zone.");
                    Message("No cells came out level, so nothing was zoned.");
                    BuildWalls(buildPlan, bankGrade, look, terrainReport, 0, new ZoneBuildReport
                    {
                        Summary = "No cells came out level, nothing to zone.",
                    });
                    return;
                }
                Log(Invariant($"zoning: {levelCells} level cells in {quads.Count} runs as {zoneName}"));
                World.GetOrCreateSystemManaged<ZoneBuildSystem>().Begin(quads, zoneName,
                    report => BuildWalls(buildPlan, bankGrade, look, terrainReport, levelCells, report));
            }
            catch (Exception exception) { Fail(exception, "Zoning the level cells failed. The ground changes remain."); }
        }

        private void BuildWalls(StepPlan buildPlan, float bankGrade, StepLook look, TerrainBuildReport terrainReport,
            int levelCells, ZoneBuildReport zoneReport)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (!IsActive) throw new InvalidOperationException("The active tool changed during the zoning build.");
                Log("zoning: " + zoneReport.Summary);
                Message(zoneReport.Summary);
                if (zoneReport.Zoned < zoneReport.Requested)
                    Message("Some zoning runs were not verified. Check the zoning log for the affected cells.");
                if (look != StepLook.LowWall)
                {
                    FinishBuild(buildPlan, terrainReport, levelCells, zoneReport, null);
                    return;
                }
                var walls = StepPlanWalls.Segments(buildPlan, bankGrade);
                World.GetOrCreateSystemManaged<WallBuildSystem>().Begin(walls,
                    report => FinishBuild(buildPlan, terrainReport, levelCells, zoneReport, report));
            }
            catch (Exception exception) { Fail(exception, "Building the low walls failed. The ground and zoning remain."); }
        }

        private void FinishBuild(StepPlan buildPlan, TerrainBuildReport terrainReport, int levelCells,
            ZoneBuildReport zoneReport, WallBuildReport wallReport)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (wallReport != null)
                {
                    Log("walls: " + wallReport.Summary);
                    Message(wallReport.Summary);
                    if (wallReport.Placed < wallReport.Requested)
                        Message("Some low wall pieces could not be placed. The planted banks remain.");
                }
                State.Stage = ToolStage.Built;
                stageBeforeClose = ToolStage.Built;
                // The zoning API verifies one cell per run, so its result cannot honestly be reported as a count of zoned cells.
                State.Summary = Invariant($"Built {buildPlan.Pads.Count} pads: {levelCells} level cells, {zoneReport.Zoned}/{zoneReport.Requested} zoning runs verified, {terrainReport.Curtains} curtains, {wallReport?.Placed ?? 0} wall pieces");
                Log(State.Summary);
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The build finished, but its result could not be read. Check the log."); }
        }
    }
}
