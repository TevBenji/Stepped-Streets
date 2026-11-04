// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Lays out a hillside neighbourhood in a drawn area: survey → contour streets → links → connection → validation →
    /// lots → ground → zones → trees and walls. Pure and deterministic: the same request and seed give the same plan.
    /// </summary>
    public static class NeighbourhoodPlanner
    {
        /// <summary>Tighter bends than this look wrong on a small road; Terraced blocks are straighter, lanes may hairpin.</summary>
        public static float MinimumRadius(NeighbourhoodStyle style) =>
            style == NeighbourhoodStyle.TerracedBlocks ? 60f : style == NeighbourhoodStyle.MountainLane ? 25f : 30f;

        /// <summary>Links and connections bend across the slope in every style, so they keep the small road's radius.</summary>
        public static float LinkRadius(NeighbourhoodStyle style) => style == NeighbourhoodStyle.MountainLane ? 25f : 30f;

        /// <summary>
        /// The steepest bank the grading makes. In steep mode banks use the steepest the game draws cleanly (50 %: more than
        /// 1.75 m over one 3.5 m heightmap texel shows as a stretched sheet), so cuts and fills meet steep ground soonest.
        /// </summary>
        public const float SteepestCleanBank = 0.5f;

        public static float EffectiveBankGrade(NeighbourhoodRequest request) =>
            request.SteepGround ? math.max(request.BankGrade, SteepestCleanBank) : request.BankGrade;

        /// <summary>On steep ground a link's hairpin has to fit between two streets, so it may turn tighter.</summary>
        public static float LinkRadius(NeighbourhoodRequest request) => request.SteepGround ? 18f : LinkRadius(request.Style);

        /// <param name="roadsReady">Called on the planning thread with a copy holding only the roads, as soon as they are settled,
        /// so the preview can show them while the lots and the ground are still being planned.</param>
        public static NeighbourhoodPlan Plan(NeighbourhoodRequest request, CancellationToken cancellation = default,
            Action<NeighbourhoodPlan> roadsReady = null)
        {
            var clock = Stopwatch.StartNew();
            Validate(request);
            var plan = new NeighbourhoodPlan();
            var stages = new System.Text.StringBuilder();
            double mark = 0;
            void Stage(string name)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                stages.Append(name).Append(' ').Append((now - mark).ToString("0", CultureInfo.InvariantCulture)).Append(" ms, ");
                mark = now;
                plan.Timings = stages.ToString().TrimEnd(',', ' ');
            }
            var survey = new SiteSurvey(request, cancellation);
            Stage("survey");
            plan.AreaSquareMetres = survey.AreaSquareMetres;
            plan.WildShare = survey.WildShare;
            if (survey.InsideCount == 0)
            {
                plan.Messages.Add("The area is too small. Draw a larger one.");
                return Finish(plan, clock);
            }
            if (survey.BuildableShare < 0.05f && survey.BlockedShare > survey.WildShare)
            {
                plan.Messages.Add("Most of the area is buildings or water. Draw it over open ground.");
                return Finish(plan, clock);
            }
            if (survey.BuildableShare < 0.05f)
            {
                plan.Messages.Add(string.Format(CultureInfo.InvariantCulture,
                    "Too steep: under 5% of the area is gentler than {0:0}%. Raise the steepest buildable ground or draw on gentler ground.",
                    request.SteepestBuildable * 100f));
                return Finish(plan, clock);
            }
            if (survey.MedianSlope < 0.03f && request.Style.FollowsContours())
            {
                plan.Messages.Add("This area is nearly flat. Pick a grid, radial or branching layout, or draw it on a hillside.");
                return Finish(plan, clock);
            }

            float radius = MinimumRadius(request.Style);
            var streets = request.Style == NeighbourhoodStyle.MountainLane ? Lane(survey, request, radius, cancellation)
                : Layouts.GridLayout.Handles(request.Style) || Layouts.RadialLayout.Handles(request.Style) || Layouts.BranchLayout.Handles(request.Style)
                    || Layouts.TerrainLayout.Handles(request.Style) || Layouts.BlockLayout.Handles(request.Style) || Layouts.PartsLayout.Handles(request.Style)
                ? Grid(survey, request, cancellation, Stage)
                : Contours(survey, request, radius, cancellation, Stage);

            Stage("connection");
            var owners = new List<int>();
            var notes = new List<string>();
            bool connected = RoadNetwork.Prune(streets, out int leftOut);
            RoadNetwork.Assemble(streets, plan, owners);
            for (int round = 0; round < 4; round++)
            {
                var bad = RoadNetwork.Violations(plan, owners, streets, request, radius, LinkRadius(request), notes);
                if (bad.Count == 0) break;
                foreach (int street in bad) streets[street].Broken = true;
                connected = RoadNetwork.Prune(streets, out leftOut);
                RoadNetwork.Assemble(streets, plan, owners);
            }
            plan.Connected = connected;
            if (leftOut > 0)
                plan.Messages.Add(leftOut == 1
                    ? "One group of streets was left out: no gentle road could join it to the rest."
                    : leftOut + " groups of streets were left out: no gentle road could join them to the rest.");
            if (plan.Roads.Count == 0)
            {
                plan.Messages.Add("No street fits here. Draw a larger area, or lower the block depth.");
                return Finish(plan, clock);
            }

            Stage("network");
            if (roadsReady != null)
            {
                var roads = new NeighbourhoodPlan
                {
                    Nodes = new List<PlannedNode>(plan.Nodes),
                    Roads = new List<PlannedRoad>(plan.Roads),
                    Connected = plan.Connected,
                    AreaSquareMetres = plan.AreaSquareMetres,
                    WildShare = plan.WildShare,
                };
                foreach (var road in roads.Roads) roads.RoadLength += road.Length;
                roadsReady(roads);
            }
            var lots = LotLayout.Plan(plan, owners, survey, request, plan.Messages, cancellation);
            plan.Buildings = ServiceSites.Place(plan, lots, survey, request, plan.Messages, cancellation);
            Stage("lots");
            // The buildings' pads are graded like the lots' and keep the trees off.
            var buildingPads = new List<PlannedLot>();
            foreach (var building in plan.Buildings) buildingPads.Add(new PlannedLot { Pad = building.Pad, Road = building.Road });
            var pads = new List<PlannedLot>(lots);
            pads.AddRange(buildingPads);
            // Shape the ground off: nothing is graded, and the game lays its roads on the ground as it is.
            plan.Ground = request.ShapeGround ? GroundField.Build(request, plan, pads, cancellation) : null;
            Stage("ground");
            int lostToBanks = 0;
            foreach (var lot in lots)
            {
                lot.LevelCells = request.LevelLots && plan.Ground != null ? StepPlanner.LevelCells(lot.Pad, plan.Ground.HeightAt, 0.3f) : AllCells(lot.Pad);
                if (lot.LevelCells.Count == 0) { lostToBanks++; continue; }
                // Zoned whole, from the kerb back: the game starts a house lot only at a zoned cell in the row against the road
                // (Game.Zones.LotSizeJobs), and that row often sits on the little bank between the shoulder and the pad.
                lot.LevelCells = AllCells(lot.Pad);
                plan.Lots.Add(lot);
                plan.ZonedCells += lot.LevelCells.Count;
            }
            if (lostToBanks > 0)
                plan.Messages.Add(lostToBanks + (lostToBanks == 1 ? " lot was" : " lots were") + " left out: the banks between levels needed the room.");
            Stage("level cells");
            var treeless = new List<PlannedLot>(plan.Lots);
            treeless.AddRange(buildingPads);
            plan.Trees = Landscaping.Trees(plan, treeless, survey, request, cancellation);
            plan.Walls = Landscaping.Walls(plan, plan.Lots, request);
            Stage("landscape");
            return Finish(plan, clock, request);
        }

        private static List<Street> Lane(SiteSurvey survey, NeighbourhoodRequest request, float radius, CancellationToken cancellation)
        {
            var lane = MountainLane.Trace(survey, request, radius, cancellation);
            return lane == null ? new List<Street>() : new List<Street> { lane };
        }

        private static List<Street> Contours(SiteSurvey survey, NeighbourhoodRequest request, float radius, CancellationToken cancellation,
            Action<string> stage)
        {
            float entry = EntryHeight(survey, request);
            float seedPhase = Geometry.Hash01(request.Seed, 0, 0);
            // Ridge and valley: the main road first, along the crest or the floor; the contour streets keep their distance from it.
            var mains = request.Style == NeighbourhoodStyle.Ridge || request.Style == NeighbourhoodStyle.Valley
                ? Layouts.TerrainLayout.Spines(survey, request, request.Style == NeighbourhoodStyle.Ridge) : new List<Street>();
            var reserved = new List<List<float2>>();
            foreach (var main in mains) reserved.Add(main.Points);
            // A few phases of the level grid, compared on their regular levels (in parallel: they share only the read-only
            // survey): the one with the longest, least broken streets wins and gets its in-between levels. The seed shifts the
            // grid, so Reroll gives another layout.
            var phases = new ContourStreets.Layout[4];
            try
            {
                System.Threading.Tasks.Parallel.For(0, phases.Length, new System.Threading.Tasks.ParallelOptions { CancellationToken = cancellation }, phase =>
                {
                    phases[phase] = ContourStreets.Regular(survey, new ContourStreets.Options
                    {
                        BlockDepth = request.BlockDepth,
                        MinimumRadius = radius,
                        Straighten = request.Style == NeighbourhoodStyle.TerracedBlocks,
                        Level = request.Style == NeighbourhoodStyle.TerracedBlocks,
                        Phase = (seedPhase + phase * 0.25f) % 1f,
                        EntryHeight = entry,
                        Reserved = reserved,
                    }, cancellation);
                });
            }
            catch (AggregateException exception) when (exception.InnerException is OperationCanceledException)
            {
                throw exception.InnerException;
            }
            ContourStreets.Layout best = null;
            foreach (var phase in phases)
                if (best == null || phase.Score > best.Score + 1e-3f) best = phase;
            ContourStreets.Infill(survey, best, cancellation);
            var result = best.Streets;
            result.InsertRange(0, mains);
            stage("contours");
            var links = new RoadLinks(survey, request, result, LinkRadius(request));
            links.LinkNeighbours(cancellation);
            stage("links");
            if (request.Style != NeighbourhoodStyle.TerracedBlocks) links.JoinEnds(request.BlockDepth * 0.8f, cancellation);
            stage("joins");
            Connect(result, links, null, cancellation);
            return result;
        }

        /// <summary>
        /// Joins the network to the city's roads, the largest piece first, then any other piece big enough to be a
        /// neighbourhood of its own, then every piece left apart to the main one. <paramref name="joinable"/>, when set,
        /// limits the streets a road from the city may join.
        /// </summary>
        private static void Connect(List<Street> result, RoadLinks links, Func<int, bool> joinable, CancellationToken cancellation)
        {
            var components = RoadNetwork.Components(result, out var lengths);
            var order = new List<int>();
            int largest = 0;
            for (int id = 1; id < lengths.Count; id++) if (lengths[id] > lengths[largest]) largest = id;
            for (int id = 0; id < lengths.Count; id++) if (id == largest || lengths[id] >= 300f) order.Add(id);
            order.Sort((x, y) => lengths[y].CompareTo(lengths[x]));
            foreach (int id in order)
            {
                cancellation.ThrowIfCancellationRequested();
                links.Connect(street => street < components.Length && components[street] == id && (joinable == null || joinable(street)), cancellation);
            }
            JoinPieces(result, links, cancellation);
        }

        /// <summary>A layout's streets, other than the hillside ones, joined to the city's roads (traffic-optimised: only by its through road).</summary>
        private static List<Street> Grid(SiteSurvey survey, NeighbourhoodRequest request, CancellationToken cancellation, Action<string> stage)
        {
            var through = new HashSet<int>();
            bool joined;
            var result = Layouts.RadialLayout.Handles(request.Style) ? Layouts.RadialLayout.Plan(survey, request, out joined, cancellation)
                : Layouts.BranchLayout.Handles(request.Style) ? Layouts.BranchLayout.Plan(survey, request, out joined, cancellation)
                : Layouts.TerrainLayout.Handles(request.Style) ? Layouts.TerrainLayout.Plan(survey, request, out joined, cancellation)
                : Layouts.BlockLayout.Handles(request.Style) ? Layouts.BlockLayout.Plan(survey, request, out joined, cancellation)
                : Layouts.PartsLayout.Handles(request.Style) ? Layouts.PartsLayout.Plan(survey, request, out joined, cancellation)
                : Layouts.GridLayout.Plan(survey, request, out through, out joined, cancellation);
            stage("grid");
            var links = new RoadLinks(survey, request, result, LinkRadius(request));
            // Met the city's road straight on: no winding connection is needed, only pieces left apart are joined.
            if (joined) JoinPieces(result, links, cancellation);
            else Connect(result, links, through.Count > 0 ? street => through.Contains(street) : (Func<int, bool>)null, cancellation);
            return result;
        }

        /// <summary>
        /// Joins every piece of network the connection missed to the main one (the piece a connection reaches, else the
        /// longest), longest first. A neighbourhood in separate pieces looks unfinished (owner's in-game test, 3 November 2026),
        /// so pieces that can't be joined are left for <see cref="RoadNetwork.Prune"/> to drop.
        /// </summary>
        private static void JoinPieces(List<Street> streets, RoadLinks links, CancellationToken cancellation)
        {
            // A piece is known by its first street; a piece that failed is not tried again.
            var failed = new HashSet<int>();
            for (int round = 0; round < MostPieces; round++)
            {
                var component = RoadNetwork.Components(streets, out var lengths);
                var first = new int[lengths.Count];
                for (int id = 0; id < first.Length; id++) first[id] = -1;
                for (int street = 0; street < component.Length; street++)
                    if (component[street] >= 0 && first[component[street]] < 0) first[component[street]] = street;
                var main = new bool[lengths.Count];
                bool rooted = false;
                for (int street = 0; street < component.Length; street++)
                    if (component[street] >= 0 && (streets[street].Start.Existing || streets[street].End.Existing))
                    {
                        main[component[street]] = true;
                        rooted = true;
                    }
                if (!rooted)
                {
                    int longest = -1;
                    for (int id = 0; id < lengths.Count; id++) if (longest < 0 || lengths[id] > lengths[longest]) longest = id;
                    if (longest < 0) return;
                    main[longest] = true;
                }
                int next = -1;
                for (int id = 0; id < lengths.Count; id++)
                    if (!main[id] && lengths[id] >= SmallestPiece && !failed.Contains(first[id]) && (next < 0 || lengths[id] > lengths[next])) next = id;
                if (next < 0) return;
                bool joined = links.JoinPiece(street => street < component.Length && component[street] == next,
                    street => street < component.Length && component[street] >= 0 && main[component[street]], cancellation);
                if (!joined) failed.Add(first[next]);
            }
        }

        private const int MostPieces = 12;
        // Shorter pieces are a street or two the connection missed; pruning drops them anyway.
        private const float SmallestPiece = 150f;

        /// <summary>The smoothed height inside the area nearest an existing road: the street through it gets a short connection.</summary>
        private static float EntryHeight(SiteSurvey survey, NeighbourhoodRequest request)
        {
            float best = float.MaxValue, height = float.NaN;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                foreach (var point in road?.Points ?? new float3[0])
                {
                    float distance = Poly.Contains(survey.Boundary, point.xz) ? 0f : Poly.DistanceToBoundary(survey.Boundary, point.xz);
                    if (distance >= best) continue;
                    best = distance;
                    // A point a little inside the area, towards its middle.
                    float2 centre = (survey.BoundaryMinimum + survey.BoundaryMaximum) * 0.5f;
                    float2 inward = point.xz + math.normalizesafe(centre - point.xz) * (distance + request.BlockDepth * 0.5f);
                    height = survey.SmoothAt(inward);
                }
            if (math.isfinite(height)) return height;
            var heights = new List<float>();
            for (int index = 0; index < survey.Count; index++) if (survey.Inside[index]) heights.Add(survey.Smooth[index]);
            heights.Sort();
            return heights.Count == 0 ? 0f : heights[heights.Count / 2];
        }

        private static List<int2> AllCells(Pad pad)
        {
            var cells = new List<int2>();
            for (int column = 0; column < pad.WidthCells; column++)
                for (int row = 0; row < pad.DepthCells; row++) cells.Add(new int2(column, row));
            return cells;
        }

        private static void Validate(NeighbourhoodRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Boundary == null || request.Boundary.Length < 3) throw new ArgumentException("The area needs at least three corners.", nameof(request));
            if (request.GroundHeight == null) throw new ArgumentException("The request needs a ground height sampler.", nameof(request));
            request.RoadHalfWidth = math.clamp(Finite(request.RoadHalfWidth, 6f), 2f, 20f);
            request.BlockDepth = math.clamp(Finite(request.BlockDepth, 90f), 50f, 250f);
            request.MaxRoadGrade = math.clamp(Finite(request.MaxRoadGrade, 0.08f), 0.03f, 0.12f);
            request.SteepestBuildable = math.clamp(Finite(request.SteepestBuildable, 0.15f), 0.05f, request.SteepGround ? 0.6f : 0.4f);
            request.LinkSpacing = math.clamp(Finite(request.LinkSpacing, 300f), 80f, 600f);
            request.LotWidthCells = math.clamp(request.LotWidthCells, StepPlanner.MinimumWidthCells, StepPlanner.MaximumRows);
            request.LotDepthCells = math.clamp(request.LotDepthCells, StepPlanner.MinimumRows, StepPlanner.MaximumRows);
            request.NarrowestWidthCells = math.clamp(request.NarrowestWidthCells, StepPlanner.MinimumWidthCells, request.LotWidthCells);
            // The game's biggest service lots are about 16 cells; anything odd is clamped rather than refused.
            foreach (var service in request.Services ?? new ServiceRequest[0])
            {
                if (service == null) continue;
                service.WidthCells = math.clamp(service.WidthCells, 1, 20);
                service.DepthCells = math.clamp(service.DepthCells, 1, 20);
                service.Count = math.clamp(service.Count, 0, ServiceRequest.MostEach);
            }
            request.EarthworkLimit = math.clamp(Finite(request.EarthworkLimit, 4f), 0.5f, 12f);
            request.BankGrade = math.clamp(Finite(request.BankGrade, 0.4f), 0.15f, 0.5f);
            request.HighestStep = math.clamp(Finite(request.HighestStep, 3.2f), 0.5f, 6f);
            request.LotSpacingCells = math.clamp(request.LotSpacingCells, -1, 3);
            request.TreeDensity = math.clamp(Finite(request.TreeDensity, 0.5f), 0.1f, 1f);
            // Without shaping the ground there are no level pads to make.
            if (!request.ShapeGround) request.LevelLots = false;
        }

        private static float Finite(float value, float fallback) => math.isfinite(value) ? value : fallback;

        private static NeighbourhoodPlan Finish(NeighbourhoodPlan plan, Stopwatch clock, NeighbourhoodRequest request = null)
        {
            foreach (var road in plan.Roads)
            {
                plan.RoadLength += road.Length;
                plan.SteepestRoadGrade = math.max(plan.SteepestRoadGrade, road.SteepestGrade);
            }
            if (plan.Ground != null) plan.Ground.Measure(out plan.CutVolume, out plan.FillVolume, out plan.MostEarthwork);
            if (request != null)
            {
                var counts = new Dictionary<string, int>();
                foreach (var lot in plan.Lots) counts[lot.Zone] = (counts.TryGetValue(lot.Zone, out int count) ? count : 0) + 1;
                foreach (var share in request.Mix ?? new ZoneShare[0])
                    if (share != null && counts.TryGetValue(share.Zone ?? "", out int count) && !plan.ZoneCounts.Exists(entry => entry.Key == share.Zone))
                        plan.ZoneCounts.Add(new KeyValuePair<string, int>(share.Zone, count));
                if (!plan.Connected && plan.Roads.Count > 0)
                    plan.Messages.Insert(0, "Not connected: no existing road within reach. The new streets are built on their own.");
                if (plan.WildShare >= 0.05f)
                    plan.Messages.Add(string.Format(CultureInfo.InvariantCulture, "{0:0}% of the area is too steep to build and is left as forest.", plan.WildShare * 100f));
            }
            if (plan.Roads.Count > 0)
            {
                plan.SummaryLine = string.Format(CultureInfo.InvariantCulture,
                    "{0:0.0} km of road, {1} lots ({2} cells zoned), {3:N0} m³ of earth moved, steepest road {4:0.#}%",
                    plan.RoadLength / 1000f, plan.Lots.Count, plan.ZonedCells, plan.CutVolume + plan.FillVolume, plan.SteepestRoadGrade * 100f);
            }
            plan.Milliseconds = clock.Elapsed.TotalMilliseconds;
            return plan;
        }
    }
}
