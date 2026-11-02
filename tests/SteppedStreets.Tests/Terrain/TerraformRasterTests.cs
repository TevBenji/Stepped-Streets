using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Terrain
{
    public sealed class TerraformRasterTests
    {
        [Fact]
        public void Flat_patch_levels_inside_and_keeps_ground_beyond_bank()
        {
            const float side = 28f;
            const float targetHeight = 112f;
            const float bank = 4f;
            float Original(float2 position) => 100f + 0.12f * position.x + 0.08f * position.y;
            var ring = new[] { new float2(0f, 0f), new float2(side, 0f), new float2(side, side), new float2(0f, side) };
            var raster = TerraformRaster.Build(new[] { (ring, targetHeight, 0) }, 1.75f, bank, original: Original);

            Assert.NotNull(raster);
            Assert.Equal(1.75f, raster.Cell);
            int insideCount = 0;
            int unchangedCount = 0;
            for (int row = 0; row < raster.Height; row++)
                for (int column = 0; column < raster.Width; column++)
                {
                    var position = raster.Center(column, row);
                    int index = row * raster.Width + column;
                    float height = raster.Desired(index, Original(position));
                    if (position.x > 0f && position.x < side && position.y > 0f && position.y < side)
                    {
                        Assert.Equal(1f, raster.Weight[index]);
                        // Full-weight texels take the target exactly apart from a few float rounding units near 100 m.
                        Assert.InRange(math.abs(height - targetHeight), 0f, 0.0001f);
                        insideCount++;
                    }
                    float distance = math.length(math.max(math.max(-position, position - side), 0f));
                    if (distance > bank)
                    {
                        Assert.Equal(0f, raster.Weight[index]);
                        Assert.Equal(Original(position), height);
                        unchangedCount++;
                    }
                }
            Assert.True(insideCount > 0);
            Assert.True(unchangedCount > 0);
        }

        [Fact]
        public void Sloped_patch_matches_sculpt_without_steps()
        {
            const float rampGrade = 0.12f;
            const float tolerance = 0.0001f;
            float Original(float2 position) => 100f + 0.08f * position.x + 0.04f * position.y;
            float Ramp(float2 position) => 104f + rampGrade * position.x + 0.04f * position.y;
            var sculpt = new Sculpt(Original, new float2(0f), new float2(40f), 2f,
                position => math.all(position >= 0f) && math.all(position <= 40f));
            sculpt.Shape((position, originalHeight, currentHeight) => Ramp(position));
            var patches = new List<PlannedTerraform>();
            int emittedCount = sculpt.Emit(patches);
            var items = patches.Select(patch => (ring: patch.Ring.Select(vertex => vertex.xz).ToArray(), target: patch.TargetHeight, layer: 0)).ToArray();
            var vertexHeights = patches.Select(TerraformSurface.VertexHeights).ToArray();
            var raster = TerraformRaster.Build(items, 1.75f, 4f, vertexHeights: vertexHeights, original: Original);

            Assert.True(emittedCount > 0);
            Assert.Equal(emittedCount, patches.Count);
            Assert.All(patches, patch => Assert.True(patch.Sloped));
            Assert.NotNull(raster);
            // Mean-value interpolation and bilinear sampling both reproduce a plane; only float rounding needs slack.
            foreach (var position in new[] { new float2(5.25f, 7.25f), new float2(12.5f, 18.5f), new float2(25.75f, 30.25f), new float2(35f, 8f) })
            {
                Assert.True(raster.TrySample(position, Original, out float height));
                Assert.InRange(math.abs(height - sculpt.HeightAfter(position)), 0f, tolerance);
                Assert.InRange(math.abs(height - Ramp(position)), 0f, tolerance);
            }

            float? previousHeight = null;
            int stepCount = 0;
            int row = raster.Height / 2;
            for (int column = 0; column < raster.Width; column++)
            {
                var position = raster.Center(column, row);
                // Stay on the ramp, clear of the transition to locked border nodes and the outside bank.
                if (position.x < 4f || position.x > 36f) continue;
                float height = raster.Desired(row * raster.Width + column, Original(position));
                Assert.InRange(math.abs(height - sculpt.HeightAfter(position)), 0f, tolerance);
                if (previousHeight.HasValue)
                {
                    Assert.InRange(height - previousHeight.Value, rampGrade * raster.Cell - tolerance, rampGrade * raster.Cell + tolerance);
                    stepCount++;
                }
                previousHeight = height;
            }
            Assert.True(stepCount > 10);
        }

        [Fact]
        public void Emit_honors_cancellation()
        {
            var sculpt = new Sculpt(position => 100f, new float2(0f), new float2(8f), 2f, null);
            sculpt.Shape((position, originalHeight, currentHeight) => 102f);
            var patches = new List<PlannedTerraform>();

            Assert.Throws<System.OperationCanceledException>(() => sculpt.Emit(patches, new CancellationToken(true)));
            Assert.Empty(patches);
        }
    }
}
