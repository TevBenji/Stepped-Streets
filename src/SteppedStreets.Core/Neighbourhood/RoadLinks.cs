// Stepped Streets, the hillside neighbourhood generator. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// The roads that climb: links between neighbouring contour streets, the connection to an existing road and joins at
    /// street ends. Each is a smooth curve that leaves and meets its streets close to a right angle and bends across the slope
    /// (an S-curve) until it is long enough to climb at no more than the maximum road grade. Candidates that crowd another
    /// road, cross wild ground, need too much earthwork or bend too tightly are dropped; the shortest good one wins.
    /// </summary>
    internal sealed class RoadLinks
    {
        public const float JunctionSpacing = 40f;
        private const float EndClearance = 12f;
        // Link shapes: how far each end leans from square towards the way the link travels, and the curve's tension.
        private static readonly (float Tilt, float Scale)[] CoarseShapes =
            { (0f, 1f), (math.radians(13f), 0.8f), (math.radians(26f), 0.6f) };
        private static readonly (float Tilt, float Scale)[] FineShapes =
        {
            (0f, 0.7f), (0f, 1f), (0f, 1.3f), (math.radians(13f), 0.6f), (math.radians(13f), 0.8f), (math.radians(13f), 1.1f),
            (math.radians(26f), 0.45f), (math.radians(26f), 0.6f), (math.radians(26f), 0.8f),
        };

        private readonly SiteSurvey survey;
        private readonly NeighbourhoodRequest request;
        private readonly List<Street> streets;
        private readonly float minimumRadius;
        private readonly float halfWidth;
        private readonly float[] roadDistance;
        private CancellationToken cancellation;

        public RoadLinks(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, float minimumRadius)
        {
            this.survey = survey;
            this.request = request;
            this.streets = streets;
            this.minimumRadius = minimumRadius;
            halfWidth = request.RoadHalfWidth;
            roadDistance = new float[survey.Count];
            for (int index = 0; index < roadDistance.Length; index++) roadDistance[index] = float.MaxValue;
            foreach (var street in streets) Stamp(street);
        }

        // Two corridors (road plus shoulders) side by side, and a metre between them.
        private float Clearance => (halfWidth + 1.5f) * 2f + 1f;
        // A link leaving at 62° is this far along before it clears the street it left by the road clearance.
        private float Exempt => (Clearance + 2f) / 0.88f;

        private void Stamp(Street street) => ContourStreets.Stamp(survey, street.Points, Clearance + 8f, roadDistance);

        internal sealed class Candidate
        {
            public List<float2> Points;
            public float[] Heights;
            public float Score;
            public int Target;
            public int TargetPoint;
            public bool Traced;
        }

        /// <summary>Links each contour street to the next one up, about every link spacing along what they share.</summary>
        public void LinkNeighbours(CancellationToken cancellation)
        {
            this.cancellation = cancellation;
            var buckets = PointBuckets();
            int contourCount = streets.Count;
            for (int lower = 0; lower < contourCount; lower++)
            {
                cancellation.ThrowIfCancellationRequested();
                var street = streets[lower];
                if (street.Kind != RoadKind.Contour) continue;
                // Which street lies uphill of each station, by a ray up the slope.
                var runs = new List<(int Upper, int First, int Last)>();
                for (int point = 0; point < street.Points.Count; point += 4)
                {
                    int upper = UphillNeighbour(street, lower, point, buckets);
                    if (runs.Count > 0 && runs[runs.Count - 1].Upper == upper && point - runs[runs.Count - 1].Last <= 4)
                        runs[runs.Count - 1] = (upper, runs[runs.Count - 1].First, point);
                    else runs.Add((upper, point, point));
                }
                foreach (var run in runs)
                {
                    if (run.Upper < 0) continue;
                    var stations = street.Stations;
                    float runLength = stations[run.Last] - stations[run.First];
                    int count = math.max(1, (int)math.round(runLength / request.LinkSpacing));
                    for (int link = 0; link < count; link++)
                    {
                        float jitter = (Geometry.Hash01(request.Seed, lower * 131 + run.First, link) - 0.5f) * 0.4f;
                        float target = stations[run.First] + (link + 0.5f + jitter) * runLength / count;
                        TryLinkNear(lower, run.Upper, stations, target, stations[run.First], stations[run.Last]);
                    }
                }
            }
        }

        private static readonly float[] Shifts = { 0f, 16f, -16f, 32f, -32f, 48f, -48f, 72f, -72f, 96f, -96f };

        private void TryLinkNear(int lower, int upper, float[] stations, float target, float runStart, float runEnd)
        {
            // A plan the player has already replaced stops here rather than finishing its link search.
            cancellation.ThrowIfCancellationRequested();
            var street = streets[lower];
            // Built upwards first; then downwards from the upper street, which suits a ramp onto a small, tightly curved ring.
            foreach (bool downwards in new[] { false, true })
                foreach (float shift in Shifts)
                {
                    float wanted = math.clamp(target + shift, runStart, runEnd);
                    int point = Array.BinarySearch(stations, wanted);
                    if (point < 0) point = math.min(~point, stations.Length - 1);
                    int from = lower, fromPoint = point, to = upper;
                    if (downwards)
                    {
                        from = upper;
                        to = lower;
                        fromPoint = NearestPoint(streets[upper], street.Points[point]);
                    }
                    if (!JunctionAllowed(streets[from], fromPoint)) continue;
                    var candidate = Search(from, fromPoint, to);
                    if (candidate == null) continue;
                    Accept(RoadKind.Link, candidate, new Attachment { Street = from, Point = fromPoint });
                    return;
                }
        }

        /// <summary>The best link from a point of one street to any point of another.</summary>
        internal Candidate Search(int from, int fromPoint, int to)
        {
            var a = streets[from];
            var b = streets[to];
            float2 p = a.Points[fromPoint];
            int nearest = NearestPoint(b, p);
            float2 normal = Geometry.Left(a.Tangent(fromPoint));
            if (math.dot(normal, b.Points[nearest] - p) < 0f) normal = -normal;
            float rise = math.abs(b.Heights[nearest] - a.Heights[fromPoint]);
            float direct = math.distance(p, b.Points[nearest]);
            float needed = rise / (request.MaxRoadGrade * 0.9f) + 2f * RoadProfile.LandingLength;
            float lateral = math.sqrt(math.max(0f, needed * needed - direct * direct)) * 1.4f + 60f;
            int window = (int)(lateral / ContourStreets.Spacing);
            float shortest = ShortestFor(rise);
            Candidate best = null;
            int bestOffset = 0;
            Candidate Try(int offset, (float Tilt, float Scale) shape)
            {
                int point = nearest + offset;
                if (b.Closed) point = (point % b.Points.Count + b.Points.Count) % b.Points.Count;
                else if (point < 0 || point >= b.Points.Count) return null;
                if (!JunctionAllowed(b, point)) return null;
                float2 q = b.Points[point];
                // A curve this shape is at most about 1.7 times its chord: too short a chord can never climb the rise.
                if (math.distance(p, q) * 1.7f < shortest) return null;
                float2 arrive = Geometry.Left(b.Tangent(point));
                if (math.dot(arrive, q - p) < 0f) arrive = -arrive;
                var path = Curve(p, normal, a.Tangent(fromPoint), q, arrive, b.Tangent(point), shape.Tilt, shape.Scale);
                var candidate = Evaluate(path, a.Heights[fromPoint], b.Heights[point], Exempt, Exempt, false, a.Tangent(fromPoint), b.Tangent(point));
                if (candidate == null) return null;
                candidate.Target = to;
                candidate.TargetPoint = point;
                return candidate;
            }
            // Coarse over the window, then every shape around the best point.
            for (int offset = -window; offset <= window; offset += 5)
                foreach (var shape in CoarseShapes)
                {
                    var candidate = Try(offset, shape);
                    if (candidate != null && (best == null || candidate.Score < best.Score)) { best = candidate; bestOffset = offset; }
                }
            // Traced ramps: the shape a builder uses on steep ground, at a constant angle to curved contours.
            foreach (float side in new[] { 1f, -1f })
                foreach (float share in new[] { 0.7f, 0.5f, 0.35f, 0.24f })
                {
                    var traced = Trace(from, fromPoint, to, side, share);
                    if (traced != null && (best == null || traced.Score < best.Score)) best = traced;
                }
            if (best == null) return null;
            if (best.Traced) return best;
            int centre = bestOffset;
            for (int offset = centre - 2; offset <= centre + 2; offset++)
                foreach (var shape in FineShapes)
                {
                    var candidate = Try(offset, shape);
                    if (candidate != null && candidate.Score < best.Score) best = candidate;
                }
            return best;
        }

        /// <summary>
        /// Joins the network to the nearest existing road it can reach (the area need not touch one). Only streets the
        /// predicate allows are targets, so a second call can reach a part the first connection missed.
        /// </summary>
        public bool Connect(Func<int, bool> targetAllowed, CancellationToken cancellation)
        {
            var starts = new List<(int Road, int Point, float Distance)>();
            var roads = request.ExistingRoads ?? new ExistingRoad[0];
            for (int road = 0; road < roads.Length; road++)
            {
                var points = roads[road]?.Points;
                if (points == null || points.Length < 2) continue;
                // Stations about every 12 m along the edge, whatever its sampling.
                float travelled = 12f;
                for (int point = 0; point < points.Length; point++)
                {
                    if (point > 0) travelled += math.distance(points[point].xz, points[point - 1].xz);
                    if (travelled < 12f && point != points.Length - 1) continue;
                    travelled = 0f;
                    float2 position = points[point].xz;
                    if (!survey.InsideGrid(position, 24f)) continue;
                    float distance = Poly.Contains(survey.Boundary, position) ? 0f : Poly.DistanceToBoundary(survey.Boundary, position);
                    if (distance <= 400f) starts.Add((road, point, distance));
                }
            }
            starts.Sort((x, y) => x.Distance != y.Distance ? x.Distance.CompareTo(y.Distance)
                : x.Road != y.Road ? x.Road.CompareTo(y.Road) : x.Point.CompareTo(y.Point));
            if (starts.Count > 48) starts.RemoveRange(48, starts.Count - 48);

            Candidate best = null;
            (int Road, int Point, float Distance) bestStart = default;
            foreach (var start in starts)
            {
                cancellation.ThrowIfCancellationRequested();
                var road = roads[start.Road];
                float3 origin = road.Points[start.Point];
                int before = math.max(0, start.Point - 1), after = math.min(road.Points.Length - 1, start.Point + 1);
                float2 along = math.normalizesafe(road.Points[after].xz - road.Points[before].xz, new float2(1f, 0f));
                var targets = new List<(int Street, int Point, float Distance, bool End)>();
                for (int street = 0; street < streets.Count; street++)
                {
                    var target = streets[street];
                    if (target.Removed || !targetAllowed(street)) continue;
                    for (int point = 0; point < target.Points.Count; point += 3)
                    {
                        float distance = math.distance(target.Points[point], origin.xz);
                        if (distance < 260f && JunctionAllowed(target, point)) targets.Add((street, point, distance, false));
                    }
                    if (!target.Closed && target.Kind != RoadKind.Link)
                    {
                        if (!target.Start.Joined) targets.Add((street, 0, math.distance(target.Points[0], origin.xz), true));
                        int last = target.Points.Count - 1;
                        if (!target.End.Joined) targets.Add((street, last, math.distance(target.Points[last], origin.xz), true));
                    }
                }
                targets.Sort((x, y) => x.Distance.CompareTo(y.Distance));
                for (int index = 0; index < math.min(30, targets.Count); index++)
                {
                    var target = targets[index];
                    var street = streets[target.Street];
                    float2 q = street.Points[target.Point];
                    float2 leave = Geometry.Left(along);
                    if (math.dot(leave, q - origin.xz) < 0f) leave = -leave;
                    float2 meet, axis = float2.zero;
                    if (target.End)
                    {
                        // Arriving at a street's open end continues the street: no junction, one smooth line.
                        meet = target.Point == 0 ? street.Tangent(0) : -street.Tangent(target.Point);
                    }
                    else
                    {
                        axis = street.Tangent(target.Point);
                        meet = Geometry.Left(axis);
                        if (math.dot(meet, q - origin.xz) < 0f) meet = -meet;
                    }
                    // Far enough out that a road leaving at 62° has cleared the existing road's corridor and bank.
                    float startExempt = (road.HalfWidth + 2f + halfWidth + 6f) / 0.88f + 2f;
                    foreach (var shape in CoarseShapes)
                    {
                        var path = Curve(origin.xz, leave, along, q, meet, axis, shape.Tilt, shape.Scale);
                        var candidate = Evaluate(path, origin.y, street.Heights[target.Point], startExempt, Exempt, true, along, axis);
                        if (candidate == null) continue;
                        candidate.Score += start.Distance * 0.1f;
                        if (best != null && candidate.Score >= best.Score) continue;
                        candidate.Target = target.Street;
                        candidate.TargetPoint = target.Point;
                        best = candidate;
                        bestStart = start;
                    }
                }
            }
            if (best == null) return false;
            var existing = roads[bestStart.Road];
            Accept(RoadKind.Connection, best, new Attachment
            {
                Street = -1, Point = -1, Existing = true, ExistingId = existing.Id,
                ExistingT = bestStart.Point / (float)(existing.Points.Length - 1),
            });
            return true;
        }

        /// <summary>Joins a contour street's dead end to a street nearby when a gentle enough road fits (loops, crescents).</summary>
        public void JoinEnds(float reach, CancellationToken cancellation)
        {
            int count = streets.Count;
            for (int index = 0; index < count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var street = streets[index];
                if (street.Kind != RoadKind.Contour || street.Closed || street.Removed) continue;
                foreach (bool atStart in new[] { true, false })
                {
                    int end = atStart ? 0 : street.Points.Count - 1;
                    if ((atStart ? street.Start : street.End).Joined || street.Junctions.Contains(end)) continue;
                    float2 origin = street.Points[end];
                    float2 outward = atStart ? -street.Tangent(0) : street.Tangent(end);
                    Candidate best = null;
                    for (int other = 0; other < count; other++)
                    {
                        var target = streets[other];
                        if (other == index || target.Kind != RoadKind.Contour || target.Removed) continue;
                        for (int point = 0; point < target.Points.Count; point += 2)
                        {
                            float2 q = target.Points[point];
                            if (math.distance(q, origin) > reach || math.dot(q - origin, outward) <= 0f || !JunctionAllowed(target, point)) continue;
                            float2 meet = Geometry.Left(target.Tangent(point));
                            if (math.dot(meet, q - origin) < 0f) meet = -meet;
                            var path = Geometry.Hermite(origin, outward, q, meet, 1f, ContourStreets.Spacing);
                            var candidate = Evaluate(path, street.Heights[end], target.Heights[point], Exempt, Exempt, false, default, target.Tangent(point));
                            if (candidate == null || (best != null && candidate.Score >= best.Score)) continue;
                            candidate.Target = other;
                            candidate.TargetPoint = point;
                            best = candidate;
                        }
                    }
                    if (best != null) Accept(RoadKind.Link, best, new Attachment { Street = index, Point = end });
                }
            }
        }

        private void Accept(RoadKind kind, Candidate candidate, Attachment start)
        {
            var link = new Street(kind, candidate.Points, candidate.Heights)
            {
                Start = start,
                End = new Attachment { Street = candidate.Target, Point = candidate.TargetPoint },
            };
            if (start.Street >= 0)
            {
                streets[start.Street].Junctions.Add(start.Point);
                var host = streets[start.Street];
                if (start.Point == 0) host.Start = new Attachment { Street = streets.Count, Point = 0 };
                else if (!host.Closed && start.Point == host.Points.Count - 1) host.End = new Attachment { Street = streets.Count, Point = 0 };
            }
            var target = streets[candidate.Target];
            target.Junctions.Add(candidate.TargetPoint);
            int last = candidate.Points.Count - 1;
            if (!target.Closed && candidate.TargetPoint == 0) target.Start = new Attachment { Street = streets.Count, Point = last };
            else if (!target.Closed && candidate.TargetPoint == target.Points.Count - 1) target.End = new Attachment { Street = streets.Count, Point = last };
            streets.Add(link);
            Stamp(link);
        }

        private const float RampLean = 26f;

        /// <summary>
        /// A ramp traced up (or down) the hill from a point of one street to the next street: it leaves near square, steers
        /// to the heading where the smoothed ground climbs at the target grade (so it follows curved contours at a constant
        /// angle), and turns back to near square to meet the street; no turn is tighter than the minimum radius.
        /// </summary>
        internal Candidate Trace(int from, int fromPoint, int to, float side, float gradeShare)
        {
            var a = streets[from];
            var b = streets[to];
            const float step = ContourStreets.Spacing;
            float2 start = a.Points[fromPoint];
            float climb = math.sign(b.Heights[NearestPoint(b, start)] - a.Heights[fromPoint]);
            if (climb == 0f) climb = 1f;
            float2 travel = a.Tangent(fromPoint) * side;
            float2 normal = Geometry.Left(a.Tangent(fromPoint));
            if (math.dot(normal, survey.UphillAt(start)) * climb < 0f) normal = -normal;
            float lean = math.radians(RampLean), arrivalLean = math.radians(18f);
            float2 heading = math.normalize(normal * math.cos(lean) + travel * math.sin(lean));
            float maximumTurn = step / (minimumRadius * 1.1f);
            float target = request.MaxRoadGrade * gradeShare;
            var points = new List<float2> { start };
            float2 position = start;
            float travelled = 0f;
            float limit = request.BlockDepth * 6f;
            bool clearedStart = false;
            while (travelled < limit)
            {
                int nearest = NearestPoint(b, position);
                float distance = math.distance(position, b.Points[nearest]);
                if (travelled > 20f && distance <= 3f)
                {
                    // Join where the heading crosses the street, so snapping to its point does not twist the approach.
                    float2 axisAtEnd = b.Tangent(nearest);
                    float2 across = Geometry.Left(axisAtEnd);
                    float offset = math.dot(b.Points[nearest] - position, across);
                    float closing = math.dot(heading, across);
                    float2 crossing = math.abs(closing) > 0.2f ? position + heading * (offset / closing) : position;
                    int junction = AllowedNear(b, NearestPoint(b, crossing));
                    if (junction < 0) return null;
                    while (points.Count > 2 && math.distance(points[points.Count - 1], b.Points[junction]) < step * 1.5f) points.RemoveAt(points.Count - 1);
                    points.Add(b.Points[junction]);
                    var path = Geometry.Resample(Geometry.Chaikin(points, 2), step);
                    var candidate = Evaluate(path, a.Heights[fromPoint], b.Heights[junction], Exempt, Exempt, false, a.Tangent(fromPoint), b.Tangent(junction));
                    if (candidate == null) return null;
                    candidate.Target = to;
                    candidate.TargetPoint = junction;
                    candidate.Traced = true;
                    return candidate;
                }
                float2 desired;
                // Hold the departure heading until the ramp has cleared the street it left.
                bool cleared = travelled > 8f && survey.Sample(roadDistance, position) >= Clearance + 3f;
                if (!clearedStart) clearedStart = cleared;
                if (!clearedStart) desired = heading;
                else if (travelled > 20f && distance < 34f)
                {
                    // Arriving: lean the same way the ramp travels, into the street, near square.
                    float2 axis = b.Tangent(nearest);
                    if (math.dot(axis, heading) < 0f) axis = -axis;
                    float2 inward = Geometry.Left(b.Tangent(nearest));
                    if (math.dot(inward, b.Points[nearest] - position) < 0f) inward = -inward;
                    desired = math.normalize(inward * math.cos(arrivalLean) + axis * math.sin(arrivalLean));
                }
                else
                {
                    float2 uphill = survey.UphillAt(position) * climb;
                    if (math.lengthsq(uphill) < 0.5f) uphill = heading;
                    float slope = survey.SlopeAt(position);
                    float2 across = Geometry.Left(uphill);
                    if (math.dot(across, travel) < 0f) across = -across;
                    float rise = slope <= target ? 1f : target / slope;
                    desired = math.normalize(across * math.sqrt(1f - rise * rise) + uphill * rise);
                }
                float angle = math.atan2(heading.x * desired.y - heading.y * desired.x, math.dot(heading, desired));
                angle = math.clamp(angle, -maximumTurn, maximumTurn);
                heading = math.normalize(new float2(heading.x * math.cos(angle) - heading.y * math.sin(angle), heading.x * math.sin(angle) + heading.y * math.cos(angle)));
                position += heading * step;
                travelled += step;
                points.Add(position);
                if (travelled > Exempt && survey.StreetRoomAt(position) < 0f) return null;
                if (clearedStart && distance > 30f && survey.Sample(roadDistance, position) < Clearance) return null;
                if (!clearedStart && travelled > 40f) return null;
            }
            return null;
        }

        private int AllowedNear(Street street, int point)
        {
            for (int offset = 0; offset <= 6; offset++)
                foreach (int sign in new[] { 1, -1 })
                {
                    int index = point + offset * sign;
                    if (street.Closed) index = (index % street.Points.Count + street.Points.Count) % street.Points.Count;
                    else if (index < 0 || index >= street.Points.Count) continue;
                    if (JunctionAllowed(street, index)) return index;
                }
            return -1;
        }

        /// <summary>
        /// A smooth curve from p to q that leaves along p's normal and arrives along q's, each leaning by the tilt towards the
        /// way the curve travels along its own street, so the junctions stay near square and the middle runs across the slope.
        /// </summary>
        private static List<float2> Curve(float2 p, float2 leaveNormal, float2 axisP, float2 q, float2 arriveNormal, float2 axisQ, float tilt, float scale)
        {
            float2 sidewaysP = math.dot(axisP, q - p) >= 0f ? axisP : -axisP;
            float2 sidewaysQ = math.dot(axisQ, q - p) >= 0f ? axisQ : -axisQ;
            float2 leave = math.normalize(leaveNormal * math.cos(tilt) + sidewaysP * math.sin(tilt));
            float2 meet = math.normalize(arriveNormal * math.cos(tilt) + sidewaysQ * math.sin(tilt));
            return Geometry.Hermite(p, leave, q, meet, scale, ContourStreets.Spacing);
        }

        /// <summary>Checks one candidate path and fits its height profile; null when it breaks a rule.</summary>
        internal static readonly int[] Failures = new int[8];
        internal Candidate Evaluate(List<float2> path, float startHeight, float endHeight, float startExempt, float endExempt, bool connection,
            float2 startAxis = default, float2 endAxis = default)
        {
            if (path.Count < 3) return null;
            float length = Geometry.Length(path);
            if (length < 16f) { Failures[0]++; return null; }
            // Cheapest first: a road too short for its rise fails whatever its shape.
            if (length < ShortestFor(math.abs(endHeight - startHeight))) { Failures[4]++; return null; }
            // T-junctions of 60° or more, measured as the validation measures them: the road's direction at the node.
            if (!SquareEnough(path, true, startAxis) || !SquareEnough(path, false, endAxis)) { Failures[7]++; return null; }
            if (Geometry.TightestRadius(path, 12f, out _) < minimumRadius * 0.98f) { Failures[1]++; return null; }
            var stations = Geometry.Stations(path);
            for (int index = 1; index < path.Count - 1; index++)
            {
                if (stations[index] < startExempt || length - stations[index] < endExempt) continue;
                float room = connection ? survey.ConnectionRoomAt(path[index]) : survey.StreetRoomAt(path[index]);
                if (room < 0f) { Failures[2]++; return null; }
                if (survey.Sample(roadDistance, path[index]) < Clearance) { Failures[3]++; return null; }
            }
            var heights = RoadProfile.Fit(path, survey.GroundAt, request.MaxRoadGrade, startHeight, endHeight);
            if (heights == null) { Failures[4]++; return null; }
            float earthwork = 0f;
            for (int index = 0; index < path.Count; index++)
                earthwork = math.max(earthwork, math.abs(heights[index] - survey.GroundAt(path[index])));
            if (earthwork > request.EarthworkLimit) { Failures[5]++; return null; }
            Failures[6]++;
            // Clean ramps over squiggles: every degree the road turns costs half a metre.
            float turning = 0f;
            for (int index = 2; index < path.Count; index++)
            {
                float2 before = math.normalizesafe(path[index - 1] - path[index - 2]), after = math.normalizesafe(path[index] - path[index - 1]);
                turning += math.degrees(math.acos(math.clamp(math.dot(before, after), -1f, 1f)));
            }
            return new Candidate { Points = path, Heights = heights, Score = length + 3f * earthwork + 0.5f * turning };
        }

        private static bool SquareEnough(List<float2> path, bool atStart, float2 axis)
        {
            if (math.lengthsq(axis) < 1e-6f) return true;
            return math.abs(math.dot(RoadNetwork.EndDirection(path, atStart), math.normalizesafe(axis))) <= MinimumJunctionCosine;
        }

        // cos 60°: the rule itself; the validation, measuring the same way, allows a degree more for rounding.
        private const float MinimumJunctionCosine = 0.5f;

        /// <summary>A junction keeps 40 m from the street's other junctions and clear of its open ends.</summary>
        internal bool JunctionAllowed(Street street, int point)
        {
            if (!street.Closed)
            {
                var stations = street.Stations;
                float along = stations[point];
                if (along < EndClearance || stations[stations.Length - 1] - along < EndClearance) return false;
            }
            foreach (int junction in street.Junctions)
                if (street.Between(junction, point) < JunctionSpacing) return false;
            return true;
        }

        /// <summary>The shortest road that can climb a rise: its landings at the landing grade, the rest at the maximum.</summary>
        private float ShortestFor(float rise)
        {
            float landings = 2f * RoadProfile.LandingLength;
            float landingRise = landings * math.min(RoadProfile.LandingGrade, request.MaxRoadGrade);
            return rise <= landingRise ? rise / math.max(0.001f, request.MaxRoadGrade) : landings + (rise - landingRise) / request.MaxRoadGrade;
        }

        // Each street's points in 16 m buckets, built once, for nearest-point queries while ramps are traced.
        private readonly Dictionary<Street, SpatialBuckets<int>> pointBuckets = new Dictionary<Street, SpatialBuckets<int>>();

        private int NearestPoint(Street street, float2 position)
        {
            if (!pointBuckets.TryGetValue(street, out var buckets))
            {
                buckets = new SpatialBuckets<int>(16f);
                for (int index = 0; index < street.Points.Count; index++) buckets.Add(street.Points[index] - 24f, street.Points[index] + 24f, index);
                pointBuckets[street] = buckets;
            }
            int nearest = -1;
            float best = float.MaxValue;
            foreach (int index in buckets.Near(position))
            {
                float distance = math.distancesq(street.Points[index], position);
                if (distance < best || (distance == best && index < nearest)) { best = distance; nearest = index; }
            }
            if (nearest >= 0) return nearest;
            // Further than the buckets reach: a full scan, which is rare (only the first look from far away).
            for (int index = 0; index < street.Points.Count; index++)
            {
                float distance = math.distancesq(street.Points[index], position);
                if (distance < best) { best = distance; nearest = index; }
            }
            return nearest;
        }

        private SpatialBuckets<int2> PointBuckets()
        {
            var buckets = new SpatialBuckets<int2>(16f);
            for (int street = 0; street < streets.Count; street++)
                for (int point = 0; point < streets[street].Points.Count; point++)
                {
                    float2 position = streets[street].Points[point];
                    buckets.Add(position - 4f, position + 4f, new int2(street, point));
                }
            return buckets;
        }

        private int UphillNeighbour(Street street, int self, int point, SpatialBuckets<int2> buckets)
        {
            float2 origin = street.Points[point];
            float2 normal = Geometry.Left(street.Tangent(point));
            if (math.dot(normal, survey.UphillAt(origin)) < 0f) normal = -normal;
            float reach = request.BlockDepth * 2.5f;
            for (float distance = 8f; distance <= reach; distance += 2f)
            {
                float2 probe = origin + normal * distance;
                foreach (var item in buckets.Near(probe))
                    if (item.x != self && math.distance(streets[item.x].Points[item.y], probe) <= 3f) return item.x;
            }
            return -1;
        }
    }
}
