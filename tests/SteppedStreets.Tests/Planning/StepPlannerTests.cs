// Stepped Streets, tests for the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Planning
{
    public sealed class StepPlannerTests
    {
        // PRD 5.3: step = grade × lot width.
        [Theory]
        [InlineData(0.06f, 2, 0.96f)]
        [InlineData(0.06f, 3, 1.44f)]
        [InlineData(0.08f, 2, 1.28f)]
        [InlineData(0.08f, 3, 1.92f)]
        [InlineData(0.10f, 2, 1.60f)]
        [InlineData(0.10f, 3, 2.40f)]
        [InlineData(0.12f, 2, 1.92f)]
        [InlineData(0.12f, 3, 2.88f)]
        [InlineData(0.15f, 2, 2.40f)]
        [InlineData(0.15f, 3, 3.60f)]
        public void StepsMatchThePrdTable(float grade, int lotWidth, float expectedStep)
        {
            var plan = StepPlanner.Plan(Straight(grade, lotWidth, lotWidth, highestStep: 10f));

            Assert.NotEmpty(plan.Steps);
            Assert.All(plan.Steps, step => Assert.InRange(step.Rise, expectedStep - 0.01f, expectedStep + 0.01f));
            Assert.Equal(0, plan.Summary.EmptyCount);
            Assert.InRange(plan.Summary.LowestStep, expectedStep - 0.01f, expectedStep + 0.01f);
            Assert.InRange(plan.Summary.TallestStep, expectedStep - 0.01f, expectedStep + 0.01f);
            Assert.All(plan.Pads, pad => Assert.Equal(lotWidth, pad.WidthCells));
        }

        [Theory]
        [InlineData(0.06f, "This street is flatter than 6%: nothing to step here.")]
        [InlineData(0.045f, "This street is flatter than 4.5%: nothing to step here.")]
        public void FlatStreetGetsNoPadsAndSaysSo(float minimumGrade, string message)
        {
            var request = Straight(0.03f);
            request.MinimumGrade = minimumGrade;

            var plan = StepPlanner.Plan(request);

            Assert.Empty(plan.Pads);
            Assert.NotEmpty(plan.FlatStretches);
            Assert.Equal(message, plan.Message);
        }

        [Fact]
        public void TooSteepLotsAreLeftEmptyOrNarrowed()
        {
            // 15 % with 3-cell lots steps 3.6 m; a 3-cell lot beside a 2-cell one steps 15 % × 20 m = 3.0 m.
            var fixedWidth = StepPlanner.Plan(Straight(0.15f, lotWidth: 3, narrowest: 3, highestStep: 3.2f));
            var narrowable = StepPlanner.Plan(Straight(0.15f, lotWidth: 3, narrowest: 2, highestStep: 3.2f));

            var empty = fixedWidth.EmptyLots.Where(lot => lot.Reason == EmptyLotReason.TooSteep).ToList();
            Assert.NotEmpty(empty);
            Assert.All(empty, lot => Assert.Equal("too steep for lots this wide", lot.Wording));
            Assert.Equal(empty.Count, fixedWidth.Summary.EmptyCount);

            Assert.Equal(0, narrowable.Summary.EmptyCount);
            Assert.Contains(narrowable.Pads, pad => pad.WidthCells == 2);
            Assert.All(narrowable.Steps, step => Assert.True(math.abs(step.Rise) <= 3.2f));
        }

        [Fact]
        public void SameInputGivesTheSamePlan()
        {
            var request = Bent(grade: 0.11f);
            request.Centreline = request.Centreline.Reverse().ToList();
            request.HighestStep = 2f;

            Assert.Equal(Describe(StepPlanner.Plan(request)), Describe(StepPlanner.Plan(request)));
        }

        [Fact]
        public void FiveHundredMetreStreetPlansWithinBudget()
        {
            var points = new List<float3>();
            for (int index = 0; index <= 250; index++)
            {
                float distance = index * 2f;
                points.Add(new float3(20f * math.sin(distance / 80f), 0.1f * distance, distance));
            }

            var request = new StepPlanRequest
            {
                Centreline = points, RoadHalfWidth = 6f, LotWidthCells = 3, NarrowestWidthCells = 2, DepthCells = 4,
                HighestStep = 2f,
            };
            StepPlanner.Plan(request);

            var stopwatch = Stopwatch.StartNew();
            var plan = StepPlanner.Plan(request);
            stopwatch.Stop();

            Assert.NotEmpty(plan.Pads);
            Assert.True(stopwatch.ElapsedMilliseconds < 250, $"took {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void LotsOnABendDoNotOverlap()
        {
            var plan = StepPlanner.Plan(Bent(grade: 0.1f));

            foreach (var side in new[] { StreetSide.Left, StreetSide.Right })
            {
                var lots = plan.Pads.Where(pad => pad.Side == side).Select(pad => pad.Corners)
                    .Concat(plan.EmptyLots.Where(lot => lot.Side == side && lot.Reason == EmptyLotReason.TooSteep)
                        .Select(lot => lot.Corners))
                    .ToList();
                Assert.True(lots.Count >= 8);
                for (int first = 0; first < lots.Count; first++)
                {
                    for (int second = 0; second < lots.Count; second++)
                    {
                        if (first != second)
                            Assert.DoesNotContain(ShrunkCorners(lots[first]), corner => Inside(lots[second], corner));
                    }
                }
            }
        }

        [Fact]
        public void EveryPadCellIsLevelOnThePlannedHeights()
        {
            var plan = StepPlanner.Plan(Straight(0.12f, lotWidth: 3, narrowest: 2, highestStep: 3f));

            float Planned(float2 point) => plan.Pads.Where(pad => Inside(pad.Corners, point)).Select(pad => pad.Height)
                .DefaultIfEmpty(float.NaN).First();

            Assert.NotEmpty(plan.Pads);
            Assert.All(plan.Pads, pad =>
                Assert.Equal(pad.WidthCells * pad.DepthCells, StepPlanner.LevelCells(pad, Planned, 0.3f).Count));
        }

        [Fact]
        public void TiltedPadLosesItsBackCells()
        {
            var plan = StepPlanner.Plan(Straight(0.12f));
            var pad = plan.Pads[1];
            float2 front = pad.Corners[0].xz;
            float2 back = pad.Corners[3].xz;

            // 0.5 m higher at the back than the front: rows 0 and 1 stay within 0.3 m, rows 2 and 3 do not.
            float Tilted(float2 point) =>
                pad.Height + 0.5f * math.dot(point - front, back - front) / math.lengthsq(back - front);

            var cells = StepPlanner.LevelCells(pad, Tilted, 0.3f);

            Assert.Equal(pad.WidthCells * 2, cells.Count);
            Assert.All(cells, cell => Assert.True(cell.y < 2));
        }

        // A straight street climbing at the given grade along a diagonal, so nothing depends on the axes.
        internal static StepPlanRequest Straight(float grade, int lotWidth = 2, int narrowest = 2, float highestStep = 10f)
        {
            const float length = 200f;
            return new StepPlanRequest
            {
                Centreline = new[]
                {
                    new float3(100f, 40f, 100f),
                    new float3(100f + 0.6f * length, 40f + grade * length, 100f + 0.8f * length),
                },
                RoadHalfWidth = 6f,
                LotWidthCells = lotWidth,
                NarrowestWidthCells = narrowest,
                DepthCells = 4,
                HighestStep = highestStep,
            };
        }

        // 120 m north, a 45° bend to the east, then 120 m on: deep lots on the inside of the bend would overlap.
        private static StepPlanRequest Bent(float grade)
        {
            const float leg = 120f;
            float diagonal = leg * math.sqrt(0.5f);
            return new StepPlanRequest
            {
                Centreline = new[]
                {
                    new float3(0f, 0f, 0f),
                    new float3(0f, grade * leg, leg),
                    new float3(diagonal, 2f * grade * leg, leg + diagonal),
                },
                RoadHalfWidth = 6f,
                LotWidthCells = 2,
                NarrowestWidthCells = 2,
                DepthCells = 6,
                HighestStep = 10f,
            };
        }

        private static bool Inside(IReadOnlyList<float3> corners, float2 point)
        {
            float2 origin = corners[0].xz;
            float2 along = corners[1].xz - origin;
            float2 away = corners[3].xz - origin;
            float u = math.dot(point - origin, along) / math.lengthsq(along);
            float v = math.dot(point - origin, away) / math.lengthsq(away);
            return u > 0f && u < 1f && v > 0f && v < 1f;
        }

        // Corners pulled 5 cm towards the middle, so lots that only share an edge do not count as overlapping.
        private static IEnumerable<float2> ShrunkCorners(IReadOnlyList<float3> corners)
        {
            float2 middle = (corners[0].xz + corners[2].xz) * 0.5f;
            return corners.Select(corner => corner.xz + math.normalize(middle - corner.xz) * 0.05f);
        }

        private static List<string> Describe(StepPlan plan)
        {
            string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
            string Corners(IReadOnlyList<float3> corners) =>
                string.Join(" ", corners.Select(corner => $"{Number(corner.x)},{Number(corner.y)},{Number(corner.z)}"));

            var lines = new List<string>();
            lines.AddRange(plan.Pads.Select(pad =>
                $"pad {pad.Side} {Number(pad.Height)} {Number(pad.Start)} {Number(pad.End)} {pad.WidthCells}x{pad.DepthCells} {Corners(pad.Corners)}"));
            lines.AddRange(plan.Steps.Select(step =>
                $"step {step.Side} {Number(step.Rise)} {Corners(new[] { step.EdgeFront, step.EdgeBack })}"));
            lines.AddRange(plan.EmptyLots.Select(lot =>
                $"empty {lot.Side} {lot.Reason} {Number(lot.Start)} {Number(lot.End)} {Corners(lot.Corners)}"));
            lines.AddRange(plan.FlatStretches.Select(flat => $"flat {flat.Side} {Number(flat.Start)} {Number(flat.End)}"));
            var summary = plan.Summary;
            lines.Add($"summary {summary.LotCount} {Number(summary.LowestStep)} {Number(summary.TallestStep)} {summary.EmptyCount} {plan.Message}");
            return lines;
        }
    }
}
