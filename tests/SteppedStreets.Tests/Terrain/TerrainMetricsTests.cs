using SteppedStreets.Core.Terrain;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Terrain
{
    public sealed class TerrainMetricsTests
    {
        [Fact]
        public void Curtains_count_a_cliff_but_not_a_twenty_metre_ramp()
        {
            float Original(float2 position) => 100f;
            float Cliff(float2 position) => 100f + (position.x >= 0f ? 2.5f : 0f);
            float Ramp(float2 position) => 100f + 2.5f * math.saturate(position.x / 20f);
            var minimum = new float2(-7f, -7f);
            var maximum = new float2(28f, 7f);

            var cliff = TerrainMetrics.Curtains(Cliff, Original, minimum, maximum);
            var ramp = TerrainMetrics.Curtains(Ramp, Original, minimum, maximum);

            Assert.True(cliff.Count > 0);
            Assert.Equal(2.5f, cliff.Max);
            Assert.Equal(0, ramp.Count);
            // A 2.5 m rise over 20 m adds at most 0.4375 m over a 3.5 m texel, below both curtain thresholds.
            Assert.InRange(ramp.Max, 0.4f, 0.4375f + 0.0001f);
        }
    }
}
