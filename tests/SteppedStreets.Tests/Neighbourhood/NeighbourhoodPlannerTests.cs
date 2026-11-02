// Stepped Streets, tests for the hillside neighbourhood generator. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;
using Xunit;

namespace SteppedStreets.Tests.Neighbourhood
{
    public sealed class NeighbourhoodPlannerTests
    {
        public static IEnumerable<object[]> Sites()
        {
            foreach (var site in SyntheticSites.All()) yield return new object[] { site.Name, "" };
            yield return new object[] { "cone", "terraced" };
            yield return new object[] { "cone", "lane" };
            yield return new object[] { "slope-18", "steep-allowed" };
            yield return new object[] { "noisy-hill", "no-road" };
        }

        // The three rule tests read the same plans; planning each site once keeps the suite quick.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<(NeighbourhoodRequest, NeighbourhoodPlan)>> Plans =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<(NeighbourhoodRequest, NeighbourhoodPlan)>>();

        private static (NeighbourhoodRequest Request, NeighbourhoodPlan Plan) Planned(string name, string variant) =>
            Plans.GetOrAdd(name + "/" + variant, _ => new Lazy<(NeighbourhoodRequest, NeighbourhoodPlan)>(() => PlanSite(name, variant))).Value;

        private static (NeighbourhoodRequest Request, NeighbourhoodPlan Plan) PlanSite(string name, string variant)
        {
            var site = SyntheticSites.ByName(name);
            if (variant == "no-road") site.Road = false;
            var request = site.Request(r =>
            {
                if (variant == "terraced") r.Style = NeighbourhoodStyle.TerracedBlocks;
                if (variant == "lane") r.Style = NeighbourhoodStyle.MountainLane;
                if (variant == "steep-allowed") r.SteepestBuildable = 0.22f;
            });
            return (request, NeighbourhoodPlanner.Plan(request));
        }

        [Theory]
        [MemberData(nameof(Sites))]
        public void RoadsKeepTheirGradesRadiiAndJunctionRules(string name, string variant)
        {
            var (request, plan) = Planned(name, variant);
            float streetRadius = NeighbourhoodPlanner.MinimumRadius(request.Style), linkRadius = NeighbourhoodPlanner.LinkRadius(request.Style);
            float contourRun = 0f, contourRise = 0f;
            foreach (var road in plan.Roads)
            {
                float limit = road.Kind == RoadKind.Contour ? 0.03f : request.MaxRoadGrade;
                Assert.True(road.SteepestGrade <= limit + 0.005f, $"{road.Kind} road at {road.SteepestGrade:P1} over {limit:P0}");
                if (road.Kind == RoadKind.Contour)
                    for (int index = 1; index < road.Points.Length; index++)
                    {
                        contourRun += math.distance(road.Points[index].xz, road.Points[index - 1].xz);
                        contourRise += math.abs(road.Points[index].y - road.Points[index - 1].y);
                    }
                var line = road.Points.Select(point => point.xz).ToList();
                float radius = road.Kind == RoadKind.Contour ? streetRadius : linkRadius;
                if (line.Count >= 7) Assert.True(Geometry.TightestRadius(line, 12f, out _) >= radius * 0.95f, $"{road.Kind} road tighter than {radius} m");
                Assert.Equal(plan.Nodes[road.StartNode].Position, road.Points[0]);
                Assert.Equal(plan.Nodes[road.EndNode].Position, road.Points[road.Points.Length - 1]);
            }
            if (contourRun > 0f) Assert.True(contourRise / contourRun < 0.03f, $"contour streets average {contourRise / contourRun:P1}");

            var junctions = new List<int>();
            for (int node = 0; node < plan.Nodes.Count; node++)
            {
                var directions = new List<float2>();
                foreach (var road in plan.Roads)
                {
                    var line = road.Points.Select(point => point.xz).ToList();
                    if (road.StartNode == node) directions.Add(RoadNetwork.EndDirection(line, true));
                    if (road.EndNode == node) directions.Add(RoadNetwork.EndDirection(line, false));
                }
                Assert.Equal(plan.Nodes[node].Degree, directions.Count);
                if (directions.Count < 3) continue;
                junctions.Add(node);
                for (int first = 0; first < directions.Count; first++)
                    for (int second = first + 1; second < directions.Count; second++)
                        Assert.True(math.degrees(math.acos(math.clamp(math.dot(directions[first], directions[second]), -1f, 1f))) >= 59f, "a junction sharper than 60°");
            }
            for (int first = 0; first < junctions.Count; first++)
                for (int second = first + 1; second < junctions.Count; second++)
                    Assert.True(math.distance(plan.Nodes[junctions[first]].Position.xz, plan.Nodes[junctions[second]].Position.xz) >= 39f, "junctions closer than 40 m");
        }

        [Theory]
        [MemberData(nameof(Sites))]
        public void RoadsKeepOffWaterBuildingsExistingRoadsAndEachOther(string name, string variant)
        {
            var (request, plan) = Planned(name, variant);
            float half = request.RoadHalfWidth;
            var attachments = plan.Nodes.Where(node => node.ExistingRoadId != 0).Select(node => node.Position.xz).ToList();
            foreach (var road in plan.Roads)
                foreach (var point in road.Points)
                {
                    if (request.Blocked != null) Assert.False(request.Blocked(point.xz), $"road through water or a building at {point}");
                    if (attachments.Any(attachment => math.distance(attachment, point.xz) < 40f)) continue;
                    foreach (var existing in request.ExistingRoads)
                        for (int index = 1; index < existing.Points.Length; index++)
                            Assert.True(Geometry.DistanceToSegment(point.xz, existing.Points[index - 1].xz, existing.Points[index].xz) > existing.HalfWidth + half,
                                $"road crosses an existing road at {point}");
                }
            // Roads only meet at their nodes.
            for (int first = 0; first < plan.Roads.Count; first++)
                for (int second = first + 1; second < plan.Roads.Count; second++)
                {
                    var a = plan.Roads[first];
                    var b = plan.Roads[second];
                    var shared = new[] { a.StartNode, a.EndNode }.Intersect(new[] { b.StartNode, b.EndNode }).Select(node => plan.Nodes[node].Position.xz).ToList();
                    foreach (var p in a.Points)
                    {
                        if (shared.Any(node => math.distance(node, p.xz) < half * 2f + 14f)) continue;
                        foreach (var q in b.Points) Assert.True(math.distance(p.xz, q.xz) >= half * 2f, $"two roads cross at {p}");
                    }
                }
        }

        [Theory]
        [MemberData(nameof(Sites))]
        public void LotsAreBuildableLevelAndTheGroundIsClean(string name, string variant)
        {
            var (request, plan) = Planned(name, variant);
            if (plan.Roads.Count == 0) return;
            var survey = new SiteSurvey(request);
            foreach (var lot in plan.Lots)
            {
                Assert.NotEmpty(lot.LevelCells);
                foreach (var cell in lot.LevelCells)
                {
                    var centre = lot.Pad.PointAt(cell.x + 0.5f, cell.y + 0.5f);
                    Assert.True(Poly.Contains(request.Boundary, centre), "a lot outside the area");
                    Assert.True(survey.SlopeAt(centre) <= request.SteepestBuildable + 0.005f, "a lot on ground too steep to build");
                    // R8: row 0's front is the driveway down (or up) to the street, so it is checked at its back quarter.
                    var level = lot.Pad.PointAt(cell.x + 0.5f, cell.y == 0 ? 0.8f : cell.y + 0.5f);
                    Assert.True(math.abs(plan.Ground.HeightAt(level) - lot.Pad.Height) <= 0.3f, "a zoned cell is not level");
                }
            }

            var field = plan.Ground;
            float2 low = field.Origin, high = field.Origin + new float2(field.Columns - 1, field.Rows - 1) * field.Cell;
            var curtains = TerrainMetrics.Curtains(field.HeightAt, request.GroundHeight, low, high);
            Assert.True(curtains.Count == 0, $"{curtains} at {curtains.At}");

            // Where the ground changed, no slope is steeper than the bank grade (or the hillside already was).
            int steep = 0;
            for (int row = 0; row < field.Rows - 1; row++)
                for (int column = 0; column < field.Columns - 1; column++)
                {
                    int index = row * field.Columns + column;
                    foreach (int other in new[] { index + 1, index + field.Columns })
                    {
                        if (math.abs(field.Height[index] - field.Original[index]) < 0.05f && math.abs(field.Height[other] - field.Original[other]) < 0.05f) continue;
                        float slope = math.abs(field.Height[index] - field.Height[other]) / field.Cell;
                        float natural = math.abs(field.Original[index] - field.Original[other]) / field.Cell;
                        if (slope > math.max(request.BankGrade, natural) + 0.02f) steep++;
                    }
                }
            Assert.True(steep == 0, $"{steep} node pairs steeper than the bank grade");
        }

        [Fact]
        public void TheSameSeedGivesTheSameLayoutAndRerollGivesAnother()
        {
            NeighbourhoodPlan Plan(uint seed) => NeighbourhoodPlanner.Plan(SyntheticSites.Cone().Request(r => r.Seed = seed));
            var first = Plan(7);
            var again = Plan(7);
            Assert.Equal(first.Roads.Count, again.Roads.Count);
            for (int road = 0; road < first.Roads.Count; road++) Assert.Equal(first.Roads[road].Points, again.Roads[road].Points);
            Assert.Equal(first.Lots.Select(lot => lot.Pad.Corners[0]), again.Lots.Select(lot => lot.Pad.Corners[0]));
            Assert.Equal(first.Lots.Select(lot => lot.Zone), again.Lots.Select(lot => lot.Zone));
            Assert.Equal(first.Trees.Select(tree => tree.Position), again.Trees.Select(tree => tree.Position));

            var other = Plan(8);
            Assert.NotEqual(first.Roads.SelectMany(road => road.Points), other.Roads.SelectMany(road => road.Points));
        }

        [Fact]
        public void AnAreaAwayFromAnyRoadStillGetsABuildableNeighbourhood()
        {
            var site = SyntheticSites.Cone();
            site.Road = false;
            var plan = NeighbourhoodPlanner.Plan(site.Request());
            Assert.False(plan.Connected);
            Assert.True(plan.Buildable);
            Assert.Contains(plan.Messages, message => message.StartsWith("Not connected"));
        }

        [Fact]
        public void GroundTooSteepEverywhereIsLeftWildWithAMessage()
        {
            var plan = NeighbourhoodPlanner.Plan(SyntheticSites.Slope(18f).Request());
            Assert.Empty(plan.Roads);
            Assert.Contains(plan.Messages, message => message.StartsWith("Too steep"));
        }

        [Fact]
        public void FlatGroundGetsAMessage()
        {
            var plan = NeighbourhoodPlanner.Plan(SyntheticSites.Slope(1f).Request());
            Assert.Empty(plan.Roads);
            Assert.Contains(plan.Messages, message => message.Contains("nearly flat"));
        }

        [Theory]
        [InlineData(MixPattern.Random)]
        [InlineData(MixPattern.Clumps)]
        [InlineData(MixPattern.Even)]
        public void TheZoneMixFollowsTheShares(MixPattern pattern)
        {
            var plan = NeighbourhoodPlanner.Plan(SyntheticSites.Cone().Request(r =>
            {
                r.Pattern = pattern;
                r.Mix = new[] { new ZoneShare { Zone = "A", Share = 3f }, new ZoneShare { Zone = "B", Share = 1f } };
            }));
            float share = plan.Lots.Count(lot => lot.Zone == "A") / (float)plan.Lots.Count;
            Assert.InRange(share, pattern == MixPattern.Even ? 0.7f : 0.6f, pattern == MixPattern.Even ? 0.8f : 0.9f);
            Assert.All(plan.Lots, lot => Assert.True(lot.Zone == "A" || lot.Zone == "B"));
        }
    }

    [CollectionDefinition("Timing", DisableParallelization = true)]
    public sealed class TimingCollection { }

    /// <summary>Timed alone, so other tests running in parallel do not skew the clock. The budget is for the Release build.</summary>
    [Collection("Timing")]
    public sealed class NeighbourhoodTimingTests
    {
        [Fact]
        public void OneSquareKilometrePlansInUnderTwoSeconds()
        {
            var request = SyntheticSites.Slope(10f, 1000f).Request();
            NeighbourhoodPlanner.Plan(SyntheticSites.Slope(10f, 300f).Request());
            var clock = Stopwatch.StartNew();
            var plan = NeighbourhoodPlanner.Plan(request);
            clock.Stop();
#if DEBUG
            const double budget = 12000;
#else
            const double budget = 2000;
#endif
            Assert.True(plan.Roads.Count > 0);
            Assert.True(clock.Elapsed.TotalMilliseconds < budget, $"{clock.Elapsed.TotalMilliseconds:0} ms");
        }
    }
}
