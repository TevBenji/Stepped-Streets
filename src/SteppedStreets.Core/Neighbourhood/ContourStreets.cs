// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Streets on the contour lines of the smoothed ground, one block depth apart on the ground (video tip: roads follow the
    /// contours). Levels sit a constant height ΔH = g × S apart, where S is the block depth and g the site's reference slope,
    /// as hill towns are laid out; a level that crowds an accepted street is clipped there, and on gentle ground a level in
    /// between fills a gap wider than 1.7 S.
    /// </summary>
    internal static class ContourStreets
    {
        public const float Spacing = 4f;
        public const float ShortestStreet = 80f;
        private const float CrowdedShare = 0.65f;
        private const float InfillShare = 0.8f;

        internal sealed class Options
        {
            public float BlockDepth;
            public float MinimumRadius;
            public bool Straighten;
            public bool Level;
            public float Phase;
            public float EntryHeight;
        }

        /// <summary>The streets of one phase of the level grid, and what the in-between levels need to carry on.</summary>
        internal sealed class Layout
        {
            public readonly List<Street> Streets = new List<Street>();
            public float[] Nearest;
            public float Lowest, Highest, Interval;
            public Options Options;

            /// <summary>How good the phase is: long streets, few pieces.</summary>
            public float Score
            {
                get
                {
                    float score = 0f;
                    foreach (var street in Streets) score += street.Length - 60f;
                    return score;
                }
            }
        }

        public static List<Street> Plan(SiteSurvey survey, Options options, CancellationToken cancellation)
        {
            var layout = Regular(survey, options, cancellation);
            Infill(survey, layout, cancellation);
            return layout.Streets;
        }

        /// <summary>The regular levels only: enough to compare phases cheaply.</summary>
        internal static Layout Regular(SiteSurvey survey, Options options, CancellationToken cancellation)
        {
            var layout = new Layout { Options = options };
            float spacing = options.BlockDepth;
            float slope = math.max(0.03f, survey.ReferenceSlope);
            layout.Interval = slope * spacing;

            float lowest = float.MaxValue, highest = float.MinValue;
            for (int index = 0; index < survey.Count; index++)
            {
                if (survey.StreetRoom[index] < 0f) continue;
                if (survey.Smooth[index] < lowest) lowest = survey.Smooth[index];
                if (survey.Smooth[index] > highest) highest = survey.Smooth[index];
            }
            layout.Lowest = lowest;
            layout.Highest = highest;
            if (lowest > highest) return layout;

            layout.Nearest = new float[survey.Count];
            for (int index = 0; index < layout.Nearest.Length; index++) layout.Nearest[index] = float.MaxValue;

            var levels = new List<float>();
            for (float level = lowest + options.Phase * layout.Interval; level <= highest; level += layout.Interval) levels.Add(level);
            // Streets near the entry height keep their whole length; later levels give way where they crowd them.
            levels.Sort((a, b) => math.abs(a - options.EntryHeight).CompareTo(math.abs(b - options.EntryHeight)));
            foreach (float level in levels)
            {
                cancellation.ThrowIfCancellationRequested();
                AddLevel(survey, options, level, spacing * CrowdedShare, layout.Nearest, layout.Streets);
            }
            return layout;
        }

        /// <summary>Levels in between the regular ones, where the ground is gentle enough to leave a wide gap.</summary>
        internal static void Infill(SiteSurvey survey, Layout layout, CancellationToken cancellation)
        {
            if (layout.Nearest == null) return;
            var options = layout.Options;
            foreach (float share in new[] { 0.5f, 0.25f, 0.75f })
                for (float level = layout.Lowest + (options.Phase + share) * layout.Interval; level <= layout.Highest; level += layout.Interval)
                {
                    cancellation.ThrowIfCancellationRequested();
                    AddLevel(survey, options, level, options.BlockDepth * InfillShare, layout.Nearest, layout.Streets);
                }
        }

        private static void AddLevel(SiteSurvey survey, Options options, float level, float clearance, float[] nearest, List<Street> streets)
        {
            foreach (var (line, closed) in Trace(survey, level))
            {
                var even = Geometry.Resample(line, Spacing);
                if (closed && even.Count > 1) even.RemoveAt(even.Count - 1);
                bool Usable(float2 point) => survey.StreetRoomAt(point) >= 0f && survey.Sample(nearest, point) >= clearance;
                foreach (var (run, ring) in Runs(even, closed, Usable))
                {
                    foreach (var shaped in Shape(survey, run, ring, options, Usable))
                    {
                        float[] heights;
                        if (options.Level)
                        {
                            float mean = 0f;
                            foreach (var point in shaped.Points) mean += survey.GroundAt(point);
                            mean /= shaped.Points.Count;
                            heights = new float[shaped.Points.Count];
                            for (int index = 0; index < heights.Length; index++) heights[index] = mean;
                        }
                        else
                        {
                            heights = shaped.Closed
                                ? RoadProfile.FitRing(shaped.Points, survey.GroundAt, RoadProfile.LandingGrade, 5)
                                : RoadProfile.Fit(shaped.Points, survey.GroundAt, RoadProfile.LandingGrade, null, null, 5);
                        }
                        var street = new Street(RoadKind.Contour, shaped.Points, heights) { Closed = shaped.Closed };
                        streets.Add(street);
                        // Only distances up to the widest clearance asked for (the in-between levels') are ever compared.
                        Stamp(survey, street.Points, options.BlockDepth * InfillShare + Spacing * 2f, nearest);
                    }
                }
            }
        }

        /// <summary>Runs of consecutive usable points; a ring that is usable all round stays a ring.</summary>
        private static IEnumerable<(List<float2> Line, bool Ring)> Runs(List<float2> points, bool closed, Func<float2, bool> usable)
        {
            int count = points.Count;
            if (count < 2) yield break;
            var ok = new bool[count];
            int first = -1;
            for (int index = 0; index < count; index++)
            {
                ok[index] = usable(points[index]);
                if (!ok[index] && first < 0) first = index;
            }
            if (first < 0)
            {
                yield return (points, closed);
                yield break;
            }
            // On a ring, start just after a cut so no run is split across the seam.
            int start = closed ? first : 0;
            var run = new List<float2>();
            for (int step = 0; step < count; step++)
            {
                int index = (start + step) % count;
                if (ok[index]) run.Add(points[index]);
                else if (run.Count > 0) { yield return (run, false); run = new List<float2>(); }
            }
            if (run.Count > 0) yield return (run, false);
        }

        internal sealed class Shaped
        {
            public List<float2> Points;
            public bool Closed;
        }

        /// <summary>Smooth a run into a street, hold its radius, and drop what is too short or no longer fits.</summary>
        internal static List<Shaped> Shape(SiteSurvey survey, List<float2> run, bool ring, Options options, Func<float2, bool> usable)
        {
            var result = new List<Shaped>();
            if (Geometry.Length(run) < ShortestStreet) return result;
            List<float2> line;
            if (ring)
            {
                // Smooth a ring by smoothing it opened twice over and keeping the middle copy.
                var tripled = new List<float2>(run);
                tripled.AddRange(run);
                tripled.AddRange(run);
                var smoothed = Geometry.Resample(Geometry.Chaikin(tripled, options.Straighten ? 5 : 3), Spacing);
                int third = smoothed.Count / 3;
                line = smoothed.GetRange(third, third);
                Relax(line, 12, true);
            }
            else
            {
                line = options.Straighten ? Geometry.Chaikin(Simplify(run, 8f), 4) : Geometry.Chaikin(run, 3);
                line = Geometry.Resample(line, Spacing);
                // A builder lays a gently winding street, not one that copies every bump of the contour: low-pass the line
                // so wiggles shorter than about 60 m go while the hill's own bends stay.
                Relax(line, 12, false);
            }
            HoldRadius(line, options.MinimumRadius, ring);
            // Smoothing moves the street a little; keep only what still fits.
            foreach (var (piece, stillRing) in Runs(line, ring, usable))
            {
                var resampled = stillRing ? piece : Geometry.Resample(piece, Spacing);
                if (Geometry.Length(resampled) < ShortestStreet) continue;
                if (stillRing && RingTightest(resampled, out int tight) < options.MinimumRadius * 0.98f)
                {
                    // A ring smoothing could not open (any ring shorter than 2πr): open it at the bend and keep the rest if it
                    // fits, rather than keep a ring the validation would remove whole after it crowded out other streets.
                    int count = resampled.Count, skip = (int)math.ceil(options.MinimumRadius / Spacing);
                    var opened = new List<float2>();
                    for (int offset = skip; offset <= count - skip; offset++) opened.Add(resampled[(tight + offset) % count]);
                    if (opened.Count > 1 && Geometry.Length(opened) >= ShortestStreet && Geometry.TightestRadius(opened, 12f, out _) >= options.MinimumRadius * 0.98f)
                        result.Add(new Shaped { Points = opened });
                    continue;
                }
                if (!stillRing && Geometry.TightestRadius(resampled, 12f, out int at) < options.MinimumRadius * 0.98f)
                {
                    // A bend that smoothing could not open: cut it out and keep both sides.
                    float cut = options.MinimumRadius;
                    var stations = Geometry.Stations(resampled);
                    var before = new List<float2>();
                    var after = new List<float2>();
                    for (int index = 0; index < resampled.Count; index++)
                    {
                        if (stations[index] < stations[at] - cut) before.Add(resampled[index]);
                        else if (stations[index] > stations[at] + cut) after.Add(resampled[index]);
                    }
                    foreach (var side in new[] { before, after })
                        if (side.Count > 1 && Geometry.Length(side) >= ShortestStreet && Geometry.TightestRadius(side, 12f, out _) >= options.MinimumRadius * 0.98f)
                            result.Add(new Shaped { Points = side });
                    continue;
                }
                result.Add(new Shaped { Points = resampled, Closed = stillRing });
            }
            return result;
        }

        /// <summary>Laplacian smoothing of a whole line, ends held (a ring wraps).</summary>
        internal static void Relax(List<float2> line, int passes, bool ring)
        {
            int count = line.Count;
            if (count < 3) return;
            var copy = new float2[count];
            for (int pass = 0; pass < passes; pass++)
            {
                line.CopyTo(copy);
                for (int index = 0; index < count; index++)
                {
                    if (!ring && (index == 0 || index == count - 1)) continue;
                    float2 before = copy[(index - 1 + count) % count], after = copy[(index + 1) % count];
                    line[index] = math.lerp(copy[index], (before + after) * 0.5f, 0.5f);
                }
            }
        }

        /// <summary>Opens bends tighter than the radius by local smoothing, ends held.</summary>
        internal static void HoldRadius(List<float2> line, float radius, bool ring)
        {
            for (int round = 0; round < 40; round++)
            {
                float tightest = ring ? RingTightest(line, out int at) : Geometry.TightestRadius(line, 12f, out at);
                if (at < 0 || tightest >= radius) return;
                int count = line.Count;
                for (int iteration = 0; iteration < 4; iteration++)
                    for (int offset = -8; offset <= 8; offset++)
                    {
                        int index = at + offset;
                        if (ring) index = (index % count + count) % count;
                        else if (index <= 0 || index >= count - 1) continue;
                        float2 before = line[(index - 1 + count) % count], after = line[(index + 1) % count];
                        line[index] = math.lerp(line[index], (before + after) * 0.5f, 0.5f);
                    }
            }
        }

        private static float RingTightest(List<float2> line, out int at)
        {
            at = -1;
            float tightest = float.PositiveInfinity;
            int count = line.Count;
            for (int index = 0; index < count; index++)
            {
                float radius = Geometry.Radius(line[(index - 3 + count) % count], line[index], line[(index + 3) % count]);
                if (radius < tightest) { tightest = radius; at = index; }
            }
            return tightest;
        }

        /// <summary>Douglas–Peucker: the fewest points within the tolerance (Terraced blocks' straighter streets).</summary>
        private static List<float2> Simplify(List<float2> line, float tolerance)
        {
            var keep = new bool[line.Count];
            keep[0] = keep[line.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, line.Count - 1));
            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                float worst = 0f;
                int worstIndex = -1;
                for (int index = first + 1; index < last; index++)
                {
                    float distance = Geometry.DistanceToSegment(line[index], line[first], line[last]);
                    if (distance > worst) { worst = distance; worstIndex = index; }
                }
                if (worstIndex < 0 || worst <= tolerance) continue;
                keep[worstIndex] = true;
                stack.Push((first, worstIndex));
                stack.Push((worstIndex, last));
            }
            var result = new List<float2>();
            for (int index = 0; index < line.Count; index++) if (keep[index]) result.Add(line[index]);
            return result;
        }

        /// <summary>Lowers the distance-to-street field around new street points.</summary>
        /// <summary>Each node's distance to the nearest of these points within reach; with heights, also that point's height.</summary>
        internal static void Stamp(SiteSurvey survey, List<float2> points, float reach, float[] nearest, float[] pointHeights = null, float[] nearestHeight = null)
        {
            int cells = (int)math.ceil(reach / survey.Cell);
            // Every other point: the points are 4 m apart, so a node's distance is off by at most 4 m, well inside the margins.
            for (int point = 0; point < points.Count; point += point + 2 < points.Count ? 2 : 1)
            {
                float2 position = points[point];
                int centreColumn = (int)math.round((position.x - survey.Origin.x) / survey.Cell);
                int centreRow = (int)math.round((position.y - survey.Origin.y) / survey.Cell);
                for (int row = math.max(0, centreRow - cells); row <= math.min(survey.Rows - 1, centreRow + cells); row++)
                    for (int column = math.max(0, centreColumn - cells); column <= math.min(survey.Columns - 1, centreColumn + cells); column++)
                    {
                        int index = row * survey.Columns + column;
                        float distance = math.distance(survey.Position(index), position);
                        if (distance >= nearest[index]) continue;
                        nearest[index] = distance;
                        if (nearestHeight != null) nearestHeight[index] = pointHeights[point];
                    }
            }
        }

        /// <summary>Marching squares over the area's part of the grid: the contour lines at one level, as polylines.</summary>
        internal static List<(List<float2> Line, bool Closed)> Trace(SiteSurvey survey, float level)
        {
            int columns = survey.Columns;
            int firstColumn = math.max(0, (int)((survey.BoundaryMinimum.x - survey.Origin.x) / survey.Cell) - 2);
            int lastColumn = math.min(columns - 2, (int)((survey.BoundaryMaximum.x - survey.Origin.x) / survey.Cell) + 2);
            int firstRow = math.max(0, (int)((survey.BoundaryMinimum.y - survey.Origin.y) / survey.Cell) - 2);
            int lastRow = math.min(survey.Rows - 2, (int)((survey.BoundaryMaximum.y - survey.Origin.y) / survey.Cell) + 2);
            var values = survey.Smooth;
            var points = new Dictionary<int, float2>();
            var touching = new Dictionary<int, List<int>>();
            var segments = new List<int2>();

            int EdgeId(int column, int row, bool vertical) => 2 * (row * columns + column) + (vertical ? 1 : 0);
            int Edge(int column, int row, int side)
            {
                // 0 bottom, 1 right, 2 top, 3 left, as in the case table below.
                int id = side == 0 ? EdgeId(column, row, false) : side == 1 ? EdgeId(column + 1, row, true)
                    : side == 2 ? EdgeId(column, row + 1, false) : EdgeId(column, row, true);
                if (!points.ContainsKey(id))
                {
                    int ax = column + (side == 1 ? 1 : 0), ay = row + (side == 2 ? 1 : 0);
                    int bx = ax + (side == 0 || side == 2 ? 1 : 0), by = ay + (side == 1 || side == 3 ? 1 : 0);
                    float va = values[ay * columns + ax], vb = values[by * columns + bx];
                    float fraction = math.abs(vb - va) > 1e-6f ? math.saturate((level - va) / (vb - va)) : 0.5f;
                    points[id] = survey.Origin + math.lerp(new float2(ax, ay), new float2(bx, by), fraction) * survey.Cell;
                }
                return id;
            }
            void Segment(int column, int row, int sideA, int sideB)
            {
                int a = Edge(column, row, sideA), b = Edge(column, row, sideB);
                int segment = segments.Count;
                segments.Add(new int2(a, b));
                foreach (int id in new[] { a, b })
                {
                    if (!touching.TryGetValue(id, out var list)) touching[id] = list = new List<int>(2);
                    list.Add(segment);
                }
            }

            for (int row = firstRow; row <= lastRow; row++)
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    float v00 = values[row * columns + column], v10 = values[row * columns + column + 1];
                    float v11 = values[(row + 1) * columns + column + 1], v01 = values[(row + 1) * columns + column];
                    int cell = (v00 >= level ? 1 : 0) | (v10 >= level ? 2 : 0) | (v11 >= level ? 4 : 0) | (v01 >= level ? 8 : 0);
                    bool centreHigh = (v00 + v10 + v11 + v01) * 0.25f >= level;
                    switch (cell)
                    {
                        case 1: case 14: Segment(column, row, 3, 0); break;
                        case 2: case 13: Segment(column, row, 0, 1); break;
                        case 3: case 12: Segment(column, row, 3, 1); break;
                        case 4: case 11: Segment(column, row, 1, 2); break;
                        case 6: case 9: Segment(column, row, 0, 2); break;
                        case 7: case 8: Segment(column, row, 3, 2); break;
                        case 5:
                            if (centreHigh) { Segment(column, row, 0, 1); Segment(column, row, 2, 3); }
                            else { Segment(column, row, 3, 0); Segment(column, row, 1, 2); }
                            break;
                        case 10:
                            if (centreHigh) { Segment(column, row, 3, 0); Segment(column, row, 1, 2); }
                            else { Segment(column, row, 0, 1); Segment(column, row, 2, 3); }
                            break;
                    }
                }

            var lines = new List<(List<float2>, bool)>();
            var used = new bool[segments.Count];
            List<float2> Walk(int segment, int fromEdge, out bool closed)
            {
                var line = new List<float2> { points[fromEdge] };
                int edge = fromEdge;
                closed = false;
                while (segment >= 0 && !used[segment])
                {
                    used[segment] = true;
                    edge = segments[segment].x == edge ? segments[segment].y : segments[segment].x;
                    line.Add(points[edge]);
                    int next = -1;
                    foreach (int candidate in touching[edge]) if (!used[candidate]) next = candidate;
                    if (next < 0) closed = edge == fromEdge;
                    segment = next;
                }
                return line;
            }
            // Open lines first, from an end only one segment touches, so none is walked from its middle.
            for (int segment = 0; segment < segments.Count; segment++)
            {
                if (used[segment]) continue;
                int a = segments[segment].x, b = segments[segment].y;
                if (touching[a].Count == 1) lines.Add((Walk(segment, a, out _), false));
                else if (touching[b].Count == 1) lines.Add((Walk(segment, b, out _), false));
            }
            for (int segment = 0; segment < segments.Count; segment++)
            {
                if (used[segment]) continue;
                var line = Walk(segment, segments[segment].x, out bool closed);
                lines.Add((line, closed));
            }
            return lines;
        }
    }
}
