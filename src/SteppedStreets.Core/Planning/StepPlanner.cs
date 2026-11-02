// Stepped Streets, the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    /// <summary>
    /// Turns one steep street into level pads that step up the hill (PRD rules R1 to R4, R8 and R10).
    /// Banks, aprons, earthwork, walls and obstacles (R5 to R7, R9) come later.
    /// </summary>
    public static class StepPlanner
    {
        /// <summary>The game's zone cell, in metres.</summary>
        public const float CellSize = 8f;

        /// <summary>The game blocks a zoned strip shallower than this.</summary>
        public const int MinimumRows = 2;

        /// <summary>A road's zone block is at most this deep.</summary>
        public const int MaximumRows = 6;

        /// <summary>The game wipes a zoned lot one column wide that stands beside empty columns.</summary>
        public const int MinimumWidthCells = 2;

        // So a street drawn at exactly the minimum grade counts as steep despite float rounding ("at least", R1).
        private const float GradeTolerance = 0.0001f;

        // So a step of exactly the highest step is allowed.
        internal const float StepTolerance = 0.001f;

        // ponytail: a lot on the inside of a bend moves on 1 m at a time until it clears the previous lot, which can waste
        // up to 1 m of frontage per bend; solve for the exact clearance if that frontage matters.
        private const float OverlapNudge = 1f;

        // Lots that only touch along an edge do not overlap.
        private const float TouchTolerance = 0.01f;

        // A shorter end of the street is rounding, not land.
        private const float ShortestLeftOver = 0.01f;

        // Where a cell is sampled for R8, in cells within the cell: the centre, and 1 m inside each corner, so a cell that
        // tilts corner to corner is caught even when its centre sits right.
        private static readonly float2[] LevelSamples =
        {
            new float2(0.5f, 0.5f),
            new float2(1f / CellSize, 1f / CellSize),
            new float2(1f - 1f / CellSize, 1f / CellSize),
            new float2(1f / CellSize, 1f - 1f / CellSize),
            new float2(1f - 1f / CellSize, 1f - 1f / CellSize),
        };

        public static StepPlan Plan(StepPlanRequest request)
        {
            var centreline = request?.Centreline;
            if (centreline == null || centreline.Count < 2)
                throw new ArgumentException("The street needs at least two centreline points.", nameof(request));
            if (request.LotWidthCells < MinimumWidthCells)
                throw new ArgumentException($"Lots must be at least {MinimumWidthCells} cells wide.", nameof(request));
            if (request.NarrowestWidthCells < MinimumWidthCells || request.NarrowestWidthCells > request.LotWidthCells)
                throw new ArgumentException("The narrowest width must lie between 2 cells and the lot width.", nameof(request));
            if (request.DepthCells < MinimumRows || request.DepthCells > MaximumRows)
                throw new ArgumentException($"Lots must be {MinimumRows} to {MaximumRows} cells deep.", nameof(request));
            if (request.HighestStep <= 0f)
                throw new ArgumentException("The highest step must be above zero.", nameof(request));

            // R2 lays lots from the downhill end. A street whose two ends are level keeps the order it was given.
            bool reversed = centreline[centreline.Count - 1].y < centreline[0].y;
            var street = new Street(centreline, reversed);
            if (street.Length <= 0f)
                throw new ArgumentException("The street has no length on the ground.", nameof(request));

            // Left and right belong to the order the centreline was given (how the game names a net's sides), so walking
            // it backwards from the downhill end swaps them.
            float leftSign = reversed ? -1f : 1f;
            var plan = new StepPlan();
            if (request.Side != StreetSide.Right)
                PlanSide(street, request, StreetSide.Left, leftSign, plan);
            if (request.Side != StreetSide.Left)
                PlanSide(street, request, StreetSide.Right, -leftSign, plan);

            plan.Summary = Summarise(plan);
            if (plan.pads.Count == 0 && plan.Summary.EmptyCount == 0 && plan.flatStretches.Count > 0)
            {
                plan.Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "This street is flatter than {0:0.#}%: nothing to step here.",
                    request.MinimumGrade * 100f);
            }

            return plan;
        }

        /// <summary>
        /// R8: the pad's cells that the ground as built leaves level and the game would keep zoned, as
        /// (column from the pad's start, row from the road). <paramref name="groundHeight"/> takes world (x, z).
        /// </summary>
        public static List<int2> LevelCells(Pad pad, Func<float2, float> groundHeight, float tolerance)
        {
            // A zone block's cells run unbroken from the road, so each column stops at its first cell that is not level.
            var rows = new int[pad.WidthCells];
            for (int column = 0; column < pad.WidthCells; column++)
            {
                while (rows[column] < pad.DepthCells && IsLevel(pad, column, rows[column], groundHeight, tolerance))
                    rows[column]++;
                if (rows[column] < MinimumRows)
                    rows[column] = 0;
            }

            var cells = new List<int2>();
            for (int column = 0; column < pad.WidthCells; column++)
            {
                bool emptyBefore = column == 0 || rows[column - 1] == 0;
                bool emptyAfter = column == pad.WidthCells - 1 || rows[column + 1] == 0;
                if (emptyBefore && emptyAfter)
                    continue;
                for (int row = 0; row < rows[column]; row++)
                    cells.Add(new int2(column, row));
            }

            return cells;
        }

        private static void PlanSide(Street street, StepPlanRequest request, StreetSide side, float sideSign, StepPlan plan)
        {
            float depth = request.DepthCells * CellSize;
            float start = 0f;
            Pad previousPad = null;
            IReadOnlyList<float3> previousLot = null;

            while (true)
            {
                // R2: the widest lot that still fits before the end of the street.
                int width = request.LotWidthCells;
                float end = street.ChordEnd(start, width * CellSize);
                while (end < 0f && width > request.NarrowestWidthCells)
                {
                    width--;
                    end = street.ChordEnd(start, width * CellSize);
                }

                if (end < 0f)
                {
                    if (street.Length - start > ShortestLeftOver)
                    {
                        float leftOverWidth = math.distance(street.PointAt(start).xz, street.PointAt(street.Length).xz);
                        plan.emptyLots.Add(new EmptyLot
                        {
                            Side = side,
                            Corners = Outline(street, start, street.Length, leftOverWidth, sideSign, request.RoadHalfWidth, depth,
                                PadHeight(street, start, street.Length)),
                            Start = start,
                            End = street.Length,
                            Reason = EmptyLotReason.LeftOver,
                        });
                    }

                    return;
                }

                // R1
                float grade = math.abs(street.PointAt(end).y - street.PointAt(start).y) / (end - start);
                if (grade < request.MinimumGrade - GradeTolerance)
                {
                    AddFlatStretch(plan, side, start, end);
                    previousPad = null;
                    start = end;
                    continue;
                }

                // R3 and R4: a pad after a gap has no neighbour to step from, so only later pads are narrowed.
                float height = PadHeight(street, start, end);
                bool tooSteep = previousPad != null && TooTall(height - previousPad.Height, request);
                while (tooSteep && width > request.NarrowestWidthCells)
                {
                    width--;
                    end = street.ChordEnd(start, width * CellSize);
                    height = PadHeight(street, start, end);
                    tooSteep = TooTall(height - previousPad.Height, request);
                }

                var outline = Outline(street, start, end, width * CellSize, sideSign, request.RoadHalfWidth, depth, height);
                if (previousLot != null && Overlaps(outline, previousLot))
                {
                    start += OverlapNudge;
                    continue;
                }

                previousLot = outline;
                if (tooSteep)
                {
                    plan.emptyLots.Add(new EmptyLot
                    {
                        Side = side, Corners = outline, Start = start, End = end, Reason = EmptyLotReason.TooSteep,
                    });
                    // The empty lot holds the drop between the pads either side of it (as a bank, later), so the next pad
                    // starts a fresh run instead of being measured against the pad behind the gap.
                    previousPad = null;
                }
                else
                {
                    var pad = new Pad
                    {
                        Side = side,
                        Corners = outline,
                        Height = height,
                        Start = start,
                        End = end,
                        WidthCells = width,
                        DepthCells = request.DepthCells,
                    };
                    if (previousPad != null)
                        plan.steps.Add(StepBetween(previousPad, pad));
                    plan.pads.Add(pad);
                    previousPad = pad;
                }

                start = end;
            }
        }

        // R3: the street's height at the middle of the lot's front. PRD open question 1 may move it to the uphill corner,
        // which would be math.max(street.PointAt(start).y, street.PointAt(end).y).
        private static float PadHeight(Street street, float start, float end) => street.PointAt((start + end) * 0.5f).y;

        private static bool TooTall(float rise, StepPlanRequest request) => math.abs(rise) > request.HighestStep + StepTolerance;

        // The lot is a rectangle on the chord of its stretch of street: on a bend the chord is the closest straight line
        // to the kerb, and zone cells must be square.
        private static float3[] Outline(Street street, float start, float end, float width, float sideSign, float halfWidth,
            float depth, float height)
        {
            float2 from = street.PointAt(start).xz;
            float2 along = math.normalize(street.PointAt(end).xz - from);
            // In the game's left-handed plan (x right, z forward) the left of a direction (x, z) is (-z, x).
            float2 away = sideSign * new float2(-along.y, along.x);
            float2 frontStart = from + away * halfWidth;
            float2 frontEnd = frontStart + along * width;
            return new[]
            {
                new float3(frontStart.x, height, frontStart.y),
                new float3(frontEnd.x, height, frontEnd.y),
                new float3(frontEnd.x + away.x * depth, height, frontEnd.y + away.y * depth),
                new float3(frontStart.x + away.x * depth, height, frontStart.y + away.y * depth),
            };
        }

        // On a straight street the two pads share an edge exactly. On a bend they do not, so the step stands halfway
        // between the earlier pad's end edge and the later pad's start edge.
        private static Step StepBetween(Pad earlier, Pad later)
        {
            float2 front = (earlier.Corners[1].xz + later.Corners[0].xz) * 0.5f;
            float2 back = (earlier.Corners[2].xz + later.Corners[3].xz) * 0.5f;
            return new Step
            {
                Side = later.Side,
                Rise = later.Height - earlier.Height,
                EdgeFront = new float3(front.x, earlier.Height, front.y),
                EdgeBack = new float3(back.x, earlier.Height, back.y),
                Earlier = earlier,
                Later = later,
            };
        }

        private static void AddFlatStretch(StepPlan plan, StreetSide side, float start, float end)
        {
            int last = plan.flatStretches.Count - 1;
            if (last >= 0 && plan.flatStretches[last].Side == side && plan.flatStretches[last].End == start)
                plan.flatStretches[last].End = end;
            else
                plan.flatStretches.Add(new FlatStretch { Side = side, Start = start, End = end });
        }

        private static StepPlanSummary Summarise(StepPlan plan)
        {
            var summary = new StepPlanSummary { LotCount = plan.pads.Count };
            for (int index = 0; index < plan.steps.Count; index++)
            {
                float height = math.abs(plan.steps[index].Rise);
                summary.LowestStep = index == 0 ? height : math.min(summary.LowestStep, height);
                summary.TallestStep = math.max(summary.TallestStep, height);
            }

            foreach (var lot in plan.emptyLots)
            {
                if (lot.Reason == EmptyLotReason.TooSteep)
                    summary.EmptyCount++;
            }

            return summary;
        }

        private static bool IsLevel(Pad pad, int column, int row, Func<float2, float> groundHeight, float tolerance)
        {
            foreach (var sample in LevelSamples)
            {
                float offset = math.abs(groundHeight(pad.PointAt(column + sample.x, row + sample.y)) - pad.Height);
                // Written this way round so a sampler that returns NaN (no ground there) counts as not level.
                if (!(offset <= tolerance))
                    return false;
            }

            return true;
        }

        // Separating-axis test for two convex outlines: they overlap unless an edge of one has the other wholly on its far side.
        // ponytail: a lot is only checked against the lot just before it on its side; a hairpin could need more.
        private static bool Overlaps(IReadOnlyList<float3> first, IReadOnlyList<float3> second) =>
            !HasSeparatingEdge(first, second) && !HasSeparatingEdge(second, first);

        private static bool HasSeparatingEdge(IReadOnlyList<float3> outline, IReadOnlyList<float3> other)
        {
            for (int index = 0; index < outline.Count; index++)
            {
                float2 edge = outline[(index + 1) % outline.Count].xz - outline[index].xz;
                float2 axis = math.normalize(new float2(-edge.y, edge.x));
                float2 mine = Project(outline, axis);
                float2 theirs = Project(other, axis);
                if (math.min(mine.y, theirs.y) - math.max(mine.x, theirs.x) < TouchTolerance)
                    return true;
            }

            return false;
        }

        private static float2 Project(IReadOnlyList<float3> outline, float2 axis)
        {
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            foreach (var corner in outline)
            {
                float position = math.dot(corner.xz, axis);
                lowest = math.min(lowest, position);
                highest = math.max(highest, position);
            }

            return new float2(lowest, highest);
        }

        /// <summary>The centreline from its downhill end, with each point's distance along it on the ground plan.</summary>
        private sealed class Street
        {
            private readonly List<float3> points = new List<float3>();
            private readonly List<float> distances = new List<float>();

            public Street(IReadOnlyList<float3> centreline, bool reversed)
            {
                for (int index = 0; index < centreline.Count; index++)
                {
                    var point = centreline[reversed ? centreline.Count - 1 - index : index];
                    if (points.Count == 0)
                    {
                        points.Add(point);
                        distances.Add(0f);
                        continue;
                    }

                    float length = math.distance(point.xz, points[points.Count - 1].xz);
                    // A repeated point would make a segment of no length to interpolate heights across.
                    if (length < 0.001f)
                        continue;
                    points.Add(point);
                    distances.Add(distances[distances.Count - 1] + length);
                }
            }

            public float Length => distances[distances.Count - 1];

            /// <summary>The centreline point at a distance along the street; heights interpolate linearly between points.</summary>
            public float3 PointAt(float distance)
            {
                int index = distances.BinarySearch(distance);
                if (index >= 0)
                    return points[index];
                index = ~index;
                if (index == 0)
                    return points[0];
                if (index == points.Count)
                    return points[points.Count - 1];
                float fraction = (distance - distances[index - 1]) / (distances[index] - distances[index - 1]);
                return math.lerp(points[index - 1], points[index], fraction);
            }

            /// <summary>
            /// The first distance past <paramref name="start"/> whose point lies <paramref name="chord"/> metres from the
            /// start point in a straight line on the ground plan, or -1 when the street ends first.
            /// </summary>
            public float ChordEnd(float start, float chord)
            {
                float2 origin = PointAt(start).xz;
                int index = distances.BinarySearch(start);
                index = index >= 0 ? index + 1 : ~index;
                float2 from = origin;
                float fromDistance = start;
                for (; index < points.Count; index++)
                {
                    float2 to = points[index].xz;
                    // Distance from the origin is convex along a segment, so the first segment whose far end reaches the
                    // chord holds the crossing.
                    if (math.distance(to, origin) >= chord)
                    {
                        float2 offset = from - origin;
                        float2 direction = to - from;
                        float a = math.dot(direction, direction);
                        float b = 2f * math.dot(offset, direction);
                        float c = math.dot(offset, offset) - chord * chord;
                        float fraction = (-b + math.sqrt(b * b - 4f * a * c)) / (2f * a);
                        return fromDistance + fraction * (distances[index] - fromDistance);
                    }

                    from = to;
                    fromDistance = distances[index];
                }

                return -1f;
            }
        }
    }
}
