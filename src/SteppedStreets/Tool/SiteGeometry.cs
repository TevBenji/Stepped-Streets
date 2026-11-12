// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;

namespace SteppedStreets.Tool
{
    // Pure helpers are shared with the off-game tests; no live game data escapes a snapshot.
    internal static class SiteGeometry
    {
        /// <summary>
        /// Level lots on existing streets, just before building: what the planned ground would now reach that the plan didn't
        /// know about, or null. Only work done since the plan counts: a cell taken now that was free then, or a building whose
        /// lot wasn't there then; what was taken or built then the ground field already holds at its own ground. The ground
        /// changes where <paramref name="change"/> moves it, and under every pad, strictly inside it, so a lot that only shares
        /// an edge with a pad never counts.
        /// </summary>
        /// <param name="takenNow">Centres of the cells zoned, built on or blocked now.</param>
        /// <param name="takenThen">Keys (<see cref="CellKey"/>) of the cells taken when the plan was made.</param>
        internal static string LevellingMeetsNewWork(Func<float2, float> change, IReadOnlyList<float2[]> pads, IEnumerable<float2> takenNow,
            ICollection<long> takenThen, IEnumerable<float2[]> buildingsNow, IReadOnlyList<float2[]> buildingsThen)
        {
            var near = new Core.Neighbourhood.SpatialBuckets<float2[]>(32f);
            foreach (var pad in pads) near.Add(math.min(math.min(pad[0], pad[1]), math.min(pad[2], pad[3])), math.max(math.max(pad[0], pad[1]), math.max(pad[2], pad[3])), pad);
            bool Graded(float2 point)
            {
                // GroundField.ToRaster's own threshold for a changed texel.
                if (math.abs(change(point)) >= 0.02f) return true;
                foreach (var pad in near.Near(point)) if (InsideBy(point, pad, 0.5f)) return true;
                return false;
            }
            foreach (var centre in takenNow)
                if (!takenThen.Contains(CellKey(centre)) && Graded(centre))
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "the cell at ({0:0}, {1:0}) was zoned or built on since the plan", centre.x, centre.y);
            foreach (var lot in buildingsNow)
            {
                bool known = false;
                foreach (var then in buildingsThen) if (SameLot(lot, then)) { known = true; break; }
                if (known) continue;
                // The lot a metre in from its edges every 4 m or so: a new house beside a pad, touching it, isn't under it.
                float2 a = lot[0], b = lot[1], d = lot[3];
                float width = math.distance(a, b), depth = math.distance(a, d);
                if (width < 2f || depth < 2f) continue;
                int across = math.max(1, (int)math.ceil((width - 2f) / 4f)), deep = math.max(1, (int)math.ceil((depth - 2f) / 4f));
                for (int i = 0; i <= across; i++)
                    for (int j = 0; j <= deep; j++)
                    {
                        float2 point = a + (b - a) * ((1f + (width - 2f) * i / across) / width) + (d - a) * ((1f + (depth - 2f) * j / deep) / depth);
                        if (Graded(point))
                            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "a building at ({0:0}, {1:0}) stands where the ground would change",
                                (a.x + lot[2].x) * 0.5f, (a.y + lot[2].y) * 0.5f);
                    }
            }
            return null;
        }

        /// <summary>A cell's centre rounded to the metre: the same cell gives the same key from one read to the next.</summary>
        internal static long CellKey(float2 centre) => ((long)math.round(centre.x) << 32) ^ (uint)(int)math.round(centre.y);

        /// <summary>The same building lot: every corner of one within a metre of a corner of the other.</summary>
        internal static bool SameLot(float2[] first, float2[] second)
        {
            foreach (var corner in first)
            {
                bool matched = false;
                foreach (var other in second) if (math.distancesq(corner, other) < 1f) { matched = true; break; }
                if (!matched) return false;
            }
            return true;
        }

        /// <summary>Inside a convex quad and more than <paramref name="inset"/> from each of its edges.</summary>
        internal static bool InsideBy(float2 point, float2[] quad, float inset)
        {
            bool positive = false, negative = false;
            for (int index = 0; index < quad.Length; index++)
            {
                float2 edge = quad[(index + 1) % quad.Length] - quad[index];
                float length = math.length(edge);
                if (length < 1e-4f) continue;
                float2 offset = point - quad[index];
                float distance = (edge.x * offset.y - edge.y * offset.x) / length;
                if (distance > inset) positive = true;
                else if (distance < -inset) negative = true;
                else return false;
            }
            return !(positive && negative);
        }

        internal static float Bilinear(float[] values, int columns, int rows, float2 origin, float spacing, float2 point)
        {
            float2 coordinate = math.clamp((point - origin) / spacing, float2.zero, new float2(columns - 1, rows - 1));
            int column = math.min((int)coordinate.x, columns - 2);
            int row = math.min((int)coordinate.y, rows - 2);
            float2 fraction = coordinate - new float2(column, row);
            return math.lerp(
                math.lerp(values[row * columns + column], values[row * columns + column + 1], fraction.x),
                math.lerp(values[(row + 1) * columns + column], values[(row + 1) * columns + column + 1], fraction.x), fraction.y);
        }

        internal static float DistanceSquared(float2 point, float2 start, float2 end)
        {
            float2 direction = end - start;
            float lengthSquared = math.lengthsq(direction);
            float fraction = lengthSquared > 0f ? math.saturate(math.dot(point - start, direction) / lengthSquared) : 0f;
            return math.distancesq(point, start + fraction * direction);
        }

        /// <summary>
        /// Whether an edge is one the build made: of a prefab the build laid new courses with, and lying along them, its two ends
        /// and its middle each within 3 m of a course of that prefab and its middle running within 15° of the course there. The
        /// game may cut a course where it meets a junction or crosses a railway, and every piece still lies along it. An edge of
        /// the city's, however the build split it, has another prefab or runs off the courses, so Remove can never take it.
        /// </summary>
        internal static bool BuiltAlong<TPrefab>(IEnumerable<(TPrefab Prefab, float2[] Line)> courses, TPrefab prefab,
            float2 start, float2 middle, float2 end, float2 direction) where TPrefab : IEquatable<TPrefab>
        {
            if (math.lengthsq(direction) < 1e-8f) return false;
            float2 heading = math.normalize(direction);
            bool startOn = false, endOn = false, middleAlong = false;
            foreach (var (coursePrefab, line) in courses)
            {
                if (line == null || !coursePrefab.Equals(prefab)) continue;
                for (int index = 1; index < line.Length; index++)
                {
                    float2 along = line[index] - line[index - 1];
                    if (math.lengthsq(along) < 1e-8f) continue;
                    startOn |= DistanceSquared(start, line[index - 1], line[index]) <= CourseReach * CourseReach;
                    endOn |= DistanceSquared(end, line[index - 1], line[index]) <= CourseReach * CourseReach;
                    middleAlong |= DistanceSquared(middle, line[index - 1], line[index]) <= CourseReach * CourseReach
                        && math.abs(math.dot(math.normalize(along), heading)) >= CourseAlong;
                }
            }
            return startOn && endOn && middleAlong;
        }


        private const float CourseReach = 3f;
        // cos 15°
        private const float CourseAlong = 0.966f;

        /// <summary>
        /// Whether Remove takes a new edge. Only one of a prefab the build laid courses with, and of those: one lying along the
        /// courses, or one that can't be a piece of an edge of the city's of its own prefab that vanished in the build, where the
        /// game reshaped the build's street at a level crossing. A highway or a railway never has the build's prefab; a road of
        /// the city's of the street's own type the build split keeps its pieces, as in doubt a piece stays.
        /// </summary>
        internal static bool BuildsEdge<TPrefab>(IList<(TPrefab Prefab, float2[] Line)> courses, IEnumerable<(TPrefab Prefab, float2[] Line)> vanished,
            TPrefab prefab, float2 start, float2 middle, float2 end, float2 direction) where TPrefab : IEquatable<TPrefab>
        {
            // An edge of no length can't be told apart: it stays.
            if (math.lengthsq(direction) < 1e-8f) return false;
            bool laid = false;
            foreach (var course in courses) laid |= course.Prefab.Equals(prefab);
            if (!laid) return false;
            if (BuiltAlong(courses, prefab, start, middle, end, direction)) return true;
            foreach (var (oldPrefab, line) in vanished)
                if (PieceOf(line, middle, direction, oldPrefab.Equals(prefab))) return false;
            return true;
        }

        /// <summary>
        /// Whether an edge may be a piece of an old edge that vanished: the same prefab, its middle within 4 m of the old line,
        /// running within 20° of it. Loose on purpose: a piece it can't rule out stays.
        /// </summary>
        internal static bool PieceOf(float2[] oldLine, float2 middle, float2 direction, bool samePrefab)
        {
            if (!samePrefab || oldLine == null || math.lengthsq(direction) < 1e-8f) return false;
            float2 heading = math.normalize(direction);
            for (int index = 1; index < oldLine.Length; index++)
            {
                float2 along = oldLine[index] - oldLine[index - 1];
                if (math.lengthsq(along) < 1e-8f) continue;
                if (DistanceSquared(middle, oldLine[index - 1], oldLine[index]) <= PieceReach * PieceReach
                    && math.abs(math.dot(math.normalize(along), heading)) >= PieceAlong) return true;
            }
            return false;
        }

        private const float PieceReach = 4f;
        // cos 20°
        private const float PieceAlong = 0.94f;

        internal static bool Inside(float2 point, float2[] quad)
        {
            bool positive = false;
            bool negative = false;
            for (int index = 0; index < quad.Length; index++)
            {
                float2 edge = quad[(index + 1) % quad.Length] - quad[index];
                float2 offset = point - quad[index];
                float cross = edge.x * offset.y - edge.y * offset.x;
                positive |= cross > 0.001f;
                negative |= cross < -0.001f;
            }
            return !(positive && negative);
        }

        internal static bool SingleChain(IReadOnlyList<(long Start, long End)> edges)
        {
            var degree = new Dictionary<long, int>();
            foreach (var edge in edges)
            {
                degree[edge.Start] = degree.TryGetValue(edge.Start, out int starts) ? starts + 1 : 1;
                degree[edge.End] = degree.TryGetValue(edge.End, out int ends) ? ends + 1 : 1;
                if (degree[edge.Start] > 2 || degree[edge.End] > 2) return false;
            }
            return edges.Count > 0 && StreetChain.Order(edges).Count == edges.Count;
        }

        internal static bool SamePlan(StepPlan first, StepPlan second)
        {
            if (first.Pads.Count != second.Pads.Count || first.Steps.Count != second.Steps.Count) return false;
            for (int index = 0; index < first.Pads.Count; index++)
            {
                var earlier = first.Pads[index];
                var later = second.Pads[index];
                if (earlier.Side != later.Side || earlier.WidthCells != later.WidthCells || earlier.DepthCells != later.DepthCells)
                    return false;
                for (int corner = 0; corner < earlier.Corners.Count; corner++)
                    if (!math.all(earlier.Corners[corner] == later.Corners[corner])) return false;
            }
            for (int index = 0; index < first.Steps.Count; index++)
            {
                var earlier = first.Steps[index];
                var later = second.Steps[index];
                if (earlier.Rise != later.Rise || !math.all(earlier.EdgeFront == later.EdgeFront)
                    || !math.all(earlier.EdgeBack == later.EdgeBack)) return false;
            }
            return true;
        }
    }
}
