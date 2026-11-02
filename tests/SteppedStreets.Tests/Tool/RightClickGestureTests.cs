// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using SteppedStreets.Tool;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Tool
{
    public sealed class RightClickGestureTests
    {
        [Fact]
        public void AShortClickFiresOnceOnRelease()
        {
            var gesture = new RightClickGesture();
            Assert.False(gesture.Update(true, true, float2.zero, true, 1f));
            Assert.True(gesture.Update(false, false, new float2(1, 1), true, 1.2f));
            Assert.False(gesture.Update(false, false, float2.zero, true, 1.3f));
        }

        [Theory]
        [InlineData(8f, 0.2f, true)]
        [InlineData(0f, 0.5f, true)]
        [InlineData(0f, 0.2f, false)]
        public void CameraDragsLongHoldsAndReleasesOverUiAreIgnored(float movement, float duration, bool overWorld)
        {
            var gesture = new RightClickGesture();
            gesture.Update(true, true, float2.zero, true, 0f);
            Assert.False(gesture.Update(false, false, new float2(movement, 0), overWorld, duration));
        }

        [Fact]
        public void StoppingTheToolCancelsTheGesture()
        {
            var gesture = new RightClickGesture();
            gesture.Update(true, true, float2.zero, true, 0f);
            gesture.Reset();
            Assert.False(gesture.Update(false, false, float2.zero, true, 0.2f));
        }
    }
}
