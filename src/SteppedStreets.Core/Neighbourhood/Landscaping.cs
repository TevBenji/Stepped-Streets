// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Trees and garden walls, placed last (video tips: landscape the banks and gaps, forest behind the homes, walls only
    /// sparingly). Forest on ground too steep to build, trees on cut banks and in the gaps between lots, open grass on the
    /// fill below houses so their view stays clear, nothing on lots or roads.
    /// </summary>
    internal static class Landscaping
    {
        private const float TreeSpacing = 6f;
        // Planted 1500 a frame, so a big area's forest costs a few frames.
        private const int MostTrees = 20000;
        private const float OpenView = 12f;

        public static List<PlannedTree> Trees(NeighbourhoodPlan plan, List<PlannedLot> lots, SiteSurvey survey, NeighbourhoodRequest request,
            System.Threading.CancellationToken cancellation = default)
        {
            var trees = new List<PlannedTree>();
            if (!request.Trees) return trees;
            float density = math.clamp(request.TreeDensity, 0.1f, 1f);
            var roads = RoadBuckets(plan, request.RoadHalfWidth + 4f);
            var pads = new SpatialBuckets<Pad>(32f);
            foreach (var lot in lots)
            {
                var corners = new float2[4];
                for (int corner = 0; corner < 4; corner++) corners[corner] = lot.Pad.Corners[corner].xz;
                Poly.Bounds(corners, out var low, out var high);
                pads.Add(low - OpenView, high + OpenView, lot.Pad);
            }
            var existing = new SpatialBuckets<(float2, float2, float)>(32f);
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < (road?.Points?.Length ?? 0); point++)
                {
                    float2 start = road.Points[point - 1].xz, end = road.Points[point].xz;
                    float clearance = road.HalfWidth + 4f;
                    existing.Add(math.min(start, end) - clearance, math.max(start, end) + clearance, (start, end, clearance));
                }

            int columns = (int)math.ceil((survey.BoundaryMaximum.x - survey.BoundaryMinimum.x) / TreeSpacing);
            int rows = (int)math.ceil((survey.BoundaryMaximum.y - survey.BoundaryMinimum.y) / TreeSpacing);
            for (int row = 0; row < rows; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int column = 0; column < columns; column++)
                {
                    float2 jitter = new float2(Geometry.Hash01(request.Seed, column, row * 2 + 1), Geometry.Hash01(request.Seed, column, row * 2 + 2));
                    float2 point = survey.BoundaryMinimum + (new float2(column, row) + 0.15f + jitter * 0.7f) * TreeSpacing;
                    if (!Poly.Contains(survey.Boundary, point)) continue;
                    if (request.Blocked != null && request.Blocked(point)) continue;
                    if (NearRoad(roads, plan, point, request.RoadHalfWidth + 3.5f)) continue;
                    bool nearExisting = false;
                    foreach (var (start, end, clearance) in existing.Near(point))
                        if (Geometry.DistanceToSegment(point, start, end) < clearance) { nearExisting = true; break; }
                    if (nearExisting) continue;

                    bool onLot = false, nearLot = false, viewOpen = false;
                    foreach (var pad in pads.Near(point))
                    {
                        if (LotLayout.InsidePad(pad, point, -1.5f)) { onLot = true; break; }
                        if (LotLayout.InsidePad(pad, point, -OpenView)) nearLot = true;
                    }
                    if (onLot) continue;
                    float change = plan.Ground != null ? plan.Ground.ChangeAt(point) : 0f;
                    // Fill below a house: grass, so the view down the hill stays open.
                    if (change > 0.3f && nearLot) viewOpen = true;
                    if (viewOpen) continue;

                    float chance;
                    TreeKind kind;
                    if (survey.SlopeAt(point) > request.SteepestBuildable) { chance = 0.9f; kind = TreeKind.Conifer; }
                    else if (change < -0.3f) { chance = 0.7f; kind = TreeKind.Conifer; }
                    else if (change > 0.3f) { chance = 0.45f; kind = TreeKind.Broadleaf; }
                    else if (nearLot) { chance = 0.35f; kind = TreeKind.Broadleaf; }
                    else { chance = 0.5f; kind = Geometry.Hash01(request.Seed, column, row + 7919) < 0.5f ? TreeKind.Conifer : TreeKind.Broadleaf; }
                    if (Geometry.Hash01(request.Seed + 17u, column, row) >= chance * density) continue;
                    float height = plan.Ground != null ? plan.Ground.HeightAt(point) : request.GroundHeight(point);
                    trees.Add(new PlannedTree
                    {
                        Position = new float3(point.x, height, point.y),
                        Kind = kind,
                        Variation = Geometry.Hash(request.Seed, column, row),
                    });
                }
            }
            if (trees.Count > MostTrees)
            {
                // Thin evenly rather than cut off one side of the area.
                var thinned = new List<PlannedTree>(MostTrees);
                float keep = MostTrees / (float)trees.Count;
                foreach (var tree in trees)
                    if ((tree.Variation & 0xFFFF) / 65536f < keep) thinned.Add(tree);
                trees = thinned;
            }
            return trees;
        }

        /// <summary>Garden walls along road shoulders where the ground beside the road is cut 0.8–3 m and no lot fronts it.</summary>
        public static List<WallSegment> Walls(NeighbourhoodPlan plan, List<PlannedLot> lots, NeighbourhoodRequest request)
        {
            var walls = new List<WallSegment>();
            if (!request.Walls || plan.Ground == null) return walls;
            float halfWidth = request.RoadHalfWidth;
            var pads = new SpatialBuckets<Pad>(32f);
            foreach (var lot in lots)
            {
                var corners = new float2[4];
                for (int corner = 0; corner < 4; corner++) corners[corner] = lot.Pad.Corners[corner].xz;
                Poly.Bounds(corners, out var low, out var high);
                pads.Add(low - 6f, high + 6f, lot.Pad);
            }
            // In buckets: a 6.4 km area has thousands of junctions and every shoulder segment asks about them.
            float junctionReach = halfWidth + 12f;
            var junctionNodes = new SpatialBuckets<float2>(32f);
            foreach (var node in plan.Nodes)
                if (node.Degree >= 3 || node.ExistingRoadId != 0) junctionNodes.Add(node.Position.xz - junctionReach, node.Position.xz + junctionReach, node.Position.xz);

            foreach (var road in plan.Roads)
            {
                var points = road.Points;
                foreach (float side in new[] { 1f, -1f })
                {
                    var run = new List<(float2 Start, float2 End, float Height)>();
                    void Flush()
                    {
                        float length = 0f;
                        foreach (var piece in run) length += math.distance(piece.Start, piece.End);
                        if (length >= 12f)
                            foreach (var piece in run) walls.Add(Oriented(piece.Start, piece.End, piece.Height, side, points));
                        run.Clear();
                    }
                    for (int index = 1; index < points.Length; index++)
                    {
                        float2 direction = math.normalizesafe(points[index].xz - points[index - 1].xz);
                        float2 outward = Geometry.Left(direction) * side;
                        float2 start = points[index - 1].xz + outward * (halfWidth + 1f), end = points[index].xz + outward * (halfWidth + 1f);
                        float2 middle = (start + end) * 0.5f;
                        float roadHeight = (points[index].y + points[index - 1].y) * 0.5f;
                        float cut = plan.Ground.HeightAt(middle + outward * 3.5f) - roadHeight;
                        bool clear = true;
                        foreach (var pad in pads.Near(middle)) if (LotLayout.InsidePad(pad, middle, -4f)) { clear = false; break; }
                        foreach (var node in junctionNodes.Near(middle)) if (math.distance(node, middle) < junctionReach) { clear = false; break; }
                        if (clear && cut >= 0.8f && cut <= 3f) run.Add((start, end, roadHeight));
                        else Flush();
                    }
                    Flush();
                }
            }
            return walls;
        }

        // The wall's base with the bank on its right in the plan, the convention WallBuildSystem builds by.
        private static WallSegment Oriented(float2 start, float2 end, float height, float side, float3[] points)
        {
            float2 direction = end - start;
            float2 right = new float2(direction.y, -direction.x);
            float2 outward = Geometry.Left(math.normalizesafe(direction)) * side;
            if (math.dot(right, outward) < 0f) (start, end) = (end, start);
            return new WallSegment { Start = new float3(start.x, height, start.y), End = new float3(end.x, height, end.y) };
        }

        private static SpatialBuckets<int2> RoadBuckets(NeighbourhoodPlan plan, float reach)
        {
            var buckets = new SpatialBuckets<int2>(24f);
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var points = plan.Roads[road].Points;
                for (int point = 1; point < points.Length; point++)
                    buckets.Add(math.min(points[point - 1].xz, points[point].xz) - reach, math.max(points[point - 1].xz, points[point].xz) + reach, new int2(road, point));
            }
            return buckets;
        }

        private static bool NearRoad(SpatialBuckets<int2> buckets, NeighbourhoodPlan plan, float2 point, float distance)
        {
            foreach (var item in buckets.Near(point))
            {
                var points = plan.Roads[item.x].Points;
                if (Geometry.DistanceToSegment(point, points[item.y - 1].xz, points[item.y].xz) < distance) return true;
            }
            return false;
        }
    }
}
