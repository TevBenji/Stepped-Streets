// Stepped Streets, tests for the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Linq;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Planning
{
    public sealed class StepPlanPatchesTests
    {
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
            // 3 m steps; pad corners stand 1.8 m off the ground, which a 4 m blend draws as sheets.
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.15f, lotWidth: 3, narrowest: 2, highestStep: 3f));

            var (curtains, final) = Build(plan, 0.15f, StepPlanPatches.BankWidth(plan, position => Ground(0.15f, position), 0.4f));

            Assert.NotEmpty(plan.Steps);
            Assert.True(curtains.Count == 0, curtains.ToString());
            AssertKeptColumnsLevel(plan, final);
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
            Assert.Equal(plan.Pads.Count + plan.Steps.Count, patches.Count);
            for (int index = 0; index < plan.Pads.Count; index++)
            {
                var pad = plan.Pads[index];
                var patch = patches[index];
                Assert.False(patch.Sloped);
                Assert.True(patch.Permanent);
                Assert.Equal(pad.Height, patch.TargetHeight);
                Assert.All(patch.Ring, corner => Assert.Equal(pad.Height, corner.y));
                Assert.Equal(pad.Corners[0], patch.Ring[0]);
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
                Assert.Equal(expectedBankColumns, StepPlanPatches.BankColumns(step, 0.4f));
                Assert.True(bank.Sloped);
                AssertLength(expectedBankColumns * StepPlanner.CellSize, bank.Ring[0], bank.Ring[1]);
                Assert.Equal(new[] { lower[1], step.Later.Corners[0], step.Later.Corners[3], lower[2] }, bank.Ring);
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
        private static (TerrainMetrics.Curtain Curtains, Func<float2, float> Final) Build(StepPlan plan, float grade, float bank)
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
