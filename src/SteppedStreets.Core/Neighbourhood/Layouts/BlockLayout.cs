// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood.Layouts
{
    /// <summary>
    /// The superblock layouts: a grid of through streets around big blocks, and inside each block only streets that serve it.
    /// Dead ends (superblock), short dead ends round a green middle (Radburn), loops (fused grid), or Barcelona's inner streets
    /// that enter from one side and leave by the next, round a central square.
    /// </summary>
    internal static class BlockLayout
    {
        private const float Step = ContourStreets.Spacing;

        public static bool Handles(NeighbourhoodStyle style) => style == NeighbourhoodStyle.Superblock || style == NeighbourhoodStyle.Radburn
            || style == NeighbourhoodStyle.FusedGrid || style == NeighbourhoodStyle.BarcelonaSuperblock;

        /// <summary>The streets of the layout; <paramref name="joined"/> says whether any meets the city's road straight on.</summary>
        public static List<Street> Plan(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation)
        {
            var streets = new List<Street>();
            joined = false;
            var style = request.Style;
            uint seed = request.Seed;
            float depth = request.BlockDepth;
            float2 along = GridLayout.Facing(survey, request);
            if (Geometry.Hash01(seed, 3, 0) >= 0.5f) along = Geometry.Left(along);
            float2 across = Geometry.Left(along);
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var corner in survey.Boundary)
            {
                float u = math.dot(corner, along), v = math.dot(corner, across);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMin = math.min(vMin, v); vMax = math.max(vMax, v);
            }

            // The big blocks: Barcelona's three small blocks across, the others four to five block depths.
            bool barcelona = style == NeighbourhoodStyle.BarcelonaSuperblock;
            float block = depth * 1.15f;
            float size = barcelona ? 3f * block : depth * (style == NeighbourhoodStyle.Radburn ? 5f : style == NeighbourhoodStyle.FusedGrid ? 4.5f : 4f);
            // The through streets' rows and columns; every point the inner streets meet falls on a row's 4 m points.
            size = Step * math.round(size / Step);
            block = size / 3f;
            float phaseU = Step * math.round(Geometry.Hash01(seed, 2, 0) * size * 0.5f / Step);
            var offsets = new List<float>();
            for (float v = vMin + depth * 0.5f + Geometry.Hash01(seed, 1, 0) * size * 0.3f; v < vMax; v += size) offsets.Add(v);
            var rows = new List<List<(int Street, float From)>>();
            foreach (float v in offsets)
            {
                cancellation.ThrowIfCancellationRequested();
                var stretches = new List<(int, float)>();
                foreach (var (from, points) in GridLayout.Clip(survey, along, across, v, uMin - Step, uMax + Step, _ => 0f))
                {
                    var street = GridLayout.Street(survey, request, points, RoadKind.Street);
                    if (street == null) continue;
                    stretches.Add((streets.Count, from));
                    streets.Add(street);
                }
                rows.Add(stretches);
            }
            int firstColumn = (int)math.floor((uMin - (uMin + phaseU)) / size) - 1, lastColumn = (int)math.ceil((uMax - uMin - phaseU) / size) + 1;
            float Column(int column) => uMin - Step + phaseU + column * size;

            // Through streets across, lined up into crossroads.
            var crossings = new Dictionary<(int Row, int Column), int>();
            for (int row = 0; row + 1 < rows.Count; row++)
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    int link = GridLayout.Cross(survey, request, streets, rows[row], rows[row + 1], along, Column(column), Column(column));
                    if (link >= 0) crossings[(row, column)] = link;
                }

            // Inside the blocks: from each row, into the blocks above and below it (the edge blocks too, as far as they reach).
            for (int row = 0; row < rows.Count; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    float left = Column(column), right = Column(column + 1);
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        float2 inward = across * sign;
                        if (barcelona)
                        {
                            if (sign > 0f && row + 1 < rows.Count) Barcelona(survey, request, streets, rows, crossings, row, column, left, block, along, across);
                            continue;
                        }
                        float half = size * 0.5f;
                        if (style == NeighbourhoodStyle.FusedGrid)
                        {
                            // Loops two blocks wide, legs a block apart, reaching to the middle of the big block.
                            for (float u = left + depth * 0.75f; u + depth < right - depth * 0.6f; u += depth * 2f)
                                BranchLayout.Loop(survey, request, streets, rows[row], u, u + depth, inward, along, half - depth * 0.45f);
                            continue;
                        }
                        // Dead ends a block apart, reaching to the middle (superblock) or a third of the way (Radburn's green middle).
                        float reach = style == NeighbourhoodStyle.Radburn ? size * 0.3f : half - depth * 0.5f;
                        for (float u = left + depth * 0.75f; u < right - depth * 0.6f; u += depth)
                            BranchLayout.Branch(survey, request, streets, rows[row], u, inward, reach);
                    }
                }
            }

            // Rows that run out towards the city's road meet it in a T, and so do the through streets across.
            int made = streets.Count;
            for (int index = 0; index < made; index++)
            {
                var street = streets[index];
                if (street.Kind != RoadKind.Street || street.Start.Joined) continue;
                int last = street.Points.Count - 1;
                joined |= GridLayout.Reach(survey, request, streets, index, 0, math.normalizesafe(street.Points[0] - street.Points[1]));
                joined |= GridLayout.Reach(survey, request, streets, index, last, math.normalizesafe(street.Points[last] - street.Points[last - 1]));
            }
            foreach (var (row, outward) in GridLayout.Outer(rows.Count, across))
            {
                if (row < 0 || row >= rows.Count) continue;
                for (int column = firstColumn; column <= lastColumn; column++)
                    if (GridLayout.Find(streets, rows[row], Column(column), out int host, out int point))
                        joined |= GridLayout.Reach(survey, request, streets, host, point, outward);
            }
            return streets;
        }

        /// <summary>
        /// One Barcelona superblock: from each side, two inner streets come in a block and turn to leave by the next side, so
        /// cars can serve every house but not cross; the square in the middle is left free.
        /// </summary>
        private static void Barcelona(SiteSurvey survey, NeighbourhoodRequest request, List<Street> streets, List<List<(int Street, float From)>> rows,
            Dictionary<(int Row, int Column), int> crossings, int row, int column, float left, float block, float2 along, float2 across)
        {
            if (!crossings.TryGetValue((row, column), out int leftSide) || !crossings.TryGetValue((row, column + 1), out int rightSide)) return;
            foreach (var (blocksAlong, blocksUp) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            {
                // In from the row below (or above), a block; then out to the through street on the left (or right).
                var rowSide = rows[blocksUp == 1 ? row : row + 1];
                int side = blocksAlong == 1 ? leftSide : rightSide;
                if (!GridLayout.Find(streets, rowSide, left + blocksAlong * block, out int host, out int hostPoint)) continue;
                var cross = streets[side];
                float2 start = streets[host].Points[hostPoint];
                // The cross street's point a block (or two) up from its lower end.
                int sidePoint = Nearest(cross, cross.Points[0] + across * (blocksUp * block));
                float2 end = cross.Points[sidePoint];
                float2 corner = along * math.dot(start, along) + across * math.dot(end, across);
                if (!Free(cross, sidePoint)) continue;
                var line = GridLayout.Line(start, corner);
                var turn = GridLayout.Line(corner, end);
                int bend = line.Count - 1;
                for (int index = 1; index < turn.Count; index++) line.Add(turn[index]);
                bool clear = true;
                for (int index = 1; index < line.Count - 1; index++) clear &= survey.StreetRoomAt(line[index]) >= 0f;
                if (!clear) continue;
                var street = GridLayout.Street(survey, request, line, RoadKind.Street, streets[host].Heights[hostPoint], cross.Heights[sidePoint]);
                if (street == null) continue;
                street.Corners.Add(bend);
                street.Start = new Attachment { Street = host, Point = hostPoint };
                street.End = new Attachment { Street = side, Point = sidePoint };
                streets[host].Junctions.Add(hostPoint);
                cross.Junctions.Add(sidePoint);
                streets.Add(street);
            }
        }

        private static int Nearest(Street street, float2 target)
        {
            int best = 0;
            for (int index = 1; index < street.Points.Count; index++)
                if (math.distancesq(street.Points[index], target) < math.distancesq(street.Points[best], target)) best = index;
            return best;
        }

        /// <summary>Whether a point of a street may take a junction: 26 m from its ends and 40 m from other junctions.</summary>
        private static bool Free(Street street, int point)
        {
            float along = street.Stations[point], length = street.Stations[street.Stations.Length - 1];
            if (along < 26f || length - along < 26f) return false;
            foreach (int junction in street.Junctions)
                if (junction != point && street.Between(junction, point) < GridLayout.JunctionGap) return false;
            return true;
        }
    }
}
