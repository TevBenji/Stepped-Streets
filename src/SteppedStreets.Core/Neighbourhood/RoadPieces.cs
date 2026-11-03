// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Cuts a planned road into the pieces the game builds, one road edge each. Two rules beyond keeping each piece's curve true:
    /// a cut never falls close to a road's end, where a junction usually is, because the game's node there then crowds the
    /// junction and their shapes overlap (owner's in-game report, 3 November 2026); and every cut lands on the road's 8 m lot
    /// grid, because the game lays its zone cells along each edge from the edge's start, so off-grid cuts set the zoned cells
    /// across the levelled lots.
    /// </summary>
    public static class RoadPieces
    {
        public const float Longest = 48f;
        /// <summary>A piece touching a road's end is at least this long, unless the whole road is shorter than two of them.</summary>
        public const float AtEnds = 16f;
        public const float MostTurnDegrees = 25f;
        private const float Grid = StepPlanner.CellSize;

        public static List<(int First, int Last)> Cut(float3[] points)
        {
            var pieces = new List<(int, int)>();
            int count = points.Length;
            if (count < 2) return pieces;
            var stations = new float[count];
            for (int index = 1; index < count; index++) stations[index] = stations[index - 1] + math.distance(points[index - 1].xz, points[index].xz);
            float length = stations[count - 1];

            // The point nearest each multiple of 8 m: the only places a cut may go.
            var candidates = new List<int>();
            int at = 1;
            for (float mark = Grid; mark < length - 1f && count > 2; mark += Grid)
            {
                while (at < count - 2 && stations[at + 1] <= mark) at++;
                int best = at + 1 < count - 1 && math.abs(stations[at + 1] - mark) < math.abs(stations[at] - mark) ? at + 1 : at;
                if (math.abs(stations[best] - mark) <= 2.5f && (candidates.Count == 0 || candidates[candidates.Count - 1] < best))
                    candidates.Add(best);
            }

            int first = 0;
            while (true)
            {
                float left = length - stations[first];
                if (left <= Longest && Turn(points, first, count - 1) <= MostTurnDegrees) break;
                // The furthest grid point that keeps this piece within its length and turn and leaves the last piece long
                // enough; on a tight bend, the nearest grid point instead, so the curve stays true.
                float shortest = first == 0 ? AtEnds : Grid;
                int next = -1, nearest = -1;
                foreach (int candidate in candidates)
                {
                    float piece = stations[candidate] - stations[first];
                    if (candidate <= first || piece < shortest - 0.5f || length - stations[candidate] < AtEnds - 0.5f) continue;
                    if (nearest < 0) nearest = candidate;
                    if (piece > Longest + 0.5f) break;
                    if (Turn(points, first, candidate) <= MostTurnDegrees) next = candidate;
                }
                if (next < 0) next = nearest;
                if (next < 0) break;
                pieces.Add((first, next));
                first = next;
            }
            pieces.Add((first, count - 1));
            return pieces;
        }

        /// <summary>How far the road turns, in degrees, between two of its points.</summary>
        internal static float Turn(float3[] points, int first, int last)
        {
            float turn = 0f;
            for (int index = first + 1; index < last; index++)
            {
                float2 before = math.normalizesafe(points[index].xz - points[index - 1].xz), after = math.normalizesafe(points[index + 1].xz - points[index].xz);
                turn += math.degrees(math.acos(math.clamp(math.dot(before, after), -1f, 1f)));
            }
            return turn;
        }
    }
}
