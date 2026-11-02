// Stepped Streets, tests for bank foot walls. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Linq;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Planning
{
    public sealed class StepPlanWallsTests
    {
        [Theory]
        [InlineData(StreetSide.Left, false)]
        [InlineData(StreetSide.Right, false)]
        [InlineData(StreetSide.Left, true)]
        [InlineData(StreetSide.Right, true)]
        public void TwelvePercentWallsFollowTheBankFootWithTheBankOnTheirRight(StreetSide side, bool reverse)
        {
            var request = StepPlannerTests.Straight(0.12f, lotWidth: 3, narrowest: 3);
            request.DepthCells = 3;
            request.Side = side;
            if (reverse) request.Centreline = request.Centreline.Reverse().ToArray();
            var plan = StepPlanner.Plan(request);

            var walls = StepPlanWalls.Segments(plan, 0.4f);

            Assert.NotEmpty(plan.Steps);
            Assert.Equal(plan.Steps.Count, walls.Count);
            for (int index = 0; index < walls.Count; index++)
            {
                var pad = plan.Steps[index].Earlier;
                var wall = walls[index];
                Assert.InRange(math.distance(wall.Start, wall.End), 19.999f, 20.001f);
                Assert.Equal(pad.Height, wall.Start.y);
                Assert.Equal(pad.Height, wall.End.y);
                var front = pad.PointAt(2f, 0.5f);
                var back = pad.PointAt(2f, 3f);
                Assert.True(Matches(wall.Start.xz, front) && Matches(wall.End.xz, back)
                    || Matches(wall.Start.xz, back) && Matches(wall.End.xz, front));
                AssertBankOnRight(wall, pad.PointAt(2.5f, 1.5f));
            }
        }

        [Fact]
        public void WiderBankStartsAtTheBoundaryAfterTheKeptColumns()
        {
            var request = StepPlannerTests.Straight(0.15f, lotWidth: 4, narrowest: 4, highestStep: 5f);
            request.DepthCells = 3;
            var plan = StepPlanner.Plan(request);

            var walls = StepPlanWalls.Segments(plan, 0.4f);

            Assert.NotEmpty(walls);
            Assert.Equal(plan.Steps.Count, walls.Count);
            for (int index = 0; index < walls.Count; index++)
            {
                var pad = plan.Steps[index].Earlier;
                var foot = pad.PointAt(2f, 1.75f);
                Assert.InRange(math.distance(foot, (walls[index].Start.xz + walls[index].End.xz) * 0.5f), 0f, 0.001f);
                AssertBankOnRight(walls[index], pad.PointAt(3f, 1.75f));
            }
        }

        [Fact]
        public void PlanWithoutStepsHasNoWalls()
        {
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.03f));

            Assert.Empty(StepPlanWalls.Segments(plan, 0.4f));
        }

        private static bool Matches(float2 first, float2 second) => math.distance(first, second) < 0.001f;

        private static void AssertBankOnRight(WallSegment wall, float2 bankPoint)
        {
            float2 direction = wall.End.xz - wall.Start.xz;
            // The game's plan uses (x right, z forward): the right normal is (z, -x).
            Assert.True(math.dot(new float2(direction.y, -direction.x), bankPoint - wall.Start.xz) > 0f);
        }
    }
}
