// Placemaker — main-thread snapshot of the world around the selected area.
//
// Generators run on a worker thread (PreviewWorker) and must never touch ECS or the terrain
// height data (both main-thread only). This copies what they need into plain arrays: a height
// grid (idea from Grid Road Generator's HeightSnapshot), a coarser water-depth grid, nearby nets
// as Bézier curves with widths, building lots as obstacles, and the asset info map. The result
// (SiteContext) is immutable after capture and therefore safe to read from any thread.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: a drawn area instead of one street; fixed 4 m terrain and 8 m water grids, building lots and water rasterised into one blocked grid (water also on its own, for the coastal layout's shore), existing road edges sampled for the neighbourhood generator with their entity handles, and a fingerprint to notice changes before a build.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SteppedStreets.Core.Neighbourhood;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SteppedStreets.Tool
{
    internal sealed class SiteSnapshot
    {
        private const float FinestSpacing = 4f;
        private const float MostSamples = 1000000f;
        // The connection may reach an existing road up to 400 m from the area; the survey reaches a little further, and only
        // as far as the nearest road needs (sampling heights is most of the snapshot's time).
        private const float MinimumMargin = 80f;
        private const float MaximumMargin = 460f;
        private const float RoadReach = 400f;
        // The fingerprint watches only the area and a strip round it (its banks): houses growing further out while the plan is
        // open must not keep sending Build back to re-plan.
        private const float FingerprintMargin = 40f;

        private readonly float2 origin;
        private readonly float HeightSpacing;
        private readonly int columns, rows;
        private readonly float[] heights;
        private readonly bool[] blocked;
        private readonly bool[] underWater;

        public readonly ExistingRoad[] ExistingRoads;
        public readonly long Fingerprint;
        public readonly int BuildingCount;

        private SiteSnapshot(EntityManager entities, World world, float2[] boundary)
        {
            float2 low = new float2(float.MaxValue), high = new float2(float.MinValue);
            foreach (var corner in boundary) { low = math.min(low, corner); high = math.max(high, corner); }
            ExistingRoads = CaptureRoads(entities, low - RoadReach, high + RoadReach);
            float nearestRoad = float.MaxValue;
            foreach (var road in ExistingRoads)
                foreach (var point in road.Points)
                    nearestRoad = math.min(nearestRoad, Core.Terrain.Poly.Contains(boundary, point.xz) ? 0f : Core.Terrain.Poly.DistanceToBoundary(boundary, point.xz));
            float margin = nearestRoad == float.MaxValue ? MinimumMargin : math.clamp(nearestRoad + 64f, MinimumMargin, MaximumMargin);
            origin = low - margin;
            float2 maximum = high + margin;
            // 4 m apart, wider on big areas: about a million heights at most (the planner's survey is coarser there anyway).
            float2 box = maximum - origin;
            HeightSpacing = math.max(FinestSpacing, math.sqrt(box.x * box.y / MostSamples));
            float WaterSpacing = HeightSpacing * 2f;
            columns = math.max(2, (int)math.ceil((maximum.x - origin.x) / HeightSpacing) + 1);
            rows = math.max(2, (int)math.ceil((maximum.y - origin.y) / HeightSpacing) + 1);
            heights = new float[checked(columns * rows)];
            blocked = new bool[heights.Length];
            underWater = new bool[heights.Length];

            var terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
            var heightData = terrain.GetHeightData();
            long fingerprint = 17;
            float2 watchLow = low - FingerprintMargin, watchHigh = high + FingerprintMargin;
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float height = TerrainUtils.SampleHeight(ref heightData, new float3(origin.x + column * HeightSpacing, 0f, origin.y + row * HeightSpacing));
                    if (!math.isfinite(height)) throw new InvalidOperationException("The terrain returned a height that is not a number.");
                    heights[row * columns + column] = height;
                    float2 node = origin + new float2(column, row) * HeightSpacing;
                    if ((row & 7) == 0 && (column & 7) == 0 && math.all(node >= watchLow) && math.all(node <= watchHigh))
                        fingerprint = fingerprint * 31 + (long)math.round(height * 10f);
                }

            // Water deeper than 0.1 m blocks the nodes around it.
            var water = world.GetOrCreateSystemManaged<WaterSystem>();
            var waterData = water.GetSurfacesData(out var dependencies);
            dependencies.Complete();
            for (float z = origin.y; z <= maximum.y; z += WaterSpacing)
                for (float x = origin.x; x <= maximum.x; x += WaterSpacing)
                {
                    float depth = WaterUtils.SampleDepth(ref waterData, new float3(x, 0f, z));
                    if (math.isfinite(depth) && depth <= 0.1f) continue;
                    MarkBox(new float2(x, z) - WaterSpacing * 0.6f, new float2(x, z) + WaterSpacing * 0.6f, _ => true, underWater);
                }

            BuildingCount = CaptureBuildings(entities, origin, maximum, watchLow, watchHigh, out long watchedBuildings);
            // Sums, so the order the game lists them in doesn't matter; a road moved, widened or replaced changes its term.
            long roads = 0;
            foreach (var road in ExistingRoads)
            {
                var points = road.Points;
                roads += math.hash(new int4((int)road.Id, (int)(road.Id >> 32), (int)math.round(road.HalfWidth * 10f), points.Length))
                    + (long)math.hash(new int4((int2)math.round(points[0].xz), (int2)math.round(points[points.Length - 1].xz)))
                    + math.hash((int2)math.round(points[points.Length / 2].xz));
            }
            fingerprint = fingerprint * 31 + watchedBuildings;
            fingerprint = fingerprint * 31 + roads;
            Fingerprint = fingerprint;
        }

        internal static SiteSnapshot Capture(EntityManager entities, World world, float2[] boundary)
        {
            try { return new SiteSnapshot(entities, world, boundary); }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: taking the site snapshot failed");
                throw;
            }
        }

        public float GroundHeight(float2 point) => SiteGeometry.Bilinear(heights, columns, rows, origin, HeightSpacing, point);

        /// <summary>True on building lots and water: any of the four grid nodes around the point is blocked.</summary>
        public bool Blocked(float2 point)
        {
            float2 coordinate = (point - origin) / HeightSpacing;
            int column = (int)math.floor(coordinate.x), row = (int)math.floor(coordinate.y);
            if (column < 0 || row < 0 || column >= columns - 1 || row >= rows - 1) return false;
            int index = row * columns + column;
            return blocked[index] || blocked[index + 1] || blocked[index + columns] || blocked[index + columns + 1];
        }

        /// <summary>True on water: any of the four grid nodes around the point is under water.</summary>
        public bool Water(float2 point)
        {
            float2 coordinate = (point - origin) / HeightSpacing;
            int column = (int)math.floor(coordinate.x), row = (int)math.floor(coordinate.y);
            if (column < 0 || row < 0 || column >= columns - 1 || row >= rows - 1) return false;
            int index = row * columns + column;
            return underWater[index] || underWater[index + 1] || underWater[index + columns] || underWater[index + columns + 1];
        }

        private void MarkBox(float2 low, float2 high, Func<float2, bool> inside, bool[] also = null)
        {
            int firstColumn = math.max(0, (int)math.floor((low.x - origin.x) / HeightSpacing));
            int lastColumn = math.min(columns - 1, (int)math.ceil((high.x - origin.x) / HeightSpacing));
            int firstRow = math.max(0, (int)math.floor((low.y - origin.y) / HeightSpacing));
            int lastRow = math.min(rows - 1, (int)math.ceil((high.y - origin.y) / HeightSpacing));
            for (int row = firstRow; row <= lastRow; row++)
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    float2 node = origin + new float2(column, row) * HeightSpacing;
                    if (!inside(node)) continue;
                    blocked[row * columns + column] = true;
                    if (also != null) also[row * columns + column] = true;
                }
        }

        /// <summary>Marks the buildings' lots blocked; <paramref name="watched"/> sums the lots near the area, by where they stand.</summary>
        private int CaptureBuildings(EntityManager entities, float2 minimum, float2 maximum, float2 watchLow, float2 watchHigh, out long watched)
        {
            int count = 0;
            watched = 0;
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
                    var lot = new[] { corners.a.xz, corners.b.xz, corners.c.xz, corners.d.xz };
                    float2 lower = math.min(math.min(lot[0], lot[1]), math.min(lot[2], lot[3]));
                    float2 upper = math.max(math.max(lot[0], lot[1]), math.max(lot[2], lot[3]));
                    if (math.any(upper < minimum) || math.any(lower > maximum)) continue;
                    count++;
                    // Rounded to the metre: a building moved, added or removed changes the sum, one levelling up in place doesn't.
                    if (math.all(upper >= watchLow) && math.all(lower <= watchHigh))
                        watched += math.hash(new int4((int2)math.round(lower), (int2)math.round(upper)));
                    // A node a few metres outside the lot still counts, so grading keeps a clear margin round the building.
                    MarkBox(lower - 4f, upper + 4f, node =>
                    {
                        if (SiteGeometry.Inside(node, lot)) return true;
                        for (int edge = 0; edge < 4; edge++)
                            if (SiteGeometry.DistanceSquared(node, lot[edge], lot[(edge + 1) % 4]) < 16f) return true;
                        return false;
                    });
                }
            return count;
        }

        private static ExistingRoad[] CaptureRoads(EntityManager entities, float2 minimum, float2 maximum)
        {
            var result = new List<ExistingRoad>();
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Composition>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var edges = query.ToEntityArray(Allocator.Temp))
                foreach (var edge in edges)
                {
                    Entity composition = entities.GetComponentData<Composition>(edge).m_Edge;
                    if (!entities.HasComponent<NetCompositionData>(composition)) continue;
                    var data = entities.GetComponentData<NetCompositionData>(composition);
                    if ((data.m_Flags.m_General & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) continue;
                    var curve = entities.GetComponentData<Curve>(edge);
                    var bounds = MathUtils.Bounds(curve.m_Bezier);
                    if (math.any(bounds.max.xz < minimum) || math.any(bounds.min.xz > maximum)) continue;
                    int segments = math.max(1, (int)math.ceil(curve.m_Length / 4f));
                    var points = new float3[segments + 1];
                    for (int index = 0; index <= segments; index++) points[index] = MathUtils.Position(curve.m_Bezier, index / (float)segments);
                    result.Add(new ExistingRoad { Id = Pack(edge), Points = points, HalfWidth = data.m_Width * 0.5f });
                }
            return result.ToArray();
        }

        public static long Pack(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;

        public static Entity Unpack(long id) => new Entity { Index = (int)(id & 0xFFFFFFFF), Version = (int)(id >> 32) };
    }
}
