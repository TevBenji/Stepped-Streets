// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Sites for the service buildings the player mixed in (police, clinic, school, park...): each on a level pad its own size
    /// beside a new road, facing it, on the flattest ground that fits, spread out over the area. The house lots there give way.
    /// </summary>
    internal static class ServiceSites
    {
        private const float Cell = StepPlanner.CellSize;
        private const float Sample = 4f;

        public static List<PlannedBuilding> Place(NeighbourhoodPlan plan, List<PlannedLot> lots, SiteSurvey survey, NeighbourhoodRequest request,
            List<string> messages, CancellationToken cancellation)
        {
            var placed = new List<PlannedBuilding>();
            var wanted = new List<ServiceRequest>();
            foreach (var service in request.Services ?? new ServiceRequest[0])
                if (service != null && service.Count > 0 && !string.IsNullOrEmpty(service.Name)) wanted.Add(service);
            if (wanted.Count == 0 || lots.Count == 0) return placed;
            // The biggest first: they have the fewest sites.
            wanted.Sort((first, second) => first.WidthCells * first.DepthCells != second.WidthCells * second.DepthCells
                ? (second.WidthCells * second.DepthCells).CompareTo(first.WidthCells * first.DepthCells) : string.CompareOrdinal(first.Name, second.Name));

            float halfWidth = request.RoadHalfWidth;
            var segments = new SpatialBuckets<(int Road, float2 Start, float2 End)>(32f);
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var points = plan.Roads[road].Points;
                for (int point = 1; point < points.Length; point++)
                {
                    float2 start = points[point - 1].xz, end = points[point].xz;
                    segments.Add(math.min(start, end) - halfWidth - 4f, math.max(start, end) + halfWidth + 4f, (road, start, end));
                }
            }
            var existing = LotLayout.ExistingRoads(request);
            var taken = new bool[lots.Count];
            var missing = new Dictionary<string, int>();
            foreach (var service in wanted)
                for (int copy = 0; copy < math.min(service.Count, ServiceRequest.MostEach); copy++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    float bestScore = float.MaxValue;
                    float2[] bestQuad = null;
                    int bestLot = -1;
                    for (int index = 0; index < lots.Count; index++)
                    {
                        if (taken[index]) continue;
                        var quad = Site(lots[index].Pad, service);
                        float score = Score(quad, lots[index], plan, placed, segments, existing, survey, request);
                        if (score < bestScore) { bestScore = score; bestQuad = quad; bestLot = index; }
                    }
                    if (bestQuad == null)
                    {
                        missing.TryGetValue(service.Name, out int count);
                        missing[service.Name] = count + 1;
                        continue;
                    }
                    var lot = lots[bestLot];
                    float height = lot.Pad.Height;
                    var pad = new Pad
                    {
                        Side = lot.Pad.Side,
                        Corners = new[]
                        {
                            new float3(bestQuad[0].x, height, bestQuad[0].y), new float3(bestQuad[1].x, height, bestQuad[1].y),
                            new float3(bestQuad[2].x, height, bestQuad[2].y), new float3(bestQuad[3].x, height, bestQuad[3].y),
                        },
                        Height = height,
                        Start = lot.Pad.Start,
                        End = lot.Pad.Start + service.WidthCells * Cell,
                        WidthCells = service.WidthCells,
                        DepthCells = service.DepthCells,
                    };
                    float2 centre = (bestQuad[0] + bestQuad[1] + bestQuad[2] + bestQuad[3]) * 0.25f;
                    placed.Add(new PlannedBuilding
                    {
                        Name = service.Name,
                        Pad = pad,
                        Road = lot.Road,
                        Position = new float3(centre.x, height, centre.y),
                        Forward = math.normalizesafe((bestQuad[0] + bestQuad[1]) * 0.5f - (bestQuad[2] + bestQuad[3]) * 0.5f),
                    });
                    // The house lots under the building give way.
                    for (int index = 0; index < lots.Count; index++)
                        if (!taken[index] && Overlap(bestQuad, Quad(lots[index].Pad), 0.5f)) taken[index] = true;
                }
            for (int index = lots.Count - 1; index >= 0; index--) if (taken[index]) lots.RemoveAt(index);
            foreach (var entry in missing)
                messages.Add(string.Format(CultureInfo.InvariantCulture, "No room for {0} × {1}: it needs a level site of its size beside a new road.",
                    entry.Value, entry.Key));
            return placed;
        }

        /// <summary>The building's site: its own width and depth, from the lot's front corner along the kerb and back from it.</summary>
        private static float2[] Site(Pad lot, ServiceRequest service)
        {
            float2 origin = lot.Corners[0].xz;
            float2 along = math.normalizesafe(lot.Corners[1].xz - origin), back = math.normalizesafe(lot.Corners[3].xz - origin);
            float width = service.WidthCells * Cell, depth = service.DepthCells * Cell;
            return new[] { origin, origin + along * width, origin + along * width + back * depth, origin + back * depth };
        }

        private static float2[] Quad(Pad pad) => new[] { pad.Corners[0].xz, pad.Corners[1].xz, pad.Corners[2].xz, pad.Corners[3].xz };

        /// <summary>Lower is better; MaxValue when the site breaks a rule.</summary>
        private static float Score(float2[] quad, PlannedLot lot, NeighbourhoodPlan plan, List<PlannedBuilding> placed,
            SpatialBuckets<(int Road, float2 Start, float2 End)> segments, SpatialBuckets<(float2 Start, float2 End, float Clearance)> existing,
            SiteSurvey survey, NeighbourhoodRequest request)
        {
            float halfWidth = request.RoadHalfWidth;
            foreach (var building in placed)
                if (Overlap(quad, Quad(building.Pad), 6f)) return float.MaxValue;
            // The house lot it replaces kept off the roads already there, but the building reaches further back. The whole
            // outline is tested, not the sample points below: a road could pass between those.
            foreach (var (start, end, clearance) in existing.Within((quad[0] + quad[2]) * 0.5f, math.distance(quad[0], quad[2]) * 0.5f))
                if (Overlap(quad, new[] { start, end, end, start }, clearance)) return float.MaxValue;
            float2 origin = quad[0], along = quad[1] - quad[0], back = quad[3] - quad[0];
            float width = math.length(along), depth = math.length(back);
            along /= width;
            back /= depth;
            // The front runs along its road: a curve that bends away would leave the building without its frontage.
            float frontGap = RoadDistance(origin, lot.Road, segments, true);
            for (float t = Sample; t <= width; t += Sample)
                if (math.abs(RoadDistance(origin + along * t, lot.Road, segments, true) - frontGap) > 1.5f) return float.MaxValue;
            float height = lot.Pad.Height, earthwork = 0f;
            for (float u = 1f; u <= width - 1f; u += Sample)
                for (float v = 1f; v <= depth - 1f; v += Sample)
                {
                    float2 point = origin + along * u + back * v;
                    if (!Poly.Contains(request.Boundary, point) || survey.SlopeAt(point) > request.SteepestBuildable) return float.MaxValue;
                    if (request.Blocked != null && request.Blocked(point)) return float.MaxValue;
                    // Clear of every other road, and of its own beyond the kerb.
                    if (RoadDistance(point, lot.Road, segments, false) < halfWidth + 3f) return float.MaxValue;
                    if (RoadDistance(point, lot.Road, segments, true) < halfWidth) return float.MaxValue;
                    float ground = request.GroundHeight(point);
                    if (!math.isfinite(ground)) return float.MaxValue;
                    earthwork = math.max(earthwork, math.abs(ground - height));
                }
            // A building pad may take a little more earth than a house lot; the flattest site wins.
            if (earthwork > request.EarthworkLimit * 1.5f) return float.MaxValue;
            // Spread out: a site far from the other buildings scores better, up to 600 m.
            float nearest = 600f;
            float2 centre = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
            foreach (var building in placed) nearest = math.min(nearest, math.distance(centre, building.Position.xz));
            return earthwork - nearest * 0.01f;
        }

        /// <summary>Distance to the road itself (<paramref name="own"/>) or to the nearest other road; MaxValue when none is near.</summary>
        private static float RoadDistance(float2 point, int road, SpatialBuckets<(int Road, float2 Start, float2 End)> segments, bool own)
        {
            float best = float.MaxValue;
            foreach (var segment in segments.Near(point))
                if ((segment.Road == road) == own) best = math.min(best, Geometry.DistanceToSegment(point, segment.Start, segment.End));
            return best;
        }

        /// <summary>
        /// Two convex quads overlap (separating axes), each grown by <paramref name="margin"/>. A segment passes as the quad
        /// (start, end, end, start) with a margin above zero: its zero-length edges give no axis, so they never separate.
        /// </summary>
        internal static bool Overlap(float2[] first, float2[] second, float margin)
        {
            foreach (var quad in new[] { first, second })
                for (int edge = 0; edge < 4; edge++)
                {
                    float2 axis = math.normalizesafe(Geometry.Left(quad[(edge + 1) % 4] - quad[edge]));
                    float firstLow = float.MaxValue, firstHigh = float.MinValue, secondLow = float.MaxValue, secondHigh = float.MinValue;
                    foreach (var corner in first) { float d = math.dot(corner, axis); firstLow = math.min(firstLow, d); firstHigh = math.max(firstHigh, d); }
                    foreach (var corner in second) { float d = math.dot(corner, axis); secondLow = math.min(secondLow, d); secondHigh = math.max(secondHigh, d); }
                    if (firstHigh + margin <= secondLow || secondHigh + margin <= firstLow) return false;
                }
            return true;
        }
    }
}
