// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood.Layouts
{
    /// <summary>
    /// The layouts led by the land: a main road along a ridge's crest or a valley's floor (the contour streets fill the slopes
    /// around it), streets running parallel to a shore, and one road winding back and forth across the area.
    /// </summary>
    internal static class TerrainLayout
    {
        private const float Step = ContourStreets.Spacing;
        // A cross street keeps clear of a street's open end, as a grid's does.
        private const float EndClearance = 26f;
        // Curves the main roads keep to, comfortably above the smallest the rules allow.
        private const float GentlestTurn = 45f;

        public static bool Handles(NeighbourhoodStyle style) => style == NeighbourhoodStyle.Coastal || style == NeighbourhoodStyle.Winding;

        /// <summary>The streets of a coastal or winding layout; <paramref name="joined"/> says whether any meets the city's road straight on.</summary>
        public static List<Street> Plan(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation) =>
            request.Style == NeighbourhoodStyle.Coastal ? Coast(survey, request, out joined, cancellation) : Wind(survey, request, out joined, cancellation);

        /// <summary>
        /// The main road of a ridge or valley layout: along the crest, or along the valley floor, one road on each bank where
        /// water fills the floor.
        /// </summary>
        public static List<Street> Spines(SiteSurvey survey, NeighbourhoodRequest request, bool crest)
        {
            var result = new List<Street>();
            // The way the ridge or valley runs: square to the ground's steepest direction, averaged over the area.
            float xx = 0f, yy = 0f, xy = 0f;
            for (int index = 0; index < survey.Count; index++)
            {
                if (!survey.Inside[index]) continue;
                float2 position = survey.Position(index), dx = new float2(survey.Cell, 0f), dy = new float2(0f, survey.Cell);
                float2 gradient = new float2(survey.SmoothAt(position + dx) - survey.SmoothAt(position - dx), survey.SmoothAt(position + dy) - survey.SmoothAt(position - dy));
                xx += gradient.x * gradient.x; yy += gradient.y * gradient.y; xy += gradient.x * gradient.y;
            }
            float steepest = 0.5f * math.atan2(2f * xy, xx - yy);
            float2 across = new float2(math.cos(steepest), math.sin(steepest)), along = Geometry.Left(across);
            float sMin = float.MaxValue, sMax = float.MinValue, rMin = float.MaxValue, rMax = float.MinValue;
            foreach (var corner in survey.Boundary)
            {
                float s = math.dot(corner, along), r = math.dot(corner, across);
                sMin = math.min(sMin, s); sMax = math.max(sMax, s); rMin = math.min(rMin, r); rMax = math.max(rMax, r);
            }

            // Every 16 m along, the highest (or lowest) point across; where water or buildings sit there, the nearest room on
            // each side of it.
            var sections = new List<(float S, float Best, float Left, float Right, bool Blocked)>();
            for (float s = sMin + 8f; s <= sMax - 8f; s += 16f)
            {
                float best = float.NaN, height = crest ? float.MinValue : float.MaxValue;
                for (float r = rMin; r <= rMax; r += Step)
                {
                    float2 point = along * s + across * r;
                    if (!Poly.Contains(survey.Boundary, point)) continue;
                    float here = survey.SmoothAt(point);
                    if (crest ? here > height : here < height) { height = here; best = r; }
                }
                if (float.IsNaN(best)) continue;
                bool Room(float r) => survey.StreetRoomAt(along * s + across * r) >= 0f;
                float left = float.NaN, right = float.NaN;
                for (float r = best; r >= rMin; r -= Step) if (Room(r)) { left = r; break; }
                for (float r = best; r <= rMax; r += Step) if (Room(r)) { right = r; break; }
                bool blocked = !Room(best);
                if (float.IsNaN(left) && float.IsNaN(right)) continue;
                sections.Add((s, best, float.IsNaN(left) ? right : left, float.IsNaN(right) ? left : right, blocked));
            }
            if (sections.Count < 4) return result;
            int blockedCount = 0;
            foreach (var section in sections) if (section.Blocked) blockedCount++;

            // Mostly blocked (a river along the floor): a road on each bank, kept at least half a block from the middle.
            var lines = new List<List<float>>();
            float spread = request.BlockDepth * 0.5f;
            if (blockedCount > sections.Count / 4)
            {
                var leftBank = new List<float>();
                var rightBank = new List<float>();
                foreach (var section in sections)
                {
                    leftBank.Add(math.min(section.Left, section.Best - spread));
                    rightBank.Add(math.max(section.Right, section.Best + spread));
                }
                lines.Add(leftBank);
                lines.Add(rightBank);
            }
            else
            {
                var single = new List<float>();
                foreach (var section in sections)
                    single.Add(!section.Blocked ? section.Best : math.abs(section.Left - section.Best) <= math.abs(section.Right - section.Best) ? section.Left : section.Right);
                lines.Add(single);
            }

            foreach (var offsets in lines)
            {
                var smoothed = Average(offsets, 3, 2);
                var line = new List<float2>();
                for (int section = 0; section < sections.Count; section++) line.Add(along * sections[section].S + across * smoothed[section]);
                foreach (var run in Runs(survey, Gentle(Geometry.Resample(Geometry.Chaikin(line, 2), Step))))
                {
                    var street = Level(survey, request, run);
                    if (street != null) result.Add(street);
                }
            }
            return result;
        }

        /// <summary>
        /// A main road: a main street (so the contour links look both ways from it), on the eased heights a contour street gets
        /// where the ground allows, else on any grade up to the max; null if neither fits.
        /// </summary>
        private static Street Level(SiteSurvey survey, NeighbourhoodRequest request, List<float2> points)
        {
            var heights = RoadProfile.Fit(points, survey.GroundAt, RoadProfile.LandingGrade, null, null, 5);
            bool fits = heights != null;
            for (int index = 0; fits && index < points.Count; index++) fits = math.abs(heights[index] - survey.GroundAt(points[index])) <= request.EarthworkLimit;
            return fits ? new Street(RoadKind.Street, points, heights) : GridLayout.Street(survey, request, points, RoadKind.Street);
        }

        /// <summary>
        /// Streets parallel to the shore, one block apart, joined by streets square to them. With no water in or near the area,
        /// they line up with the city's road instead (or with the area's longest side).
        /// </summary>
        private static List<Street> Coast(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation)
        {
            var streets = new List<Street>();
            joined = false;
            float depth = request.BlockDepth;
            var seeds = new bool[survey.Count];
            bool any = false;
            if (request.Water != null)
                for (int index = 0; index < survey.Count; index++)
                    if (request.Water(survey.Position(index))) seeds[index] = any = true;
            if (!any)
                foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                    foreach (var point in road?.Points ?? new float3[0])
                        any |= Mark(survey, seeds, point.xz);
            if (!any)
            {
                int longest = 0;
                var boundary = survey.Boundary;
                for (int corner = 1; corner < boundary.Length; corner++)
                    if (math.distance(boundary[corner], boundary[(corner + 1) % boundary.Length]) > math.distance(boundary[longest], boundary[(longest + 1) % boundary.Length]))
                        longest = corner;
                var line = Geometry.Resample(new List<float2> { boundary[longest], boundary[(longest + 1) % boundary.Length] }, Step);
                foreach (var point in line) Mark(survey, seeds, point);
            }
            var distance = Geometry.Chamfer(seeds, survey.Columns, survey.Rows, survey.Cell);
            float farthest = 0f;
            for (int index = 0; index < survey.Count; index++) if (survey.Inside[index]) farthest = math.max(farthest, distance[index]);

            float2 Inland(float2 point)
            {
                float cell = survey.Cell;
                return math.normalizesafe(new float2(survey.Sample(distance, point + new float2(cell, 0f)) - survey.Sample(distance, point - new float2(cell, 0f)),
                    survey.Sample(distance, point + new float2(0f, cell)) - survey.Sample(distance, point - new float2(0f, cell))));
            }

            // Rows from 0.6 of a block off the shore, then every block inland, so the first has lots on its shore side too.
            var rows = new List<List<int>>();
            for (float level = depth * 0.6f; level < farthest; level += depth)
            {
                cancellation.ThrowIfCancellationRequested();
                var row = new List<int>();
                foreach (var (traced, closed) in ContourStreets.Trace(survey, level, distance))
                {
                    // A distance contour wobbles a little from grid point to grid point: eased out, or the game draws the wobble.
                    var line = Gentle(Ease(Geometry.Resample(Geometry.Chaikin(traced, 3), Step), 4));
                    if (closed && line.Count > 1 && math.distance(line[0], line[line.Count - 1]) < Step * 1.5f) line.RemoveAt(line.Count - 1);
                    bool whole = closed;
                    foreach (var point in line) whole &= survey.StreetRoomAt(point) >= 0f;
                    if (whole && line.Count * Step > ContourStreets.ShortestStreet)
                    {
                        var ring = RadialLayout.Closed(survey, request, line);
                        if (ring != null) { row.Add(streets.Count); streets.Add(ring); }
                        continue;
                    }
                    if (closed)
                    {
                        // Start from a point that doesn't fit, so a stretch running across the trace's start stays one street
                        // instead of two whose ends nearly touch.
                        int cut = line.FindIndex(point => survey.StreetRoomAt(point) < 0f);
                        if (cut > 0) line = line.GetRange(cut, line.Count - cut).Concat(line.GetRange(0, cut)).ToList();
                    }
                    foreach (var run in Runs(survey, line))
                    {
                        var street = GridLayout.Street(survey, request, run, RoadKind.Street);
                        if (street == null) continue;
                        row.Add(streets.Count);
                        streets.Add(street);
                    }
                }
                rows.Add(row);
            }

            // Cross streets every two and a half blocks, offset like brickwork from row to row, each square to both rows.
            float spacing = depth * 2.5f;
            for (int row = 0; row + 1 < rows.Count; row++)
                foreach (int a in rows[row])
                {
                    cancellation.ThrowIfCancellationRequested();
                    var street = streets[a];
                    float length = street.Length;
                    for (float along = EndClearance + spacing * (row % 2 == 1 ? 0.8f : 0.3f); along < length - EndClearance; along += spacing)
                    {
                        int i = At(street, along);
                        if (!Free(street, i)) continue;
                        float2 from = street.Points[i], target = from + Inland(from) * depth;
                        int best = -1, bestPoint = -1;
                        float nearest = depth * 0.45f;
                        foreach (int b in rows[row + 1])
                            for (int point = 0; point < streets[b].Points.Count; point++)
                            {
                                float gap = math.distance(streets[b].Points[point], target);
                                if (gap < nearest) { nearest = gap; best = b; bestPoint = point; }
                            }
                        if (best < 0 || !Free(streets[best], bestPoint)) continue;
                        float2 direction = math.normalizesafe(streets[best].Points[bestPoint] - from);
                        if (math.abs(math.dot(direction, street.Tangent(i))) > 0.45f || math.abs(math.dot(direction, streets[best].Tangent(bestPoint))) > 0.45f) continue;
                        GridLayout.Join(survey, request, streets, a, i, best, bestPoint);
                    }
                }

            // Open ends run on to the city's road, and so do streets square to the first and last rows: a coastal road often
            // runs along the shore, and the city usually lies inland.
            joined = ReachEnds(survey, request, streets);
            for (int edge = 0; edge < 2 && rows.Count > 0; edge++)
                foreach (int index in edge == 0 ? rows[0] : rows[rows.Count - 1])
                {
                    var street = streets[index];
                    for (float along = EndClearance + spacing * 0.55f; along < street.Length - EndClearance; along += spacing)
                    {
                        int i = At(street, along);
                        if (!Free(street, i)) continue;
                        float2 outward = Inland(street.Points[i]) * (edge == 0 ? -1f : 1f);
                        joined |= GridLayout.Reach(survey, request, streets, index, i, outward);
                    }
                }
            return streets;
        }

        /// <summary>
        /// One road winding back and forth across the area: rows a block apart, each a gentle wave, turned at the ends in half
        /// circles at the waves' crests, with a cross street every four blocks so no house is far from the way out.
        /// </summary>
        private static List<Street> Wind(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation)
        {
            var streets = new List<Street>();
            joined = false;
            uint seed = request.Seed;
            float depth = request.BlockDepth, radius = depth * 0.5f;
            float2 along = GridLayout.Facing(survey, request), across = Geometry.Left(along);
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var corner in survey.Boundary)
            {
                float u = math.dot(corner, along), v = math.dot(corner, across);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMin = math.min(vMin, v); vMax = math.max(vMax, v);
            }
            float waveLength = depth * 5f, amplitude = depth * 0.18f, phase = Geometry.Hash01(seed, 5, 0) * waveLength;
            float Bend(float u) => amplitude * math.sin(2f * math.PI * (u + phase) / waveLength);
            // The waves' crests and troughs, where a row runs level across and a half circle meets it smoothly.
            float Crest(float u, bool up)
            {
                float first = waveLength * 0.25f - phase, half = waveLength * 0.5f;
                float n = up ? math.floor((u - first) / half) : math.ceil((u - first) / half);
                return first + n * half;
            }

            // Each row's longest stretch that fits.
            var offsets = new List<float>();
            for (float v = vMin + Geometry.Hash01(seed, 1, 0) * depth * 0.5f + depth * 0.25f; v < vMax; v += depth) offsets.Add(v);
            var spans = new List<(float From, float To)>();
            foreach (float v in offsets)
            {
                float from = float.NaN, to = float.NaN, best = 0f;
                foreach (var (start, points) in GridLayout.Clip(survey, along, across, v, uMin - Step, uMax + Step, Bend))
                    if ((points.Count - 1) * Step > best) { best = (points.Count - 1) * Step; from = start; to = start + (points.Count - 1) * Step; }
                spans.Add((from, to));
            }

            float2 Point(int row, float u) => along * u + across * (offsets[row] + Bend(u));
            bool Clear(List<float2> points)
            {
                foreach (var point in points) if (survey.StreetRoomAt(point) < 0f) return false;
                return true;
            }
            List<float2> Turn(int row, float u, bool right)
            {
                // A half circle from this row to the next, bulging out past the turn.
                var arc = new List<float2>();
                float2 centre = along * u + across * ((offsets[row] + offsets[row + 1]) * 0.5f + Bend(u));
                int count = math.max(6, (int)math.ceil(math.PI * radius / Step));
                for (int index = 1; index < count; index++)
                {
                    float angle = math.PI * index / count;
                    arc.Add(centre + along * (right ? 1f : -1f) * radius * math.sin(angle) - across * radius * math.cos(angle));
                }
                return arc;
            }

            // Snakes: runs of rows joined at alternate ends; a row that can't turn into the next ends its snake there. The snake's
            // heights are fitted along its whole length, as one road's would be, and then it is cut: each row a street of its
            // own and each turn a link between two, so a row the rules drop takes only its turns with it.
            var segments = new List<(int Snake, int Street, int Row, float U, int Direction, int Count)>();
            int snakes = 0;
            for (int row = 0; row < offsets.Count; )
            {
                cancellation.ThrowIfCancellationRequested();
                if (float.IsNaN(spans[row].From)) { row++; continue; }
                var points = new List<float2>();
                // Each row's first and last point in the snake, where it starts along and which way it runs.
                var pieces = new List<(int Row, int First, int Last, float U, int Direction)>();
                bool right = true;
                float entry = spans[row].From;
                int current = row;
                while (true)
                {
                    float exit = right ? spans[current].To : spans[current].From;
                    float turn = float.NaN;
                    List<float2> arc = null;
                    if (current + 1 < offsets.Count && !float.IsNaN(spans[current + 1].From))
                    {
                        float limit = right ? math.min(spans[current].To, spans[current + 1].To) - radius - Step
                            : math.max(spans[current].From, spans[current + 1].From) + radius + Step;
                        float candidate = Crest(limit, right);
                        bool within = candidate >= math.max(spans[current].From, spans[current + 1].From) + radius
                            && candidate <= math.min(spans[current].To, spans[current + 1].To) - radius;
                        if (within && (right ? candidate - entry > radius * 2f : entry - candidate > radius * 2f))
                        {
                            arc = Turn(current, candidate, right);
                            if (Clear(arc)) turn = candidate;
                        }
                    }
                    float end = float.IsNaN(turn) ? exit : turn;
                    int first = points.Count;
                    float direction = right ? 1f : -1f;
                    for (float u = entry; direction * (end - u) > 0.5f; u += direction * Step) points.Add(Point(current, u));
                    points.Add(Point(current, end));
                    pieces.Add((current, first, points.Count - 1, entry, right ? 1 : -1));
                    if (float.IsNaN(turn)) break;
                    points.AddRange(arc);
                    entry = turn;
                    right = !right;
                    current++;
                }
                row = current + 1;
                if ((points.Count - 1) * Step < ContourStreets.ShortestStreet) continue;
                // One fit along the whole snake; where that can't keep to the grade and the earth limit, each row on its own
                // (and each turn between its rows' ends).
                var whole = GridLayout.Street(survey, request, points, RoadKind.Street);
                Street Cut(int from, int to, RoadKind kind, float? startHeight = null, float? endHeight = null)
                {
                    var slice = points.GetRange(from, to - from + 1);
                    if (whole == null) return GridLayout.Street(survey, request, slice, kind, startHeight, endHeight);
                    var heights = new float[slice.Count];
                    System.Array.Copy(whole.Heights, from, heights, 0, slice.Count);
                    return new Street(kind, slice, heights);
                }
                var rowStreets = new int[pieces.Count];
                for (int piece = 0; piece < pieces.Count; piece++)
                {
                    var (pieceRow, first, last, u, direction) = pieces[piece];
                    rowStreets[piece] = -1;
                    if ((last - first) * Step < 32f) continue;
                    var built = Cut(first, last, RoadKind.Street);
                    if (built == null) continue;
                    rowStreets[piece] = streets.Count;
                    segments.Add((snakes, streets.Count, pieceRow, u, direction, last - first + 1));
                    streets.Add(built);
                }
                for (int piece = 0; piece + 1 < pieces.Count; piece++)
                {
                    int from = rowStreets[piece], to = rowStreets[piece + 1];
                    if (from < 0 || to < 0) continue;
                    var a = streets[from];
                    var b = streets[to];
                    var link = Cut(pieces[piece].Last, pieces[piece + 1].First, RoadKind.Link, a.Heights[a.Heights.Length - 1], b.Heights[0]);
                    if (link == null) continue;
                    int index = streets.Count;
                    link.Start = new Attachment { Street = from, Point = a.Points.Count - 1 };
                    link.End = new Attachment { Street = to, Point = 0 };
                    a.End = new Attachment { Street = index, Point = 0 };
                    b.Start = new Attachment { Street = index, Point = link.Points.Count - 1 };
                    streets.Add(link);
                }
                snakes++;
            }

            // A cross street every four blocks between neighbouring rows of a snake, offset from row to row, clear of the turns.
            float spacing = depth * 4f;
            for (int index = 0; index + 1 < segments.Count; index++)
            {
                var low = segments[index];
                var high = segments[index + 1];
                if (low.Snake != high.Snake || high.Row != low.Row + 1) continue;
                float lowEnd = low.U + low.Direction * (low.Count - 1) * Step;
                float first = math.min(low.U, lowEnd) + radius + EndClearance, last = math.max(low.U, lowEnd) - radius - EndClearance;
                for (float u = first + (low.Row % 2 == 1 ? spacing * 0.5f : spacing * 0.25f); u < last; u += spacing)
                {
                    int a = (int)math.round((u - low.U) * low.Direction / Step);
                    int b = (int)math.round((u - high.U) * high.Direction / Step);
                    if (a <= 0 || a >= low.Count - 1 || b <= 0 || b >= high.Count - 1) continue;
                    if (!Free(streets[low.Street], a) || !Free(streets[high.Street], b)) continue;
                    GridLayout.Join(survey, request, streets, low.Street, a, high.Street, b);
                }
            }

            // The snakes' ends, and streets square to the first and last rows, run on to the city's road.
            joined = ReachEnds(survey, request, streets);
            foreach (var segment in segments)
                // The first row joins outwards one way and the last the other; a single row, both.
                foreach (var (edge, outward) in new[] { (segment.Row == 0, -across), (segment.Row == offsets.Count - 1, across) })
                {
                    if (!edge) continue;
                    for (int point = 20; point < segment.Count - 20; point += (int)(spacing / Step))
                        if (Free(streets[segment.Street], point))
                            joined |= GridLayout.Reach(survey, request, streets, segment.Street, point, outward);
                }
            return streets;
        }

        /// <summary>Every open end of a main street runs on to the city's road where it meets it straight ahead.</summary>
        private static bool ReachEnds(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets)
        {
            bool joined = false;
            int made = streets.Count;
            for (int index = 0; index < made; index++)
            {
                var street = streets[index];
                if (street.Kind != RoadKind.Street || street.Closed) continue;
                int last = street.Points.Count - 1;
                if (!street.Start.Joined) joined |= GridLayout.Reach(survey, request, streets, index, 0, math.normalizesafe(street.Points[0] - street.Points[1]));
                if (!street.End.Joined) joined |= GridLayout.Reach(survey, request, streets, index, last, math.normalizesafe(street.Points[last] - street.Points[last - 1]));
            }
            return joined;
        }

        /// <summary>Marks the survey's node nearest a point; false if the point is off the grid.</summary>
        private static bool Mark(SiteSurvey survey, bool[] seeds, float2 point)
        {
            int column = (int)math.round((point.x - survey.Origin.x) / survey.Cell), row = (int)math.round((point.y - survey.Origin.y) / survey.Cell);
            if (column < 0 || row < 0 || column >= survey.Columns || row >= survey.Rows) return false;
            seeds[row * survey.Columns + column] = true;
            return true;
        }

        /// <summary>The point at a distance along a street.</summary>
        private static int At(Street street, float along)
        {
            var stations = street.Stations;
            int point = 0;
            while (point < stations.Length - 1 && stations[point] < along) point++;
            return point;
        }

        /// <summary>Whether a point of a street may take a junction: clear of its open ends and 40 m from others.</summary>
        private static bool Free(Street street, int point)
        {
            if (!street.Closed)
            {
                float along = street.Stations[point], length = street.Stations[street.Stations.Length - 1];
                if (along < EndClearance || length - along < EndClearance) return false;
            }
            foreach (int junction in street.Junctions)
                if (junction != point && street.Between(junction, point) < GridLayout.JunctionGap) return false;
            return true;
        }

        /// <summary>The stretches of a line where a street fits, each long enough to be one.</summary>
        private static List<List<float2>> Runs(SiteSurvey survey, List<float2> line)
        {
            var runs = new List<List<float2>>();
            List<float2> current = null;
            foreach (var point in line)
            {
                if (survey.StreetRoomAt(point) >= 0f) (current ?? (current = new List<float2>())).Add(point);
                else if (current != null) { runs.Add(current); current = null; }
            }
            if (current != null) runs.Add(current);
            runs.RemoveAll(run => (run.Count - 1) * Step < ContourStreets.ShortestStreet);
            return runs;
        }

        /// <summary>A line eased until its tightest curve is gentle, a few times at most.</summary>
        private static List<float2> Gentle(List<float2> line)
        {
            for (int pass = 0; pass < 8 && line.Count >= 7 && Geometry.TightestRadius(line, 12f, out _) < GentlestTurn; pass++)
                line = Ease(line, 1);
            return line;
        }

        /// <summary>A line averaged over its neighbours 12 m each way, its ends kept, and spaced evenly again.</summary>
        private static List<float2> Ease(List<float2> line, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                var eased = new List<float2>(line.Count);
                for (int index = 0; index < line.Count; index++)
                {
                    float2 sum = float2.zero;
                    int count = 0;
                    for (int offset = -3; offset <= 3; offset++)
                    {
                        int at = index + offset;
                        if (at < 0 || at >= line.Count) continue;
                        sum += line[at];
                        count++;
                    }
                    eased.Add(index == 0 || index == line.Count - 1 ? line[index] : sum / count);
                }
                line = Geometry.Resample(eased, Step);
            }
            return line;
        }

        /// <summary>A moving average over <paramref name="half"/> neighbours each side, several passes.</summary>
        private static List<float> Average(List<float> values, int half, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new List<float>(values.Count);
                for (int index = 0; index < values.Count; index++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int offset = -half; offset <= half; offset++)
                    {
                        int at = index + offset;
                        if (at < 0 || at >= values.Count) continue;
                        sum += values[at];
                        count++;
                    }
                    next.Add(sum / count);
                }
                values = next;
            }
            return values;
        }
    }
}
