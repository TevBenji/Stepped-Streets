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
        private int idleBuildFrames;
        private string taskRoadName, plannedRoadName;

        // Every stage calls back within its own frame limits, so a build with no stage running this long is stuck.
        private const int MostIdleBuildFrames = 300;

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
                ResetBuild();
                buildPlan = Plan;
                buildRequest = PlannedRequest;
                // The road the plan was laid out for (its half-width), even if the Options page changed it since.
                buildRoadName = plannedRoadName ?? Mod.Settings?.RoadName ?? Settings.SteppedStreetsSettings.DefaultRoad;
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
            || World.GetOrCreateSystemManaged<ZoneBuildSystem>().Busy || World.GetOrCreateSystemManaged<ObjectBuildSystem>().Busy
            || World.GetOrCreateSystemManaged<WallBuildSystem>().Busy;

        private void AfterGround(TerrainBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (report != null)
                {
                    Log("ground: " + report.Summary);
                    // The roads are built whatever the check says: the ground lands within a metre or so of the plan even when the
                    // last few texels never settle, and the game's roads follow the ground they are laid on.
                    buildReport.Add(report.Reached
                        ? Invariant($"ground reached in {report.Passes} passes")
                        : Invariant($"ground within {report.WorstError:0.0} m of the plan ({100f - report.FractionOff * 100f:0}% exact)"));
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

        private void ResetBuild()
        {
            waitingForBlocks = false;
            zoneQueue = null;
            idleBuildFrames = 0;
            buildReport.Clear();
        }

        /// <summary>
        /// Called every frame by StepStreetHotkeySystem, which always runs: a ToolBaseSystem stops updating when the player picks
        /// another tool, and the build must still wait for the zone blocks and finish.
        /// </summary>
        internal void BuildTick()
        {
            if (State.Stage != ToolStage.Building) return;
            try
            {
                if (waitingForBlocks) { idleBuildFrames = 0; WaitForBlocks(); return; }
                if (BuildSystemsBusy()) { idleBuildFrames = 0; return; }
                if (++idleBuildFrames > MostIdleBuildFrames) Stop("The build stopped before it finished. What was built remains.");
            }
            catch (Exception exception) { BuildFailed(exception, "The build stopped. What was built remains."); }
        }

        /// <summary>Waits for the game's zone blocks along the new roads, then zones.</summary>
        private void WaitForBlocks()
        {
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
                var byZone = new Dictionary<string, List<float2[]>>();
                levelCells = 0;
                foreach (var lot in buildPlan.Lots)
                {
                    // Every cell of the lot, front row included, like the game's fill: the game starts a house lot only at a
                    // zoned cell against the road, so zoning just the cells that came out dead level grew nothing.
                    var cells = lot.LevelCells;
                    if (cells.Count == 0) continue;
                    levelCells += cells.Count;
                    if (!byZone.TryGetValue(lot.Zone, out var quads)) byZone[lot.Zone] = quads = new List<float2[]>();
                    quads.AddRange(StepPlanPatches.CellQuads(lot.Pad, cells));
                }
                zoneQueue = new Queue<(string, List<float2[]>)>(byZone.Select(pair => (pair.Key, pair.Value)));
                zonedRuns = requestedRuns = 0;
                Log(Invariant($"zoning: {levelCells} cells in {byZone.Count} zones"));
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
                    buildReport.Add(Invariant($"{levelCells} cells zoned ({zonedRuns} of {requestedRuns} runs verified)"));
                    if (zonedRuns < requestedRuns) Message("Some zoning runs were not verified. The log lists the cells.");
                    ReportGrowth();
                    PlaceServices();
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

        /// <summary>
        /// Houses only grow where the game sees demand (ZoneSpawnSystem: residential needs the average of low, medium and high
        /// building demand above zero, and each density its own demand of at least 1). Logs the vacant lots the new zoning made
        /// and the demand, and tells the player when a chosen zone has none, so empty lots don't look like a failed build.
        /// </summary>
        private void ReportGrowth()
        {
            try
            {
                Core.Terrain.Poly.Bounds(corners.ToArray(), out var low, out var high);
                low -= 60f;
                high += 60f;
                int lots = 0;
                using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<VacantLot>(),
                    ComponentType.Exclude<Game.Tools.Temp>()))
                using (var blocks = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                    foreach (var block in blocks)
                    {
                        var position = EntityManager.GetComponentData<Block>(block).m_Position.xz;
                        if (math.any(position < low) || math.any(position > high)) continue;
                        lots += EntityManager.GetBuffer<VacantLot>(block, true).Length;
                    }
                var residential = World.GetOrCreateSystemManaged<Game.Simulation.ResidentialDemandSystem>().buildingDemand;
                int commercial = World.GetOrCreateSystemManaged<Game.Simulation.CommercialDemandSystem>().buildingDemand;
                var industrial = World.GetOrCreateSystemManaged<Game.Simulation.IndustrialDemandSystem>();
                Log(Invariant($"growth: {lots} vacant lots in and around the area; building demand residential low/medium/high {residential.x}/{residential.y}/{residential.z}, commercial {commercial}, industrial {industrial.industrialBuildingDemand}, office {industrial.officeBuildingDemand}"));
                if (lots == 0) Message("The zoning made no vacant lots for houses. The log has the zoning details.");
                var waiting = new List<string>();
                foreach (var zone in buildPlan.ZoneCounts)
                {
                    string name = zone.Key.ToLowerInvariant();
                    bool none = name.Contains("residential")
                        ? (residential.x + residential.y + residential.z) / 3 <= 0
                            || (name.Contains("high") ? residential.z : name.Contains("medium") || name.Contains("row") ? residential.y : residential.x) < 1
                        : name.Contains("commercial") ? commercial <= 0
                        : name.Contains("office") ? industrial.officeBuildingDemand <= 0
                        : name.Contains("industrial") && industrial.industrialBuildingDemand <= 0;
                    if (none) waiting.Add(zone.Key);
                }
                if (waiting.Count > 0)
                    Message("No demand for " + string.Join(", ", waiting) + " right now, so houses there grow once the city needs them (the demand bars in the zoning menu show it).");
            }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets: reading the demand after zoning failed: " + exception.Message); }
        }

        /// <summary>The service buildings the player mixed in, on their graded pads, facing their roads; after zoning, so they
        /// don't change the zone blocks the zoning was checked against.</summary>
        private void PlaceServices()
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                var catalogue = ServiceCatalog.List(EntityManager, World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>(), null);
                var placements = new List<ObjectBuildSystem.Placement>();
                foreach (var building in buildPlan.Buildings)
                {
                    var entry = catalogue.Find(candidate => candidate.Name == building.Name);
                    if (entry == null) { Log("services: " + building.Name + " is no longer available; skipped"); continue; }
                    // A building's front (local +z) faces the road (Game.Buildings.BuildingUtils.CalculateFrontPosition).
                    var forward = new float3(building.Forward.x, 0f, building.Forward.y);
                    placements.Add(new ObjectBuildSystem.Placement
                    {
                        Prefab = entry.Prefab,
                        Position = building.Position,
                        Rotation = quaternion.LookRotationSafe(forward, math.up()),
                        Seed = (int)(math.hash(building.Position) & 0x7FFFFFFF),
                    });
                }
                if (placements.Count == 0) { PlantTrees(null); return; }
                State.Progress = 0.82f;
                State.Summary = "Placing the services...";
                Touch();
                World.GetOrCreateSystemManaged<ObjectBuildSystem>().BeginBuildings(placements, PlantTrees);
            }
            catch (Exception exception) { BuildFailed(exception, "Placing the services failed. The ground, roads and zones remain."); }
        }

        private void PlantTrees(ObjectBuildReport services)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (services != null) { Log("services: " + services.Summary); buildReport.Add(Invariant($"{services.Placed} service buildings")); }
                if (!buildRequest.Trees || buildPlan.Trees.Count == 0) { BuildWalls(null); return; }
                State.Progress = 0.87f;
                State.Summary = "Planting trees...";
                Touch();
                World.GetOrCreateSystemManaged<ObjectBuildSystem>().Begin(buildPlan.Trees, BuildWalls);
            }
            catch (Exception exception) { BuildFailed(exception, "Planting trees failed. Everything else remains."); }
        }

        private void BuildWalls(ObjectBuildReport report)
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
            // The built area is history now: its plan cannot be built twice, and the preview stops drawing it over the real thing.
            Plan = null;
            EarlyRoads = null;
            PlanVersion++;
            // The new streets are snapping targets for the next area.
            RefreshRoadSnap();
            Touch();
        }

        private void Stop(string message)
        {
            State.Stage = ToolStage.Failed;
            State.Summary = message;
            Message(message);
            Log(message);
            waitingForBlocks = false;
            zoneQueue = null;
            Touch();
        }

        private void BuildFailed(Exception exception, string message)
        {
            Mod.Logger.Error(exception, "Stepped Streets: " + message);
            Stop(message);
        }
    }
}
