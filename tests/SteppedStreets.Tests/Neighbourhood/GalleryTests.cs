// Stepped Streets, renders the gallery of synthetic sites. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.IO;
using System.Linq;
using SteppedStreets.Core.Neighbourhood;
using Xunit;
using Xunit.Abstractions;

namespace SteppedStreets.Tests.Neighbourhood
{
    public sealed class GalleryTests
    {
        private readonly ITestOutputHelper output;

        public GalleryTests(ITestOutputHelper output) { this.output = output; }

        public static string Folder()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SteppedStreets.sln"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? AppContext.BaseDirectory, "gallery");
        }

        [Fact]
        public void RendersEverySiteAndStyle()
        {
            var renders = SyntheticSites.All().Select(site => (site, Name: site.Name, Adjust: (Action<NeighbourhoodRequest>)null)).ToList();
            var cone = SyntheticSites.Cone();
            renders.Add((cone, "cone-terraced", request => request.Style = NeighbourhoodStyle.TerracedBlocks));
            renders.Add((cone, "cone-lane", request => request.Style = NeighbourhoodStyle.MountainLane));
            renders.Add((cone, "cone-mix", request =>
            {
                request.Mix = new[]
                {
                    new ZoneShare { Zone = "Residential Low", Share = 60f },
                    new ZoneShare { Zone = "Residential Medium Row", Share = 25f },
                    new ZoneShare { Zone = "Commercial Low", Share = 15f },
                };
                request.Walls = true;
            }));
            renders.Add((SyntheticSites.Slope(18f), "slope-18-steep-allowed", request => request.SteepestBuildable = 0.22f));
            var standalone = SyntheticSites.NoisyHill();
            standalone.Road = false;
            renders.Add((standalone, "noisy-hill-no-road", null));
            void Steep(NeighbourhoodRequest request) { request.SteepGround = true; request.SteepestBuildable = 0.5f; request.EarthworkLimit = 8f; }
            renders.Add((SyntheticSites.Mountain(), "mountain-steep", Steep));
            renders.Add((SyntheticSites.Slope(30f), "slope-30-steep", Steep));
            renders.Add((SyntheticSites.Mountain(), "mountain-steep-lane", request => { Steep(request); request.Style = NeighbourhoodStyle.MountainLane; }));

            foreach (var (site, name, adjust) in renders)
            {
                var request = site.Request(adjust);
                var plan = NeighbourhoodPlanner.Plan(request);
                Gallery.Render(request, plan, Path.Combine(Folder(), name + ".png"));
                output.WriteLine($"{name}: {plan.Milliseconds:0} ms ({plan.Timings}), {plan.Roads.Count} roads, {plan.Lots.Count} lots, {plan.Trees.Count} trees");
            }
        }
    }
}
