using System;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Terrain
{
    public sealed class TerraformApplyMathTests
    {
        [Fact]
        public void ObstacleBeyondPadStillBlocksItsTerrainBlend()
        {
            var ring = new[] { new float2(0f, 0f), new float2(24f, 0f), new float2(24f, 24f), new float2(0f, 24f) };
            var raster = TerraformRaster.Build(new[] { (ring, 2f, 0) }, 1.75f, 8f);

            Assert.Throws<InvalidOperationException>(() => TerraformApplyMath.RequireClearGround(raster, point => point.y >= 28f));
            TerraformApplyMath.RequireClearGround(raster, point => point.y >= 48f);
            TerraformApplyMath.RequireClearGround(raster, null);
        }

        [Fact]
        public void ObstacleCheckIncludesBrushEdgesButIgnoresUntouchedTexels()
        {
            var raster = new TerraformRaster
            {
                Origin = float2.zero, Cell = 1.75f, Width = 2, Height = 1,
                Target = new[] { 2f, 2f }, Weight = new[] { 1f, 0f }
            };
            Assert.Throws<InvalidOperationException>(() => TerraformApplyMath.RequireClearGround(raster, point => point.x >= 5f));
            raster.Weight[0] = 0f;
            TerraformApplyMath.RequireClearGround(raster, point => true);
        }

        [Fact]
        public void Prepared_patches_copy_rings_and_keep_flat_and_sloped_targets_in_order()
        {
            var ring = new[] { new float3(0f, 10f, 0f), new float3(16f, 12f, 0f), new float3(16f, 14f, 16f) };
            var patches = new[]
            {
                new PlannedTerraform { Ring = ring, TargetHeight = 10f },
                new PlannedTerraform { Ring = ring, TargetHeight = 12f, Sloped = true }
            };

            var items = TerraformApplyMath.PreparePatches(patches, out var vertexHeights);
            ring[0] = new float3(99f);

            Assert.Equal(2, items.Count);
            Assert.Equal(new float2(0f), items[0].ring[0]);
            Assert.Equal(10f, items[0].target);
            Assert.Equal(12f, items[1].target);
            Assert.Null(vertexHeights[0]);
            Assert.Equal(new[] { 10f, 12f, 14f }, vertexHeights[1]);
        }

        [Fact]
        public void Preparing_patches_rejects_missing_or_nonfinite_geometry()
        {
            Assert.Throws<ArgumentException>(() => TerraformApplyMath.PreparePatches(null, out _));
            Assert.Throws<ArgumentException>(() => TerraformApplyMath.PreparePatches(Array.Empty<PlannedTerraform>(), out _));
            Assert.Throws<ArgumentException>(() => TerraformApplyMath.PreparePatches(new PlannedTerraform[] { null }, out _));
            Assert.Throws<ArgumentException>(() => TerraformApplyMath.PreparePatches(new[] { new PlannedTerraform() }, out _));
            var patch = new PlannedTerraform
            {
                Ring = new[] { new float3(0f), new float3(1f, 0f, 0f), new float3(1f, 0f, float.NaN) }
            };
            Assert.Throws<ArgumentException>(() => TerraformApplyMath.PreparePatches(new[] { patch }, out _));
            patch.Ring[2] = new float3(1f, 0f, 1f);
            patch.TargetHeight = float.PositiveInfinity;
            Assert.Throws<ArgumentException>(() => TerraformApplyMath.PreparePatches(new[] { patch }, out _));
        }

        [Theory]
        [InlineData(0.0021f, 0.05f, 1f)]
        [InlineData(-0.0021f, 0.05f, 1f)]
        [InlineData(0.0021f, 1f, 0.01f)]
        [InlineData(-0.15f, 2f, 0.2f)]
        public void Reversal_cancels_shift_even_when_delta_time_changes(float strength, float appliedDeltaTime, float currentDeltaTime)
        {
            float reverseStrength = TerraformApplyMath.ReverseStrength(strength, appliedDeltaTime, currentDeltaTime);
            float applied = math.clamp(appliedDeltaTime, 0.05f, 1f) * EffectiveStrength(strength) * 2000f;
            float reversed = math.clamp(currentDeltaTime, 0.05f, 1f) * EffectiveStrength(reverseStrength) * 2000f;

            Assert.InRange(math.abs(applied + reversed), 0f, math.abs(applied) * 0.00001f);
        }

        [Theory]
        [InlineData(1, 0.5f, true)]
        [InlineData(2, 0.75f, true)]
        [InlineData(1, 0.751f, false)]
        [InlineData(3, 0.5f, false)]
        public void Residual_requires_two_percent_or_fewer_inside_texels_off_and_none_far_off(int offCount, float error, bool reached)
        {
            var raster = new TerraformRaster
            {
                Origin = new float2(0f), Cell = 1f, Width = 101, Height = 1,
                Target = new float[101], Weight = new float[101]
            };
            for (int index = 0; index < 100; index++) raster.Weight[index] = 1f;
            raster.Weight[100] = 0.5f;
            float Live(float2 position) => position.x > 100f ? 20f : position.x < offCount ? error : 0f;

            raster.Deltas(Live, 0.3f, out var residual);

            Assert.Equal(100, residual.Inside);
            Assert.Equal(offCount, residual.InsideOff);
            Assert.Equal(error, residual.InsideMaxAbs);
            Assert.Equal(reached, residual.Done(0.3f, 0.02f));
        }

        private static float EffectiveStrength(float strength) => math.abs(strength) < 0.002f
            ? math.sign(strength) * 0.002f * math.pow(math.abs(strength) / 0.002f, 0.055f)
            : strength;
    }
}
