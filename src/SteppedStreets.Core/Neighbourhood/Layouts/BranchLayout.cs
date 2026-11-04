// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood.Layouts
{
    /// <summary>
    /// The linear and branching layouts: trunk streets running into the area from the city's road, with streets branching
    /// off them. Straight side streets (spine), side streets on one side only (comb), slanting pairs (fishbone), rungs between
    /// rails (ladder), limbs with twigs (tree), short dead ends (cul-de-sacs), loops, and small rings on sticks (lollipops).
    /// </summary>
    internal static class BranchLayout
    {
        private const float Step = ContourStreets.Spacing;
        // A branch keeps clear of a trunk's open end, as a grid's cross street does of a row's.
        private const float EndClearance = 26f;
        // A fishbone's side streets slant 65° off the trunk: clear of the sharpest junction allowed (60°) after rounding.
        private const float BoneDegrees = 65f;

        public static bool Handles(NeighbourhoodStyle style) => style == NeighbourhoodStyle.Spine || style == NeighbourhoodStyle.Ladder
            || style == NeighbourhoodStyle.Comb || style == NeighbourhoodStyle.Fishbone || style == NeighbourhoodStyle.Tree
            || style == NeighbourhoodStyle.CulDeSacs || style == NeighbourhoodStyle.Loops || style == NeighbourhoodStyle.LoopsAndLollipops
            || style == NeighbourhoodStyle.Lollipops;

        /// <summary>The streets of the layout; <paramref name="joined"/> says whether any meets the city's road straight on.</summary>
        public static List<Street> Plan(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation)
        {
            var streets = new List<Street>();
            joined = false;
            var style = request.Style;
            uint seed = request.Seed;
            float depth = request.BlockDepth;
            // Junctions along a trunk at least 40 m apart, even where the two sides' branches alternate.
            float unit = math.max(depth, 2f * RoadLinks.JunctionSpacing);

            // Trunks run away from the city's road, so each meets it at its near end; Reroll mirrors the layout.
            float2 inward = Inward(survey, request);
            float2 side = Geometry.Left(inward);
            if (Geometry.Hash01(seed, 3, 0) >= 0.5f) side = -side;
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var corner in survey.Boundary)
            {
                float u = math.dot(corner, side), v = math.dot(corner, inward);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMin = math.min(vMin, v); vMax = math.max(vMax, v);
            }
            float width = uMax - uMin;

            // Where the trunks run. Long branches reach the edge from one trunk; short ones need a trunk every few blocks, with
            // the branches of neighbouring trunks stopping well short of each other.
            bool shortBranches = style == NeighbourhoodStyle.CulDeSacs || style == NeighbourhoodStyle.Loops
                || style == NeighbourhoodStyle.LoopsAndLollipops || style == NeighbourhoodStyle.Lollipops;
            var positions = new List<float>();
            float reach = 4000f;
            if (style == NeighbourhoodStyle.Comb) positions.Add(uMin + depth * 0.5f);
            else if (style == NeighbourhoodStyle.Ladder)
            {
                int rails = math.max(2, (int)math.ceil((width - depth) / 420f) + 1);
                for (int rail = 0; rail < rails; rail++) positions.Add(math.lerp(uMin + depth * 0.5f, uMax - depth * 0.5f, rail / (float)(rails - 1)));
            }
            else if (shortBranches)
            {
                int count = math.max(1, (int)math.ceil(width / (3.8f * depth)));
                float spacing = width / count;
                for (int trunk = 0; trunk < count; trunk++) positions.Add(uMin + (trunk + 0.5f) * spacing);
                reach = math.min(1.5f * depth, spacing * 0.5f - 0.4f * depth);
            }
            else positions.Add((uMin + uMax) * 0.5f);

            var trunks = new List<List<(int Street, float From)>>();
            foreach (float u in positions)
            {
                cancellation.ThrowIfCancellationRequested();
                var pieces = new List<(int, float)>();
                foreach (var (from, points) in GridLayout.Clip(survey, inward, side, u, vMin - Step, vMax + Step, _ => 0f))
                {
                    var street = GridLayout.Street(survey, request, points, RoadKind.Street);
                    if (street == null) continue;
                    pieces.Add((streets.Count, from));
                    streets.Add(street);
                }
                trunks.Add(pieces);
            }

            float first = unit * 0.75f;
            if (style == NeighbourhoodStyle.Ladder)
            {
                // Rungs between neighbouring rails, one block depth apart, lined up so a middle rail gets crossroads.
                for (int rail = 0; rail + 1 < trunks.Count; rail++)
                    for (float v = vMin + first; v < vMax; v += unit)
                        if (Point(streets, trunks[rail], v, out int a, out int pointA) && Point(streets, trunks[rail + 1], v, out int b, out int pointB))
                            GridLayout.Join(survey, request, streets, a, pointA, b, pointB);
            }
            else
                foreach (var pieces in trunks)
                    foreach (var (trunk, from) in pieces)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        Branches(survey, request, streets, trunk, from, inward, side, unit, reach, first, seed);
                    }

            // Every street that runs out towards the city's road meets it in a T: the trunks at their near ends above all.
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
            return streets;
        }

        /// <summary>The branches off one trunk, by layout.</summary>
        private static void Branches(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, int trunk, float from,
            float2 inward, float2 side, float unit, float reach, float first, uint seed)
        {
            var style = request.Style;
            float length = streets[trunk].Stations[streets[trunk].Stations.Length - 1];
            var single = new List<(int, float)> { (trunk, from) };
            switch (style)
            {
                case NeighbourhoodStyle.Spine:
                case NeighbourhoodStyle.Comb:
                case NeighbourhoodStyle.CulDeSacs:
                    // Straight side streets one block apart; on both sides they alternate, so every junction is a T.
                    for (float s = first; s < length; s += unit)
                    {
                        Branch(survey, request, streets, single, from + s, side, reach);
                        if (style != NeighbourhoodStyle.Comb) Branch(survey, request, streets, single, from + s + unit * 0.5f, -side, reach);
                    }
                    break;
                case NeighbourhoodStyle.Fishbone:
                {
                    // Pairs slanting away from the city's road, one block apart measured square to them.
                    float slant = math.radians(BoneDegrees);
                    for (float s = first; s < length; s += unit / math.sin(slant))
                        foreach (float sign in new[] { 1f, -1f })
                            Branch(survey, request, streets, single, from + s, inward * math.cos(slant) + side * sign * math.sin(slant), reach);
                    break;
                }
                case NeighbourhoodStyle.Tree:
                {
                    // Limbs every two and a half blocks, alternating sides; twigs off each limb run on away from the city's road,
                    // one block apart, stopping short of the next limb.
                    float limbs = unit * 2.5f;
                    for (float s = first; s < length; s += limbs)
                        foreach (var (at, toward) in new[] { (s, side), (s + limbs * 0.5f, -side) })
                        {
                            int limb = Branch(survey, request, streets, single, from + at, toward, reach);
                            if (limb < 0) continue;
                            float limbLength = streets[limb].Stations[streets[limb].Stations.Length - 1];
                            var limbPiece = new List<(int, float)> { (limb, 0f) };
                            for (float t = unit; t < limbLength; t += unit) Branch(survey, request, streets, limbPiece, t, inward, limbs - unit * 0.75f);
                        }
                    break;
                }
                default:
                {
                    // Loops and lollipops in slots two blocks wide, the same on both sides of the trunk.
                    for (float s = first; s < length; s += unit * 2f)
                    {
                        int slot = (int)math.round((s - first) / (unit * 2f));
                        foreach (float sign in new[] { 1f, -1f })
                        {
                            bool loop = style == NeighbourhoodStyle.Loops
                                || (style == NeighbourhoodStyle.LoopsAndLollipops && (slot + (sign > 0f ? 0 : 1)) % 2 == 0);
                            if (loop) Loop(survey, request, streets, single, from + s, from + s + unit, side * sign, inward, reach);
                            else Lollipop(survey, request, streets, single, from + s + unit * 0.5f, side * sign, reach, unit);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>A straight side street from the trunk point at <paramref name="v"/>, up to <paramref name="longest"/>; −1 where none fits.</summary>
        private static int Branch(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, List<(int Street, float From)> trunk,
            float v, float2 direction, float longest)
        {
            if (!Point(streets, trunk, v, out int host, out int point)) return -1;
            return GridLayout.Spoke(survey, request, streets, host, point, math.normalizesafe(direction), longest);
        }

        /// <summary>
        /// A loop off the trunk: out from one point, round a half circle and back to a point a block further on, the two legs a
        /// block apart so the lots inside meet back to back. As long as fits, up to <paramref name="reach"/>.
        /// </summary>
        private static void Loop(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, List<(int Street, float From)> trunk,
            float vA, float vB, float2 outward, float2 forward, float reach)
        {
            if (!Point(streets, trunk, vA, out int host, out int a) || !Point(streets, trunk, vB, out int other, out int b) || other != host) return;
            var street = streets[host];
            float2 start = street.Points[a], end = street.Points[b];
            float radius = math.distance(start, end) * 0.5f;
            for (float leg = reach - radius; leg >= 24f; leg -= 8f)
            {
                var points = new List<float2>();
                for (float t = 0f; t < leg; t += Step) points.Add(start + outward * t);
                float2 centre = start + outward * leg + forward * radius;
                int arc = math.max(6, (int)math.ceil(math.PI * radius / Step));
                for (int index = 0; index <= arc; index++)
                {
                    float angle = math.PI * index / arc;
                    points.Add(centre - forward * radius * math.cos(angle) + outward * radius * math.sin(angle));
                }
                for (float t = leg - Step; t > 0f; t -= Step) points.Add(end + outward * t);
                points.Add(end);
                if (!Fits(survey, points, 1, points.Count - 1)) continue;
                var loop = GridLayout.Street(survey, request, points, RoadKind.Street, street.Heights[a], street.Heights[b]);
                if (loop == null) continue;
                loop.Start = new Attachment { Street = host, Point = a };
                loop.End = new Attachment { Street = host, Point = b };
                street.Junctions.Add(a);
                street.Junctions.Add(b);
                streets.Add(loop);
                return;
            }
        }

        /// <summary>
        /// A lollipop off the trunk: a short stick ending in a small ring street, the lots round the ring. As long as fits, up to
        /// <paramref name="reach"/>.
        /// </summary>
        private static void Lollipop(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, List<(int Street, float From)> trunk,
            float v, float2 outward, float reach, float unit)
        {
            if (!Point(streets, trunk, v, out int host, out int at)) return;
            var street = streets[host];
            float2 start = street.Points[at];
            // Small, but no tighter than the street's smallest curve, and narrow enough for its slot with lots round it.
            float radius = math.clamp(unit * 0.4f, NeighbourhoodPlanner.MinimumRadius(request.Style) + 6f, 48f);
            // The stick's two junctions, on the trunk and on the ring, at least 40 m apart, even if the lollipop then reaches a
            // little further than a loop would.
            float shortest = RoadLinks.JunctionSpacing + Step;
            for (float stick = math.max(reach - 2f * radius, shortest); stick >= shortest; stick -= 8f)
            {
                float2 centre = start + outward * (stick + radius);
                int around = (int)math.round(2f * math.PI * radius / Step);
                var ring = new List<float2>(around);
                float angle = math.atan2(-outward.y, -outward.x);
                for (int index = 0; index < around; index++)
                {
                    float turn = angle + 2f * math.PI * index / around;
                    ring.Add(centre + new float2(math.cos(turn), math.sin(turn)) * radius);
                }
                var line = GridLayout.Line(start, ring[0]);
                if (!Fits(survey, ring, 0, ring.Count) || !Fits(survey, line, 1, line.Count)) continue;
                var loop = RadialLayout.Closed(survey, request, ring);
                if (loop == null) continue;
                var stem = GridLayout.Street(survey, request, line, RoadKind.Link, street.Heights[at], loop.Heights[0]);
                if (stem == null) continue;
                streets.Add(loop);
                stem.Start = new Attachment { Street = host, Point = at };
                stem.End = new Attachment { Street = streets.Count - 1, Point = 0 };
                street.Junctions.Add(at);
                loop.Junctions.Add(0);
                streets.Add(stem);
                return;
            }
        }

        private static bool Fits(SiteSurvey survey, List<float2> points, int from, int to)
        {
            for (int index = from; index < to; index++) if (survey.StreetRoomAt(points[index]) < 0f) return false;
            return true;
        }

        /// <summary>A trunk's point at a place along it, clear of its ends and of other junctions on it.</summary>
        private static bool Point(List<Street> streets, List<(int Street, float From)> trunk, float v, out int street, out int point)
        {
            street = point = -1;
            foreach (var (index, from) in trunk)
            {
                var candidate = streets[index];
                int at = (int)math.round((v - from) / Step);
                if (at < 0 || at >= candidate.Points.Count) continue;
                float along = candidate.Stations[at], length = candidate.Stations[candidate.Stations.Length - 1];
                if (along < EndClearance || length - along < EndClearance) return false;
                foreach (int junction in candidate.Junctions)
                    if (junction != at && candidate.Between(junction, at) < RoadLinks.JunctionSpacing) return false;
                street = index;
                point = at;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The way the trunks run: away from the city's nearest road within 400 m, else along the area's longest side.
        /// </summary>
        private static float2 Inward(SiteSurvey survey, NeighbourhoodRequest request)
        {
            float2 facing = GridLayout.Facing(survey, request);
            float2 middle = (survey.BoundaryMinimum + survey.BoundaryMaximum) * 0.5f, nearest = default;
            float best = 400f;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                foreach (var point in road?.Points ?? new float3[0])
                {
                    float distance = Poly.Contains(survey.Boundary, point.xz) ? 0f : Poly.DistanceToBoundary(survey.Boundary, point.xz);
                    if (distance < best) { best = distance; nearest = point.xz; }
                }
            if (best >= 400f) return facing;
            float2 inward = Geometry.Left(facing);
            return math.dot(nearest - middle, inward) > 0f ? -inward : inward;
        }
    }
}
