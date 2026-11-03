// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;

namespace SteppedStreets.Tool
{
    // Pure helpers are shared with the off-game tests; no live game data escapes a snapshot.
    internal static class SiteGeometry
    {
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
