// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using SteppedStreets.Build;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Core.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static System.FormattableString;

namespace SteppedStreets.Tool
{
    /// <summary>
    /// Remove last neighbourhood. Before a build the tool notes the edges, objects and buildings round the area; once built,
    /// what stands there that didn't before is the build's, bar the player's road where a connection split it and houses grown
    /// meanwhile. Removing deletes those the way the bulldozer does, with the houses grown on the build's lots since, and then
    /// takes the zone off the cells it zoned. The ground stays. The record lasts for this game session: the next build or
    /// another city clears it.
    /// </summary>
    public sealed partial class NeighbourhoodToolSystem
    {
        // Round the area and the planned roads: room for the banks, the trees on them and the connections' ends.
        private const float RecordMargin = 60f;

        private sealed class BuildRecord
        {
            public float2 Low, High;
            public readonly HashSet<Entity> Before = new HashSet<Entity>();
            public readonly Dictionary<Entity, Bezier4x3> EdgesBefore = new Dictionary<Entity, Bezier4x3>();
            /// <summary>The zoned lots' outlines, for the houses that grow on them.</summary>
            public readonly List<float2[]> Lots = new List<float2[]>();
            public readonly List<(string Zone, List<float2[]> Quads)> Zoned = new List<(string, List<float2[]>)>();
            public readonly List<Entity> Created = new List<Entity>();
            public int Edges, Buildings, Objects, Cells, Houses, NotRemoved;
        }

        private BuildRecord lastBuild;
        private Queue<(string Zone, List<float2[]> Quads)> unzoneQueue;

        /// <summary>Notes what stands round the area before a build, so what the build adds can be told apart.</summary>
        private void TrackBuild()
        {
            try
            {
                var record = new BuildRecord();
                var points = new List<float2>(corners);
                foreach (var road in buildPlan.Roads)
                    foreach (var point in road.Points) points.Add(point.xz);
                Poly.Bounds(points.ToArray(), out var low, out var high);
                record.Low = low - RecordMargin;
                record.High = high + RecordMargin;
                foreach (var (entity, _) in Standing(record)) record.Before.Add(entity);
                foreach (var (edge, curve) in StandingEdges(record))
                {
                    record.Before.Add(edge);
                    record.EdgesBefore[edge] = curve;
                }
                foreach (var lot in buildPlan.Lots)
                    if (lot.Zone.Length > 0) record.Lots.Add(lot.Pad.Corners.Select(corner => corner.xz).ToArray());
                lastBuild = record;
            }
            catch (Exception exception)
            {
                lastBuild = null;
                Mod.Logger.Warn("Stepped Streets: noting the site before the build failed, so this build can't be removed: " + exception.Message);
            }
        }

        private void RememberZoning(Dictionary<string, List<float2[]>> byZone)
        {
            if (lastBuild == null) return;
            foreach (var pair in byZone)
            {
                lastBuild.Zoned.Add((pair.Key, pair.Value));
                lastBuild.Cells += pair.Value.Count;
            }
        }

        /// <summary>
        /// What the build added: what stands in the box now that didn't before. Not the two halves of a player's road a connection
        /// split (they lie along the old edge), nor houses grown meanwhile: those count only on the build's lots, when removing.
        /// </summary>
        private void FinishTracking()
        {
            var record = lastBuild;
            if (record == null) return;
            try
            {
                var split = record.EdgesBefore.Where(pair => Gone(pair.Key)).Select(pair => pair.Value.xz).ToList();
                foreach (var (edge, curve) in StandingEdges(record))
                {
                    if (record.Before.Contains(edge)) continue;
                    float2 middle = MathUtils.Position(curve, 0.5f).xz;
                    if (split.Any(old => MathUtils.Distance(old, middle, out _) < 1f)) continue;
                    record.Created.Add(edge);
                    record.Edges++;
                }
                foreach (var (entity, _) in Standing(record))
                {
                    if (record.Before.Contains(entity)) continue;
                    if (EntityManager.HasComponent<Game.Buildings.Building>(entity))
                    {
                        // The game grows houses by itself; any other building can only be the build's (services, parks).
                        if (Growable(entity)) continue;
                        record.Buildings++;
                    }
                    else record.Objects++;
                    record.Created.Add(entity);
                }
                State.CanRemoveLast = true;
                State.RemoveLastSummary = Contents(record, "any houses grown on its lots");
                Log("remove record: " + State.RemoveLastSummary);
            }
            catch (Exception exception)
            {
                ForgetLastBuild();
                Mod.Logger.Warn("Stepped Streets: noting what the build placed failed, so it can't be removed: " + exception.Message);
            }
        }

        /// <summary>Removes what the last build placed and the houses grown on its lots, then its zoning. The ground stays.</summary>
        public void RemoveLast()
        {
            try
            {
                if (State.Stage == ToolStage.Building || BuildSystemsBusy()) { Message("Still building. Wait until it is done."); return; }
                if (lastBuild == null || !State.CanRemoveLast)
                {
                    Message("There is nothing to remove. Remove works on the last build until the next one, or until you load a city.");
                    return;
                }
                var record = lastBuild;
                var targets = record.Created.Where(entity => !Gone(entity)).ToList();
                var houses = GrownHouses(record);
                record.Houses = houses.Count;
                targets.AddRange(houses);
                SetConfirming(false);
                World.GetOrCreateSystemManaged<BuildToolSystem>().Interrupted = false;
                State.Stage = ToolStage.Building;
                State.Removing = true;
                State.Progress = 0.1f;
                State.Summary = "Removing the roads, buildings and greenery...";
                Touch();
                Log(Invariant($"removing the last build: {targets.Count} roads, buildings and objects ({houses.Count} grown houses), then {record.Cells} zoned cells"));
                World.GetOrCreateSystemManaged<RemoveBuildSystem>().Begin(targets, AfterRemoving);
            }
            catch (Exception exception) { RemovalFailed(exception); }
        }

        private void AfterRemoving(RemoveBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building || !State.Removing) return;
                Log("removal: " + report.Summary);
                if (RemovalInterrupted()) return;
                lastBuild.NotRemoved = report.Requested - report.Removed;
                unzoneQueue = new Queue<(string, List<float2[]>)>(lastBuild.Zoned);
                UnzoneNext(null);
            }
            catch (Exception exception) { RemovalFailed(exception); }
        }

        // After the deletions: houses can no longer hold a cell, and new roads' blocks are gone with them (their cells count as done).
        private void UnzoneNext(ZoneBuildReport report)
        {
            try
            {
                if (State.Stage != ToolStage.Building || !State.Removing) return;
                if (report != null)
                {
                    Log("dezoning: " + report.Summary);
                    if (RemovalInterrupted()) return;
                }
                if (unzoneQueue == null || unzoneQueue.Count == 0) { FinishRemoving(); return; }
                var (zone, quads) = unzoneQueue.Dequeue();
                State.Progress = 0.7f;
                State.Summary = "Taking off the zoning...";
                Touch();
                World.GetOrCreateSystemManaged<ZoneBuildSystem>().BeginRemove(quads, zone, UnzoneNext);
            }
            catch (Exception exception) { RemovalFailed(exception); }
        }

        private void FinishRemoving()
        {
            var record = lastBuild;
            string summary = "Removed " + Contents(record, record.Houses > 0 ? Count(record.Houses, "grown house", "grown houses") : null) + "."
                + (record.NotRemoved > 0 ? Invariant($" {record.NotRemoved} of them would not go; the log lists the removal.") : "")
                + " The ground changes stay.";
            ForgetLastBuild();
            // The area is still drawn: a setting changed or a corner moved plans it again on the ground as it is now.
            Plan = null;
            EarlyRoads = null;
            PlanVersion++;
            ClearPlanState();
            State.Stage = IsActive ? ToolStage.Drawing : ToolStage.Closed;
            State.Summary = summary;
            State.Messages = new[] { summary };
            RefreshRoadSnap();
            Touch();
            Log(summary);
        }

        /// <summary>A removal another tool cut short: the record stays, so Remove can finish it.</summary>
        private bool RemovalInterrupted()
        {
            var tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
            if (!tool.Interrupted) return false;
            tool.Interrupted = false;
            State.Removing = false;
            State.Stage = IsActive ? ToolStage.Built : ToolStage.Closed;
            Message("Removing stopped because another tool was opened. Press Remove again to finish.");
            Touch();
            return true;
        }

        private void RemovalFailed(Exception exception)
        {
            State.Removing = false;
            BuildFailed(exception, "Removing stopped. The log has the details.");
        }

        /// <summary>Clears the record: a new build starts, or another city loads.</summary>
        private void ForgetLastBuild()
        {
            lastBuild = null;
            unzoneQueue = null;
            State.CanRemoveLast = false;
            State.RemoveLastSummary = "";
            State.Removing = false;
        }

        /// <summary>Houses the game grew on the build's zoned lots, which weren't there before it.</summary>
        private List<Entity> GrownHouses(BuildRecord record)
        {
            var houses = new List<Entity>();
            if (record.Lots.Count == 0) return houses;
            var lots = new SpatialBuckets<int>(64f);
            for (int index = 0; index < record.Lots.Count; index++)
            {
                Poly.Bounds(record.Lots[index], out var low, out var high);
                lots.Add(low - 1f, high + 1f, index);
            }
            var created = new HashSet<Entity>(record.Created);
            foreach (var (entity, position) in Standing(record))
            {
                if (record.Before.Contains(entity) || created.Contains(entity) || !EntityManager.HasComponent<Game.Buildings.Building>(entity) || !Growable(entity)) continue;
                foreach (int index in lots.Near(position.xz))
                    if (SiteGeometry.Inside(position.xz, record.Lots[index])) { houses.Add(entity); break; }
            }
            return houses;
        }

        private bool Growable(Entity building) => EntityManager.HasComponent<PrefabRef>(building)
            && EntityManager.HasComponent<SpawnableBuildingData>(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab);

        private bool Gone(Entity entity) => !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity);

        /// <summary>
        /// The objects and buildings of their own (not parts of another) standing in the record's box: trees and plants, props,
        /// walls and buildings, as Placemaker's clearing finds them; not vehicles or people.
        /// </summary>
        private List<(Entity Entity, float3 Position)> Standing(BuildRecord record)
        {
            var found = new List<(Entity, float3)>();
            using (var query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                Any = new[] { ComponentType.ReadOnly<Game.Objects.Plant>(), ComponentType.ReadOnly<Game.Objects.Static>(), ComponentType.ReadOnly<Game.Buildings.Building>() },
                None = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var entities = query.ToEntityArray(Allocator.Temp))
            using (var transforms = query.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
                for (int index = 0; index < entities.Length; index++)
                {
                    float2 position = transforms[index].m_Position.xz;
                    if (math.all(position >= record.Low) && math.all(position <= record.High)) found.Add((entities[index], transforms[index].m_Position));
                }
            return found;
        }

        /// <summary>The edges of their own (roads, paths) with an end or their middle in the record's box.</summary>
        private List<(Entity Edge, Bezier4x3 Curve)> StandingEdges(BuildRecord record)
        {
            var found = new List<(Entity, Bezier4x3)>();
            bool Inside(float3 point) => math.all(point.xz >= record.Low) && math.all(point.xz <= record.High);
            using (var query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var edges = query.ToEntityArray(Allocator.Temp))
            using (var curves = query.ToComponentDataArray<Curve>(Allocator.Temp))
                for (int index = 0; index < edges.Length; index++)
                {
                    var curve = curves[index].m_Bezier;
                    if (Inside(curve.a) || Inside(curve.d) || Inside(MathUtils.Position(curve, 0.5f))) found.Add((edges[index], curve));
                }
            return found;
        }

        /// <summary>"34 road pieces, 2 service buildings, the zoning of 210 cells and ..."; <paramref name="last"/> ends the list.</summary>
        private static string Contents(BuildRecord record, string last)
        {
            var parts = new List<string>();
            if (record.Edges > 0) parts.Add(Count(record.Edges, "road piece", "road pieces"));
            if (record.Buildings > 0) parts.Add(Count(record.Buildings, "service building", "service buildings"));
            if (record.Objects > 0) parts.Add(Count(record.Objects, "tree, plant or wall", "trees, plants and walls"));
            if (record.Cells > 0) parts.Add("the zoning of " + Count(record.Cells, "cell", "cells"));
            if (!string.IsNullOrEmpty(last)) parts.Add(last);
            if (parts.Count == 0) return "nothing";
            return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        private static string Count(int count, string one, string many) => Invariant($"{count:N0} {(count == 1 ? one : many)}");
    }
}
