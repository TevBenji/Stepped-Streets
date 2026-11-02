// Placemaker — main-thread snapshot of the world around the selected area.
//
// Generators run on a worker thread (PreviewWorker) and must never touch ECS or the terrain
// height data (both main-thread only). This copies what they need into plain arrays: a height
// grid (idea from Grid Road Generator's HeightSnapshot), a coarser water-depth grid, nearby nets
// as Bézier curves with widths, building lots as obstacles, and the asset info map. The result
// (SiteContext) is immutable after capture and therefore safe to read from any thread.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: keep fixed 4 m terrain and 8 m water grids, building lots and sampled road corridors only; reject capture failures instead of assuming dry ground.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SteppedStreets.Tool
{
    internal sealed class SiteSnapshot
    {
        private const float HeightSpacing = 4f;
        private const float WaterSpacing = 8f;
        private readonly float2 origin;
        private readonly int heightColumns, heightRows, waterColumns, waterRows;
        private readonly float[] heights, waterDepths;
        private readonly float2[][] lots;
        private readonly RoadSegment[] roads;

        private struct RoadSegment
        {
            public float2 Start, End;
            public float Clearance;
        }

        private SiteSnapshot(EntityManager entities, World world, StreetCapture street, int depthCells)
        {
            float2 minimum = new float2(float.MaxValue), maximum = new float2(float.MinValue);
            foreach (var point in street.Centreline)
            {
                minimum = math.min(minimum, point.xz);
                maximum = math.max(maximum, point.xz);
            }
            float margin = street.HalfWidth + depthCells * 8f + 32f;
            origin = minimum - margin;
            maximum += margin;
            heightColumns = math.max(2, (int)math.ceil((maximum.x - origin.x) / HeightSpacing) + 1);
            heightRows = math.max(2, (int)math.ceil((maximum.y - origin.y) / HeightSpacing) + 1);
            heights = new float[checked(heightColumns * heightRows)];
            var terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
            var heightData = terrain.GetHeightData();
            for (int row = 0; row < heightRows; row++)
                for (int column = 0; column < heightColumns; column++)
                    heights[row * heightColumns + column] = TerrainUtils.SampleHeight(ref heightData,
                        new float3(origin.x + column * HeightSpacing, 0f, origin.y + row * HeightSpacing));

            waterColumns = math.max(2, (int)math.ceil((maximum.x - origin.x) / WaterSpacing) + 1);
            waterRows = math.max(2, (int)math.ceil((maximum.y - origin.y) / WaterSpacing) + 1);
            waterDepths = new float[checked(waterColumns * waterRows)];
            var water = world.GetOrCreateSystemManaged<WaterSystem>();
            var waterData = water.GetSurfacesData(out var dependencies);
            dependencies.Complete();
            for (int row = 0; row < waterRows; row++)
                for (int column = 0; column < waterColumns; column++)
                    waterDepths[row * waterColumns + column] = WaterUtils.SampleDepth(ref waterData,
                        new float3(origin.x + column * WaterSpacing, 0f, origin.y + row * WaterSpacing));

            // ponytail: one scan per selection/settings edit; use the game's spatial search trees if capture exceeds R10's 250 ms budget.
            lots = CaptureLots(entities, origin, maximum);
            roads = CaptureRoads(entities, new HashSet<Entity>(street.Edges), origin, maximum);
        }

        internal static SiteSnapshot Capture(EntityManager entities, World world, StreetCapture street, int depthCells)
        {
            try { return new SiteSnapshot(entities, world, street, depthCells); }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: taking the site snapshot failed");
                throw;
            }
        }

        public float GroundHeight(float2 point) => SiteGeometry.Bilinear(heights, heightColumns, heightRows, origin, HeightSpacing, point);

        public bool Blocked(float2 point)
        {
            float depth = SiteGeometry.Bilinear(waterDepths, waterColumns, waterRows, origin, WaterSpacing, point);
            if (!math.isfinite(depth) || depth > 0.1f) return true;
            foreach (var lot in lots)
                if (SiteGeometry.Inside(point, lot)) return true;
            foreach (var road in roads)
                if (SiteGeometry.DistanceSquared(point, road.Start, road.End) <= road.Clearance * road.Clearance) return true;
            return false;
        }

        private static float2[][] CaptureLots(EntityManager entities, float2 minimum, float2 maximum)
        {
            var result = new List<float2[]>();
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var buildings = query.ToEntityArray(Allocator.Temp))
                foreach (var building in buildings)
                {
                    Entity prefab = entities.GetComponentData<PrefabRef>(building).m_Prefab;
                    if (!entities.HasComponent<BuildingData>(prefab)) continue;
                    float2 size = (float2)entities.GetComponentData<BuildingData>(prefab).m_LotSize * 8f;
                    if (!math.all(size > 0f)) continue;
                    var transform = entities.GetComponentData<Game.Objects.Transform>(building);
                    var corners = Game.Objects.ObjectUtils.CalculateBaseCorners(transform.m_Position, transform.m_Rotation, size);
                    float2 lower = math.min(math.min(corners.a.xz, corners.b.xz), math.min(corners.c.xz, corners.d.xz));
                    float2 upper = math.max(math.max(corners.a.xz, corners.b.xz), math.max(corners.c.xz, corners.d.xz));
                    if (math.any(upper < minimum) || math.any(lower > maximum)) continue;
                    result.Add(new[] { corners.a.xz, corners.b.xz, corners.c.xz, corners.d.xz });
                }
            return result.ToArray();
        }

        private static RoadSegment[] CaptureRoads(EntityManager entities, HashSet<Entity> selected, float2 minimum, float2 maximum)
        {
            var result = new List<RoadSegment>();
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Composition>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var edges = query.ToEntityArray(Allocator.Temp))
                foreach (var edge in edges)
                {
                    if (selected.Contains(edge)) continue;
                    Entity composition = entities.GetComponentData<Composition>(edge).m_Edge;
                    var data = entities.GetComponentData<NetCompositionData>(composition);
                    if ((data.m_Flags.m_General & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) continue;
                    var curve = entities.GetComponentData<Curve>(edge);
                    float clearance = data.m_Width * 0.5f + 2f;
                    var bounds = MathUtils.Bounds(curve.m_Bezier);
                    if (math.any(bounds.max.xz + clearance < minimum) || math.any(bounds.min.xz - clearance > maximum)) continue;
                    int segments = math.max(1, (int)math.ceil(curve.m_Length / 4f));
                    float2 start = curve.m_Bezier.a.xz;
                    for (int index = 1; index <= segments; index++)
                    {
                        float2 end = MathUtils.Position(curve.m_Bezier, index / (float)segments).xz;
                        result.Add(new RoadSegment { Start = start, End = end, Clearance = clearance });
                        start = end;
                    }
                }
            return result.ToArray();
        }
    }
}
