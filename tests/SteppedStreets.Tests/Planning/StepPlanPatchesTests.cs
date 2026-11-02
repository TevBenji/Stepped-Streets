// Stepped Streets, tests for the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
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
        public void BanksTakeRiseOverBankGradeHalfFromEachPadAndMeetThePadHeights()
        {
            // 12 % with 3-cell lots steps 2.88 m; at a bank grade of 0.4 each bank is 7.2 m, 3.6 m taken from each pad.
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.12f, lotWidth: 3, narrowest: 3));

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
                bool stepBefore = plan.Steps.Any(step => step.Later == pad);
                bool stepAfter = plan.Steps.Any(step => step.Earlier == pad);
                float expectedLength = 24f - (stepBefore ? 3.6f : 0f) - (stepAfter ? 3.6f : 0f);
                Assert.InRange(math.distance(patch.Ring[0].xz, patch.Ring[1].xz), expectedLength - 0.01f, expectedLength + 0.01f);
            }

            for (int index = 0; index < plan.Steps.Count; index++)
            {
                var step = plan.Steps[index];
                var bank = patches[plan.Pads.Count + index].Ring;
                var earlier = patches[plan.Pads.ToList().IndexOf(step.Earlier)].Ring;
                var later = patches[plan.Pads.ToList().IndexOf(step.Later)].Ring;
                Assert.True(patches[plan.Pads.Count + index].Sloped);
                Assert.InRange(math.distance(bank[0].xz, bank[1].xz), 7.2f - 0.01f, 7.2f + 0.01f);
                Assert.Equal(new[] { earlier[1], later[0], later[3], earlier[2] }, bank);
            }
        }

        [Fact]
        public void SteppedTwelvePercentStreetBuildsWithoutCurtains()
        {
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.12f, lotWidth: 3, narrowest: 3));
            var patches = StepPlanPatches.ToPatches(plan, 0.4f);
            // The ground climbs with the street (from (100, 100) towards (0.6, 0.8) at 12 %) and is flat across it.
            float Ground(float2 position) => 40f + 0.12f * math.dot(position - new float2(100f, 100f), new float2(0.6f, 0.8f));
            var items = patches.Select(patch => (ring: patch.Ring.Select(corner => corner.xz).ToArray(), target: patch.TargetHeight, layer: 0)).ToArray();

            var raster = TerraformRaster.Build(items, 1.75f, 4f, vertexHeights: patches.Select(TerraformSurface.VertexHeights).ToArray(),
                original: Ground);
            float Final(float2 position) => raster.TrySample(position, Ground, out float height) ? height : Ground(position);
            var curtains = TerrainMetrics.Curtains(Final, Ground, raster.Origin,
                raster.Origin + new float2(raster.Width, raster.Height) * raster.Cell);

            Assert.NotEmpty(plan.Steps);
            Assert.True(curtains.Count == 0, curtains.ToString());
        }

        [Fact]
        public void FlatStreetGivesNoPatches()
        {
            var plan = StepPlanner.Plan(StepPlannerTests.Straight(0.03f));

            Assert.Empty(StepPlanPatches.ToPatches(plan, 0.4f));
        }

        [Fact]
        public void CellQuadsAreSixMetreSquaresOnTheirCellCentres()
        {
            var pad = StepPlanner.Plan(StepPlannerTests.Straight(0.12f)).Pads[1];
            var cells = StepPlanner.LevelCells(pad, position => pad.Height, 0.3f);

            var quads = StepPlanPatches.CellQuads(pad, cells);

            Assert.Equal(pad.WidthCells * pad.DepthCells, quads.Count);
            for (int index = 0; index < quads.Count; index++)
            {
                var quad = quads[index];
                for (int corner = 0; corner < 4; corner++)
                    Assert.InRange(math.distance(quad[corner], quad[(corner + 1) % 4]), 6f - 0.001f, 6f + 0.001f);
                float2 centre = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
                Assert.InRange(math.distance(centre, pad.PointAt(cells[index].x + 0.5f, cells[index].y + 0.5f)), 0f, 0.001f);
            }
        }
    }
}
