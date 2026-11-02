// Stepped Streets, the hillside neighbourhood generator. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Turns the planned streets into the game's shape: roads between nodes, split wherever another road joins, and checks
    /// every rule again. A road that breaks one is dropped rather than kept; so is anything cut off from the rest.
    /// </summary>
    internal static class RoadNetwork
    {
        private const float ShortestStandalone = 300f;

        /// <summary>Removes links whose streets are gone and every part of the network that cannot be reached.</summary>
        public static bool Prune(List<Street> streets)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var street in streets)
                {
                    if (street.Removed || street.Kind == RoadKind.Contour) continue;
                    bool lost = (street.Start.Street >= 0 && streets[street.Start.Street].Removed)
                        || (street.End.Street >= 0 && streets[street.End.Street].Removed);
                    if (lost) { street.Removed = true; changed = true; }
                }
            }

            var component = Components(streets, out var lengths);
            int count = streets.Count;
            var rooted = new List<bool>();
            for (int id = 0; id < lengths.Count; id++) rooted.Add(false);
            for (int index = 0; index < count; index++)
                if (!streets[index].Removed && (streets[index].Start.Existing || streets[index].End.Existing)) rooted[component[index]] = true;
            bool connected = rooted.Contains(true);
            int largest = -1;
            for (int id = 0; id < lengths.Count; id++) if (largest < 0 || lengths[id] > lengths[largest]) largest = id;
            for (int index = 0; index < count; index++)
            {
                if (streets[index].Removed) continue;
                int id = component[index];
                // With a connection, keep what it reaches. Without one (the area need not touch a road), keep every network
                // big enough to be a neighbourhood of its own, or at least the largest.
                bool keep = connected ? rooted[id] : (lengths[id] >= ShortestStandalone || id == largest);
                if (!keep) streets[index].Removed = true;
            }
            return connected;
        }

        /// <summary>Which network piece each street belongs to (−1 for removed streets), and each piece's length.</summary>
        public static int[] Components(List<Street> streets, out List<float> lengths)
        {
            int count = streets.Count;
            var component = new int[count];
            for (int index = 0; index < count; index++) component[index] = -1;
            var neighbours = new List<int>[count];
            for (int index = 0; index < count; index++) neighbours[index] = new List<int>();
            for (int index = 0; index < count; index++)
            {
                var street = streets[index];
                if (street.Removed) continue;
                foreach (var attachment in new[] { street.Start, street.End })
                    if (attachment.Street >= 0 && !streets[attachment.Street].Removed)
                    {
                        neighbours[index].Add(attachment.Street);
                        neighbours[attachment.Street].Add(index);
                    }
            }
            lengths = new List<float>();
            for (int index = 0; index < count; index++)
            {
                if (streets[index].Removed || component[index] >= 0) continue;
                int id = lengths.Count;
                float length = 0f;
                var stack = new Stack<int>();
                stack.Push(index);
                component[index] = id;
                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    length += streets[current].Length;
                    foreach (int next in neighbours[current])
                        if (component[next] < 0) { component[next] = id; stack.Push(next); }
                }
                lengths.Add(length);
            }
            return component;
        }

        /// <summary>Roads between nodes; <paramref name="owners"/> gets the street each road came from.</summary>
        public static void Assemble(List<Street> streets, NeighbourhoodPlan plan, List<int> owners)
        {
            plan.Nodes.Clear();
            plan.Roads.Clear();
            owners.Clear();
            var nodeByKey = new Dictionary<long, int>();
            var cuts = new List<SortedSet<int>>();
            foreach (var street in streets)
            {
                var set = new SortedSet<int>();
                if (!street.Closed) { set.Add(0); set.Add(street.Points.Count - 1); }
                cuts.Add(set);
            }
            for (int index = 0; index < streets.Count; index++)
            {
                var street = streets[index];
                if (street.Removed) continue;
                foreach (var attachment in new[] { street.Start, street.End })
                    if (attachment.Street >= 0 && !streets[attachment.Street].Removed) cuts[attachment.Street].Add(attachment.Point);
            }

            long Key(int street, int point)
            {
                var owner = streets[street];
                if (owner.Kind != RoadKind.Contour)
                {
                    var attachment = point == 0 ? owner.Start : point == owner.Points.Count - 1 ? owner.End : Attachment.None;
                    if (attachment.Existing) return -1 - street;
                    if (attachment.Street >= 0) return ((long)attachment.Street << 20) | (uint)attachment.Point;
                }
                return ((long)street << 20) | (uint)point;
            }
            int Node(int street, int point)
            {
                long key = Key(street, point);
                if (nodeByKey.TryGetValue(key, out int node)) return node;
                var owner = streets[street];
                var attachment = point == 0 ? owner.Start : owner.End;
                var planned = new PlannedNode { Position = owner.Point3(point) };
                if (key < 0)
                {
                    planned.ExistingRoadId = attachment.ExistingId;
                    planned.ExistingRoadT = attachment.ExistingT;
                }
                nodeByKey[key] = plan.Nodes.Count;
                plan.Nodes.Add(planned);
                return plan.Nodes.Count - 1;
            }

            for (int index = 0; index < streets.Count; index++)
            {
                var street = streets[index];
                if (street.Removed) continue;
                var list = new List<int>(cuts[index]);
                int count = street.Points.Count;
                if (street.Closed)
                {
                    // A loop needs two nodes: the game has no edge from a node back to itself.
                    if (list.Count == 0) list.Add(0);
                    if (list.Count == 1) { list.Add((list[0] + count / 2) % count); list.Sort(); }
                }
                int pieces = street.Closed ? list.Count : list.Count - 1;
                for (int piece = 0; piece < pieces; piece++)
                {
                    int first = list[piece];
                    int last = street.Closed && piece == list.Count - 1 ? list[0] + count : list[piece + 1];
                    if (last - first < 1) continue;
                    var points = new float3[last - first + 1];
                    for (int offset = 0; offset < points.Length; offset++) points[offset] = street.Point3((first + offset) % count);
                    var road = new PlannedRoad
                    {
                        Kind = street.Kind,
                        Points = points,
                        StartNode = Node(index, first % count),
                        EndNode = Node(index, last % count),
                        SteepestGrade = RoadProfile.SteepestGrade(points),
                    };
                    for (int offset = 1; offset < points.Length; offset++) road.Length += math.distance(points[offset].xz, points[offset - 1].xz);
                    // Ends sit exactly on their nodes, so the game merges the junctions.
                    road.Points[0] = plan.Nodes[road.StartNode].Position;
                    road.Points[points.Length - 1] = plan.Nodes[road.EndNode].Position;
                    plan.Roads.Add(road);
                    owners.Add(index);
                }
            }
            foreach (var road in plan.Roads)
            {
                plan.Nodes[road.StartNode].Degree++;
                plan.Nodes[road.EndNode].Degree++;
            }
            foreach (var node in plan.Nodes) node.DeadEnd = node.Degree == 1 && node.ExistingRoadId == 0;
        }

        /// <summary>The streets whose roads break a rule; the caller drops them and assembles again.</summary>
        public static HashSet<int> Violations(NeighbourhoodPlan plan, List<int> owners, List<Street> streets, NeighbourhoodRequest request,
            float streetRadius, float linkRadius, List<string> notes)
        {
            var bad = new HashSet<int>();
            void Flag(int road, string why)
            {
                int street = owners[road];
                if (bad.Add(street)) notes.Add(why);
            }
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var planned = plan.Roads[road];
                float limit = planned.Kind == RoadKind.Contour ? RoadProfile.LandingGrade : request.MaxRoadGrade;
                if (planned.SteepestGrade > limit + 0.003f) Flag(road, $"a {planned.Kind} road was steeper than {limit:P0}");
                var plan2 = new List<float2>(planned.Points.Length);
                foreach (var point in planned.Points) plan2.Add(point.xz);
                float minimumRadius = planned.Kind == RoadKind.Contour ? streetRadius : linkRadius;
                if (plan2.Count >= 7 && Geometry.TightestRadius(plan2, 12f, out _) < minimumRadius * 0.95f)
                    Flag(road, $"a {planned.Kind} road turned tighter than {minimumRadius:0} m");
            }

            // Junction angles and spacing.
            var ends = new List<(int Road, float2 Direction)>[plan.Nodes.Count];
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var points = plan.Roads[road].Points;
                AddEnd(ends, plan.Roads[road].StartNode, road, Direction(points, true));
                AddEnd(ends, plan.Roads[road].EndNode, road, Direction(points, false));
            }
            var junctions = new List<int>();
            for (int node = 0; node < plan.Nodes.Count; node++)
            {
                var list = ends[node];
                if (list == null || list.Count < 3) continue;
                junctions.Add(node);
                for (int first = 0; first < list.Count; first++)
                    for (int second = first + 1; second < list.Count; second++)
                    {
                        float angle = math.degrees(math.acos(math.clamp(math.dot(list[first].Direction, list[second].Direction), -1f, 1f)));
                        if (angle >= 59f) continue;
                        int blamed = streets[owners[list[first].Road]].Kind == RoadKind.Contour ? list[second].Road : list[first].Road;
                        Flag(blamed, "a junction was sharper than 60°");
                    }
            }
            for (int first = 0; first < junctions.Count; first++)
                for (int second = first + 1; second < junctions.Count; second++)
                {
                    float distance = math.distance(plan.Nodes[junctions[first]].Position.xz, plan.Nodes[junctions[second]].Position.xz);
                    if (distance >= RoadLinks.JunctionSpacing - 1f) continue;
                    foreach (var end in ends[junctions[second]])
                        if (streets[owners[end.Road]].Kind != RoadKind.Contour) { Flag(end.Road, "two junctions were closer than 40 m"); break; }
                }

            // Clearance between roads away from the nodes they share: both corridors (road and shoulders), and room for a bank
            // no steeper than the bank grade across their difference in height.
            float clearance = (request.RoadHalfWidth + 1.5f) * 2f + 1f;
            float bank = request.BankGrade / 1.1f;
            var buckets = new SpatialBuckets<int2>(48f);
            float reach = clearance + 20f;
            for (int road = 0; road < plan.Roads.Count; road++)
                for (int point = 0; point < plan.Roads[road].Points.Length; point++)
                {
                    float2 position = plan.Roads[road].Points[point].xz;
                    buckets.Add(position - reach, position + reach, new int2(road, point));
                }
            float nodeReach = request.RoadHalfWidth * 2f + 22f;
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var planned = plan.Roads[road];
                for (int point = 0; point < planned.Points.Length; point++)
                {
                    float2 position = planned.Points[point].xz;
                    foreach (var other in buckets.Near(position))
                    {
                        if (other.x <= road) continue;
                        var otherRoad = plan.Roads[other.x];
                        float needed = clearance + math.abs(otherRoad.Points[other.y].y - planned.Points[point].y) / bank;
                        if (math.distance(otherRoad.Points[other.y].xz, position) >= needed) continue;
                        if (NearShared(planned, otherRoad, plan, position, nodeReach)) continue;
                        int blamed = streets[owners[road]].Kind == RoadKind.Contour ? other.x : road;
                        Flag(blamed, "two roads came too close");
                    }
                }
            }
            return bad;
        }

        private static bool NearShared(PlannedRoad first, PlannedRoad second, NeighbourhoodPlan plan, float2 position, float reach)
        {
            foreach (int node in new[] { first.StartNode, first.EndNode })
                if ((node == second.StartNode || node == second.EndNode) && math.distance(plan.Nodes[node].Position.xz, position) < reach)
                    return true;
            return false;
        }

        private static void AddEnd(List<(int, float2)>[] ends, int node, int road, float2 direction)
        {
            if (ends[node] == null) ends[node] = new List<(int, float2)>();
            ends[node].Add((road, direction));
        }

        /// <summary>
        /// The direction a road leaves its start (or end) node: over its first 4–6 m, the way the game builds the junction
        /// from the edge's tangent at the node.
        /// </summary>
        private static float2 Direction(float3[] points, bool fromStart)
        {
            var plan = new List<float2>(points.Length);
            foreach (var point in points) plan.Add(point.xz);
            return EndDirection(plan, fromStart);
        }

        internal static float2 EndDirection(IReadOnlyList<float2> points, bool fromStart)
        {
            float2 origin = fromStart ? points[0] : points[points.Count - 1];
            for (int step = 1; step < points.Count; step++)
            {
                float2 point = fromStart ? points[step] : points[points.Count - 1 - step];
                if (math.distance(point, origin) >= 3.5f || step == points.Count - 1) return math.normalizesafe(point - origin, new float2(1f, 0f));
            }
            return new float2(1f, 0f);
        }
    }
}
