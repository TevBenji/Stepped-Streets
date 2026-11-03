// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>The height a road runs at along its centreline: as close to the ground as its grade limits allow.</summary>
    internal static class RoadProfile
    {
        /// <summary>
        /// Junctions get a landing this many metres long that is no steeper than <see cref="LandingGrade"/>: a road leaving at
        /// 64° is then clear of the street's corridor before it starts to climb, so the two corridors meet at one height.
        /// </summary>
        public const float LandingLength = 18f;

        public const float LandingGrade = 0.03f;

        /// <summary>
        /// Heights along <paramref name="line"/> that follow the (averaged) ground, never steeper than
        /// <paramref name="maximumGrade"/>, pinned to the given end heights with gentle landings there. Null when the ends
        /// are too far apart in height for the length of road between them.
        /// </summary>
        public static float[] Fit(IReadOnlyList<float2> line, Func<float2, float> ground, float maximumGrade,
            float? startHeight, float? endHeight, int averagingPoints = 0)
        {
            int count = line.Count;
            var stations = Geometry.Stations(line);
            float length = stations[count - 1];
            var raw = new float[count];
            for (int index = 0; index < count; index++) raw[index] = ground(line[index]);
            if (averagingPoints > 0) raw = MovingAverage(raw, averagingPoints);

            // The allowed grade of the segment ending at each point: gentle landings next to pinned ends.
            var grade = new float[count];
            for (int index = 1; index < count; index++)
            {
                float middle = (stations[index] + stations[index - 1]) * 0.5f;
                bool landing = (startHeight.HasValue && middle < LandingLength) || (endHeight.HasValue && length - middle < LandingLength);
                grade[index] = landing ? math.min(LandingGrade, maximumGrade) : maximumGrade;
            }
            var fromStart = new float[count];
            for (int index = 1; index < count; index++)
                fromStart[index] = fromStart[index - 1] + grade[index] * (stations[index] - stations[index - 1]);
            var toEnd = new float[count];
            for (int index = count - 2; index >= 0; index--)
                toEnd[index] = toEnd[index + 1] + grade[index + 1] * (stations[index + 1] - stations[index]);

            var heights = new float[count];
            for (int index = 0; index < count; index++)
            {
                float low = float.MinValue, high = float.MaxValue;
                if (startHeight.HasValue) { low = math.max(low, startHeight.Value - fromStart[index]); high = math.min(high, startHeight.Value + fromStart[index]); }
                if (endHeight.HasValue) { low = math.max(low, endHeight.Value - toEnd[index]); high = math.min(high, endHeight.Value + toEnd[index]); }
                if (low > high + 0.001f) return null;
                heights[index] = math.clamp(raw[index], low, math.max(low, high));
            }
            if (startHeight.HasValue) heights[0] = startHeight.Value;
            if (endHeight.HasValue) heights[count - 1] = endHeight.Value;

            LimitGrade(heights, stations, grade, startHeight.HasValue, endHeight.HasValue);
            // Soften the kinks the limiter leaves where it starts and stops cutting; averaging never steepens an even line.
            for (int pass = 0; pass < 2; pass++)
            {
                var copy = (float[])heights.Clone();
                for (int index = 1; index < count - 1; index++) heights[index] = (copy[index - 1] + copy[index] * 2f + copy[index + 1]) * 0.25f;
                if (!startHeight.HasValue) heights[0] = (copy[0] * 3f + copy[1]) * 0.25f;
                if (!endHeight.HasValue) heights[count - 1] = (copy[count - 1] * 3f + copy[count - 2]) * 0.25f;
            }
            LimitGrade(heights, stations, grade, startHeight.HasValue, endHeight.HasValue);
            return heights;
        }

        /// <summary>
        /// A closed street's profile: fitted three laps over, keeping the middle lap (so the averaging and the limiter see the
        /// ring as endless), then the grade limited once more round the ring, across the seam too.
        /// </summary>
        public static float[] FitRing(IReadOnlyList<float2> ring, Func<float2, float> ground, float maximumGrade, int averagingPoints = 0)
        {
            int count = ring.Count;
            var laps = new List<float2>(count * 3);
            for (int lap = 0; lap < 3; lap++)
                for (int index = 0; index < count; index++) laps.Add(ring[index]);
            var fitted = Fit(laps, ground, maximumGrade, null, null, averagingPoints);
            var heights = new float[count];
            Array.Copy(fitted, count, heights, 0, count);
            for (int pass = 0; pass < 12; pass++)
            {
                bool changed = false;
                for (int step = 1; step <= 2 * count; step++)
                {
                    // Forward round the ring, then backward.
                    int index = step <= count ? step % count : (2 * count - step) % count;
                    int previous = step <= count ? (index - 1 + count) % count : (index + 1) % count;
                    float most = maximumGrade * math.distance(ring[index], ring[previous]);
                    float limited = math.clamp(heights[index], heights[previous] - most, heights[previous] + most);
                    if (limited != heights[index]) { heights[index] = limited; changed = true; }
                }
                if (!changed) break;
            }
            return heights;
        }

        /// <summary>The steepest grade between neighbouring points.</summary>
        public static float SteepestGrade(IReadOnlyList<float3> points)
        {
            float steepest = 0f;
            for (int index = 1; index < points.Count; index++)
            {
                float run = math.distance(points[index].xz, points[index - 1].xz);
                if (run > 0.5f) steepest = math.max(steepest, math.abs(points[index].y - points[index - 1].y) / run);
            }
            return steepest;
        }

        /// <summary>Overall grade: rise and fall over the length, so a level street that undulates still counts its climbs.</summary>
        public static float MeanGrade(IReadOnlyList<float3> points)
        {
            float run = 0f, rise = 0f;
            for (int index = 1; index < points.Count; index++)
            {
                run += math.distance(points[index].xz, points[index - 1].xz);
                rise += math.abs(points[index].y - points[index - 1].y);
            }
            return run > 0f ? rise / run : 0f;
        }

        private static void LimitGrade(float[] heights, float[] stations, float[] grade, bool pinnedStart, bool pinnedEnd)
        {
            int count = heights.Length;
            for (int pass = 0; pass < 12; pass++)
            {
                bool changed = false;
                for (int index = 1; index < count; index++)
                {
                    if (index == count - 1 && pinnedEnd) continue;
                    float step = grade[index] * (stations[index] - stations[index - 1]);
                    float limited = math.clamp(heights[index], heights[index - 1] - step, heights[index - 1] + step);
                    if (limited != heights[index]) { heights[index] = limited; changed = true; }
                }
                for (int index = count - 2; index >= 0; index--)
                {
                    if (index == 0 && pinnedStart) continue;
                    float step = grade[index + 1] * (stations[index + 1] - stations[index]);
                    float limited = math.clamp(heights[index], heights[index + 1] - step, heights[index + 1] + step);
                    if (limited != heights[index]) { heights[index] = limited; changed = true; }
                }
                if (!changed) break;
            }
        }

        private static float[] MovingAverage(float[] values, int radius)
        {
            var result = new float[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                int first = math.max(0, index - radius), last = math.min(values.Length - 1, index + radius);
                float sum = 0f;
                for (int other = first; other <= last; other++) sum += values[other];
                result[index] = sum / (last - first + 1);
            }
            return result;
        }
    }
}
