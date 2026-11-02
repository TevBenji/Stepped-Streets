using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests
{
    public sealed class UnityMathematicsSmokeTests
    {
        [Fact]
        public void Float2AdditionLoadsGameAssembly()
        {
            var result = new float2(1f, 2f) + new float2(3f, 4f);

            Assert.Equal(new float2(4f, 6f), result);
        }
    }
}
