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

        /// <summary>
        /// Cuts a road into pieces. <paramref name="cutPoints"/> is the road's points with one put in at every 8 m mark the
        /// samples miss (they are spread to fit the road's length, so a 101 m road samples every 4.04 m); the pieces index it.
        /// </summary>
        public static List<(int First, int Last)> Cut(float3[] points, out float3[] cutPoints)
        {
            var pieces = new List<(int, int)>();
            cutPoints = OnGrid(points);
            points = cutPoints;
            int count = points.Length;
            if (count < 2) return pieces;
            var stations = new float[count];
            for (int index = 1; index < count; index++) stations[index] = stations[index - 1] + math.distance(points[index - 1].xz, points[index].xz);
            float length = stations[count - 1];

            // The points on a multiple of 8 m: the only places a cut may go.
            var candidates = new List<int>();
            for (int index = 1; index < count - 1; index++)
                if (math.abs(stations[index] - math.round(stations[index] / Grid) * Grid) <= Snap) candidates.Add(index);

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

        private const float Snap = 0.05f;

        /// <summary>The road's points with one put in, its height between its neighbours', at every 8 m mark along it.</summary>
        internal static float3[] OnGrid(float3[] points)
        {
            if (points.Length < 2) return points;
            float total = 0f;
            for (int index = 1; index < points.Length; index++) total += math.distance(points[index - 1].xz, points[index].xz);
            var result = new List<float3>(points.Length + (int)(total / Grid) + 1) { points[0] };
            float travelled = 0f, mark = Grid;
            for (int index = 1; index < points.Length; index++)
            {
                float3 from = points[index - 1], to = points[index];
                float step = math.distance(from.xz, to.xz);
                float end = travelled + step;
                while (mark < end - Snap && mark < total - Snap)
                {
                    if (mark > travelled + Snap) result.Add(math.lerp(from, to, (mark - travelled) / step));
                    mark += Grid;
                }
                result.Add(to);
                if (math.abs(mark - end) <= Snap) mark += Grid;
                travelled = end;
            }
            return result.ToArray();
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
