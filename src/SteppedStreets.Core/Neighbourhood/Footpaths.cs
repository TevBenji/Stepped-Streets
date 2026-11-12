// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Footpaths through long blocks: where the cross streets on one side of a street are more than <see cref="LongestBlock"/>
    /// apart, a path runs straight across the block from the middle of that side to the street behind, so people on foot
    /// needn't walk round. A street's block face runs on through junctions whose side roads leave on its other side. Planned once the streets are settled and before the lots, which keep off the path, as service
    /// buildings, trees and walls do; the ground under a path is graded with the roads.
    /// </summary>
    internal static class Footpaths
    {
        /// <summary>A block longer than this between cross streets gets a path through its middle.</summary>
        internal const float LongestBlock = 180f;

        /// <summary>Half the width of the game's Pavement Path (3 m).</summary>
        internal const float HalfWidth = 1.5f;

        /// <summary>How far lots, service buildings and walls keep from a path's centreline: a corridor 5 m wide.</summary>
        internal const float Clearance = 2.5f;

        /// <summary>
        /// A path end stops this far short of the kerb, facing it: the game then joins the path to the sidewalk without a
        /// crossing (Footbridge Builder's ramp feet, tested in game).
        /// </summary>
        internal const float KerbGap = 0.25f;

        /// <summary>The steepest a path climbs: the game refuses a Pavement Path steeper than 20 %, measured end to end.</summary>
        internal const float SteepestGrade = 0.18f;

        // The far end must face its kerb within 37° of square; the game joins a path to a sidewalk only within 45°.
        private const float MostSideways = 0.6f;
        // A path end keeps this far from the junctions and ends of the streets it joins, past their half width.
        private const float NodeClearance = 16f;
        // Paths closer than this, middle to middle, serve the same block.
        private const float Spacing = LongestBlock / 3f;
        // Where a path can't go at a block's middle, it tries this much either way, out to a quarter of the block.
        private const float Stride = 8f;
        // How square to a street a road leaving it on the path's side must be to end the block: a cross street, not the street going on.
        private const float CrossStreet = 0.35f;
        // How straight on a road must carry a street through a junction for the block to go on along it.
        private const float StraightOn = 0.7f;

        public static List<PlannedPath> Plan(NeighbourhoodPlan plan, SiteSurvey survey, NeighbourhoodRequest request, CancellationToken cancellation)
        {
            var paths = new List<PlannedPath>();
            if (!request.Footpaths) return paths;
            var segments = new SpatialBuckets<int2>(32f);
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var points = plan.Roads[road].Points;
                float reach = plan.Roads[road].HalfWidth + 1f;
                for (int point = 1; point < points.Length; point++)
                    segments.Add(math.min(points[point - 1].xz, points[point].xz) - reach, math.max(points[point - 1].xz, points[point].xz) + reach, new int2(road, point));
            }
            var ends = new List<(int Road, bool AtStart)>[plan.Nodes.Count];
            for (int road = 0; road < plan.Roads.Count; road++)
                foreach (var (node, atStart) in new[] { (plan.Roads[road].StartNode, true), (plan.Roads[road].EndNode, false) })
                    (ends[node] ?? (ends[node] = new List<(int, bool)>())).Add((road, atStart));
            var existing = LotLayout.ExistingRoads(request);
            // The street behind is about a block depth away; a little further where the streets wander. A path shorter than a
            // lot is deep crosses no block.
            float farthest = request.BlockDepth * 1.6f + 20f, shortest = request.LotDepthCells * Planning.StepPlanner.CellSize;
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                cancellation.ThrowIfCancellationRequested();
                var planned = plan.Roads[road];
                // A connection runs out to the city's road past no lots, so no block lies behind it.
                if (planned.Kind == RoadKind.Connection) continue;
                foreach (float side in new[] { 1f, -1f })
                {
                    // The block face on this side: the street before and after this road, up to a cross street on this side.
                    float before = Walk(plan, ends, road, false, -side), after = Walk(plan, ends, road, true, side);
                    float face = before + planned.Length + after;
                    int count = (int)(face / LongestBlock);
                    // One path in the middle of a long block, more evenly along a very long one, each tried by the road it falls on.
                    for (int index = 1; index <= count; index++)
                    {
                        float middle = face * index / (count + 1) - before;
                        if (middle < 0f || middle >= planned.Length) continue;
                        for (float offset = 0f; offset <= face * 0.25f; offset += Stride)
                        {
                            PlannedPath path = null;
                            foreach (float station in offset == 0f ? new[] { middle } : new[] { middle - offset, middle + offset })
                            {
                                if (station < 0f || station > planned.Length) continue;
                                var (point, tangent, height) = At(planned.Points, station);
                                path = Across(plan, segments, existing, survey, request, road, point, Geometry.Left(tangent) * side, height, farthest, shortest);
                                if (path != null) break;
                            }
                            if (path == null) continue;
                            if (!Crowded(paths, path)) paths.Add(path);
                            break;
                        }
                    }
                }
            }
            return paths;
        }

        /// <summary>
        /// How far a street runs on from one end of a road with no road leaving it on one side (<paramref name="side"/>, left
        /// of the way walked as +1): through junctions whose other roads leave on the far side, to a cross street on this side,
        /// a turn, a fork, a dead end or the city's road.
        /// </summary>
        private static float Walk(NeighbourhoodPlan plan, List<(int Road, bool AtStart)>[] ends, int road, bool forward, float side)
        {
            float total = 0f;
            int current = road;
            bool ahead = forward;
            var walked = new HashSet<int> { road };
            while (total < LongestBlock * 8f)
            {
                var here = plan.Roads[current];
                int node = ahead ? here.EndNode : here.StartNode;
                if (plan.Nodes[node].ExistingRoadId != 0 || ends[node] == null) break;
                float2 heading = -Leaving(here, !ahead);
                int next = -1;
                bool nextAtStart = false, crossed = false;
                float straightest = StraightOn;
                foreach (var (other, atStart) in ends[node])
                {
                    if (other == current && atStart == !ahead) continue;
                    float2 leaving = Leaving(plan.Roads[other], atStart);
                    if (math.dot(leaving, Geometry.Left(heading) * side) > CrossStreet) { crossed = true; break; }
                    float straight = math.dot(leaving, heading);
                    if (straight > straightest) { straightest = straight; next = other; nextAtStart = atStart; }
                }
                if (crossed || next < 0 || !walked.Add(next)) break;
                total += plan.Roads[next].Length;
                current = next;
                ahead = nextAtStart;
            }
            return total;
        }

        /// <summary>The way a road leaves one of its end nodes, over its first 20 m.</summary>
        private static float2 Leaving(PlannedRoad road, bool atStart)
        {
            var line = new List<float2>(road.Points.Length);
            foreach (var point in road.Points) line.Add(point.xz);
            return RoadNetwork.EndDirection(line, atStart, RoadNetwork.HeadingReach);
        }

        /// <summary>
        /// The path from a point of a street straight out across the block to the next street's kerb, or null where it would
        /// leave the area, cross buildings, water or a city road, climb too steeply, need too much earth, meet the far street
        /// too far from square, come near either street's junctions, or come back to its own street.
        /// </summary>
        private static PlannedPath Across(NeighbourhoodPlan plan, SpatialBuckets<int2> segments,
            SpatialBuckets<(float2 Start, float2 End, float Clearance)> existing, SiteSurvey survey, NeighbourhoodRequest request,
            int road, float2 point, float2 outward, float height, float farthest, float shortest)
        {
            float2 start = point + outward * (plan.Roads[road].HalfWidth + KerbGap);
            if (NearNodes(plan, plan.Roads[road], start)) return null;
            int hit = -1;
            float travelled = 0f;
            while (hit < 0)
            {
                travelled += 1f;
                if (travelled > farthest) return null;
                float2 at = start + outward * travelled;
                if (!Poly.Contains(survey.Boundary, at) || (request.Blocked != null && request.Blocked(at))) return null;
                foreach (var (from, to, clearance) in existing.Near(at))
                    if (Geometry.DistanceToSegment(at, from, to) < clearance) return null;
                hit = Inside(plan, segments, at, KerbGap, out _);
            }
            if (hit == road) return null;
            // Back to just short of the kerb: halve the last metre until the end is within a couple of centimetres of it.
            float clear = travelled - 1f, inside = travelled;
            for (int halving = 0; halving < 6; halving++)
            {
                float middle = (clear + inside) * 0.5f;
                if (Inside(plan, segments, start + outward * middle, KerbGap, out _) >= 0) inside = middle; else clear = middle;
            }
            float length = clear;
            float2 end = start + outward * length;
            Inside(plan, segments, start + outward * inside, KerbGap, out int segment);
            var far = plan.Roads[hit];
            float3 a = far.Points[segment - 1], b = far.Points[segment];
            if (math.abs(math.dot(math.normalizesafe(b.xz - a.xz), outward)) > MostSideways || NearNodes(plan, far, end)) return null;
            float2 ab = b.xz - a.xz;
            float share = math.lengthsq(ab) > 1e-6f ? math.saturate(math.dot(end - a.xz, ab) / math.lengthsq(ab)) : 0f;
            float endHeight = math.lerp(a.y, b.y, share);
            if (length < shortest || math.abs(endHeight - height) > SteepestGrade * length) return null;
            // The ground under it is graded to an even climb, within the earthwork limit like a road's.
            for (float along = 0f; along <= length; along += 4f)
            {
                float2 at = start + outward * along;
                if (math.abs(math.lerp(height, endHeight, along / length) - request.GroundHeight(at)) > request.EarthworkLimit) return null;
            }
            return new PlannedPath
            {
                Start = new float3(start.x, height, start.y),
                End = new float3(end.x, endHeight, end.y),
                StartRoad = road,
                EndRoad = hit,
            };
        }

        private static bool NearNodes(NeighbourhoodPlan plan, PlannedRoad road, float2 point)
        {
            foreach (int node in new[] { road.StartNode, road.EndNode })
                if (math.distance(plan.Nodes[node].Position.xz, point) < plan.Nodes[node].HalfWidth + NodeClearance) return true;
            return false;
        }

        /// <summary>The road whose kerb, and <paramref name="margin"/> past it, the point lies within, the deepest one; −1 for none.</summary>
        private static int Inside(NeighbourhoodPlan plan, SpatialBuckets<int2> segments, float2 point, float margin, out int segment)
        {
            int best = -1;
            float bestInside = 0f;
            segment = -1;
            foreach (var item in segments.Near(point))
            {
                var points = plan.Roads[item.x].Points;
                float inside = plan.Roads[item.x].HalfWidth + margin - Geometry.DistanceToSegment(point, points[item.y - 1].xz, points[item.y].xz);
                if (inside > bestInside) { bestInside = inside; best = item.x; segment = item.y; }
            }
            return best;
        }

        /// <summary>A point a distance along a road, with its direction and height.</summary>
        private static (float2 Point, float2 Tangent, float Height) At(float3[] points, float distance)
        {
            for (int index = 1; index < points.Length; index++)
            {
                float length = math.distance(points[index - 1].xz, points[index].xz);
                if (distance > length && index < points.Length - 1) { distance -= length; continue; }
                float share = length > 1e-6f ? math.saturate(distance / length) : 0f;
                var point = math.lerp(points[index - 1], points[index], share);
                return (point.xz, math.normalizesafe(points[index].xz - points[index - 1].xz, new float2(1f, 0f)), point.y);
            }
            return (points[0].xz, new float2(1f, 0f), points[0].y);
        }

        /// <summary>Another path already serves this block (their middles are near), or the two would cross.</summary>
        private static bool Crowded(List<PlannedPath> paths, PlannedPath candidate)
        {
            float2 middle = (candidate.Start.xz + candidate.End.xz) * 0.5f;
            foreach (var path in paths)
            {
                if (math.distance(middle, (path.Start.xz + path.End.xz) * 0.5f) < Spacing) return true;
                if (Cross(candidate.Start.xz, candidate.End.xz, path.Start.xz, path.End.xz)) return true;
            }
            return false;
        }

        private static bool Cross(float2 a, float2 b, float2 c, float2 d)
        {
            float Side(float2 from, float2 to, float2 point) => (to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x);
            return Side(a, b, c) * Side(a, b, d) < 0f && Side(c, d, a) * Side(c, d, b) < 0f;
        }

        /// <summary>The planned paths in buckets, for <see cref="Near"/>.</summary>
        internal static SpatialBuckets<PlannedPath> Buckets(NeighbourhoodPlan plan)
        {
            var buckets = new SpatialBuckets<PlannedPath>(32f);
            foreach (var path in plan.Paths)
                buckets.Add(math.min(path.Start.xz, path.End.xz) - MostReach, math.max(path.Start.xz, path.End.xz) + MostReach, path);
            return buckets;
        }

        /// <summary>Whether a point is closer than <paramref name="distance"/> (at most <see cref="MostReach"/>) to a path's centreline.</summary>
        internal static bool Near(SpatialBuckets<PlannedPath> buckets, float2 point, float distance)
        {
            foreach (var path in buckets.Near(point))
                if (Geometry.DistanceToSegment(point, path.Start.xz, path.End.xz) < distance) return true;
            return false;
        }

        /// <summary>The farthest <see cref="Near"/> looks.</summary>
        internal const float MostReach = 6f;
    }
}
