// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using SteppedStreets.Tool;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Tool
{
    public sealed class SiteGeometryTests
    {
        [Fact]
        public void GridPreservesASlopeAndClampsAtItsBoundary()
        {
            var heights = new[] { 10f, 14f, 18f, 18f, 22f, 26f };
            Assert.Equal(16f, SiteGeometry.Bilinear(heights, 3, 2, new float2(20, 40), 4f, new float2(22, 42)));
            Assert.Equal(26f, SiteGeometry.Bilinear(heights, 3, 2, new float2(20, 40), 4f, new float2(100, 100)));
            Assert.Equal(10f, SiteGeometry.Bilinear(heights, 3, 2, new float2(20, 40), 4f, float2.zero));
        }

        [Fact]
        public void RotatedLotIncludesItsBoundaryInEitherWinding()
        {
            var lot = new[] { new float2(0, 2), new float2(2, 0), new float2(0, -2), new float2(-2, 0) };
            Assert.True(SiteGeometry.Inside(float2.zero, lot));
            Assert.True(SiteGeometry.Inside(new float2(1, 1), lot));
            Assert.False(SiteGeometry.Inside(new float2(2, 2), lot));
            System.Array.Reverse(lot);
            Assert.True(SiteGeometry.Inside(float2.zero, lot));
        }

        [Fact]
        public void RoadClearanceUsesTheSegmentAndItsEnds()
        {
            Assert.Equal(9f, SiteGeometry.DistanceSquared(new float2(5, 3), float2.zero, new float2(10, 0)));
            Assert.Equal(25f, SiteGeometry.DistanceSquared(new float2(13, 4), float2.zero, new float2(10, 0)));
            Assert.Equal(25f, SiteGeometry.DistanceSquared(new float2(3, 4), float2.zero, float2.zero));
        }

        [Fact]
        public void AdditionRequiresOneChainWithoutBranches()
        {
            Assert.True(SiteGeometry.SingleChain(new[] { (1L, 2L), (3L, 2L), (3L, 4L) }));
            Assert.True(SiteGeometry.SingleChain(new[] { (1L, 2L), (2L, 3L), (3L, 1L) }));
            Assert.False(SiteGeometry.SingleChain(new[] { (1L, 2L), (3L, 4L) }));
            Assert.False(SiteGeometry.SingleChain(new[] { (1L, 2L), (2L, 3L), (2L, 4L) }));
        }

        [Fact]
        public void ARefreshedPreviewMustKeepItsPadGeometryBeforeItCanBuild()
        {
            var request = new StepPlanRequest
            {
                Centreline = new[] { new float3(0, 0, 0), new float3(240, 24, 0) },
                RoadHalfWidth = 8f, Side = StreetSide.Left, LotWidthCells = 3, NarrowestWidthCells = 3,
                DepthCells = 3, HighestStep = 3.2f,
            };
            var approved = StepPlanner.Plan(request);
            Assert.True(SiteGeometry.SamePlan(approved, StepPlanner.Plan(request)));
            request.DepthCells = 2;
            Assert.False(SiteGeometry.SamePlan(approved, StepPlanner.Plan(request)));
            request.DepthCells = 3;
            request.Centreline = new[] { new float3(0, 1, 0), new float3(240, 25, 0) };
            Assert.False(SiteGeometry.SamePlan(approved, StepPlanner.Plan(request)));
        }
    }
}
