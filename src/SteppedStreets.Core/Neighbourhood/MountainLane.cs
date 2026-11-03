// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Mountain lane: one road that climbs the hill across the slope at no more than the maximum road grade, turning back in
    /// a hairpin where it runs out of room, as a mountain road does (video tip: be deliberate on steep stretches).
    /// </summary>
    internal static class MountainLane
    {
        private const float Step = 4f;
        private const float LongestLane = 4000f;
        private const int MostHairpins = 14;

        /// <summary>The longest lane over a few starting sides and climbing grades: the hill decides which winds furthest.</summary>
        public static Street Trace(SiteSurvey survey, NeighbourhoodRequest request, float minimumRadius, CancellationToken cancellation)
        {
            Street best = null;
            foreach (float side in new[] { 1f, -1f })
                foreach (float share in new[] { 0.8f, 0.6f })
                {
                    var lane = TraceOnce(survey, request, minimumRadius, side, share, cancellation);
                    if (lane != null && (best == null || lane.Length > best.Length + 1f)) best = lane;
                }
            return best;
        }

        private static Street TraceOnce(SiteSurvey survey, NeighbourhoodRequest request, float minimumRadius, float startSide, float share,
            CancellationToken cancellation)
        {
            var start = Entry(survey, request, out float2 heading, out Attachment attachment, out float startHeight);
            if (!start.HasValue) return null;
            float targetGrade = request.MaxRoadGrade * share;
            float turnRadius = minimumRadius * 1.15f;
            float maximumTurn = Step / turnRadius;
            float2 position = start.Value;
            float side = startSide;
            var points = new List<float2> { position };
            float travelled = 0f;
            int hairpins = 0;
            bool turning = false;
            float lookAhead = 2f * turnRadius + 16f;
            while (travelled < LongestLane)
            {
                if ((points.Count & 31) == 0) cancellation.ThrowIfCancellationRequested();
                float2 uphill = survey.UphillAt(position);
                if (math.lengthsq(uphill) < 0.5f) uphill = heading;
                float slope = survey.SlopeAt(position);
                float2 across = Geometry.Left(uphill) * side;
                float2 desired = slope <= targetGrade ? uphill
                    : math.normalize(across * math.sqrt(1f - (targetGrade / slope) * (targetGrade / slope)) + uphill * (targetGrade / slope));

                // Out of room ahead: turn back across the slope, swinging through uphill.
                if (!turning && travelled > 40f && !Clear(survey, position, heading, lookAhead, travelled))
                {
                    if (++hairpins > MostHairpins) break;
                    side = -side;
                    turning = true;
                    continue;
                }
                float angle = SignedAngle(heading, desired);
                if (turning && math.abs(angle) < math.radians(20f)) turning = false;
                if (turning && math.abs(angle) > math.radians(120f))
                {
                    // Swing through uphill rather than the short way round, which would point the lane downhill.
                    angle = SignedAngle(heading, uphill);
                    if (math.abs(angle) < 0.05f) angle = math.sign(SignedAngle(heading, desired)) * maximumTurn;
                }
                heading = Rotate(heading, math.clamp(angle, -maximumTurn, maximumTurn));
                float2 next = position + heading * Step;
                if (!Room(survey, next, travelled)) break;
                points.Add(next);
                position = next;
                travelled += Step;
            }
            if (travelled < ContourStreets.ShortestStreet) return null;
            var line = Geometry.Resample(Geometry.Chaikin(points, 2), ContourStreets.Spacing);
            ContourStreets.HoldRadius(line, minimumRadius, false);
            line = Geometry.Resample(line, ContourStreets.Spacing);
            var heights = RoadProfile.Fit(line, survey.GroundAt, request.MaxRoadGrade, attachment.Existing ? startHeight : (float?)null, null);
            if (heights == null) return null;
            return new Street(RoadKind.Lane, line, heights) { Start = attachment };
        }

        // Leaving the existing road it may cross its corridor; on the way in it may cross the band inside the outline that
        // streets keep clear of; after that it stays where streets may go.
        private static bool Room(SiteSurvey survey, float2 point, float travelled)
        {
            if (travelled < 24f) return survey.ConnectionRoomAt(point) >= -20f;
            if (survey.StreetRoomAt(point) >= 0f) return true;
            return survey.ConnectionRoomAt(point) >= 0f && (travelled < 80f || !Poly.Contains(survey.Boundary, point) && travelled < 400f);
        }

        private static bool Clear(SiteSurvey survey, float2 position, float2 heading, float reach, float travelled)
        {
            for (float distance = 8f; distance <= reach; distance += 8f)
                if (!Room(survey, position + heading * distance, travelled + distance)) return false;
            return true;
        }

        /// <summary>Starts on the nearest existing road, heading into the area; otherwise at the lowest buildable point.</summary>
        private static float2? Entry(SiteSurvey survey, NeighbourhoodRequest request, out float2 heading, out Attachment attachment,
            out float height)
        {
            attachment = Attachment.None;
            heading = new float2(1f, 0f);
            height = 0f;
            float2 centre = (survey.BoundaryMinimum + survey.BoundaryMaximum) * 0.5f;
            float best = 400f;
            float2? start = null;
            var roads = request.ExistingRoads ?? new ExistingRoad[0];
            foreach (var road in roads)
            {
                if (road?.Points == null || road.Points.Length < 2) continue;
                for (int point = 0; point < road.Points.Length; point++)
                {
                    float2 position = road.Points[point].xz;
                    if (!survey.InsideGrid(position, 24f)) continue;
                    float distance = Poly.Contains(survey.Boundary, position) ? 0f : Poly.DistanceToBoundary(survey.Boundary, position);
                    if (distance >= best) continue;
                    int before = math.max(0, point - 1), after = math.min(road.Points.Length - 1, point + 1);
                    float2 normal = Geometry.Left(math.normalizesafe(road.Points[after].xz - road.Points[before].xz, new float2(1f, 0f)));
                    if (math.dot(normal, centre - position) < 0f) normal = -normal;
                    best = distance;
                    start = position;
                    heading = normal;
                    height = road.Points[point].y;
                    attachment = new Attachment { Street = -1, Point = -1, Existing = true, ExistingId = road.Id, ExistingT = point / (float)(road.Points.Length - 1) };
                }
            }
            if (start.HasValue) return start;
            float lowest = float.MaxValue;
            for (int index = 0; index < survey.Count; index++)
            {
                if (survey.StreetRoom[index] < 8f || survey.Smooth[index] >= lowest) continue;
                lowest = survey.Smooth[index];
                start = survey.Position(index);
            }
            // On dead flat ground there is no uphill: head for the middle of the area instead.
            if (start.HasValue) heading = math.normalizesafe(survey.UphillAt(start.Value), math.normalizesafe(centre - start.Value, new float2(1f, 0f)));
            return start;
        }

        private static float SignedAngle(float2 from, float2 to) =>
            math.atan2(from.x * to.y - from.y * to.x, math.dot(from, to));

        private static float2 Rotate(float2 vector, float angle)
        {
            float cos = math.cos(angle), sin = math.sin(angle);
            return math.normalize(new float2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos));
        }
    }
}
