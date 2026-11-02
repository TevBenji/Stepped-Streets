// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Collections.Generic;

namespace SteppedStreets.Core.Planning
{
    /// <summary>Puts the edges of one street end to end, the way the game's nets join at nodes.</summary>
    public static class StreetChain
    {
        /// <summary>
        /// Each edge's index in the order the street runs, and whether it is walked backwards (from its end node to its start
        /// node) to keep one direction. The walk starts at a dead end when the chain has one; a loop starts at the first edge.
        /// An edge that does not join the chain is left out.
        /// </summary>
        public static List<(int Edge, bool Reversed)> Order(IReadOnlyList<(long Start, long End)> edges)
        {
            // ponytail: finds the next edge with a scan, O(n²) in a street's edges (tens); index the nodes if streets get long.
            var order = new List<(int Edge, bool Reversed)>();
            if (edges.Count == 0)
                return order;

            var touching = new Dictionary<long, int>();
            foreach (var edge in edges)
            {
                touching[edge.Start] = touching.TryGetValue(edge.Start, out int starts) ? starts + 1 : 1;
                touching[edge.End] = touching.TryGetValue(edge.End, out int ends) ? ends + 1 : 1;
            }

            // A node only one edge touches is an end of the street; starting mid-street would leave the far half behind.
            int current = 0;
            bool reversed = false;
            for (int index = 0; index < edges.Count; index++)
            {
                if (touching[edges[index].Start] == 1 || touching[edges[index].End] == 1)
                {
                    current = index;
                    reversed = touching[edges[index].Start] != 1;
                    break;
                }
            }

            var used = new bool[edges.Count];
            while (current >= 0)
            {
                used[current] = true;
                order.Add((current, reversed));
                long node = reversed ? edges[current].Start : edges[current].End;
                current = -1;
                for (int index = 0; index < edges.Count && current < 0; index++)
                {
                    if (used[index])
                        continue;
                    if (edges[index].Start == node || edges[index].End == node)
                    {
                        current = index;
                        reversed = edges[index].Start != node;
                    }
                }
            }

            return order;
        }
    }
}
