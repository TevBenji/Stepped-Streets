// Stepped Streets, the hillside neighbourhood generator. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// The planned ground for the whole neighbourhood on one grid (video tips: grade the lots, terrace whole blocks).
    ///
    /// Road corridors (the road plus 1.5 m shoulders, level across) and pads are held at their heights. Everywhere else the
    /// ground stays as close to the hillside as it can while no slope it makes is steeper than the bank grade: each held node
    /// casts an upper and a lower cone (its height ± bank grade × distance), and a free node keeps its own ground clamped
    /// between the lowest upper cone and the highest lower cone. Nodes far from anything built, buildings, water and existing
    /// roads are held at their own ground, so every change fades out before it reaches them.
    ///
    /// Roads are held first. Pads follow one by one: a pad node that would need a bank steeper than the grade against what is
    /// already held gives way and becomes bank, so neighbouring lots on different levels never fight. The cones grow by
    /// shortest paths on the 8-neighbour grid, which overstate distance by up to 8 %, so they use the grade ÷ 1.082.
    /// </summary>
    public sealed class GroundField
    {
        private const float Shoulder = 1.5f;
        private const float PadMargin = 1.5f;
        private const float SteepCorridorChange = 1.5f;
        private const int MaximumNodes = 600000;
        private const byte Free = 0, Locked = 1, Road = 2, Held = 3;

        public readonly float2 Origin;
        public readonly float Cell;
        public readonly int Columns, Rows;
        public readonly float[] Original;
        public readonly float[] Height;
        private readonly byte[] state;
        private readonly float[] upper, lower;
        private readonly float grade;
        private readonly float straightStep, diagonalStep, cap;
        private bool followHillside;
        private readonly Func<float2, float> ground;

        public int Count => Columns * Rows;

        internal byte StateAt(int index) => state[index];
        internal float UpperAt(int index) => upper[index];
        internal float LowerAt(int index) => lower[index];

        private GroundField(float2 minimum, float2 maximum, float bankGrade, float earthworkLimit, Func<float2, float> ground)
        {
            this.ground = ground;
            float cell = 2f;
            while ((math.ceil((maximum.x - minimum.x) / cell) + 1) * (math.ceil((maximum.y - minimum.y) / cell) + 1) > MaximumNodes) cell *= 1.15f;
            Cell = cell;
            Origin = minimum;
            Columns = (int)math.ceil((maximum.x - minimum.x) / cell) + 1;
            Rows = (int)math.ceil((maximum.y - minimum.y) / cell) + 1;
            Original = new float[Count];
            Height = new float[Count];
            state = new byte[Count];
            upper = new float[Count];
            lower = new float[Count];
            grade = bankGrade / 1.082f;
            straightStep = grade * cell;
            diagonalStep = grade * cell * 1.41421356f;
            // A cone more than this above (or below) the ground can never shape a pad or the ground, so it is not grown.
            cap = earthworkLimit + 0.5f;
        }

        public float2 Position(int index) => Origin + new float2(index % Columns, index / Columns) * Cell;

        /// <summary>Planned ground at a point; the original ground outside the grid.</summary>
        public float HeightAt(float2 point)
        {
            float value = Sample(Height, point);
            return math.isfinite(value) ? value : ground(point);
        }

        public float ChangeAt(float2 point)
        {
            float planned = Sample(Height, point), before = Sample(Original, point);
            return math.isfinite(planned) ? planned - before : 0f;
        }

        private float Sample(float[] values, float2 point)
        {
            float2 coordinate = (point - Origin) / Cell;
            if (coordinate.x < 0f || coordinate.y < 0f || coordinate.x > Columns - 1 || coordinate.y > Rows - 1) return float.NaN;
            int column = math.min((int)coordinate.x, Columns - 2), row = math.min((int)coordinate.y, Rows - 2);
            float2 fraction = coordinate - new float2(column, row);
            int index = row * Columns + column;
            return math.lerp(math.lerp(values[index], values[index + 1], fraction.x),
                math.lerp(values[index + Columns], values[index + Columns + 1], fraction.x), fraction.y);
        }

        /// <summary>The build's terrain raster: one texel per node, the planned height where the ground changes.</summary>
        public TerraformRaster ToRaster(float minimumChange = 0.02f)
        {
            var raster = new TerraformRaster
            {
                Origin = Origin - Cell * 0.5f,
                Cell = Cell,
                Width = Columns,
                Height = Rows,
                Target = new float[Count],
                Weight = new float[Count],
            };
            for (int index = 0; index < Count; index++)
            {
                bool changed = math.abs(Height[index] - Original[index]) >= minimumChange;
                raster.Target[index] = changed ? Height[index] : float.NaN;
                raster.Weight[index] = changed ? 1f : 0f;
            }
            return raster;
        }

        internal static GroundField Build(NeighbourhoodRequest request, NeighbourhoodPlan plan, List<PlannedLot> lots, CancellationToken cancellation)
        {
            if (plan.Roads.Count == 0) return null;
            float halfWidth = request.RoadHalfWidth;
            // A bank meets hillside that itself rises at up to the steepest buildable grade, so the steeper the buildable
            // ground, the further a cut must reach to meet it.
            float bankGrade = NeighbourhoodPlanner.EffectiveBankGrade(request);
            float climb = math.max(0.08f, bankGrade / 1.082f - request.SteepestBuildable);
            float bankReach = math.clamp((request.EarthworkLimit + 1f) / climb + 4f, 16f, request.SteepGround ? 96f : 48f);
            float2 minimum = float.MaxValue, maximum = float.MinValue;
            foreach (var road in plan.Roads)
                foreach (var point in road.Points) { minimum = math.min(minimum, point.xz); maximum = math.max(maximum, point.xz); }
            foreach (var lot in lots)
                foreach (var corner in lot.Pad.Corners) { minimum = math.min(minimum, corner.xz); maximum = math.max(maximum, corner.xz); }
            float margin = halfWidth + bankReach + 12f;
            var field = new GroundField(minimum - margin, maximum + margin, bankGrade, request.EarthworkLimit, request.GroundHeight)
            {
                // ponytail: only in steep mode, where ground at the bank limit is common; gentle sites keep the plain cones
                // their tests were tuned on. Turn it on everywhere if a gentle site ever shows a cliff at a steep patch.
                followHillside = request.SteepGround,
            };
            int count = field.Count;
            for (int index = 0; index < count; index++)
            {
                float height = request.GroundHeight(field.Position(index));
                field.Original[index] = math.isfinite(height) ? height : 0f;
            }

            // Road corridors: level across each road at its centreline's planned height. Where corridors overlap (a junction)
            // the heights blend by how deep the node sits in each, so the ground is continuous from one road into the other.
            var roadDistance = new float[count];
            var weightSum = new float[count];
            var weightedHeight = new float[count];
            var nearest = new float[count];
            var nearestHeight = new float[count];
            for (int index = 0; index < count; index++) roadDistance[index] = nearest[index] = float.MaxValue;
            float corridor = halfWidth + Shoulder;
            var touched = new List<int>();
            // The road each corridor node belongs to most (the deepest corridor), for steep mode's road-by-road test.
            var owner = new int[count];
            var ownerWeight = new float[count];
            for (int roadIndex = 0; roadIndex < plan.Roads.Count; roadIndex++)
            {
                var road = plan.Roads[roadIndex];
                touched.Clear();
                // At an existing road the corridor stops square: past the join is that road's ground, not ours.
                bool squareStart = plan.Nodes[road.StartNode].ExistingRoadId != 0, squareEnd = plan.Nodes[road.EndNode].ExistingRoadId != 0;
                for (int point = 1; point < road.Points.Length; point++)
                {
                    float3 start = road.Points[point - 1], end = road.Points[point];
                    bool first = point == 1 && squareStart, last = point == road.Points.Length - 1 && squareEnd;
                    field.ForBox(math.min(start.xz, end.xz) - corridor, math.max(start.xz, end.xz) + corridor, index =>
                    {
                        float2 position = field.Position(index);
                        float2 direction = end.xz - start.xz;
                        float lengthSquared = math.lengthsq(direction);
                        float along = lengthSquared > 1e-9f ? math.dot(position - start.xz, direction) / lengthSquared : 0f;
                        if ((first && along < 0f) || (last && along > 1f)) return;
                        float fraction = math.saturate(along);
                        float distance = math.distance(position, start.xz + direction * fraction);
                        if (distance > corridor || distance >= nearest[index]) return;
                        if (nearest[index] == float.MaxValue) touched.Add(index);
                        nearest[index] = distance;
                        nearestHeight[index] = math.lerp(start.y, end.y, fraction);
                    });
                }
                foreach (int index in touched)
                {
                    float depth = corridor - nearest[index] + 0.05f;
                    if (depth * depth > ownerWeight[index]) { ownerWeight[index] = depth * depth; owner[index] = roadIndex; }
                    weightSum[index] += depth * depth;
                    weightedHeight[index] += depth * depth * nearestHeight[index];
                    roadDistance[index] = math.min(roadDistance[index], nearest[index]);
                    nearest[index] = float.MaxValue;
                }
            }
            var roadHeight = new float[count];
            for (int index = 0; index < count; index++) if (weightSum[index] > 0f) roadHeight[index] = weightedHeight[index] / weightSum[index];

            // What is built, to bound where the ground may change.
            var built = new bool[count];
            for (int index = 0; index < count; index++) built[index] = roadDistance[index] <= corridor;
            if (request.LevelLots)
                foreach (var lot in lots)
                    field.ForPad(lot.Pad, PadMargin, index => built[index] = true);
            var reach = Geometry.Chamfer(built, field.Columns, field.Rows, field.Cell);
            // Existing road corridors, stamped segment by segment (a node-by-segment test was the slowest part of the plan).
            var nearExisting = new bool[count];
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < (road?.Points?.Length ?? 0); point++)
                {
                    float2 start = road.Points[point - 1].xz, end = road.Points[point].xz;
                    float clearance = road.HalfWidth + 0.5f;
                    field.ForBox(math.min(start, end) - clearance, math.max(start, end) + clearance, index =>
                    {
                        if (!nearExisting[index] && Geometry.DistanceToSegment(field.Position(index), start, end) < clearance) nearExisting[index] = true;
                    });
                }

            for (int index = 0; index < count; index++)
            {
                if ((index & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                if (reach[index] > bankReach)
                {
                    // Too far from anything built to change, even under a new road's corridor end.
                    if (roadDistance[index] <= corridor) field.Hold(index, Road, roadHeight[index]);
                    else field.Hold(index, Locked, field.Original[index]);
                    continue;
                }
                // An existing road's own corridor keeps its ground even under a new road's end: the game joins the two there.
                bool locked = nearExisting[index] || (request.Blocked != null && request.Blocked(field.Position(index)));
                // On ground too steep for any clean bank, a deep cut or fill under a road is left to the game's road, which
                // builds its own retaining walls; forcing it into the terrain would leave a cliff where the bank gives out.
                bool roadHere = roadDistance[index] <= corridor
                    && (!request.SteepGround || math.abs(roadHeight[index] - field.Original[index]) <= SteepCorridorChange);
                if (locked) field.Hold(index, Locked, field.Original[index]);
                // In steep mode a road node is tested like a pad, below, once the untouched ground's cones are known.
                else if (roadHere && !request.SteepGround) field.Hold(index, Road, roadHeight[index]);
                else { field.upper[index] = float.MaxValue; field.lower[index] = float.MinValue; }
            }
            field.ReleaseClashingShoulders(roadDistance, halfWidth);
            field.Sweep(cancellation);
            if (request.SteepGround)
            {
                // A corridor node the cones already push off the road's height would need a cut or fill that never meets the
                // ground, or a bank to the road above that steep: it is left to the game's road (its own retaining walls)
                // rather than forced into a cliff. Roads go one at a time, contour streets first, each tested against those
                // already held.
                var byRoad = new List<int>[plan.Roads.Count];
                for (int index = 0; index < count; index++)
                {
                    if (field.state[index] != Free || !(roadDistance[index] <= corridor)) continue;
                    if (math.abs(roadHeight[index] - field.Original[index]) > SteepCorridorChange) continue;
                    (byRoad[owner[index]] ?? (byRoad[owner[index]] = new List<int>())).Add(index);
                }
                var order = new List<int>();
                for (int road = 0; road < plan.Roads.Count; road++) order.Add(road);
                order.Sort((first, second) =>
                {
                    int kind = (plan.Roads[first].Kind == RoadKind.Contour ? 0 : 1).CompareTo(plan.Roads[second].Kind == RoadKind.Contour ? 0 : 1);
                    return kind != 0 ? kind : first.CompareTo(second);
                });
                var held = new List<int>();
                var heap = new MinHeap();
                foreach (int road in order)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (byRoad[road] == null) continue;
                    held.Clear();
                    foreach (int index in byRoad[road])
                    {
                        float height = roadHeight[index];
                        if (field.lower[index] > height + 0.01f || field.upper[index] < height - 0.01f) continue;
                        field.Hold(index, Road, height);
                        held.Add(index);
                    }
                    field.Grow(held, heap);
                }
                // Blended junction corners can still differ from a neighbour by more than a bank allows; the nodes that give
                // way take their bank from their neighbours.
                field.ReleaseClashingShoulders(roadDistance, -1f);
                field.Sweep(cancellation);
            }

            if (request.LevelLots)
            {
                var queue = new List<int>();
                var heap = new MinHeap();
                foreach (var lot in lots)
                {
                    cancellation.ThrowIfCancellationRequested();
                    queue.Clear();
                    float height = lot.Pad.Height;
                    field.ForPad(lot.Pad, PadMargin, index =>
                    {
                        if (field.state[index] != Free) return;
                        // A node the cones already push off this height would need a bank steeper than the grade: it yields.
                        if (field.lower[index] > height + 0.01f || field.upper[index] < height - 0.01f) return;
                        field.Hold(index, Held, height);
                        queue.Add(index);
                    });
                    field.Grow(queue, heap);
                }
            }

            for (int index = 0; index < count; index++)
            {
                if (field.state[index] != Free) continue;
                float low = field.lower[index], high = field.upper[index];
                // Crossed bounds come from rounding where two cones meet, or from roads the planner forced; the middle keeps the
                // ground continuous either way, where the original ground would leave a step.
                field.Height[index] = low <= high ? math.clamp(field.Original[index], low, high) : (low + high) * 0.5f;
            }
            // ponytail: no softening pass: averaging steepened banks against untouched neighbours; the 3.5 m heightmap rounds
            // the crease where a bank meets the hillside. Add a slope-limited smoothing if creases show in game.
            return field;
        }

        private void Hold(int index, byte kind, float height)
        {
            state[index] = kind;
            Height[index] = height;
            upper[index] = height;
            lower[index] = height;
        }

        /// <summary>
        /// Where two roads' corridors meet at different heights (close to a junction), a shoulder node (outside the
        /// carriageway) that would be steeper than the bank grade against its neighbour becomes free bank instead.
        /// </summary>
        private void ReleaseClashingShoulders(float[] roadDistance, float halfWidth)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool released = false;
                for (int index = 0; index < Count; index++)
                {
                    // Normally only the shoulder (outside the carriageway) gives way; a negative half width lets any node.
                    if (state[index] != Road || roadDistance[index] <= halfWidth) continue;
                    bool clash = false;
                    int column = index % Columns, row = index / Columns;
                    for (int neighbour = 0; neighbour < 8 && !clash; neighbour++)
                    {
                        int x = column + OffsetX[neighbour], y = row + OffsetY[neighbour];
                        if (x < 0 || y < 0 || x >= Columns || y >= Rows) continue;
                        int other = y * Columns + x;
                        float step = Step(index, other, neighbour < 4 ? straightStep : diagonalStep);
                        if (state[other] != Free && math.abs(Height[other] - Height[index]) > step + 0.01f) clash = true;
                    }
                    if (!clash) continue;
                    state[index] = Free;
                    upper[index] = float.MaxValue;
                    lower[index] = float.MinValue;
                    released = true;
                }
                if (!released) break;
            }
        }

        // The 8 neighbours: the 4 straight ones first, then the diagonals.
        private static readonly int[] OffsetX = { 1, -1, 0, 0, 1, -1, 1, -1 };
        private static readonly int[] OffsetY = { 0, 0, 1, -1, 1, 1, -1, -1 };

        /// <summary>Grows every cone from the held nodes until nothing changes (raster sweeps both ways).</summary>
        private void Sweep(CancellationToken cancellation)
        {
            // Only the box holding free nodes can change.
            int firstColumn = Columns, lastColumn = -1, firstRow = Rows, lastRow = -1;
            for (int index = 0; index < Count; index++)
            {
                if (state[index] != Free) continue;
                int column = index % Columns, row = index / Columns;
                if (column < firstColumn) firstColumn = column;
                if (column > lastColumn) lastColumn = column;
                if (row < firstRow) firstRow = row;
                if (row > lastRow) lastRow = row;
            }
            if (lastColumn < 0) return;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                cancellation.ThrowIfCancellationRequested();
                bool changed = false;
                for (int row = firstRow; row <= lastRow; row++)
                    for (int column = firstColumn; column <= lastColumn; column++)
                        changed |= Relax(row * Columns + column, column, row, -1);
                for (int row = lastRow; row >= firstRow; row--)
                    for (int column = lastColumn; column >= firstColumn; column--)
                        changed |= Relax(row * Columns + column, column, row, 1);
                if (!changed) break;
            }
        }

        // Pull a free node's cones from its already-swept neighbours (the half of the 8 behind the sweep direction).
        private bool Relax(int index, int column, int row, int direction)
        {
            if (state[index] != Free) return false;
            float high = upper[index], low = lower[index];
            int x = column + direction, y = row + direction, back = column - direction;
            bool insideX = x >= 0 && x < Columns, insideY = y >= 0 && y < Rows, insideBack = back >= 0 && back < Columns;
            if (insideX) Pull(index, row * Columns + x, straightStep, ref high, ref low);
            if (insideY) Pull(index, y * Columns + column, straightStep, ref high, ref low);
            if (insideX && insideY) Pull(index, y * Columns + x, diagonalStep, ref high, ref low);
            if (insideBack && insideY) Pull(index, y * Columns + back, diagonalStep, ref high, ref low);
            bool changed = false;
            if (high < upper[index] && high <= Original[index] + cap) { upper[index] = high; changed = true; }
            if (low > lower[index] && low >= Original[index] - cap) { lower[index] = low; changed = true; }
            return changed;
        }

        /// <summary>
        /// How far two neighbouring nodes may differ: the bank grade over their distance, or, on steep ground (steep mode), the
        /// hillside's own difference where it is larger, so the grading never makes a slope steeper than the hill already is.
        /// </summary>
        private float Step(int index, int other, float bankStep)
        {
            if (!followHillside) return bankStep;
            float natural = math.abs(Original[index] - Original[other]);
            return natural > bankStep ? natural : bankStep;
        }

        private void Pull(int index, int other, float bankStep, ref float high, ref float low)
        {
            float step = Step(index, other, bankStep);
            float otherUpper = upper[other], otherLower = lower[other];
            if (otherUpper < float.MaxValue && otherUpper + step < high) high = otherUpper + step;
            if (otherLower > float.MinValue && otherLower - step > low) low = otherLower - step;
        }

        /// <summary>Grows the cones of newly held nodes outwards, shortest paths first (one heap per bound).</summary>
        private void Grow(List<int> seeds, MinHeap heap)
        {
            if (seeds.Count == 0) return;
            foreach (int seed in seeds) heap.Push(upper[seed], seed);
            while (heap.Count > 0)
            {
                heap.Pop(out float value, out int index);
                if (value > upper[index]) continue;
                int column = index % Columns, row = index / Columns;
                for (int neighbour = 0; neighbour < 8; neighbour++)
                {
                    int x = column + OffsetX[neighbour], y = row + OffsetY[neighbour];
                    if (x < 0 || y < 0 || x >= Columns || y >= Rows) continue;
                    int other = y * Columns + x;
                    if (state[other] != Free) continue;
                    float candidate = value + Step(index, other, neighbour < 4 ? straightStep : diagonalStep);
                    if (candidate < upper[other] && candidate <= Original[other] + cap) { upper[other] = candidate; heap.Push(candidate, other); }
                }
            }
            foreach (int seed in seeds) heap.Push(-lower[seed], seed);
            while (heap.Count > 0)
            {
                heap.Pop(out float negative, out int index);
                float value = -negative;
                if (value < lower[index]) continue;
                int column = index % Columns, row = index / Columns;
                for (int neighbour = 0; neighbour < 8; neighbour++)
                {
                    int x = column + OffsetX[neighbour], y = row + OffsetY[neighbour];
                    if (x < 0 || y < 0 || x >= Columns || y >= Rows) continue;
                    int other = y * Columns + x;
                    if (state[other] != Free) continue;
                    float candidate = value - Step(index, other, neighbour < 4 ? straightStep : diagonalStep);
                    if (candidate > lower[other] && candidate >= Original[other] - cap) { lower[other] = candidate; heap.Push(-candidate, other); }
                }
            }
        }

        private void ForBox(float2 minimum, float2 maximum, Action<int> visit)
        {
            int firstColumn = math.max(0, (int)math.floor((minimum.x - Origin.x) / Cell));
            int lastColumn = math.min(Columns - 1, (int)math.ceil((maximum.x - Origin.x) / Cell));
            int firstRow = math.max(0, (int)math.floor((minimum.y - Origin.y) / Cell));
            int lastRow = math.min(Rows - 1, (int)math.ceil((maximum.y - Origin.y) / Cell));
            for (int row = firstRow; row <= lastRow; row++)
                for (int column = firstColumn; column <= lastColumn; column++) visit(row * Columns + column);
        }

        /// <summary>Nodes on a pad, grown by <paramref name="margin"/> at its back and sides (its front meets the road).</summary>
        private void ForPad(Pad pad, float margin, Action<int> visit)
        {
            float2 origin = pad.Corners[0].xz;
            float2 along = pad.Corners[1].xz - origin, across = pad.Corners[3].xz - origin;
            float alongLength = math.length(along), acrossLength = math.length(across);
            if (alongLength < 1e-3f || acrossLength < 1e-3f) return;
            float2 alongUnit = along / alongLength, acrossUnit = across / acrossLength;
            float2 minimum = float.MaxValue, maximum = float.MinValue;
            foreach (var corner in pad.Corners) { minimum = math.min(minimum, corner.xz); maximum = math.max(maximum, corner.xz); }
            ForBox(minimum - margin, maximum + margin, index =>
            {
                float2 offset = Position(index) - origin;
                float u = math.dot(offset, alongUnit), v = math.dot(offset, acrossUnit);
                if (u >= -margin && u <= alongLength + margin && v >= 0f && v <= acrossLength + margin) visit(index);
            });
        }

        /// <summary>Cut and fill volumes and the largest change.</summary>
        internal void Measure(out float cut, out float fill, out float most)
        {
            cut = fill = most = 0f;
            float area = Cell * Cell;
            for (int index = 0; index < Count; index++)
            {
                float change = Height[index] - Original[index];
                if (change < 0f) cut -= change * area; else fill += change * area;
                most = math.max(most, math.abs(change));
            }
        }

        private sealed class MinHeap
        {
            private readonly List<float> keys = new List<float>();
            private readonly List<int> values = new List<int>();
            public int Count => keys.Count;

            public void Push(float key, int value)
            {
                keys.Add(key);
                values.Add(value);
                int child = keys.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (keys[parent] <= keys[child]) break;
                    Swap(parent, child);
                    child = parent;
                }
            }

            public void Pop(out float key, out int value)
            {
                key = keys[0];
                value = values[0];
                int last = keys.Count - 1;
                keys[0] = keys[last];
                values[0] = values[last];
                keys.RemoveAt(last);
                values.RemoveAt(last);
                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1, right = left + 1, smallest = parent;
                    if (left < keys.Count && keys[left] < keys[smallest]) smallest = left;
                    if (right < keys.Count && keys[right] < keys[smallest]) smallest = right;
                    if (smallest == parent) break;
                    Swap(parent, smallest);
                    parent = smallest;
                }
            }

            private void Swap(int first, int second)
            {
                (keys[first], keys[second]) = (keys[second], keys[first]);
                (values[first], values[second]) = (values[second], values[first]);
            }
        }
    }
}
