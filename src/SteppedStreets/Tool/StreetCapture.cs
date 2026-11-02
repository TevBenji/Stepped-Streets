// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using SteppedStreets.Core.Planning;
using Unity.Entities;
using Unity.Mathematics;
using static System.FormattableString;

namespace SteppedStreets.Tool
{
    /// <summary>A named street, captured as one centreline the planner can read.</summary>
    internal sealed class StreetCapture
    {
        // One centreline point about every 4 m follows a curve closely and keeps the line short enough to plan quickly.
        private const float SampleSpacing = 4f;

        public List<float3> Centreline { get; } = new List<float3>();
        public List<Entity> Edges { get; } = new List<Entity>();
        public Entity Aggregate { get; private set; }

        /// <summary>Metres from the centreline to the kerb.</summary>
        public float HalfWidth { get; private set; }

        /// <summary>
        /// Every edge of the selected street, end to end, whether the selection is one of its road edges or the street (its
        /// aggregate) itself. Null, after one log line naming what was wrong, when nothing usable is selected.
        /// </summary>
        public static StreetCapture Capture(EntityManager entities, Entity selected)
        {
            if (selected == Entity.Null || !entities.Exists(selected))
            {
                Mod.Logger.Info("Stepped Streets: nothing is selected. Click a street with the select tool first.");
                return null;
            }

            // The aggregate is the named street; its element buffer lists every edge of that street.
            bool isStreet = entities.HasBuffer<AggregateElement>(selected);
            bool isRoadEdge = IsUsableEdge(entities, selected) && entities.HasComponent<Aggregated>(selected);
            if (!isStreet && !isRoadEdge)
            {
                Mod.Logger.Info($"Stepped Streets: the selection ({selected}) is neither a road edge nor a street. Click the road itself with the select tool.");
                return null;
            }

            var edges = new List<Entity>();
            Entity aggregate = isStreet ? selected : entities.GetComponentData<Aggregated>(selected).m_Aggregate;
            if (aggregate != Entity.Null && entities.HasBuffer<AggregateElement>(aggregate))
            {
                var elements = entities.GetBuffer<AggregateElement>(aggregate, true);
                for (int index = 0; index < elements.Length; index++)
                {
                    Entity edge = elements[index].m_Edge;
                    if (IsUsableEdge(entities, edge))
                        edges.Add(edge);
                }
            }

            if (isRoadEdge && !edges.Contains(selected))
                edges.Add(selected);
            if (edges.Count == 0)
            {
                Mod.Logger.Info($"Stepped Streets: the selected street ({selected}) has no road edges to step.");
                return null;
            }

            return CaptureEdges(entities, edges, aggregate);
        }

        public StreetCapture Add(EntityManager entities, StreetCapture addition)
        {
            var edges = new List<Entity>(Edges);
            foreach (var edge in addition.Edges)
                if (!edges.Contains(edge)) edges.Add(edge);
            var nodes = Nodes(entities, edges);
            if (!SiteGeometry.SingleChain(nodes)) return null;
            return CaptureEdges(entities, edges, Aggregate);
        }

        public StreetCapture Refresh(EntityManager entities)
        {
            foreach (var edge in Edges)
                if (!IsUsableEdge(entities, edge))
                    throw new InvalidOperationException("The selected road changed. Select the street again.");
            return CaptureEdges(entities, Edges, Aggregate);
        }

        private static List<(long Start, long End)> Nodes(EntityManager entities, List<Entity> edges)
        {
            var nodes = new List<(long Start, long End)>();
            foreach (var edge in edges)
            {
                var ends = entities.GetComponentData<Edge>(edge);
                nodes.Add((Pack(ends.m_Start), Pack(ends.m_End)));
            }
            return nodes;
        }

        private static long Pack(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;

        private static StreetCapture CaptureEdges(EntityManager entities, List<Entity> edges, Entity aggregate)
        {
            var order = StreetChain.Order(Nodes(entities, edges));
            var street = new StreetCapture { Aggregate = aggregate };
            foreach (var (edgeIndex, reversed) in order)
            {
                street.Edges.Add(edges[edgeIndex]);
                Entity composition = entities.GetComponentData<Composition>(edges[edgeIndex]).m_Edge;
                // ponytail: one width per chain; use its widest road to keep pads outside it, upgrade the planner to per-edge widths for mixed streets.
                street.HalfWidth = math.max(street.HalfWidth, entities.GetComponentData<NetCompositionData>(composition).m_Width * 0.5f);
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

        internal static bool IsUsableEdge(EntityManager entities, Entity edge) =>
            entities.HasComponent<Road>(edge) && entities.HasComponent<Edge>(edge) && entities.HasComponent<Curve>(edge)
            && entities.HasComponent<Composition>(edge) && !entities.HasComponent<Deleted>(edge)
            && !entities.HasComponent<Game.Tools.Temp>(edge);
    }
}
