// Stepped Streets, tests for site checks and preview messages. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Globalization;
using System.Linq;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Planning
{
    public sealed class StepPlannerSiteTests
    {
        [Theory]
        [InlineData(24f, 6f, 6f)]
        [InlineData(24f, 6f, -6f)]
        [InlineData(28f, 10f, 5f)]
        [InlineData(28f, 10f, -5f)]
        public void EarthworkChecksCornersAndCellCentresAndBreaksTheRun(float x, float z, float offset)
        {
            var request = Straight();
            request.GroundHeight = point => Ground(point) + (math.distance(point, new float2(x, z)) < 0.1f ? offset : 0f);

            var plan = StepPlanner.Plan(request);

            var empty = Assert.Single(plan.EmptyLots, lot => lot.Start == 24f);
            Assert.Equal(EmptyLotReason.TooMuchEarthwork, empty.Reason);
            Assert.Equal("too much earthwork", empty.Wording);
            Assert.Contains(plan.Pads, pad => pad.Start == 48f);
            Assert.DoesNotContain(plan.Steps, step => step.Later.Start == 48f);
            Assert.Equal(plan.EmptyLots.Count(lot => lot.Reason != EmptyLotReason.LeftOver), plan.Summary.EmptyCount);
        }

        [Fact]
        public void EarthworkAtTheLimitIsAllowedAndNullSkipsIt()
        {
            var request = Straight(length: 24f);
            request.GroundHeight = point => 37.2f;
            Assert.Single(StepPlanner.Plan(request).Pads);

            request.EarthworkLimit = 0f;
            Assert.Empty(StepPlanner.Plan(request).Pads);
            request.GroundHeight = null;
            Assert.Single(StepPlanner.Plan(request).Pads);
        }

        [Theory]
        [InlineData(36f, 34f, 3)]
        [InlineData(36f, 26f, 2)]
        [InlineData(32f, 30f, 2)]
        public void ObstaclesShortenTheWholePadBeforeTheFirstBlockedRow(float x, float z, int depth)
        {
            var request = Straight();
            request.Blocked = point => math.distance(point, new float2(x, z)) < 0.1f;

            var plan = StepPlanner.Plan(request);

            var pad = Assert.Single(plan.Pads, lot => lot.Start == 24f);
            Assert.Equal(depth, pad.DepthCells);
            Assert.InRange(math.distance(pad.Corners[0].xz, pad.Corners[3].xz), depth * 8f - 0.001f, depth * 8f + 0.001f);
            Assert.All(plan.Pads.Where(lot => lot.Start != 24f), lot => Assert.Equal(4, lot.DepthCells));
        }

        [Theory]
        [InlineData(10f)]
        [InlineData(18f)]
        public void ABlockedFrontOrSecondRowLeavesAGapAndBreaksTheRun(float z)
        {
            var request = Straight();
            request.Blocked = point => math.distance(point, new float2(36f, z)) < 0.1f;

            var plan = StepPlanner.Plan(request);

            var empty = Assert.Single(plan.EmptyLots);
            Assert.Equal(EmptyLotReason.Blocked, empty.Reason);
            Assert.Equal("blocked by a building, road or water", empty.Wording);
            Assert.DoesNotContain(plan.Steps, step => step.Later.Start == 48f);
            Assert.Equal(1, plan.Summary.EmptyCount);
            Assert.Equal("1 lot left empty: blocked by a building, road or water.", Assert.Single(plan.Messages));
        }

        [Fact]
        public void EarthworkChecksTheShortenedPadRatherThanDiscardedRows()
        {
            var request = Straight(length: 24f);
            request.Blocked = point => point.y > 30.1f;
            request.GroundHeight = point => point.y > 30.1f ? Ground(point) + 20f : Ground(point);

            Assert.Equal(3, Assert.Single(StepPlanner.Plan(request).Pads).DepthCells);
        }

        [Fact]
        public void SummaryUsesPluralsStepRangeAndAllEmptyReasons()
        {
            var request = Straight(length: 192f);
            request.GroundHeight = point => (point.x <= 96f ? Ground(point) : 49.6f + (point.x - 96f) * 0.2f)
                + (point.x > 27f && point.x < 45f ? 20f : 0f);
            request.Blocked = point => point.x > 75f && point.x < 93f;
            request.Centreline = new[] { new float3(0f, 40f, 0f), new float3(96f, 49.6f, 0f), new float3(192f, 68.8f, 0f) };

            var plan = StepPlanner.Plan(request);

            Assert.Equal(4, plan.Summary.EmptyCount);
            Assert.Equal("4 lots, no steps, 4 lots left empty (too steep for this width; too much earthwork; blocked by a building, road or water)", plan.SummaryLine);
            Assert.Equal(new[]
            {
                "2 lots left empty: too steep for lots this wide. Adjust lot width, bank grade or highest step.",
                "1 lot left empty: too much earthwork (over 4 m). Raise the earthwork limit.",
                "1 lot left empty: blocked by a building, road or water.",
            }, plan.Messages);
        }

        [Fact]
        public void SummaryHandlesNoStepsLeftoversAndInvariantNumbers()
        {
            var request = Straight(length: 30f);
            var plan = StepPlanner.Plan(request);
            Assert.Equal("1 lot, no steps", plan.SummaryLine);
            Assert.Equal(0, plan.Summary.EmptyCount);
            Assert.Empty(plan.Messages);

            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                request = Straight(length: 72f);
                request.Centreline = new[] { new float3(0f, 40f, 0f), new float3(24f, 41.6f, 0f), new float3(72f, 47.36f, 0f) };
                Assert.Equal("3 lots, steps 2.2-2.9 m", StepPlanner.Plan(request).SummaryLine);
                request.EarthworkLimit = 2.5f;
                request.GroundHeight = point => 0f;
                Assert.Equal("3 lots left empty: too much earthwork (over 2.5 m). Raise the earthwork limit.", Assert.Single(StepPlanner.Plan(request).Messages));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Fact]
        public void FlatterMessageAlsoAppearsInMessages()
        {
            var request = Straight();
            request.MinimumGrade = 0.12f;
            var plan = StepPlanner.Plan(request);
            Assert.Equal("This street is flatter than 12%: nothing to step here.", Assert.Single(plan.Messages));
            Assert.Equal(plan.Message, plan.Messages[0]);
            Assert.Equal("0 lots, no steps", plan.SummaryLine);
        }

        [Fact]
        public void SiteSamplersAndMessagesAreDeterministic()
        {
            var request = Straight();
            request.GroundHeight = Ground;
            request.Blocked = point => point.x > 30f && point.x < 40f && point.y > 30.1f;
            var first = StepPlanner.Plan(request);
            var second = StepPlanner.Plan(request);
            Assert.Equal(first.SummaryLine, second.SummaryLine);
            Assert.Equal(first.Messages, second.Messages);
            Assert.Equal(first.Pads.SelectMany(pad => pad.Corners), second.Pads.SelectMany(pad => pad.Corners));
            Assert.Equal(first.Pads.Select(pad => pad.DepthCells), second.Pads.Select(pad => pad.DepthCells));
        }

        private static float Ground(float2 point) => 40f + point.x * 0.1f;

        private static StepPlanRequest Straight(float length = 96f) => new StepPlanRequest
        {
            Centreline = new[] { new float3(0f, 40f, 0f), new float3(length, 40f + length * 0.1f, 0f) },
            Side = StreetSide.Left, RoadHalfWidth = 6f, LotWidthCells = 3, NarrowestWidthCells = 3, DepthCells = 4,
            HighestStep = 10f,
        };
    }
}
