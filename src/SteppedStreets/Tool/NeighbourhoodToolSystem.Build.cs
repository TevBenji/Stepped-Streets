// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
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

        // Every stage calls back within its own frame limits, so a build with no stage running this long is stuck.
        private const int MostIdleBuildFrames = 300;

        public void Build()
        {
            try
            {
                SetConfirming(false);
                if (State.Stage != ToolStage.Planned || Plan == null || !Plan.Buildable) { Message("Draw and close an area with a plan before building."); return; }
                if (BuildSystemsBusy()) { Message("Still building. Wait until it is done."); return; }
                if (SettingsMoved())
                {
                    Replan();
                    Message("The settings changed. Review the updated plan before building.");
                    return;
                }
                if (PlannedRequest?.ExistingStreets == true) { ZoneExistingStreets(); return; }
                // Buildings, roads and the ground can change while the plan is open.
                var fresh = SiteSnapshot.Capture(EntityManager, World, corners.ToArray());
                if (plannedSnapshot == null || fresh.Fingerprint != plannedSnapshot.Fingerprint)
                {
                    // After the re-plan, which clears the messages.
                    Replan();
                    Message("The site changed. Review the updated plan before building.");
                    return;
                }
                ResetBuild();
                buildPlan = Plan;
                buildRequest = PlannedRequest;
                // The settings match the plan (checked above), so this is the road it was laid out for.
                buildRoadName = Mod.Settings?.RoadName ?? Settings.SteppedStreetsSettings.DefaultRoad;
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

        /// <summary>
        /// Existing streets: no roads, and their zone blocks are already there, so the lots are levelled (when asked) and zoned
        /// straight away; then the services, parks and greenery as on new streets. No site check: the zoning reads the live
        /// cells and only paints the free ones, so houses growing along the streets while the plan is open can't spoil it, and
        /// in a living city they would otherwise send every Build back to re-plan. The levelling keeps off what was built and
        /// zoned when the plan was made.
        /// </summary>
        private void ZoneExistingStreets()
        {
            ResetBuild();
            buildPlan = Plan;
            buildRequest = PlannedRequest;
            State.Stage = ToolStage.Building;
            State.Progress = 0.1f;
            State.Summary = buildPlan.Ground != null ? "Levelling the lots..." : "Zoning...";
            Touch();
            Log(Invariant($"zoning existing streets: {buildPlan.Lots.Count} lots, {buildPlan.ZonedCells} cells, {buildPlan.Buildings.Count} buildings, {buildPlan.Trees.Count} trees and plants, ground {(buildPlan.Ground != null ? "levelled" : "as it is")}"));
            if (buildPlan.Ground == null) { ZoneExistingLots(); return; }
            var ground = buildPlan.Ground;
            World.GetOrCreateSystemManaged<TerrainBuildSystem>().Begin(() => ground.ToRaster(), AfterExistingGround);
        }

        private void AfterExistingGround(TerrainBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                Log("ground: " + report.Summary);
                buildReport.Add(report.Reached
                    ? Invariant($"lots levelled in {report.Passes} passes")
                    : Invariant($"lots levelled within {report.WorstError:0.0} m of the plan ({100f - report.FractionOff * 100f:0}% exact)"));
                ZoneExistingLots();
            }
            catch (Exception exception) { BuildFailed(exception, "Zoning failed to start. The ground changes remain."); }
        }

        private void ZoneExistingLots()
        {
            // Nothing to zone (an empty mix): the services, parks and greenery still go in.
            if (buildPlan.ZonedCells == 0)
            {
                buildReport.Add("nothing to zone");
                PlaceServices();
                return;
            }
            State.Progress = 0.4f;
            State.Summary = "Zoning...";
            Touch();
            StartZoning();
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
                if (StoppedByAnotherTool("laying the roads")) return;
                buildReport.Add(Invariant($"{report.Built} of {report.Pieces} road pieces"));
                if (report.Built == 0)
                {
                    Stop("The game did not build the roads, so nothing was zoned. The ground changes remain.");
                    return;
                }
                if (report.Built < report.Pieces) Message("Some road pieces were not built. Check the log for where.");
                // No zones in the mix: the lots are levelled for the player to zone, so there is nothing to wait for.
                if (buildPlan.ZonedCells == 0)
                {
                    buildReport.Add(buildPlan.Lots.Count > 0 ? "lots left unzoned" : "nothing to zone");
                    PlaceServices();
                    return;
                }
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
            World.GetOrCreateSystemManaged<BuildToolSystem>().Interrupted = false;
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
                var byZone = ZoneCells();
                zoneQueue = new Queue<(string, List<float2[]>)>(byZone.Select(pair => (pair.Key, pair.Value)));
                zonedRuns = requestedRuns = 0;
                Log(Invariant($"zoning: {levelCells} cells in {byZone.Count} zones"));
                ZoneNext(null);
            }
            catch (Exception exception) { BuildFailed(exception, "Zoning failed. The ground and roads remain."); }
        }

        /// <summary>
        /// The game's own zone cells to zone, by zone: every free cell of the blocks along the new roads that lies on a planned
        /// lot, as deep as the lot is levelled. Zoning the planner's own lot grid instead left patches across the game's grid, so
        /// hardly any house fitted (Game.Simulation.ZoneSpawnSystem.SelectBuilding needs the house's lot to fit the vacant lot).
        /// Zoning the blocks' full six cells put the deeper houses half on the bank behind the levelled lot (code review,
        /// 3 November 2026). The game's low-density houses are 2 or 4 cells deep, so the default 4-cell lot fits them all.
        /// </summary>
        private Dictionary<string, List<float2[]>> ZoneCells()
        {
            var byZone = new Dictionary<string, List<float2[]>>();
            levelCells = 0;
            var lots = buildPlan.Lots;
            if (lots.Count == 0) return byZone;
            // Each lot's frontage, carried back to the blocks' full depth.
            var reaches = new float2[lots.Count][];
            var backs = new float2[lots.Count];
            var buckets = new SpatialBuckets<int>(64f);
            float2 low = float.MaxValue, high = float.MinValue;
            for (int index = 0; index < lots.Count; index++)
            {
                var corners = lots[index].Pad.Corners;
                float2 start = corners[0].xz, end = corners[1].xz, back = math.normalizesafe(corners[3].xz - start);
                // The lot itself, front to its own back edge: a lot shortened to keep within the earthwork limit is zoned only
                // as deep as it was levelled.
                var reach = new[] { start, end, corners[2].xz, corners[3].xz };
                reaches[index] = reach;
                backs[index] = back;
                float2 min = math.min(math.min(reach[0], reach[1]), math.min(reach[2], reach[3]));
                float2 max = math.max(math.max(reach[0], reach[1]), math.max(reach[2], reach[3]));
                buckets.Add(min, max, index);
                low = math.min(low, min);
                high = math.max(high, max);
            }
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(),
                ComponentType.Exclude<Game.Tools.Temp>(), ComponentType.Exclude<Game.Common.Deleted>()))
            using (var blocks = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var entity in blocks)
                {
                    var block = EntityManager.GetComponentData<Block>(entity);
                    float extent = math.length((float2)block.m_Size) * 4f;
                    if (math.any(block.m_Position.xz < low - extent) || math.any(block.m_Position.xz > high + extent)) continue;
                    var cells = EntityManager.GetBuffer<Cell>(entity, true);
                    if (cells.Length < block.m_Size.x * block.m_Size.y) continue;
                    var area = EntityManager.HasComponent<ValidArea>(entity) ? EntityManager.GetComponentData<ValidArea>(entity).m_Area
                        : new int4(0, block.m_Size.x, 0, block.m_Size.y);
                    // Row 0 lies against the road, at the end the block's direction points to.
                    float2 forward = block.m_Direction, right = new float2(forward.y, -forward.x);
                    for (int row = area.z; row < area.w; row++)
                        for (int column = area.x; column < area.y; column++)
                        {
                            // Free cells only: the player's zoning, buildings and cells the game blocked stay as they are.
                            if (!SiteSnapshot.FreeCell(cells[row * block.m_Size.x + column])) continue;
                            float2 centre = ZoneUtils.GetCellPosition(block, new int2(column, row)).xz;
                            string zone = null;
                            foreach (int index in buckets.Near(centre))
                                // The lot must face the same road: its back points away from the road, as the block does.
                                if (math.dot(backs[index], forward) < -0.5f && SiteGeometry.Inside(centre, reaches[index])) { zone = lots[index].Zone; break; }
                            if (string.IsNullOrEmpty(zone)) continue;
                            if (!byZone.TryGetValue(zone, out var quads)) byZone[zone] = quads = new List<float2[]>();
                            // The cell's own square, pulled in a metre so the marquee takes this cell and no neighbour.
                            quads.Add(new[]
                            {
                                centre - right * 3f + forward * 3f, centre + right * 3f + forward * 3f,
                                centre + right * 3f - forward * 3f, centre - right * 3f - forward * 3f,
                            });
                            levelCells++;
                        }
                }
            return byZone;
        }

        private void ZoneNext(ZoneBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building) return;
                if (report != null && StoppedByAnotherTool("zoning")) return;
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
                var prefabs = World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
                var catalogue = ServiceCatalog.List(EntityManager, prefabs, Panel.PanelSystem.Localised);
                // Pocket parks come from the game's park buildings, which the services list may not hold.
                catalogue.AddRange(ServiceCatalog.Parks(EntityManager, prefabs, Panel.PanelSystem.Localised));
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
                        Label = entry.Label,
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
                if (services != null && StoppedByAnotherTool("placing the services")) return;
                if (services != null)
                {
                    Log("services: " + services.Summary);
                    buildReport.Add(Invariant($"{services.Placed} service buildings"));
                    if (services.Missing.Count > 0)
                        Message("The game would not place " + string.Join(", ", services.Missing) + " here. The rest of the neighbourhood is built.");
                }
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
                if (report != null && StoppedByAnotherTool("planting trees")) return;
                if (report != null) { Log("trees: " + report.Summary); buildReport.Add(Invariant($"{report.Placed} trees, bushes and rocks")); }
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
            if (report != null && StoppedByAnotherTool("building the walls")) return;
            if (report != null) { Log("walls: " + report.Summary); buildReport.Add(Invariant($"{report.Placed} wall pieces")); }
            State.Stage = ToolStage.Built;
            State.Progress = 1f;
            State.Summary = (buildRequest.ExistingStreets ? Invariant($"Zoned {buildPlan.Lots.Count} lots along existing streets: ")
                : Invariant($"Built {buildPlan.RoadLength / 1000f:0.0} km of road and {buildPlan.Lots.Count} lots: ")) + string.Join(", ", buildReport) + ".";
            Log(State.Summary);
            // The built area is history now: its plan cannot be built twice, and the preview stops drawing it over the real thing.
            Plan = null;
            EarlyRoads = null;
            PlanVersion++;
            // The new streets are snapping targets for the next area.
            RefreshRoadSnap();
            Touch();
        }

        /// <summary>
        /// True, once the build is stopped with a plain message, when another tool was opened while a stage waited to apply:
        /// carrying on would build the rest around the gap and say nothing (code review, 3 November 2026).
        /// </summary>
        private bool StoppedByAnotherTool(string doing)
        {
            var tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
            if (!tool.Interrupted) return false;
            tool.Interrupted = false;
            string done = buildReport.Count > 0 ? " Done before that: " + string.Join(", ", buildReport) + "." : "";
            Stop("The build stopped while " + doing + " because another tool was opened." + done);
            return true;
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
