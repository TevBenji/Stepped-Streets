// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Trees, low greenery and garden walls, placed last (video tips: landscape the banks and gaps, forest behind the homes,
    /// walls only sparingly). Forest on ground too steep to build, trees on cut banks and in the gaps between lots, open grass on
    /// the fill below houses so their view stays clear; bushes along lot edges and road verges, flowers near the lots, a few
    /// rocks on the slopes; nothing on lots, roads or their sidewalks.
    /// </summary>
    internal static class Landscaping
    {
        private const float TreeSpacing = 6f;
        // Planted 1500 a frame, so a big area's forest costs a few frames.
        private const int MostTrees = 20000;
        private const float OpenView = 12f;

        /// <summary>
        /// How far greenery keeps off a lot's edge. Widen it to a cell (8 m) if houses turn out not to grow on lots beside it.
        /// </summary>
        internal const float LotMargin = 1.5f;

        // Bushes, flowers and rocks come in groups, so their seeds lie wider apart than the trees.
        private const float ClusterSpacing = 12f;

        // A road's verge: the strip past its sidewalk where bushes line it.
        private const float VergeWidth = 6f;

        /// <param name="keepOff">True where nothing may be planted besides lots, roads and buildings: on existing streets, the
        /// cells the player already zoned or something stands on. Null for none.</param>
        public static List<PlannedTree> Trees(NeighbourhoodPlan plan, List<PlannedLot> lots, SiteSurvey survey, NeighbourhoodRequest request,
            System.Threading.CancellationToken cancellation = default, System.Func<float2, bool> keepOff = null)
        {
            var trees = new List<PlannedTree>();
            if (!request.Trees) return trees;
            float density = math.clamp(request.TreeDensity, 0.1f, 1f);
            // Reaching past the verge, so a bush there finds the road it lines.
            var roads = RoadBuckets(plan, VergeWidth);
            var paths = Footpaths.Buckets(plan);
            var pads = new SpatialBuckets<Pad>(32f);
            foreach (var lot in lots)
            {
                var corners = new float2[4];
                for (int corner = 0; corner < 4; corner++) corners[corner] = lot.Pad.Corners[corner].xz;
                Poly.Bounds(corners, out var low, out var high);
                pads.Add(low - OpenView, high + OpenView, lot.Pad);
            }
            var existing = new SpatialBuckets<(float2 Start, float2 End, float HalfWidth)>(32f);
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < (road?.Points?.Length ?? 0); point++)
                {
                    float2 start = road.Points[point - 1].xz, end = road.Points[point].xz;
                    float reach = road.HalfWidth + VergeWidth;
                    existing.Add(math.min(start, end) - reach, math.max(start, end) + reach, (start, end, road.HalfWidth));
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
                    if (keepOff != null && keepOff(point)) continue;
                    if (NearRoad(roads, plan, point, 3.5f) || Footpaths.Near(paths, point, Footpaths.HalfWidth + 2f)) continue;
                    bool nearExisting = false;
                    foreach (var (start, end, halfWidth) in existing.Near(point))
                        if (Geometry.DistanceToSegment(point, start, end) < halfWidth + 4f) { nearExisting = true; break; }
                    if (nearExisting) continue;

                    bool onLot = false, nearLot = false, viewOpen = false;
                    foreach (var pad in pads.Near(point))
                    {
                        if (LotLayout.InsidePad(pad, point, -LotMargin)) { onLot = true; break; }
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
            Clusters(trees, plan, pads, roads, existing, paths, survey, request, keepOff, density, cancellation);
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

        /// <summary>
        /// Bush clusters along lot edges and road verges, small groups of flowers near the lots, and a few rocks, mostly on the
        /// slopes, with an odd boulder on steep ground; a sparse bush or rock out in the open. Clear of the lots, buildings,
        /// water, <paramref name="keepOff"/> and every road and its sidewalk (a road's half width includes it).
        /// </summary>
        private static void Clusters(List<PlannedTree> plants, NeighbourhoodPlan plan, SpatialBuckets<Pad> pads, SpatialBuckets<int2> roads,
            SpatialBuckets<(float2 Start, float2 End, float HalfWidth)> existing, SpatialBuckets<PlannedPath> paths, SiteSurvey survey,
            NeighbourhoodRequest request, System.Func<float2, bool> keepOff, float density, System.Threading.CancellationToken cancellation)
        {
            // How far past the nearest road's edge a point is; far when no road is within its verge.
            float PastKerb(float2 point)
            {
                float best = float.MaxValue;
                foreach (var item in roads.Near(point))
                {
                    var points = plan.Roads[item.x].Points;
                    best = math.min(best, Geometry.DistanceToSegment(point, points[item.y - 1].xz, points[item.y].xz) - plan.Roads[item.x].HalfWidth);
                }
                foreach (var (start, end, halfWidth) in existing.Near(point))
                    best = math.min(best, Geometry.DistanceToSegment(point, start, end) - halfWidth);
                return best;
            }
            bool Clear(float2 point, float kerb)
            {
                if (!Poly.Contains(survey.Boundary, point)) return false;
                if (request.Blocked != null && request.Blocked(point)) return false;
                if (keepOff != null && keepOff(point)) return false;
                if (PastKerb(point) < kerb) return false;
                if (Footpaths.Near(paths, point, Footpaths.HalfWidth + 1.5f)) return false;
                foreach (var pad in pads.Near(point))
                    if (LotLayout.InsidePad(pad, point, -LotMargin)) return false;
                return true;
            }

            uint seed = request.Seed + 4099u;
            int columns = (int)math.ceil((survey.BoundaryMaximum.x - survey.BoundaryMinimum.x) / ClusterSpacing);
            int rows = (int)math.ceil((survey.BoundaryMaximum.y - survey.BoundaryMinimum.y) / ClusterSpacing);
            for (int row = 0; row < rows; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int column = 0; column < columns; column++)
                {
                    float2 jitter = new float2(Geometry.Hash01(seed, column, row * 2 + 1), Geometry.Hash01(seed, column, row * 2 + 2));
                    float2 centre = survey.BoundaryMinimum + (new float2(column, row) + 0.15f + jitter * 0.7f) * ClusterSpacing;
                    if (!Clear(centre, 1f)) continue;
                    bool lotEdge = false, nearLot = false;
                    foreach (var pad in pads.Near(centre))
                    {
                        if (LotLayout.InsidePad(pad, centre, -VergeWidth)) lotEdge = true;
                        if (LotLayout.InsidePad(pad, centre, -OpenView)) nearLot = true;
                    }
                    float slope = survey.SlopeAt(centre), steep = request.SteepestBuildable;
                    float pick = Geometry.Hash01(seed + 1u, column, row), size = Geometry.Hash01(seed + 2u, column, row);
                    TreeKind kind;
                    float chance, spread;
                    int count;
                    if (lotEdge || PastKerb(centre) < VergeWidth) { kind = TreeKind.Bush; chance = 0.4f; count = 3 + (int)(size * 3f); spread = 2.2f; }
                    else if (nearLot) { kind = TreeKind.Flower; chance = 0.25f; count = 2 + (int)(size * 3f); spread = 1.4f; }
                    else if (slope > steep * 0.5f)
                    {
                        // Sparse, and more of them the steeper it gets; an odd boulder where it is too steep to build.
                        bool boulder = slope > steep && pick < 0.3f;
                        kind = boulder ? TreeKind.Boulder : TreeKind.Rock;
                        chance = 0.08f + 0.12f * math.saturate(slope / steep - 0.5f);
                        count = boulder ? 1 : 1 + (int)(size * 3f);
                        spread = 2.5f;
                    }
                    else
                    {
                        kind = pick < 0.2f ? TreeKind.Rock : TreeKind.Bush;
                        chance = pick < 0.2f ? 0.03f : 0.12f;
                        count = 1 + (int)(size * 3f);
                        spread = 2.5f;
                    }
                    if (Geometry.Hash01(seed + 3u, column, row) >= chance * density) continue;
                    // A boulder is about 5 m across, so it keeps further from the road.
                    float kerb = kind == TreeKind.Boulder ? 4f : 1f;
                    for (int member = 0; member < count; member++)
                    {
                        float angle = Geometry.Hash01(seed + 4u, column * 8 + member, row) * 2f * math.PI;
                        float radius = member == 0 ? 0f : spread * math.sqrt(Geometry.Hash01(seed + 5u, column * 8 + member, row));
                        float2 point = centre + new float2(math.cos(angle), math.sin(angle)) * radius;
                        if (!Clear(point, kerb)) continue;
                        float height = plan.Ground != null ? plan.Ground.HeightAt(point) : request.GroundHeight(point);
                        plants.Add(new PlannedTree
                        {
                            Position = new float3(point.x, height, point.y),
                            Kind = kind,
                            Variation = Geometry.Hash(seed + 6u, column * 8 + member, row),
                        });
                    }
                }
            }
        }

        /// <summary>Garden walls along road shoulders where the ground beside the road is cut 0.8–3 m and no lot fronts it.</summary>
        public static List<WallSegment> Walls(NeighbourhoodPlan plan, List<PlannedLot> lots, NeighbourhoodRequest request)
        {
            var walls = new List<WallSegment>();
            if (!request.Walls || plan.Ground == null) return walls;
            var pads = new SpatialBuckets<Pad>(32f);
            foreach (var lot in lots)
            {
                var corners = new float2[4];
                for (int corner = 0; corner < 4; corner++) corners[corner] = lot.Pad.Corners[corner].xz;
                Poly.Bounds(corners, out var low, out var high);
                pads.Add(low - 6f, high + 6f, lot.Pad);
            }
            // In buckets: a 6.4 km area has thousands of junctions and every shoulder segment asks about them.
            var junctionNodes = new SpatialBuckets<(float2 Position, float Reach)>(32f);
            foreach (var node in plan.Nodes)
            {
                float junctionReach = node.HalfWidth + 12f;
                if (node.Degree >= 3 || node.ExistingRoadId != 0) junctionNodes.Add(node.Position.xz - junctionReach, node.Position.xz + junctionReach, (node.Position.xz, junctionReach));
            }
            // No wall across the end of a footpath.
            var paths = Footpaths.Buckets(plan);

            foreach (var road in plan.Roads)
            {
                var points = road.Points;
                float halfWidth = road.HalfWidth;
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
                        foreach (var (node, junctionReach) in junctionNodes.Near(middle)) if (math.distance(node, middle) < junctionReach) { clear = false; break; }
                        if (Footpaths.Near(paths, middle, Footpaths.HalfWidth + 3f)) clear = false;
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

        /// <summary>Every road's pieces, each box grown by its half width and <paramref name="margin"/>.</summary>
        private static SpatialBuckets<int2> RoadBuckets(NeighbourhoodPlan plan, float margin)
        {
            var buckets = new SpatialBuckets<int2>(24f);
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var points = plan.Roads[road].Points;
                float reach = plan.Roads[road].HalfWidth + margin;
                for (int point = 1; point < points.Length; point++)
                    buckets.Add(math.min(points[point - 1].xz, points[point].xz) - reach, math.max(points[point - 1].xz, points[point].xz) + reach, new int2(road, point));
            }
            return buckets;
        }

        /// <summary>Whether a point is within a road's half width and <paramref name="margin"/> of its centreline.</summary>
        private static bool NearRoad(SpatialBuckets<int2> buckets, NeighbourhoodPlan plan, float2 point, float margin)
        {
            foreach (var item in buckets.Near(point))
            {
                var points = plan.Roads[item.x].Points;
                if (Geometry.DistanceToSegment(point, points[item.y - 1].xz, points[item.y].xz) < plan.Roads[item.x].HalfWidth + margin) return true;
            }
            return false;
        }
    }
}
