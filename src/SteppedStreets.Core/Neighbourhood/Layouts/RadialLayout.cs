// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood.Layouts
{
    /// <summary>
    /// The radial layouts for flat or gentle ground: streets spreading from a centre. A star of streets from a small ring,
    /// concentric rings joined by staggered streets, a spiderweb of rings crossed by straight radial streets, or a fan of arcs
    /// and radial streets opening from the edge of the area nearest the city's road.
    /// </summary>
    internal static class RadialLayout
    {
        private const float Step = ContourStreets.Spacing;
        // A radial street keeps clear of an arc's open end, as a grid's cross street does of a row's.
        private const float EndClearance = 26f;

        public static bool Handles(NeighbourhoodStyle style) => style == NeighbourhoodStyle.Star || style == NeighbourhoodStyle.Rings
            || style == NeighbourhoodStyle.Spiderweb || style == NeighbourhoodStyle.Fan;

        /// <summary>One street of a ring: the whole circle, or the stretch of it that fits in the area.</summary>
        private struct Arc
        {
            public int Street, Start, Count, Around;
        }

        /// <summary>The streets of the layout; <paramref name="joined"/> says whether any meets the city's road straight on.</summary>
        public static List<Street> Plan(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation)
        {
            var streets = new List<Street>();
            joined = false;
            var style = request.Style;
            float depth = request.BlockDepth;
            bool fan = style == NeighbourhoodStyle.Fan, star = style == NeighbourhoodStyle.Star;
            bool share = style != NeighbourhoodStyle.Rings;

            // The centre, and the direction its first radial street takes: towards the city's road, turned a little by Reroll;
            // a fan opens from the edge nearest that road.
            float2 facing = Geometry.Left(GridLayout.Facing(survey, request));
            float2 centre = fan ? Edge(survey, request, out facing) : Middle(survey);
            float start = math.atan2(facing.y, facing.x) + (fan ? 0f : (Geometry.Hash01(request.Seed, 7, 0) - 0.5f) * math.PI / 6f);
            float farthest = 0f;
            foreach (var corner in survey.Boundary) farthest = math.max(farthest, math.distance(corner, centre));

            // Rings one block depth apart. A star has two: a small one its eight streets meet 40 m apart, and a boulevard two
            // block depths out, past which twice as many streets run on to the edge.
            const int StarStreets = 8;
            var radii = new List<float>();
            if (star)
            {
                radii.Add(math.max(depth * 0.6f, StarStreets * GridLayout.JunctionRoom / (2f * math.PI)));
                if (radii[0] + depth * 3f < farthest) radii.Add(radii[0] + depth * 2f);
            }
            else
                for (float radius = depth * (fan ? 0.75f : 0.6f); radius < farthest; radius += depth) radii.Add(radius);
            var rings = new List<(float Radius, List<Arc> Arcs)>();
            foreach (float radius in radii)
            {
                cancellation.ThrowIfCancellationRequested();
                rings.Add((radius, Ring(survey, request, streets, centre, start, radius)));
            }
            if (rings.Count == 0) return streets;

            // Radial streets between neighbouring rings: staggered between rings, or lined up from the centre out (spiderweb and
            // fan), doubling where the rings grow so far apart round that the blocks would get too long.
            for (int ring = 0; ring + 1 < rings.Count; ring++)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = star ? StarStreets : Radials(style, rings[ring + 1].Radius, depth);
                float offset = style == NeighbourhoodStyle.Rings && ring % 2 == 1 ? 0.5f : 0f;
                for (int radial = 0; radial < count; radial++)
                {
                    float turn = (radial + offset) / count;
                    // Staggered streets of rings whose counts differ can land too near each other: try 4 m steps either way,
                    // far enough to clear a junction right where this one would go.
                    int tries = style == NeighbourhoodStyle.Rings ? 2 * (int)math.ceil(GridLayout.JunctionRoom / Step) + 1 : 1;
                    for (int attempt = 0; attempt < tries; attempt++)
                    {
                        float nudge = (attempt + 1) / 2 * (attempt % 2 == 0 ? 1f : -1f) * Step / (2f * math.PI * rings[ring].Radius);
                        if (!Find(streets, rings[ring].Arcs, turn + nudge, share, out int a, out int pointA)
                            || !Find(streets, rings[ring + 1].Arcs, turn + nudge, share, out int b, out int pointB)) continue;
                        if (GridLayout.Join(survey, request, streets, a, pointA, b, pointB) >= 0) break;
                    }
                }
            }

            // A star's streets run on from its outer ring to the edge of the area.
            if (star)
            {
                int outer = rings.Count - 1, count = StarStreets * (rings.Count > 1 ? 2 : 1);
                for (int spoke = 0; spoke < count; spoke++)
                    if (Find(streets, rings[outer].Arcs, spoke / (float)count, share, out int host, out int point))
                        GridLayout.Spoke(survey, request, streets, host, point, math.normalizesafe(streets[host].Points[point] - centre), farthest);
            }

            // Like the grids, every street that runs out towards the city's road meets it in a T, and so do radial streets run
            // on from the outermost ring; a fan's first arc also meets the road behind it, the way in.
            int made = streets.Count;
            for (int index = 0; index < made; index++)
            {
                var street = streets[index];
                if (street.Kind != RoadKind.Street || street.Closed) continue;
                int last = street.Points.Count - 1;
                if (!street.Start.Joined)
                    joined |= GridLayout.Reach(survey, request, streets, index, 0, math.normalizesafe(street.Points[0] - street.Points[1]));
                if (!street.End.Joined)
                    joined |= GridLayout.Reach(survey, request, streets, index, last, math.normalizesafe(street.Points[last] - street.Points[last - 1]));
            }
            for (int ring = 0; ring < rings.Count && !star; ring++)
            {
                int count = Radials(style, rings[ring].Radius, depth);
                for (int radial = 0; radial < count; radial++)
                {
                    float turn = (radial + (style == NeighbourhoodStyle.Rings && ring % 2 == 1 ? 0.5f : 0f)) / count;
                    float2 outward = Direction(start + 2f * math.PI * turn);
                    // Only where no further ring lies outside this one.
                    if (survey.StreetRoomAt(centre + outward * (rings[ring].Radius + depth)) >= 0f) continue;
                    if (Find(streets, rings[ring].Arcs, turn, share, out int host, out int point))
                        joined |= GridLayout.Reach(survey, request, streets, host, point, outward);
                }
            }
            if (fan && Find(streets, rings[0].Arcs, 0f, share, out int apex, out int apexPoint))
                joined |= GridLayout.Reach(survey, request, streets, apex, apexPoint, -facing);
            return streets;
        }

        /// <summary>How many radial streets meet a ring: about one every 2.5 block depths round it.</summary>
        private static int Radials(NeighbourhoodStyle style, float radius, float depth)
        {
            float around = 2f * math.PI * radius, longest = depth * 2.5f;
            if (style == NeighbourhoodStyle.Rings) return math.max(3, (int)math.round(around / longest));
            int count = 6;
            while (around / count > longest) count *= 2;
            return count;
        }

        /// <summary>The streets of one ring: the whole circle where it all fits, else each stretch that does.</summary>
        private static List<Arc> Ring(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, float2 centre, float start, float radius)
        {
            int around = math.max(12, (int)math.round(2f * math.PI * radius / Step));
            var points = new float2[around];
            var inside = new bool[around];
            int fits = 0;
            for (int index = 0; index < around; index++)
            {
                points[index] = centre + Direction(start + 2f * math.PI * index / around) * radius;
                inside[index] = survey.StreetRoomAt(points[index]) >= 0f;
                if (inside[index]) fits++;
            }
            var arcs = new List<Arc>();
            if (fits == around)
            {
                var whole = Closed(survey, request, new List<float2>(points));
                if (whole == null) return arcs;
                streets.Add(whole);
                arcs.Add(new Arc { Street = streets.Count - 1, Start = 0, Count = around, Around = around });
                return arcs;
            }
            // Stretches, starting from a point that doesn't fit so none wraps past the start.
            int begin = 0;
            while (inside[begin]) begin++;
            int runStart = -1;
            for (int step = 1; step <= around; step++)
            {
                int index = (begin + step) % around;
                if (inside[index]) { if (runStart < 0) runStart = step; continue; }
                if (runStart < 0) continue;
                int count = step - runStart, first = (begin + runStart) % around;
                runStart = -1;
                if ((count - 1) * Step < ContourStreets.ShortestStreet) continue;
                var line = new List<float2>(count);
                for (int offset = 0; offset < count; offset++) line.Add(points[(first + offset) % around]);
                var street = GridLayout.Street(survey, request, line, RoadKind.Street);
                if (street == null) continue;
                streets.Add(street);
                arcs.Add(new Arc { Street = streets.Count - 1, Start = first, Count = count, Around = around });
            }
            return arcs;
        }

        /// <summary>A ring street on the ground's line, or null where it would need more earth than the limit.</summary>
        internal static Street Closed(SiteSurvey survey, NeighbourhoodRequest request, List<float2> ring)
        {
            var heights = RoadProfile.FitRing(ring, survey.GroundAt, request.MaxRoadGrade);
            for (int index = 0; index < ring.Count; index++)
                if (math.abs(heights[index] - survey.GroundAt(ring[index])) > request.EarthworkLimit) return null;
            return new Street(RoadKind.Street, ring, heights) { Closed = true };
        }

        /// <summary>
        /// The ring's point at a turn round it (0 to 1 from the first radial), clear of arc ends and other junctions; with
        /// <paramref name="share"/>, it may be a junction already there.
        /// </summary>
        private static bool Find(List<Street> streets, List<Arc> arcs, float turn, bool share, out int street, out int point)
        {
            street = point = -1;
            foreach (var arc in arcs)
            {
                int index = ((int)math.round(turn * arc.Around) % arc.Around + arc.Around) % arc.Around;
                int at = (index - arc.Start + arc.Around) % arc.Around;
                if (at >= arc.Count) continue;
                var candidate = streets[arc.Street];
                if (!candidate.Closed)
                {
                    float along = candidate.Stations[at], length = candidate.Stations[candidate.Stations.Length - 1];
                    if (along < EndClearance || length - along < EndClearance) return false;
                }
                // A point already a junction is shared only where radials line up (crossroads); rings stay all T-junctions.
                foreach (int junction in candidate.Junctions)
                    if ((junction != at || !share) && candidate.Between(junction, at) < GridLayout.JunctionGap) return false;
                street = arc.Street;
                point = at;
                return true;
            }
            return false;
        }

        private static float2 Direction(float angle) => new float2(math.cos(angle), math.sin(angle));

        /// <summary>The roomiest place in the area, nearest its middle: where the rings have most room to close.</summary>
        private static float2 Middle(SiteSurvey survey)
        {
            float2 middle = (survey.BoundaryMinimum + survey.BoundaryMaximum) * 0.5f, best = middle;
            float score = float.MinValue;
            for (float x = survey.BoundaryMinimum.x; x <= survey.BoundaryMaximum.x; x += 16f)
                for (float y = survey.BoundaryMinimum.y; y <= survey.BoundaryMaximum.y; y += 16f)
                {
                    var point = new float2(x, y);
                    float room = survey.StreetRoomAt(point);
                    if (room < 0f) continue;
                    float here = room - 0.05f * math.distance(point, middle);
                    if (here > score) { score = here; best = point; }
                }
            return best;
        }

        /// <summary>The point of the area's edge nearest the city's road within 400 m, else the middle of its longest side.</summary>
        private static float2 Edge(SiteSurvey survey, NeighbourhoodRequest request, out float2 inward)
        {
            var boundary = survey.Boundary;
            int corners = boundary.Length, side = -1;
            float best = float.MaxValue;
            float2 found = default, middle = (survey.BoundaryMinimum + survey.BoundaryMaximum) * 0.5f;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                foreach (var point in road?.Points ?? new float3[0])
                    for (int corner = 0; corner < corners; corner++)
                    {
                        float2 a = boundary[corner], b = boundary[(corner + 1) % corners], edge = b - a;
                        float share = math.saturate(math.dot(point.xz - a, edge) / math.max(math.lengthsq(edge), 1e-6f));
                        float2 nearest = a + edge * share;
                        float distance = math.distance(point.xz, nearest);
                        // Along a road that runs beside a whole side, the middle of that side rather than its corner.
                        float score = distance + 0.5f * math.distance(nearest, middle);
                        if (distance <= 400f && score < best) { best = score; found = nearest; side = corner; }
                    }
            if (side < 0)
            {
                float longest = 0f;
                for (int corner = 0; corner < corners; corner++)
                {
                    float length = math.distance(boundary[corner], boundary[(corner + 1) % corners]);
                    if (length > longest) { longest = length; side = corner; }
                }
                found = (boundary[side] + boundary[(side + 1) % corners]) * 0.5f;
            }
            float2 along = math.normalizesafe(boundary[(side + 1) % corners] - boundary[side], new float2(1f, 0f));
            inward = Geometry.Left(along);
            if (!Poly.Contains(boundary, found + inward * 2f)) inward = -inward;
            return found;
        }
    }
}
