// Stepped Streets, tests for the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using SteppedStreets.Core.Planning;
using Xunit;

namespace SteppedStreets.Tests.Planning
{
    public sealed class StreetChainTests
    {
        [Fact]
        public void EdgesChainFromADeadEndInOneDirection()
        {
            // Nodes 1-2-3-4 given out of order, two of them drawn against the street.
            var chain = StreetChain.Order(new (long, long)[] { (3, 4), (1, 2), (3, 2) });
            var loop = StreetChain.Order(new (long, long)[] { (1, 2), (2, 3), (3, 1) });

            Assert.Equal(new[] { (0, true), (2, false), (1, true) }, chain);
            Assert.Equal(new[] { (0, false), (1, false), (2, false) }, loop);
        }
    }
}
