// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using SteppedStreets.Core.Planning;
using Unity.Entities;
using Unity.Mathematics;
using static System.FormattableString;

namespace SteppedStreets.Spike
{
    /// <summary>The street selected with the game's own select tool, as one centreline the planner can read.</summary>
    internal sealed class SelectedStreet
    {
        // One centreline point about every 4 m follows a curve closely and keeps the line short enough to plan quickly.
        private const float SampleSpacing = 4f;

        public List<float3> Centreline { get; } = new List<float3>();

        /// <summary>Metres from the centreline to the kerb.</summary>
        public float HalfWidth { get; private set; }

        /// <summary>Every edge of the selected edge's street, end to end. Null, after one log line, when the selection is not a road edge.</summary>
        public static SelectedStreet Capture(EntityManager entities, Entity selected)
        {
            if (selected == Entity.Null || !entities.Exists(selected) || !entities.HasComponent<Road>(selected)
                || !entities.HasComponent<Edge>(selected) || !entities.HasComponent<Curve>(selected)
                || !entities.HasComponent<Aggregated>(selected))
            {
                Mod.Logger.Info($"Stepped Streets: the selection ({selected}) is not a road edge. Click a street with the select tool first.");
                return null;
            }

            // The aggregate is the named street the edge belongs to; its element buffer lists every edge of that street.
            var edges = new List<Entity>();
            Entity aggregate = entities.GetComponentData<Aggregated>(selected).m_Aggregate;
            if (aggregate != Entity.Null && entities.HasBuffer<AggregateElement>(aggregate))
            {
                var elements = entities.GetBuffer<AggregateElement>(aggregate, true);
                for (int index = 0; index < elements.Length; index++)
                {
                    Entity edge = elements[index].m_Edge;
                    if (entities.HasComponent<Edge>(edge) && entities.HasComponent<Curve>(edge) && !entities.HasComponent<Deleted>(edge))
                        edges.Add(edge);
                }
            }

            if (!edges.Contains(selected))
                edges.Add(selected);

            var nodes = new List<(long Start, long End)>();
            foreach (var edge in edges)
            {
                var ends = entities.GetComponentData<Edge>(edge);
                nodes.Add((ends.m_Start.Index, ends.m_End.Index));
            }

            var order = StreetChain.Order(nodes);
            Entity composition = entities.GetComponentData<Composition>(selected).m_Edge;
            var street = new SelectedStreet { HalfWidth = entities.GetComponentData<NetCompositionData>(composition).m_Width * 0.5f };
            foreach (var (edgeIndex, reversed) in order)
            {
                var curve = entities.GetComponentData<Curve>(edges[edgeIndex]);
                var bezier = curve.m_Bezier;
                if (reversed)
                    bezier = new Bezier4x3(bezier.d, bezier.c, bezier.b, bezier.a);
                int segments = math.max(1, (int)math.ceil(curve.m_Length / SampleSpacing));
                // Each edge starts where the one before it ended, so its first point is already in the line.
                for (int index = street.Centreline.Count == 0 ? 0 : 1; index <= segments; index++)
                    street.Centreline.Add(MathUtils.Position(bezier, index / (float)segments));
            }

            float run = 0f;
            for (int index = 1; index < street.Centreline.Count; index++)
                run += math.distance(street.Centreline[index].xz, street.Centreline[index - 1].xz);
            float rise = math.abs(street.Centreline[street.Centreline.Count - 1].y - street.Centreline[0].y);
            Mod.Logger.Info(Invariant(
                $"Stepped Streets: street of {order.Count} edges ({edges.Count} in its aggregate), {run:0.0} m long, {rise:0.00} m rise, grade {(run > 0f ? rise / run * 100f : 0f):0.0}%, half width {street.HalfWidth:0.0} m, {street.Centreline.Count} points"));
            return street;
        }
    }
}
