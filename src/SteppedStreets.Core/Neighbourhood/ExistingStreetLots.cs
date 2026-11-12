// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Lots on the empty zone cells of streets that already exist. Each lot is cut from the game's own cell grid, so the zoning
    /// lands on whole cells: a block's columns whose cell against the road is free and inside the area, as deep as they stay
    /// free (up to the lot depth), split into lots about the lot width wherever the depth stays the same, with the chosen space
    /// between them. Service buildings take their runs of cells first; with pocket parks on, a run too short for a lot becomes
    /// a small park.
    /// </summary>
    internal static class ExistingStreetLots
    {
        private const float Half = StepPlanner.CellSize * 0.5f;

        /// <param name="buildings">Receives the service buildings and pocket parks, each on its own run of free cells.</param>
        public static List<PlannedLot> Plan(NeighbourhoodRequest request, List<PlannedBuilding> buildings, List<string> messages,
            CancellationToken cancellation)
        {
            var free = new Dictionary<(int Block, int Column, int Row), ZoneCell>();
            foreach (var cell in request.ZoneCells ?? new ZoneCell[0])
                if (!cell.Taken && math.all(math.isfinite(cell.Centre)) && Poly.Contains(request.Boundary, cell.Centre))
                    free[(cell.Block, cell.Column, cell.Row)] = cell;
            // Block, then column, in order, so the lots of one street side follow each other and Clumps makes runs along it.
            var fronts = new SortedDictionary<int, SortedDictionary<int, ZoneCell>>();
            foreach (var cell in free.Values)
            {
                if (cell.Row != 0) continue;
                if (!fronts.TryGetValue(cell.Block, out var columns)) fronts[cell.Block] = columns = new SortedDictionary<int, ZoneCell>();
                columns[cell.Column] = cell;
            }
            int Depth(int block, int column, int most)
            {
                int depth = 0;
                while (depth < most && free.ContainsKey((block, column, depth))) depth++;
                return depth;
            }

            PlaceServices(request, free, fronts, Depth, buildings, messages, cancellation);
            var streets = StreetPieces(request);

            var lots = new List<PlannedLot>();
            var parks = new List<PlannedBuilding>();
            var run = new List<ZoneCell>();
            int runDepth = 0;
            void Flush(int block)
            {
                if (run.Count == 0) return;
                if (request.PocketParks && run.Count < request.NarrowestWidthCells && Park(request, run, runDepth, block, parks))
                {
                    run.Clear();
                    return;
                }
                int gap = request.LotSpacingCells >= 0 ? request.LotSpacingCells
                    : LotLayout.SlopeGap(StreetGrade(Outline(run, 0, run.Count, runDepth), streets), request);
                // About the lot width each with the gaps between, spread evenly, so no sliver of one cell is left at the end.
                int count = math.max(1, (int)math.round((run.Count + gap) / (float)(request.LotWidthCells + gap)));
                int cells = run.Count - (count - 1) * gap;
                while (count > 1 && cells < count) { count--; cells += gap; }
                for (int lot = 0; lot < count; lot++)
                {
                    int first = lot * gap + lot * cells / count, end = lot * gap + (lot + 1) * cells / count;
                    if (end > first) lots.Add(new PlannedLot { Road = block, Pad = Outline(run, first, end, runDepth) });
                }
                run.Clear();
            }
            foreach (var block in fronts)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var front in block.Value)
                {
                    // A front cell a service building took is no longer free.
                    if (!free.ContainsKey((block.Key, front.Key, 0))) { Flush(block.Key); continue; }
                    int depth = Depth(block.Key, front.Key, request.LotDepthCells);
                    if (run.Count > 0 && (front.Key != run[run.Count - 1].Column + 1 || depth != runDepth)) Flush(block.Key);
                    run.Add(front.Value);
                    runDepth = depth;
                }
                Flush(block.Key);
            }
            buildings.AddRange(parks);
            LotLayout.AssignZones(lots, request);
            PositionZones.ForExistingStreets(lots, request);
            return lots;
        }

        /// <summary>
        /// Each service building asked for on a run of free cells as wide and deep as its lot, facing the block's road: the
        /// flattest run, spread out from the others. Its cells are taken out of <paramref name="free"/> before the lots are cut.
        /// </summary>
        private static void PlaceServices(NeighbourhoodRequest request, Dictionary<(int Block, int Column, int Row), ZoneCell> free,
            SortedDictionary<int, SortedDictionary<int, ZoneCell>> fronts, Func<int, int, int, int> depth, List<PlannedBuilding> buildings,
            List<string> messages, CancellationToken cancellation)
        {
            var wanted = new List<ServiceRequest>();
            foreach (var service in request.Services ?? new ServiceRequest[0])
                if (service != null && service.Count > 0 && !string.IsNullOrEmpty(service.Name)) wanted.Add(service);
            // The biggest first: they have the fewest runs.
            wanted.Sort((first, second) => first.WidthCells * first.DepthCells != second.WidthCells * second.DepthCells
                ? (second.WidthCells * second.DepthCells).CompareTo(first.WidthCells * first.DepthCells) : string.CompareOrdinal(first.Name, second.Name));
            var missing = new Dictionary<string, int>();
            var window = new List<ZoneCell>();
            foreach (var service in wanted)
                for (int copy = 0; copy < math.min(service.Count, ServiceRequest.MostEach); copy++)
                {
                    float bestScore = float.MaxValue;
                    int bestBlock = 0, bestColumn = 0;
                    foreach (var block in fronts)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        foreach (int column in block.Value.Keys)
                        {
                            bool fits = true;
                            for (int offset = 0; offset < service.WidthCells && fits; offset++)
                                fits = depth(block.Key, column + offset, service.DepthCells) == service.DepthCells;
                            if (!fits) continue;
                            // The flattest run, and the furthest from the other buildings (up to 600 m), as on new streets.
                            float low = float.MaxValue, high = float.MinValue;
                            float2 centre = float2.zero;
                            for (int offset = 0; offset < service.WidthCells; offset++)
                                for (int row = 0; row < service.DepthCells; row++)
                                {
                                    var cell = free[(block.Key, column + offset, row)];
                                    float ground = request.GroundHeight(cell.Centre);
                                    low = math.min(low, ground);
                                    high = math.max(high, ground);
                                    centre += cell.Centre;
                                }
                            centre /= service.WidthCells * service.DepthCells;
                            float nearest = 600f;
                            foreach (var building in buildings) nearest = math.min(nearest, math.distance(centre, building.Position.xz));
                            float score = (high - low) - nearest * 0.01f;
                            if (score < bestScore) { bestScore = score; bestBlock = block.Key; bestColumn = column; }
                        }
                    }
                    if (bestScore == float.MaxValue)
                    {
                        missing.TryGetValue(service.Name, out int count);
                        missing[service.Name] = count + 1;
                        continue;
                    }
                    window.Clear();
                    for (int offset = 0; offset < service.WidthCells; offset++) window.Add(free[(bestBlock, bestColumn + offset, 0)]);
                    buildings.Add(Building(service.Name, window, service.WidthCells, service.DepthCells, bestBlock));
                    for (int offset = 0; offset < service.WidthCells; offset++)
                        for (int row = 0; row < service.DepthCells; row++) free.Remove((bestBlock, bestColumn + offset, row));
                }
            foreach (var entry in missing)
                messages.Add(string.Format(CultureInfo.InvariantCulture, "No room for {0} × {1}: it needs free cells of its size along a street.",
                    entry.Value, entry.Key));
        }

        /// <summary>The biggest park that fits a run too short for a lot, from its first cell, if no other park is near.</summary>
        private static bool Park(NeighbourhoodRequest request, List<ZoneCell> run, int depth, int block, List<PlannedBuilding> parks)
        {
            var parkSizes = ServiceSites.ParksThatFit(request, run.Count, depth, Geometry.Hash(request.Seed, block, run[0].Column));
            if (parkSizes.Count == 0) return false;
            var park = Building(parkSizes[0].Name, run, parkSizes[0].WidthCells, parkSizes[0].DepthCells, block);
            if (!ServiceSites.FarFromParks(parks, park.Position.xz)) return false;
            parks.Add(park);
            return true;
        }

        private static PlannedBuilding Building(string name, List<ZoneCell> run, int width, int depth, int block)
        {
            var pad = Outline(run, 0, width, depth);
            float2 centre = (pad.Corners[0].xz + pad.Corners[1].xz + pad.Corners[2].xz + pad.Corners[3].xz) * 0.25f;
            return new PlannedBuilding
            {
                Name = name,
                Pad = pad,
                Road = block,
                Position = new float3(centre.x, pad.Height, centre.y),
                Forward = math.normalizesafe(run[0].Forward, new float2(0f, 1f)),
            };
        }

        /// <summary>Columns <paramref name="first"/> to <paramref name="end"/> of a run, <paramref name="depth"/> rows deep, corners in the pad's order.</summary>
        private static Pad Outline(List<ZoneCell> run, int first, int end, int depth)
        {
            ZoneCell start = run[first], last = run[end - 1];
            float2 forward = math.normalizesafe(start.Forward, new float2(0f, 1f));
            float2 along = end - first > 1 ? math.normalizesafe(last.Centre - start.Centre) : new float2(forward.y, -forward.x);
            float2 frontStart = start.Centre - along * Half + forward * Half, frontEnd = last.Centre + along * Half + forward * Half;
            float2 back = -forward * StepPlanner.CellSize * depth;
            // The street's height: the lot is drawn there, and a lot that is levelled starts from it.
            float height = (start.Height + last.Height) * 0.5f;
            float3 Corner(float2 point) => new float3(point.x, height, point.y);
            return new Pad
            {
                Side = StreetSide.Left,
                Height = height,
                Start = start.Column * StepPlanner.CellSize,
                End = (last.Column + 1) * StepPlanner.CellSize,
                WidthCells = end - first,
                DepthCells = depth,
                Corners = new[] { Corner(frontStart), Corner(frontEnd), Corner(frontEnd + back), Corner(frontStart + back) },
            };
        }

        /// <summary>
        /// Auto spacing reads the grade of the street a run fronts, from the existing road's own heights at the two ends of
        /// the run's front: neighbouring lots step apart by that grade, so it decides the room their banks need. The snapshot
        /// always has the roads, so the lots come out the same whether or not greenery or levelling read the ground.
        /// </summary>
        private static float StreetGrade(Pad run, SpatialBuckets<(float3 Start, float3 End)> streets)
        {
            float2 start = run.Corners[0].xz, end = run.Corners[1].xz;
            float length = math.distance(start, end);
            if (length < 1f) return 0f;
            float2 along = (end - start) / length;
            float? Height(float2 point)
            {
                float best = StreetReach;
                float? height = null;
                foreach (var (from, to) in streets.Near(point))
                {
                    float2 direction = to.xz - from.xz;
                    float lengthSquared = math.lengthsq(direction);
                    // The street the run lines, not one crossing it at a corner.
                    if (lengthSquared < 1e-4f || math.abs(math.dot(math.normalize(direction), along)) < 0.7f) continue;
                    float share = math.saturate(math.dot(point - from.xz, direction) / lengthSquared);
                    float distance = math.distance(point, from.xz + direction * share);
                    if (distance < best) { best = distance; height = math.lerp(from.y, to.y, share); }
                }
                return height;
            }
            float? first = Height(start), last = Height(end);
            return first.HasValue && last.HasValue ? math.abs(last.Value - first.Value) / length : 0f;
        }

        // A block's front lies at its road's edge: the centreline is within half the widest road's width.
        private const float StreetReach = 24f;

        private static SpatialBuckets<(float3 Start, float3 End)> StreetPieces(NeighbourhoodRequest request)
        {
            var pieces = new SpatialBuckets<(float3, float3)>(32f);
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < (road?.Points?.Length ?? 0); point++)
                {
                    float3 from = road.Points[point - 1], to = road.Points[point];
                    pieces.Add(math.min(from.xz, to.xz) - StreetReach, math.max(from.xz, to.xz) + StreetReach, (from, to));
                }
            return pieces;
        }

        /// <summary>
        /// True on a cell the player zoned or something stands on, for keeping greenery and grading off it; null when the
        /// request has no such cell.
        /// </summary>
        internal static Func<float2, bool> TakenCells(NeighbourhoodRequest request)
        {
            var cells = new SpatialBuckets<(float2 Centre, float2 Forward)>(32f);
            int count = 0;
            foreach (var cell in request.ZoneCells ?? new ZoneCell[0])
            {
                if (!cell.Taken || !math.all(math.isfinite(cell.Centre))) continue;
                // A turned cell reaches up to half its diagonal from its centre.
                cells.Add(cell.Centre - Half * 1.5f, cell.Centre + Half * 1.5f, (cell.Centre, math.normalizesafe(cell.Forward, new float2(0f, 1f))));
                count++;
            }
            if (count == 0) return null;
            return point =>
            {
                foreach (var (centre, forward) in cells.Near(point))
                {
                    float2 offset = point - centre;
                    if (math.abs(math.dot(offset, forward)) <= Half && math.abs(offset.x * forward.y - offset.y * forward.x) <= Half) return true;
                }
                return false;
            };
        }
    }
}
