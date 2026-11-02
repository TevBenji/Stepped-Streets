// Stepped Streets, tests for the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Linq;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace SteppedStreets.Tests.Planning
{
    public sealed class StepPlanPatchesTests
    {
        private readonly ITestOutputHelper output;

        public StepPlanPatchesTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [InlineData(0.08f, 3, 4f)]
        [InlineData(0.12f, 3, 4f)]
        [InlineData(0.15f, 4, 6f)]
        public void ApronsJoinTheStreetToBothTheLevelPadAndItsBank(float grade, int width, float apronDepth)
        {
            var request = StepPlannerTests.Straight(grade, width, width, highestStep: 5f);
            request.Side = StreetSide.Left;
            var plan = StepPlanner.Plan(request);
            var pad = plan.Pads[1];
            var patches = StepPlanPatches.ToPatches(plan, 0.4f);
            var aprons = patches.Where(patch => patch.Sloped && patch.Ring.Count(corner =>
                math.abs(math.distance(corner.xz, pad.PointAt(0f, 0f)) + math.distance(corner.xz, pad.PointAt(width, 0f)) - width * 8f) < 0.001f) == 2).ToList();

            Assert.Equal(2, aprons.Count);
            foreach (var apron in aprons)
            {
                Assert.Equal(Ground(grade, apron.Ring[0].xz), apron.Ring[0].y, 3);
                Assert.Equal(Ground(grade, apron.Ring[1].xz), apron.Ring[1].y, 3);
                AssertLength(apronDepth, apron.Ring[0], apron.Ring[3]);
                AssertLength(apronDepth, apron.Ring[1], apron.Ring[2]);
                foreach (var back in new[] { apron.Ring[2], apron.Ring[3], math.lerp(apron.Ring[2], apron.Ring[3], 0.5f) })
                {
                    var joined = patches.Where(patch => !aprons.Contains(patch)
                        && (patch.Ring.Any(corner => math.distance(corner, back) < 0.001f) || OnEdge(patch.Ring[0], patch.Ring[1], back))).ToList();
                    Assert.NotEmpty(joined);
                    Assert.All(joined, patch => Assert.Equal(back.y, TerraformSurface.TargetAt(patch, back.xz), 3));
                }
                Assert.True(math.abs(apron.Ring[0].y - apron.Ring[3].y) / apronDepth <= 0.4001f);
                Assert.True(math.abs(apron.Ring[1].y - apron.Ring[2].y) / apronDepth <= 0.4001f);
            }
        }

        [Theory]
        [InlineData(0.08f, 3, 102, 4.50f)]
        [InlineData(0.12f, 3, 102, 7.7143f)]
        [InlineData(0.15f, 4, 78, 14.40f)]
        public void RasterScenariosHaveNoCurtainsWithAprons(float grade, int width, int expectedLevel, float expectedBlend)
        {
            var request = StepPlannerTests.Straight(grade, width, width, highestStep: 5f);
            request.DepthCells = 3;
            var plan = StepPlanner.Plan(request);
            float bankWidth = StepPlanPatches.BankWidth(plan, point => Ground(grade, point), 0.4f);

            var (curtains, final) = Build(plan, grade, bankWidth);

            int total = plan.Pads.Sum(pad => pad.WidthCells * pad.DepthCells);
            int level = plan.Pads.Sum(pad => StepPlanner.LevelCells(pad, final, 0.3f).Count);
            output.WriteLine($"{grade:P0}: {level} of {total} cells level, {curtains.Count} curtains, blend {bankWidth:0.00} m; {curtains}");
            Assert.Equal(144, total);
            Assert.Equal(expectedLevel, level);
            Assert.InRange(bankWidth, expectedBlend - 0.01f, expectedBlend + 0.01f);
            Assert.True(curtains.Count == 0, curtains.ToString());
            if (grade <= 0.12f)
            {
                AssertKeptColumnsLevel(plan, final);
            }
        }

        [Fact]
        public void BackBankWidthAccountsForGroundContinuingToFall()
        {
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.15f, 4, 4, 5f));
            float width = StepPlanPatches.BankWidth(plan, point => Ground(0.15f, point), 0.4f);
            var pad = plan.Pads[0];
            float2 corner = pad.Corners[3].xz;
            float2 downhill = new float2(-0.6f, -0.8f);
            float offset = pad.Height - Ground(0.15f, corner);
            float previous = pad.Height;
            for (float distance = 0.05f; distance <= width; distance += 0.05f)
            {
                float fraction = distance / width;
                float height = Ground(0.15f, corner + downhill * distance) + offset * (1f - fraction * fraction * (3f - 2f * fraction));
                Assert.InRange(math.abs(height - previous) / 0.05f, 0f, 0.401f);
                previous = height;
            }
            Assert.InRange(width, 4f, 24f);
        }

        private static bool OnEdge(float3 start, float3 end, float3 point) =>
            math.abs(math.distance(start, point) + math.distance(point, end) - math.distance(start, end)) < 0.001f;

        [Fact]
        public void ApronBankEdgeStaysContinuousWhenTheNextLotNarrows()
        {
            var request = StepPlannerTests.Straight(0.1f, 6, 3, 4f);
            request.Side = StreetSide.Left;
            request.Centreline = new[] { new float3(100f, 40f, 100f), new float3(167.2f, 51.2f, 189.6f) };
            var plan = StepPlanner.Plan(request);
            var step = plan.Steps[0];
            Assert.Equal(6, step.Earlier.WidthCells);
            Assert.Equal(4, step.Later.WidthCells);
            var patches = StepPlanPatches.ToPatches(plan, 0.4f);
            var bank = patches[plan.Pads.Count];
            var apron = Assert.Single(patches, patch => patch.Ring[0].xz.Equals(step.Earlier.PointAt(4f, 0f)));
            for (int index = 0; index <= 8; index++)
            {
                float3 edge = math.lerp(apron.Ring[3], apron.Ring[2], index / 8f);
                Assert.True(OnEdge(bank.Ring[0], bank.Ring[1], edge), $"apron back {edge} misses the bank front");
                Assert.Equal(edge.y, TerraformSurface.TargetAt(bank, edge.xz), 3);
            }
            var nextApron = Assert.Single(patches, patch => patch.Ring[0].xz.Equals(step.Later.PointAt(0f, 0f)));
            Assert.InRange(math.distance(apron.Ring[2], nextApron.Ring[3]), 0f, 0.001f);
        }

        [Fact]
        public void ApronFollowsAChangeOfStreetGradeWithinTheLot()
        {
            var request = StepPlannerTests.Straight(0.1f, 3, 3);
            request.Side = StreetSide.Left;
            request.Centreline = new[]
            {
                new float3(0f, 40f, 0f), new float3(8f, 40.5f, 0f), new float3(24f, 42.5f, 0f),
            };
            var plan = StepPlanner.Plan(request);
            var pad = Assert.Single(plan.Pads);

            var patches = StepPlanPatches.ToPatches(plan, 0.4f);

            Assert.Contains(patches.SelectMany(patch => patch.Ring), corner => corner.x == 8f && corner.y == 40.5f && corner.z == 6f);
            foreach (var patch in patches.Skip(1))
                foreach (var front in patch.Ring.Take(2))
                    Assert.Equal(front.x <= 8f ? 40f + front.x * 0.0625f : 40.5f + (front.x - 8f) * 0.125f, front.y, 3);
            Assert.Equal(pad.Corners, StepPlanner.Plan(request).Pads[0].Corners);
            Assert.Equal(patches.SelectMany(patch => patch.Ring), StepPlanPatches.ToPatches(plan, 0.4f).SelectMany(patch => patch.Ring));
        }

        [Theory]
        [InlineData(0f, 4f)]
        [InlineData(100f, 24f)]
        public void BlendWidthsKeepTheSpecifiedLimits(float groundOffset, float expectedWidth)
        {
            var request = StepPlannerTests.Straight(0.1f, 3, 3);
            request.Centreline = new[] { new float3(0f, 40f, 0f), new float3(24f, 42.4f, 0f) };
            var plan = StepPlanner.Plan(request);
            float width = StepPlanPatches.BankWidth(plan, point => plan.Pads[0].Height + groundOffset, 0.4f);
            Assert.Equal(expectedWidth, width);
        }

        [Fact]
        public void BanksTakeOneWholeColumnFromTheLowerPadAtTwelvePercent()
        {
            // 12 % with 3-cell lots steps 2.88 m, under one column's 3.2 m at a bank grade of 0.4.
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.12f, lotWidth: 3, narrowest: 3));

            AssertBanksTakeWholeColumns(plan, expectedBankColumns: 1);
        }

        [Fact]
        public void BanksTakeTwoWholeColumnsAtFifteenPercentWithFourCellLots()
        {
            // 15 % with 4-cell lots steps 4.8 m: two columns at a bank grade of 0.4, leaving each lower pad two to zone.
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.15f, lotWidth: 4, narrowest: 4, highestStep: 5f));

            AssertBanksTakeWholeColumns(plan, expectedBankColumns: 2);
            Assert.All(plan.Pads, pad => Assert.True(KeptColumns(plan, pad) >= 2));
        }

        [Theory]
        [InlineData(3, 0.4f, 3.2f)]
        [InlineData(2, 0.4f, 0f)]
        public void HighestStepForLeavesTheNarrowestPadTwoZonedColumns(int narrowest, float bankGrade, float expected)
        {
            Assert.InRange(StepPlanPatches.HighestStepFor(narrowest, bankGrade), expected - 0.0001f, expected + 0.0001f);
        }

        [Fact]
        public void SteppedTwelvePercentStreetBuildsWithoutCurtainsAndKeepsItsColumnsLevel()
        {
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.12f, lotWidth: 3, narrowest: 3));

            var (curtains, final) = Build(plan, 0.12f, bank: 4f);

            Assert.NotEmpty(plan.Steps);
            Assert.True(curtains.Count == 0, curtains.ToString());
            AssertKeptColumnsLevel(plan, final);
        }

        [Fact]
        public void SteppedFifteenPercentStreetBuildsWithoutCurtainsAtTheComputedBankWidth()
        {
            // R4 needs four columns at 15%; the 6 m apron and the blend must also work at four rows deep.
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.15f, lotWidth: 4, narrowest: 4, highestStep: 5f));

            var (curtains, final) = Build(plan, 0.15f, StepPlanPatches.BankWidth(plan, position => Ground(0.15f, position), 0.4f));

            Assert.NotEmpty(plan.Steps);
            Assert.True(curtains.Count == 0, curtains.ToString());
            Assert.NotEmpty(plan.Pads.SelectMany(pad => StepPlanner.LevelCells(pad, final, 0.3f)));
        }

        [Fact]
        public void FlatStreetGivesNoPatches()
        {
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.03f));

            Assert.Empty(StepPlanPatches.ToPatches(plan, 0.4f));
        }

        [Fact]
        public void CellQuadsCoverEachRunOfLevelCellsInARow()
        {
            var request = StepPlannerTests.Straight(0.12f, lotWidth: 3, narrowest: 3);
            request.DepthCells = 3;
            var pad = StepPlanner.Plan(request).Pads[1];
            // The last column is not level, so each row is one run of two cells.
            var cells = Enumerable.Range(0, 3).SelectMany(row => new[] { new int2(0, row), new int2(1, row) });

            var quads = StepPlanPatches.CellQuads(pad, cells);

            Assert.Equal(3, quads.Count);
            for (int row = 0; row < 3; row++)
            {
                var quad = quads[row];
                Assert.InRange(math.distance(quad[0], quad[1]), 14f - 0.001f, 14f + 0.001f);
                Assert.InRange(math.distance(quad[1], quad[2]), 6f - 0.001f, 6f + 0.001f);
                Assert.InRange(math.distance(quad[2], quad[3]), 14f - 0.001f, 14f + 0.001f);
                float2 centre = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
                Assert.InRange(math.distance(centre, pad.PointAt(1f, row + 0.5f)), 0f, 0.001f);
            }
        }

        // Each step's bank spans its whole columns at the lower pad's uphill end, at the lower pad's height on its downhill
        // edge and the upper pad's on the edge it shares with the upper pad; the lower pad's level patch keeps the rest.
        private static void AssertBanksTakeWholeColumns(StepPlan plan, int expectedBankColumns)
        {
            var patches = StepPlanPatches.ToPatches(plan, 0.4f);

            Assert.NotEmpty(plan.Steps);
            Assert.Equal(2 * (plan.Pads.Count + plan.Steps.Count), patches.Count);
            for (int index = 0; index < plan.Pads.Count; index++)
            {
                var pad = plan.Pads[index];
                var patch = patches[index];
                // Equal vertex heights make a flat surface while keeping offset blending consistent with the aprons.
                Assert.True(patch.Sloped);
                Assert.True(patch.Permanent);
                Assert.Equal(pad.Height, patch.TargetHeight);
                Assert.All(patch.Ring, corner => Assert.Equal(pad.Height, corner.y));
                AssertLength(pad.WidthCells == 4 ? 6f : 4f, pad.Corners[0], patch.Ring[0]);
                Assert.Equal(pad.Corners[3], patch.Ring[3]);
                bool stepAfter = plan.Steps.Any(step => step.Earlier == pad);
                AssertLength((pad.WidthCells - (stepAfter ? expectedBankColumns : 0)) * StepPlanner.CellSize, patch.Ring[0], patch.Ring[1]);
            }

            var pads = plan.Pads.ToList();
            for (int index = 0; index < plan.Steps.Count; index++)
            {
                var step = plan.Steps[index];
                var bank = patches[plan.Pads.Count + index];
                var lower = patches[pads.IndexOf(step.Earlier)].Ring;
                var upper = patches[pads.IndexOf(step.Later)].Ring;
                Assert.Equal(expectedBankColumns, StepPlanPatches.BankColumns(step, 0.4f));
                Assert.True(bank.Sloped);
                AssertLength(expectedBankColumns * StepPlanner.CellSize, bank.Ring[0], bank.Ring[1]);
                Assert.Equal(new[] { lower[1], upper[0], upper[3], lower[2] }, bank.Ring);
                Assert.Equal(new[] { step.Earlier.Height, step.Later.Height, step.Later.Height, step.Earlier.Height },
                    TerraformSurface.VertexHeights(bank));
            }
        }

        private static void AssertLength(float expected, float3 from, float3 to) =>
            Assert.InRange(math.distance(from.xz, to.xz), expected - 0.01f, expected + 0.01f);

        private static int KeptColumns(StepPlan plan, Pad pad)
        {
            var stepAfter = plan.Steps.FirstOrDefault(step => step.Earlier == pad);
            return pad.WidthCells - (stepAfter == null ? 0 : StepPlanPatches.BankColumns(stepAfter, 0.4f));
        }

        // R8 on the built ground: every column a pad keeps comes out level all the way back, and no bank column does. A pad
        // left one column (a 2-cell pad here) loses it to the game's lone-column rule.
        private static void AssertKeptColumnsLevel(StepPlan plan, Func<float2, float> final)
        {
            foreach (var pad in plan.Pads)
            {
                int kept = KeptColumns(plan, pad);
                var expected = Enumerable.Range(0, kept < StepPlanner.MinimumWidthCells ? 0 : kept).SelectMany(column =>
                    Enumerable.Range(0, pad.DepthCells).Select(row => new int2(column, row)));
                Assert.Equal(expected, StepPlanner.LevelCells(pad, final, 0.3f));
            }
        }

        // The ground climbs with the Straight street (from (100, 100) towards (0.6, 0.8)) and is flat across it.
        private static float Ground(float grade, float2 position) =>
            40f + grade * math.dot(position - new float2(100f, 100f), new float2(0.6f, 0.8f));

        // The plan's patches through the build's raster (1.75 m cells): the curtains measured against the ground they
        // replace, and the ground as built.
        private (TerrainMetrics.Curtain Curtains, Func<float2, float> Final) Build(StepPlan plan, float grade, float bank)
        {
            var patches = StepPlanPatches.ToPatches(plan, 0.4f);
            float Original(float2 position) => Ground(grade, position);
            var items = patches.Select(patch => (ring: patch.Ring.Select(corner => corner.xz).ToArray(), target: patch.TargetHeight, layer: 0)).ToArray();
            var raster = TerraformRaster.Build(items, 1.75f, bank, vertexHeights: patches.Select(TerraformSurface.VertexHeights).ToArray(),
                original: Original);
            float Final(float2 position) => raster.TrySample(position, Original, out float height) ? height : Original(position);
            var curtains = TerrainMetrics.Curtains(Final, Original, raster.Origin, raster.Origin + new float2(raster.Width, raster.Height) * raster.Cell);
            return (curtains, Final);
        }
    }
}
