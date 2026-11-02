// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Zones;
using SteppedStreets.Build;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Core.Planning;
using Unity.Entities;
using Unity.Mathematics;
using static System.FormattableString;

namespace SteppedStreets.Tool
{
    /// <summary>
    /// The build, in Placemaker's order (Game/Build/BuildService.cs: terraform, then nets, then zoning after the zone blocks):
    /// the ground first, so the roads sit on graded corridors; then the roads; then, once the game has made zone blocks along
    /// them, each zone's level cells; then trees and garden walls. Each stage logs a line; a failed stage stops the build.
    /// </summary>
    public sealed partial class NeighbourhoodToolSystem
    {
        private const float LevelTolerance = 0.3f;
        private const int BlocksFirstCheck = 30, BlocksStableFrames = 12, BlocksMostFrames = 150;

        private NeighbourhoodPlan buildPlan;
        private NeighbourhoodRequest buildRequest;
        private string buildRoadName;
        private readonly List<string> buildReport = new List<string>();
        private bool waitingForBlocks;
        private int blocksWaitStart, blockCount = -1, blocksStableSince;
        private Queue<(string Zone, List<float2[]> Quads)> zoneQueue;
        private int zonedRuns, requestedRuns, levelCells;
        private EntityQuery blockQuery;

        public void Build()
        {
            try
            {
                if (State.Stage != ToolStage.Planned || Plan == null || !Plan.Buildable) { Message("Draw and close an area with a plan before building."); return; }
                if (BuildSystemsBusy()) { Message("Still building. Wait until it is done."); return; }
                // Buildings, roads and the ground can change while the plan is open.
                var fresh = SiteSnapshot.Capture(EntityManager, World, corners.ToArray());
                if (plannedSnapshot == null || fresh.Fingerprint != plannedSnapshot.Fingerprint)
                {
                    Message("The site changed. Review the updated plan before building.");
                    Replan();
                    return;
                }
                buildPlan = Plan;
                buildRequest = PlannedRequest;
                buildRoadName = Mod.Settings?.RoadName ?? Settings.SteppedStreetsSettings.DefaultRoad;
                buildReport.Clear();
                State.Stage = ToolStage.Building;
                State.Progress = 0.05f;
                State.Summary = "Building the ground...";
                Touch();
                Log(Invariant($"build: {buildPlan.Roads.Count} roads, {buildPlan.Lots.Count} lots, {buildPlan.Trees.Count} trees, {buildPlan.Walls.Count} wall pieces"));
                if (buildPlan.Ground == null) { AfterGround(null); return; }
                var ground = buildPlan.Ground;
                // The planner already held buildings, water and existing roads at their own ground, so no blend can reach them.
                World.GetOrCreateSystemManaged<TerrainBuildSystem>().Begin(() => ground.ToRaster(), AfterGround);
            }
            catch (Exception exception) { BuildFailed(exception, "The build could not start. Nothing was changed."); }
        }

        private bool BuildSystemsBusy() =>
            World.GetOrCreateSystemManaged<TerrainBuildSystem>().Busy || World.GetOrCreateSystemManaged<NetBuildSystem>().Busy
            || World.GetOrCreateSystemManaged<ZoneBuildSystem>().Busy || World.GetOrCreateSystemManaged<TreeBuildSystem>().Busy
            || World.GetOrCreateSystemManaged<WallBuildSystem>().Busy;

        private void AfterGround(TerrainBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (report != null)
                {
                    Log("ground: " + report.Summary);
                    buildReport.Add(Invariant($"ground {(report.Reached ? "reached" : "missed")} in {report.Passes} passes, {report.Curtains} curtains"));
                    if (!report.Reached)
                    {
                        Stop("The ground did not reach the plan, so no roads were built. The ground changes made remain.");
                        return;
                    }
                }
                State.Progress = 0.35f;
                State.Summary = "Building the roads...";
                Touch();
                World.GetOrCreateSystemManaged<NetBuildSystem>().Begin(buildPlan, buildRoadName, AfterRoads);
            }
            catch (Exception exception) { BuildFailed(exception, "Building the roads failed. The ground changes remain."); }
        }

        private void AfterRoads(NetBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                Log("roads: " + report.Summary);
                buildReport.Add(Invariant($"{report.Built} of {report.Pieces} road pieces"));
                if (report.Built == 0)
                {
                    Stop("The game did not build the roads, so nothing was zoned. The ground changes remain.");
                    return;
                }
                if (report.Built < report.Pieces) Message("Some road pieces were not built. Check the log for where.");
                State.Progress = 0.55f;
                State.Summary = "Waiting for the zone blocks...";
                Touch();
                if (blockQuery == default) blockQuery = GetEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.Exclude<Game.Tools.Temp>());
                waitingForBlocks = true;
                blocksWaitStart = UnityEngine.Time.frameCount;
                blockCount = -1;
            }
            catch (Exception exception) { BuildFailed(exception, "Zoning failed to start. The ground and roads remain."); }
        }

        /// <summary>Runs every frame while building: waits for the game's zone blocks along the new roads.</summary>
        private void BuildUpdate()
        {
            if (!waitingForBlocks) return;
            int frame = UnityEngine.Time.frameCount;
            int count = blockQuery.CalculateEntityCount();
            if (count != blockCount) { blockCount = count; blocksStableSince = frame; }
            int waited = frame - blocksWaitStart;
            if (waited < BlocksFirstCheck || (frame - blocksStableSince < BlocksStableFrames && waited < BlocksMostFrames)) return;
            waitingForBlocks = false;
            Log(Invariant($"zone blocks settled after {waited} frames ({count} in the city)"));
            StartZoning();
        }

        private void StartZoning()
        {
            try
            {
                var terrain = World.GetOrCreateSystemManaged<TerrainBuildSystem>();
                var byZone = new Dictionary<string, List<float2[]>>();
                levelCells = 0;
                foreach (var lot in buildPlan.Lots)
                {
                    // Zone only what really came out level on the live ground (R8), unless the player turned levelling off.
                    var cells = buildRequest.LevelLots ? StepPlanner.LevelCells(lot.Pad, terrain.HeightAt, LevelTolerance) : lot.LevelCells;
                    if (cells.Count == 0) continue;
                    levelCells += cells.Count;
                    if (!byZone.TryGetValue(lot.Zone, out var quads)) byZone[lot.Zone] = quads = new List<float2[]>();
                    quads.AddRange(StepPlanPatches.CellQuads(lot.Pad, cells));
                }
                zoneQueue = new Queue<(string, List<float2[]>)>(byZone.Select(pair => (pair.Key, pair.Value)));
                zonedRuns = requestedRuns = 0;
                Log(Invariant($"zoning: {levelCells} level cells in {byZone.Count} zones"));
                ZoneNext(null);
            }
            catch (Exception exception) { BuildFailed(exception, "Zoning failed. The ground and roads remain."); }
        }

        private void ZoneNext(ZoneBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (report != null)
                {
                    Log("zoning: " + report.Summary);
                    zonedRuns += report.Zoned;
                    requestedRuns += report.Requested;
                }
                if (zoneQueue.Count == 0)
                {
                    buildReport.Add(Invariant($"{levelCells} level cells zoned ({zonedRuns} of {requestedRuns} runs verified)"));
                    if (zonedRuns < requestedRuns) Message("Some zoning runs were not verified. The log lists the cells.");
                    PlantTrees();
                    return;
                }
                var (zone, quads) = zoneQueue.Dequeue();
                State.Progress = 0.6f + 0.2f * (1f - zoneQueue.Count / (float)math.max(1, zoneQueue.Count + 1));
                State.Summary = "Zoning " + zone + "...";
                Touch();
                World.GetOrCreateSystemManaged<ZoneBuildSystem>().Begin(quads, zone, ZoneNext);
            }
            catch (Exception exception) { BuildFailed(exception, "Zoning failed. The ground and roads remain."); }
        }

        private void PlantTrees()
        {
            if (!buildRequest.Trees || buildPlan.Trees.Count == 0) { BuildWalls(null); return; }
            State.Progress = 0.85f;
            State.Summary = "Planting trees...";
            Touch();
            World.GetOrCreateSystemManaged<TreeBuildSystem>().Begin(buildPlan.Trees, BuildWalls);
        }

        private void BuildWalls(TreeBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (report != null) { Log("trees: " + report.Summary); buildReport.Add(Invariant($"{report.Placed} trees")); }
                if (buildPlan.Walls.Count == 0) { FinishBuild(null); return; }
                State.Progress = 0.93f;
                State.Summary = "Building walls...";
                Touch();
                World.GetOrCreateSystemManaged<WallBuildSystem>().Begin(buildPlan.Walls, FinishBuild);
            }
            catch (Exception exception) { BuildFailed(exception, "Building the walls failed. Everything else remains."); }
        }

        private void FinishBuild(WallBuildReport report)
        {
            if (State.Stage != ToolStage.Building) return;
            if (report != null) { Log("walls: " + report.Summary); buildReport.Add(Invariant($"{report.Placed} wall pieces")); }
            State.Stage = ToolStage.Built;
            State.Progress = 1f;
            State.Summary = Invariant($"Built {buildPlan.RoadLength / 1000f:0.0} km of road and {buildPlan.Lots.Count} lots: ") + string.Join(", ", buildReport) + ".";
            Log(State.Summary);
            // The built area is history now: the next area starts fresh, and its plan cannot be built twice.
            Plan = null;
            Touch();
        }

        private void Stop(string message)
        {
            State.Stage = ToolStage.Failed;
            State.Summary = message;
            Message(message);
            Log(message);
            waitingForBlocks = false;
            Touch();
        }

        private void BuildFailed(Exception exception, string message)
        {
            Mod.Logger.Error(exception, "Stepped Streets: " + message);
            Stop(message);
        }
    }
}
