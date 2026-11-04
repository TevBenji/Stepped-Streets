// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood.Layouts
{
    /// <summary>
    /// The grid layouts for flat or gentle ground: parallel main streets one block depth apart, joined by cross streets.
    /// Square blocks, long blocks, varied spacing, skewed cross streets, or a broken grid with gaps; cross streets meet the
    /// main streets as crossroads, staggered T-junctions, or a traffic-optimised mix where one through road carries the
    /// traffic and is the only road that meets the city's.
    /// </summary>
    internal static class GridLayout
    {
        private const float Step = ContourStreets.Spacing;
        // A cross street keeps clear of a main street's open end (RoadLinks.EndClearance) and of other junctions on it.
        private const float EndClearance = 26f;
        private const float SkewDegrees = 68f;

        public static bool Handles(NeighbourhoodStyle style) => style == NeighbourhoodStyle.SquareGrid
            || style == NeighbourhoodStyle.RectangularGrid || style == NeighbourhoodStyle.IrregularGrid
            || style == NeighbourhoodStyle.SkewedGrid || style == NeighbourhoodStyle.FragmentedGrid
            || style == NeighbourhoodStyle.DiagonalGrid || style == NeighbourhoodStyle.CurvedGrid
            || style == NeighbourhoodStyle.WarpedGrid || style == NeighbourhoodStyle.RadialGrid;

        // A diagonal avenue meets the streets at 64°: clear of the sharpest junction allowed (60°) once its ends are rounded to
        // the streets' 4 m points.
        private static readonly float DiagonalLean = 1f / math.tan(math.radians(64f));

        /// <summary>
        /// The streets of the grid. Streets that run out towards the city's road meet it straight on; <paramref name="joined"/>
        /// says whether any did. <paramref name="through"/> holds the streets a winding connection may still join, when one
        /// is needed (traffic-optimised: only the through road).
        /// </summary>
        public static List<Street> Plan(SiteSurvey survey, NeighbourhoodRequest request, out HashSet<int> through, out bool joined,
            CancellationToken cancellation)
        {
            var streets = new List<Street>();
            through = new HashSet<int>();
            joined = false;
            var style = request.Style;
            uint seed = request.Seed;
            bool traffic = request.Junctions == JunctionPattern.TrafficOptimised;
            bool staggered = request.Junctions != JunctionPattern.Crossroads;

            // Rows run along the city's nearest road, else the area's longest side; Reroll may turn the grid a quarter, except
            // traffic-optimised, whose through road crosses the rows and so must point at the city's road.
            float2 along = Facing(survey, request);
            if (!traffic && Geometry.Hash01(seed, 3, 0) >= 0.5f) along = Geometry.Left(along);
            float2 across = Geometry.Left(along);
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var corner in survey.Boundary)
            {
                float u = math.dot(corner, along), v = math.dot(corner, across);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMin = math.min(vMin, v); vMax = math.max(vMax, v);
            }

            float rowSpacing = request.BlockDepth;
            bool curved = style == NeighbourhoodStyle.CurvedGrid || style == NeighbourhoodStyle.WarpedGrid;
            bool broken = style == NeighbourhoodStyle.FragmentedGrid || style == NeighbourhoodStyle.WarpedGrid;
            float waveLength = rowSpacing * 4.5f, amplitude = curved ? rowSpacing * 0.22f : 0f;
            float wavePhase = Geometry.Hash01(seed, 5, 0) * waveLength;
            float Bend(float u) => amplitude * math.sin(2f * math.PI * (u + wavePhase) / waveLength);
            float crossSpacing = style == NeighbourhoodStyle.SquareGrid ? rowSpacing : rowSpacing * (traffic ? 3f : 2.5f);
            // Skewed: cross streets lean, so each one meets the next row further along.
            float lean = style == NeighbourhoodStyle.SkewedGrid ? 1f / math.tan(math.radians(SkewDegrees)) : 0f;

            // The rows' offsets across: even, or varied for the irregular grid.
            var offsets = new List<float>();
            for (float v = vMin + Geometry.Hash01(seed, 1, 0) * rowSpacing * 0.5f + rowSpacing * 0.25f; v < vMax; )
            {
                offsets.Add(v);
                float spacing = rowSpacing;
                if (style == NeighbourhoodStyle.IrregularGrid) spacing *= 0.8f + 0.5f * Geometry.Hash01(seed, 10, offsets.Count);
                v += spacing;
            }

            // Each row cut into the stretches that fit, as main streets; a broken grid loses a stretch here and there.
            var rows = new List<List<(int Street, float From)>>();
            for (int row = 0; row < offsets.Count; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                var stretches = new List<(int, float)>();
                foreach (var (from, points) in Clip(survey, along, across, offsets[row], uMin - Step, uMax + Step, Bend))
                {
                    var pieces = new List<(float, List<float2>)> { (from, points) };
                    if (broken) pieces = Break(points, from, crossSpacing, seed, row);
                    foreach (var (start, line) in pieces)
                    {
                        var street = Street(survey, request, line, RoadKind.Street);
                        if (street == null) continue;
                        stretches.Add((streets.Count, start));
                        streets.Add(street);
                    }
                }
                rows.Add(stretches);
            }

            // Cross streets between each pair of neighbouring rows.
            float phase = Geometry.Hash01(seed, 2, 0) * crossSpacing;
            float collector = (uMin + uMax) * 0.5f;
            // The through road's place: the middle, or the nearest place within 64 m where its straight join to the city's road
            // lands clear of the junctions already on it.
            if (traffic && offsets.Count > 0)
            {
                int last = offsets.Count - 1;
                for (int step = 0; step <= 8; step++)
                {
                    bool found = false;
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        float u = (uMin + uMax) * 0.5f + sign * step * StepPlanner.CellSize;
                        float uLast = u + (offsets[last] - offsets[0]) * lean;
                        if (Hit(request, along * u + across * offsets[0], -across, out _, out _, out _)
                            || Hit(request, along * uLast + across * offsets[last], across, out _, out _, out _)) { collector = u; found = true; break; }
                    }
                    if (found) break;
                }
            }
            var collectorLinks = new List<int>();
            // Diagonal avenues: chains of cross streets leaning across the rows between two of them, each on a line through a
            // centre. Two cross the whole diagonal grid; the radial grid's four run out from the corners of its middle block, so
            // no junction has more than four roads.
            var avenues = new List<(float U, float V, float Lean, int FromRow, int ToRow)>();
            if (offsets.Count > 1)
            {
                float middleU = (uMin + uMax) * 0.5f, middleV = offsets[offsets.Count / 2];
                if (style == NeighbourhoodStyle.DiagonalGrid)
                {
                    float shiftBy = (Geometry.Hash01(seed, 6, 0) - 0.5f) * crossSpacing * 0.4f;
                    foreach (float share in new[] { -1f / 6f, 1f / 6f })
                        avenues.Add((middleU + (uMax - uMin) * share + shiftBy, middleV, DiagonalLean, 0, rows.Count - 1));
                }
                if (style == NeighbourhoodStyle.RadialGrid)
                {
                    int middle = (offsets.Count - 1) / 2;
                    float centreV = (offsets[middle] + offsets[middle + 1]) * 0.5f;
                    avenues.Add((middleU, centreV, DiagonalLean, middle + 1, rows.Count - 1));
                    avenues.Add((middleU, centreV, -DiagonalLean, middle + 1, rows.Count - 1));
                    avenues.Add((middleU, centreV, DiagonalLean, 0, middle));
                    avenues.Add((middleU, centreV, -DiagonalLean, 0, middle));
                }
            }
            foreach (var (u, v, diagonal, fromRow, toRow) in avenues)
                for (int row = fromRow; row < toRow; row++)
                {
                    float at = u + (offsets[row] - v) * diagonal;
                    Cross(survey, request, streets, rows[row], rows[row + 1], along, at, at + (offsets[row + 1] - offsets[row]) * diagonal);
                }
            for (int row = 0; row + 1 < rows.Count; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                float shift = (offsets[row + 1] - offsets[row]) * lean;
                float offset = staggered && row % 2 == 1 ? crossSpacing * 0.5f : 0f;
                // The through road leans with the cross streets, so it runs on unbroken from row to row.
                float collectorHere = collector + (offsets[row] - offsets[0]) * lean;
                // Columns are counted from the first row and lean with a skewed grid, so crossroads line up from row to row; the
                // irregular grid's jitter is per column for crossroads (per column and row when staggered).
                float leanHere = (offsets[row] - offsets[0]) * lean;
                var positions = new List<(float U, bool Collector)>();
                // The columns that fall on this row once leaned: a tall skewed grid's columns drift a long way along the rows.
                int firstColumn = (int)math.floor((-phase - offset - leanHere) / crossSpacing) - 1;
                for (int column = firstColumn; uMin + phase + column * crossSpacing + offset + leanHere < uMax + crossSpacing; column++)
                {
                    float at = uMin + phase + column * crossSpacing + offset + leanHere;
                    if (style == NeighbourhoodStyle.IrregularGrid)
                        at += (Geometry.Hash01(seed, staggered ? 20 + row : 20, column + 1000) - 0.5f) * crossSpacing * 0.5f;
                    if (broken && Geometry.Hash01(seed, 40 + row, column + 1000) < 0.3f) continue;
                    // Traffic-optimised: locals keep clear of the through road, which lines up across every row.
                    if (traffic && math.abs(at - collectorHere) < crossSpacing * 0.35f) continue;
                    bool nearAvenue = false;
                    foreach (var (u, v, diagonal, fromRow, toRow) in avenues)
                    {
                        if (row < fromRow || row >= toRow) continue;
                        float low = u + (offsets[row] - v) * diagonal, high = u + (offsets[row + 1] - v) * diagonal;
                        nearAvenue |= math.abs(at - low) < crossSpacing * 0.35f || math.abs(at + shift - high) < crossSpacing * 0.35f
                            || (at - low) * (at + shift - high) < 0f;
                    }
                    if (nearAvenue) continue;
                    positions.Add((at, false));
                }
                if (traffic) positions.Add((collectorHere, true));
                foreach (var (u, isCollector) in positions)
                {
                    int link = Cross(survey, request, streets, rows[row], rows[row + 1], along, u, u + shift);
                    if (link >= 0 && isCollector) collectorLinks.Add(link);
                }
            }

            if (traffic)
            {
                // The through road runs on past the outermost rows to the edge of the area, and on to the city's road.
                foreach (int link in collectorLinks) through.Add(link);
                foreach (int row in new[] { 0, rows.Count - 1 })
                {
                    if (row < 0 || row >= rows.Count) continue;
                    int stub = Stub(survey, request, streets, rows[row], along, row == 0 ? -across : across, collector + (offsets[row] - offsets[0]) * lean);
                    if (stub < 0) continue;
                    var end = streets[stub];
                    bool reached = Reach(survey, request, streets, stub, end.Points.Count - 1, row == 0 ? -across : across);
                    // An end of the through road that meets no city road would be a road to nowhere, unless no city road is near
                    // at all; then both stay, for a winding connection to find.
                    if (reached) { joined = true; through.Add(stub); }
                    else if ((request.ExistingRoads?.Length ?? 0) > 0) end.Broken = true;
                    else through.Add(stub);
                }
                return streets;
            }

            // Like a real grid, every street that runs out towards the city's road meets it in a T, and where the outermost
            // street runs alongside it, short straight streets join the two at the cross streets' spacing.
            int mainStreets = streets.Count;
            for (int index = 0; index < mainStreets; index++)
            {
                var street = streets[index];
                if (street.Kind != RoadKind.Street || street.Start.Joined) continue;
                int last = street.Points.Count - 1;
                joined |= Reach(survey, request, streets, index, 0, math.normalizesafe(street.Points[0] - street.Points[1]));
                joined |= Reach(survey, request, streets, index, last, math.normalizesafe(street.Points[last] - street.Points[last - 1]));
            }
            foreach (int row in new[] { 0, rows.Count - 1 })
            {
                if (row < 0 || row >= rows.Count) continue;
                float2 outward = row == 0 ? -across : across;
                float offset = staggered && (row == 0 ? 1 : row + 1) % 2 == 1 ? crossSpacing * 0.5f : 0f;
                for (float u = uMin + phase + offset - crossSpacing; u < uMax + crossSpacing; u += crossSpacing)
                    if (Find(streets, rows[row], u, out int host, out int point)) joined |= Reach(survey, request, streets, host, point, outward);
            }
            return streets;
        }

        /// <summary>
        /// A straight street from a point of the grid to the city's road, where one lies within reach ahead, meets it at 60°
        /// or more and nothing is in the way; false where none does.
        /// </summary>
        internal static bool Reach(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, int host, int point, float2 direction)
        {
            float2 origin = streets[host].Points[point];
            if (!Hit(request, origin, direction, out float3 hit, out ExistingRoad target, out float targetT)) return false;
            var line = Line(origin, hit.xz);
            // Clear of buildings and water all the way, and of the area's own streets until it nears the city's road.
            for (int index = 1; index < line.Count - 1; index++)
            {
                if (request.Blocked != null && request.Blocked(line[index])) return false;
                if (math.distance(line[index], hit.xz) > target.HalfWidth + request.RoadHalfWidth + 8f && survey.ConnectionRoomAt(line[index]) < 0f
                    && survey.StreetRoomAt(line[index]) < 0f) return false;
            }
            // An even grade from the grid to the city's road: the landings a longer road gets don't fit in a short join on a slope.
            float from = streets[host].Heights[point], length = math.distance(origin, hit.xz);
            if (math.abs(hit.y - from) > request.MaxRoadGrade * length) return false;
            var heights = new float[line.Count];
            for (int index = 0; index < line.Count; index++) heights[index] = math.lerp(from, hit.y, index / (float)(line.Count - 1));
            var street = new Street(RoadKind.Connection, line, heights)
            {
                Start = new Attachment { Street = host, Point = point },
                End = new Attachment { Street = -1, Point = -1, Existing = true, ExistingId = target.Id, ExistingT = targetT },
            };
            int count = streets[host].Points.Count;
            if (point > 0 && point < count - 1) streets[host].Junctions.Add(point);
            streets.Add(street);
            return true;
        }

        /// <summary>
        /// Where a straight line from a point meets the city's road within reach: square enough for a clean junction (60° or
        /// more) and clear of the junctions at the ends of the road's edges.
        /// </summary>
        private static bool Hit(NeighbourhoodRequest request, float2 origin, float2 direction, out float3 hit, out ExistingRoad target, out float targetT)
        {
            hit = default;
            target = null;
            targetT = 0f;
            if (math.lengthsq(direction) < 0.5f) return false;
            float best = ReachLength;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int index = 1; index < (road?.Points?.Length ?? 0); index++)
                {
                    float3 a = road.Points[index - 1], b = road.Points[index];
                    float2 edge = b.xz - a.xz;
                    float cross = direction.x * edge.y - direction.y * edge.x;
                    if (math.abs(cross) < 1e-4f) continue;
                    float2 offset = a.xz - origin;
                    float along = (offset.x * edge.y - offset.y * edge.x) / cross;
                    float share = (offset.x * direction.y - offset.y * direction.x) / cross;
                    if (along <= Step || along >= best || share < 0f || share > 1f) continue;
                    // Square enough to the city's road for a clean junction, and clear of the junctions at its edge's ends.
                    if (math.abs(math.dot(direction, math.normalizesafe(edge))) > 0.5f || !road.ClearOfEnds(index - 1, share)) continue;
                    best = along;
                    hit = math.lerp(a, b, share);
                    target = road;
                    targetT = (index - 1 + share) / (road.Points.Length - 1);
                }
            return target != null;
        }

        /// <summary>How far past the grid a street may run on to meet the city's road.</summary>
        private const float ReachLength = 160f;

        /// <summary>The city's nearest road's direction within 400 m of the area, else the area's longest side.</summary>
        internal static float2 Facing(SiteSurvey survey, NeighbourhoodRequest request)
        {
            float best = 400f;
            float2 facing = float2.zero;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < (road?.Points?.Length ?? 0); point++)
                {
                    float2 start = road.Points[point - 1].xz, end = road.Points[point].xz, middle = (start + end) * 0.5f;
                    if (math.distance(start, end) < 1f) continue;
                    float distance = Poly.Contains(survey.Boundary, middle) ? 0f : Poly.DistanceToBoundary(survey.Boundary, middle);
                    if (distance < best) { best = distance; facing = math.normalize(end - start); }
                }
            if (math.lengthsq(facing) > 0f) return facing;
            float longest = 0f;
            for (int corner = 0; corner < survey.Boundary.Length; corner++)
            {
                float2 start = survey.Boundary[corner], end = survey.Boundary[(corner + 1) % survey.Boundary.Length];
                float length = math.distance(start, end);
                if (length > longest) { longest = length; facing = (end - start) / length; }
            }
            return math.lengthsq(facing) > 0f ? facing : new float2(1f, 0f);
        }

        /// <summary>The stretches of a straight line where a street fits, each with where along the line it starts.</summary>
        private static List<(float From, List<float2> Points)> Clip(SiteSurvey survey, float2 along, float2 across, float v, float from, float to,
            Func<float, float> bend)
        {
            var stretches = new List<(float, List<float2>)>();
            List<float2> current = null;
            float start = 0f;
            for (float u = from; u <= to; u += Step)
            {
                float2 point = along * u + across * (v + bend(u));
                if (survey.StreetRoomAt(point) >= 0f)
                {
                    if (current == null) { current = new List<float2>(); start = u; }
                    current.Add(point);
                }
                else if (current != null) { stretches.Add((start, current)); current = null; }
            }
            if (current != null) stretches.Add((start, current));
            stretches.RemoveAll(stretch => (stretch.Item2.Count - 1) * Step < ContourStreets.ShortestStreet);
            return stretches;
        }

        /// <summary>The broken grid: a long stretch loses a gap about two-thirds of a block long, at a seeded place.</summary>
        private static List<(float From, List<float2> Points)> Break(List<float2> points, float from, float crossSpacing, uint seed, int row)
        {
            var pieces = new List<(float, List<float2>)>();
            float length = (points.Count - 1) * Step;
            if (length < crossSpacing * 3f || Geometry.Hash01(seed, 60, row) < 0.4f) { pieces.Add((from, points)); return pieces; }
            int gap = (int)(crossSpacing * 0.65f / Step);
            // Anywhere from a sixth to five-sixths along, so the gaps of neighbouring rows don't line up into one empty belt.
            int cut = (int)((0.17f + 0.66f * Geometry.Hash01(seed, 61 + row * 7, row)) * points.Count);
            pieces.Add((from, points.GetRange(0, cut)));
            if (cut + gap < points.Count) pieces.Add((from + (cut + gap) * Step, points.GetRange(cut + gap, points.Count - cut - gap)));
            pieces.RemoveAll(piece => (piece.Item2.Count - 1) * Step < ContourStreets.ShortestStreet);
            return pieces;
        }

        /// <summary>A street on the ground's line, or null where the ground is too steep for the grade and the earthwork limit.</summary>
        internal static Street Street(SiteSurvey survey, NeighbourhoodRequest request, List<float2> points, RoadKind kind,
            float? startHeight = null, float? endHeight = null)
        {
            var heights = RoadProfile.Fit(points, survey.GroundAt, request.MaxRoadGrade, startHeight, endHeight);
            if (heights == null) return null;
            for (int index = 0; index < points.Count; index++)
                if (math.abs(heights[index] - survey.GroundAt(points[index])) > request.EarthworkLimit) return null;
            return new Street(kind, points, heights);
        }

        /// <summary>A cross street from one row to the next, at a place along each; −1 where it can't go.</summary>
        private static int Cross(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, List<(int Street, float From)> lower,
            List<(int Street, float From)> upper, float2 along, float uLower, float uUpper)
        {
            if (!Find(streets, lower, uLower, out int a, out int pointA) || !Find(streets, upper, uUpper, out int b, out int pointB)) return -1;
            return Join(survey, request, streets, a, pointA, b, pointB);
        }

        /// <summary>A straight cross street between points of two streets; −1 where it can't go.</summary>
        internal static int Join(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, int a, int pointA, int b, int pointB)
        {
            var line = Line(streets[a].Points[pointA], streets[b].Points[pointB]);
            for (int index = 1; index < line.Count - 1; index++) if (survey.StreetRoomAt(line[index]) < 0f) return -1;
            var street = Street(survey, request, line, RoadKind.Link, streets[a].Heights[pointA], streets[b].Heights[pointB]);
            if (street == null) return -1;
            street.Start = new Attachment { Street = a, Point = pointA };
            street.End = new Attachment { Street = b, Point = pointB };
            streets[a].Junctions.Add(pointA);
            streets[b].Junctions.Add(pointB);
            streets.Add(street);
            return streets.Count - 1;
        }

        /// <summary>The through road's last stretch: from the outermost row out to the edge of the area, a main street.</summary>
        private static int Stub(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, List<(int Street, float From)> row,
            float2 along, float2 outward, float u)
        {
            return Find(streets, row, u, out int host, out int point) ? Spoke(survey, request, streets, host, point, outward) : -1;
        }

        /// <summary>A main street from a point of another straight out to the edge of the area (at most <paramref name="longest"/>); −1 where under 32 m fits.</summary>
        internal static int Spoke(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, int host, int point, float2 outward,
            float longest = 400f)
        {
            var start = streets[host].Points[point];
            var line = new List<float2> { start };
            for (float travelled = Step; travelled < longest; travelled += Step)
            {
                var next = start + outward * travelled;
                if (survey.StreetRoomAt(next) < 0f) break;
                line.Add(next);
            }
            if ((line.Count - 1) * Step < 32f) return -1;
            var street = Street(survey, request, line, RoadKind.Street, streets[host].Heights[point]);
            if (street == null) return -1;
            street.Start = new Attachment { Street = host, Point = point };
            streets[host].Junctions.Add(point);
            streets.Add(street);
            return streets.Count - 1;
        }

        /// <summary>The row stretch and point nearest a place along it, clear of its ends and of other junctions on it.</summary>
        private static bool Find(List<Street> streets, List<(int Street, float From)> row, float u, out int street, out int point)
        {
            street = point = -1;
            foreach (var (index, from) in row)
            {
                var candidate = streets[index];
                int at = (int)math.round((u - from) / Step);
                if (at < 0 || at >= candidate.Points.Count) continue;
                float along = at * Step, length = (candidate.Points.Count - 1) * Step;
                if (along < EndClearance || length - along < EndClearance) return false;
                foreach (int junction in candidate.Junctions)
                    if (junction != at && candidate.Between(junction, at) < RoadLinks.JunctionSpacing) return false;
                street = index;
                point = at;
                return true;
            }
            return false;
        }

        internal static List<float2> Line(float2 start, float2 end)
        {
            int steps = math.max(1, (int)math.ceil(math.distance(start, end) / Step));
            var line = new List<float2>(steps + 1);
            for (int index = 0; index <= steps; index++) line.Add(math.lerp(start, end, index / (float)steps));
            return line;
        }
    }
}
