// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// The ground under and around the drawn area on a regular grid: raw heights, a smoothed copy the layout reads (so small
    /// lumps don't bend streets), its slope, and how much room a road centreline has at every node.
    /// </summary>
    internal sealed class SiteSurvey
    {
        /// <summary>Gaussian smoothing for the layout: lumps under ~40 m go, the hill stays.</summary>
        public const float SmoothingSigma = 16f;

        /// <summary>Wider smoothing for judging steepness (wild ground and the reference slope).</summary>
        public const float SlopeSigma = 24f;

        // How far the survey reaches past the area: banks always, and connections up to 400 m to the nearest road.
        private const float MinimumMargin = 64f;
        private const float MaximumMargin = 448f;
        // About 4 m nodes up to a 4 km box, coarsening slowly beyond (7 m on a 6.4 km area).
        private const int MaximumNodes = 1000000;

        public readonly float2 Origin;
        public readonly float Cell;
        public readonly int Columns, Rows;
        public readonly float[] Ground, Smooth, Slope;
        public readonly bool[] Inside, Wild;

        /// <summary>Clearance for a new street's centreline inside the area, in metres; negative where none may go.</summary>
        public readonly float[] StreetRoom;

        /// <summary>Clearance for a connection or lane centreline, which may leave the area; negative where none may go.</summary>
        public readonly float[] ConnectionRoom;

        public readonly float2[] Boundary;
        public readonly float2 BoundaryMinimum, BoundaryMaximum;
        public readonly float AreaSquareMetres;

        /// <summary>The 60th-percentile slope of buildable ground in the area: the slope the street spacing is set for.</summary>
        public readonly float ReferenceSlope;

        public readonly float MedianSlope;
        public readonly float BuildableShare, WildShare, BlockedShare;
        public readonly int InsideCount;

        public int Count => Columns * Rows;

        public SiteSurvey(NeighbourhoodRequest request, CancellationToken cancellation = default)
        {
            Boundary = request.Boundary;
            Poly.Bounds(Boundary, out BoundaryMinimum, out BoundaryMaximum);
            AreaSquareMetres = math.abs(SignedArea(Boundary));
            // Reach as far as the nearest existing road (at most 400 m), so a connection to it can be planned.
            float nearestRoad = float.MaxValue;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                foreach (var point in road?.Points ?? new float3[0])
                    nearestRoad = math.min(nearestRoad, Poly.Contains(Boundary, point.xz) ? 0f : Poly.DistanceToBoundary(Boundary, point.xz));
            // No road in reach: only the banks need room past the area.
            float margin = nearestRoad == float.MaxValue ? MinimumMargin : math.clamp(nearestRoad + 48f, MinimumMargin, MaximumMargin);
            float2 minimum = BoundaryMinimum - margin, maximum = BoundaryMaximum + margin;
            float cell = 4f;
            while ((math.ceil((maximum.x - minimum.x) / cell) + 1) * (math.ceil((maximum.y - minimum.y) / cell) + 1) > MaximumNodes)
                cell *= 1.15f;
            Cell = cell;
            Origin = minimum;
            Columns = (int)math.ceil((maximum.x - minimum.x) / cell) + 1;
            Rows = (int)math.ceil((maximum.y - minimum.y) / cell) + 1;
            int count = Count;

            Ground = new float[count];
            var obstacle = new bool[count];
            Inside = new bool[count];
            for (int index = 0; index < count; index++)
            {
                // A 6.4 km area is millions of nodes: a cancel should not wait for all of them.
                if ((index & 0xFFFF) == 0) cancellation.ThrowIfCancellationRequested();
                var position = Position(index);
                float height = request.GroundHeight(position);
                obstacle[index] = !math.isfinite(height) || (request.Blocked != null && request.Blocked(position));
                Ground[index] = math.isfinite(height) ? height : 0f;
                Inside[index] = Poly.Contains(Boundary, position);
            }
            StampExistingRoads(request.ExistingRoads, obstacle);

            cancellation.ThrowIfCancellationRequested();
            Smooth = Gaussian(Ground, Columns, Rows, SmoothingSigma / cell);
            // Steepness is judged on the hill, not its bumps: grading flattens a 40 m lump, it cannot flatten a hillside.
            var broad = Gaussian(Ground, Columns, Rows, SlopeSigma / cell);
            Slope = new float[count];
            Wild = new bool[count];
            var slopes = new List<float>();
            int buildable = 0, wild = 0, blockedNodes = 0;
            for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    int index = row * Columns + column;
                    Slope[index] = math.length(Gradient(broad, column, row));
                    Wild[index] = Slope[index] > request.SteepestBuildable;
                    if (!Inside[index]) continue;
                    InsideCount++;
                    if (Wild[index]) wild++;
                    else if (!obstacle[index]) { buildable++; slopes.Add(Slope[index]); }
                    else blockedNodes++;
                }
            slopes.Sort();
            ReferenceSlope = slopes.Count == 0 ? 0f : slopes[(int)(slopes.Count * 0.6f)];
            MedianSlope = slopes.Count == 0 ? 0f : slopes[slopes.Count / 2];
            BuildableShare = InsideCount == 0 ? 0f : buildable / (float)InsideCount;
            WildShare = InsideCount == 0 ? 0f : wild / (float)InsideCount;
            BlockedShare = InsideCount == 0 ? 0f : blockedNodes / (float)InsideCount;

            // Clearances from chamfer distance fields: a street keeps its whole corridor on buildable ground inside the area,
            // and keeps clear of buildings, water and existing roads by its half width plus a shoulder and a bank.
            float halfWidth = request.RoadHalfWidth;
            var outside = new bool[count];
            for (int index = 0; index < count; index++) outside[index] = !Inside[index];
            cancellation.ThrowIfCancellationRequested();
            var toOutside = Distances(outside);
            var toObstacle = Distances(obstacle);
            var toWild = Distances(Wild);
            StreetRoom = new float[count];
            ConnectionRoom = new float[count];
            for (int index = 0; index < count; index++)
            {
                float edges = math.min(toObstacle[index] - (halfWidth + 6f), toWild[index] - halfWidth);
                StreetRoom[index] = math.min(edges, toOutside[index] - (halfWidth + 4f));
                ConnectionRoom[index] = math.min(edges, BorderDistance(index) - 24f);
            }
        }

        public float2 Position(int index) => Origin + new float2(index % Columns, index / Columns) * Cell;

        public float GroundAt(float2 point) => Sample(Ground, point);
        public float SmoothAt(float2 point) => Sample(Smooth, point);
        public float SlopeAt(float2 point) => Sample(Slope, point);
        public float StreetRoomAt(float2 point) => Sample(StreetRoom, point);
        public float ConnectionRoomAt(float2 point) => Sample(ConnectionRoom, point);

        /// <summary>The direction the smoothed ground rises fastest, unit length; zero on flat ground.</summary>
        public float2 UphillAt(float2 point)
        {
            float step = Cell;
            var gradient = new float2(SmoothAt(point + new float2(step, 0f)) - SmoothAt(point - new float2(step, 0f)),
                SmoothAt(point + new float2(0f, step)) - SmoothAt(point - new float2(0f, step))) / (2f * step);
            return math.lengthsq(gradient) > 1e-10f ? math.normalize(gradient) : float2.zero;
        }

        /// <summary>Bilinear sample, clamped to the grid.</summary>
        public float Sample(float[] values, float2 point)
        {
            float2 coordinate = math.clamp((point - Origin) / Cell, float2.zero, new float2(Columns - 1, Rows - 1));
            int column = math.min((int)coordinate.x, Columns - 2), row = math.min((int)coordinate.y, Rows - 2);
            float2 fraction = coordinate - new float2(column, row);
            int index = row * Columns + column;
            return math.lerp(math.lerp(values[index], values[index + 1], fraction.x),
                math.lerp(values[index + Columns], values[index + Columns + 1], fraction.x), fraction.y);
        }

        public bool InsideGrid(float2 point, float margin)
        {
            float2 relative = point - Origin;
            return relative.x >= margin && relative.y >= margin
                && relative.x <= (Columns - 1) * Cell - margin && relative.y <= (Rows - 1) * Cell - margin;
        }

        private float BorderDistance(int index)
        {
            int column = index % Columns, row = index / Columns;
            return math.min(math.min(column, Columns - 1 - column), math.min(row, Rows - 1 - row)) * Cell;
        }

        private float2 Gradient(float[] Smooth, int column, int row)
        {
            int left = math.max(0, column - 1), right = math.min(Columns - 1, column + 1);
            int down = math.max(0, row - 1), up = math.min(Rows - 1, row + 1);
            float dx = (Smooth[row * Columns + right] - Smooth[row * Columns + left]) / ((right - left) * Cell);
            float dz = (Smooth[up * Columns + column] - Smooth[down * Columns + column]) / ((up - down) * Cell);
            return new float2(dx, dz);
        }

        private void StampExistingRoads(ExistingRoad[] roads, bool[] obstacle)
        {
            if (roads == null) return;
            foreach (var road in roads)
            {
                if (road?.Points == null) continue;
                float reach = road.HalfWidth + 2f;
                for (int point = 1; point < road.Points.Length; point++)
                {
                    float2 start = road.Points[point - 1].xz, end = road.Points[point].xz;
                    float2 low = math.min(start, end) - reach, high = math.max(start, end) + reach;
                    int firstColumn = math.max(0, (int)math.floor((low.x - Origin.x) / Cell));
                    int lastColumn = math.min(Columns - 1, (int)math.ceil((high.x - Origin.x) / Cell));
                    int firstRow = math.max(0, (int)math.floor((low.y - Origin.y) / Cell));
                    int lastRow = math.min(Rows - 1, (int)math.ceil((high.y - Origin.y) / Cell));
                    for (int row = firstRow; row <= lastRow; row++)
                        for (int column = firstColumn; column <= lastColumn; column++)
                        {
                            int index = row * Columns + column;
                            if (Geometry.DistanceToSegment(Position(index), start, end) <= reach) obstacle[index] = true;
                        }
                }
            }
        }

        /// <summary>Chamfer distance (metres) from every node to the nearest seed node; large where there is none.</summary>
        public float[] Distances(bool[] seeds) => Geometry.Chamfer(seeds, Columns, Rows, Cell);

        private static float[] Gaussian(float[] values, int columns, int rows, float sigmaCells)
        {
            int radius = math.max(1, (int)math.ceil(sigmaCells * 3f));
            var kernel = new float[radius * 2 + 1];
            float total = 0f;
            for (int offset = -radius; offset <= radius; offset++)
                total += kernel[offset + radius] = math.exp(-offset * offset / (2f * sigmaCells * sigmaCells));
            for (int index = 0; index < kernel.Length; index++) kernel[index] /= total;
            var across = new float[values.Length];
            var result = new float[values.Length];
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float sum = 0f;
                    for (int offset = -radius; offset <= radius; offset++)
                        sum += kernel[offset + radius] * values[row * columns + math.clamp(column + offset, 0, columns - 1)];
                    across[row * columns + column] = sum;
                }
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float sum = 0f;
                    for (int offset = -radius; offset <= radius; offset++)
                        sum += kernel[offset + radius] * across[math.clamp(row + offset, 0, rows - 1) * columns + column];
                    result[row * columns + column] = sum;
                }
            return result;
        }

        private static float SignedArea(float2[] ring)
        {
            float area = 0f;
            for (int index = 0; index < ring.Length; index++)
            {
                float2 current = ring[index], next = ring[(index + 1) % ring.Length];
                area += current.x * next.y - next.x * current.y;
            }
            return area * 0.5f;
        }
    }
}
