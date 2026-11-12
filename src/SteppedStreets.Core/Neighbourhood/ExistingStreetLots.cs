// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Lots on the empty zone cells of streets that already exist. Each lot is cut from the game's own cell grid, so the zoning
    /// lands on whole cells: a block's columns whose cell against the road is free and inside the area, as deep as they stay
    /// free (up to the lot depth), split into lots about the lot width wherever the depth stays the same.
    /// </summary>
    internal static class ExistingStreetLots
    {
        public static List<PlannedLot> Plan(NeighbourhoodRequest request, CancellationToken cancellation)
        {
            var free = new HashSet<(int Block, int Column, int Row)>();
            // Block, then column, in order, so the lots of one street side follow each other and Clumps makes runs along it.
            var fronts = new SortedDictionary<int, SortedDictionary<int, ZoneCell>>();
            foreach (var cell in request.ZoneCells ?? new ZoneCell[0])
            {
                if (!math.all(math.isfinite(cell.Centre)) || !Poly.Contains(request.Boundary, cell.Centre)) continue;
                free.Add((cell.Block, cell.Column, cell.Row));
                if (cell.Row != 0) continue;
                if (!fronts.TryGetValue(cell.Block, out var columns)) fronts[cell.Block] = columns = new SortedDictionary<int, ZoneCell>();
                columns[cell.Column] = cell;
            }

            var lots = new List<PlannedLot>();
            var run = new List<ZoneCell>();
            int runDepth = 0;
            void Flush(int block)
            {
                // About the lot width each, spread evenly, so no sliver of one cell is left at the end of a run.
                int count = math.max(1, (int)math.round(run.Count / (float)request.LotWidthCells));
                for (int lot = 0; lot < count; lot++)
                {
                    int first = lot * run.Count / count, end = (lot + 1) * run.Count / count;
                    if (end > first) lots.Add(Lot(run, first, end, runDepth, block));
                }
                run.Clear();
            }
            foreach (var block in fronts)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var front in block.Value)
                {
                    int depth = 1;
                    while (depth < request.LotDepthCells && free.Contains((block.Key, front.Key, depth))) depth++;
                    if (run.Count > 0 && (front.Key != run[run.Count - 1].Column + 1 || depth != runDepth)) Flush(block.Key);
                    run.Add(front.Value);
                    runDepth = depth;
                }
                if (run.Count > 0) Flush(block.Key);
            }
            LotLayout.AssignZones(lots, request);
            PositionZones.ForExistingStreets(lots, request);
            return lots;
        }

        /// <summary>Columns <paramref name="first"/> to <paramref name="end"/> of a run as one lot, corners in the pad's order.</summary>
        private static PlannedLot Lot(List<ZoneCell> run, int first, int end, int depth, int block)
        {
            ZoneCell start = run[first], last = run[end - 1];
            float2 forward = math.normalizesafe(start.Forward, new float2(0f, 1f));
            float2 along = end - first > 1 ? math.normalizesafe(last.Centre - start.Centre) : new float2(forward.y, -forward.x);
            float half = StepPlanner.CellSize * 0.5f;
            float2 frontStart = start.Centre - along * half + forward * half, frontEnd = last.Centre + along * half + forward * half;
            float2 back = -forward * StepPlanner.CellSize * depth;
            // Only for drawing the lot: nothing is levelled.
            float height = (start.Height + last.Height) * 0.5f;
            float3 Corner(float2 point) => new float3(point.x, height, point.y);
            return new PlannedLot
            {
                Road = block,
                Pad = new Pad
                {
                    Side = StreetSide.Left,
                    Height = height,
                    Start = start.Column * StepPlanner.CellSize,
                    End = (last.Column + 1) * StepPlanner.CellSize,
                    WidthCells = end - first,
                    DepthCells = depth,
                    Corners = new[] { Corner(frontStart), Corner(frontEnd), Corner(frontEnd + back), Corner(frontStart + back) },
                },
            };
        }
    }
}
