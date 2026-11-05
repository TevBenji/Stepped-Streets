// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Rendering;
using Game.Simulation;
using SteppedStreets.Core.Neighbourhood;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace SteppedStreets.Tool
{
    /// <summary>
    /// Draws the area and its plan as an overlay projected on the ground (terrain heights are never previewed: changing them
    /// is permanent). Roads by grade, lots by zone, cut and fill shaded, trees as dots. The shapes are worked out once per plan.
    /// </summary>
    public sealed partial class NeighbourhoodPreviewSystem : GameSystemBase
    {
        private static readonly Color Outline = new Color(0.75f, 0.9f, 1f, 0.95f);
        private static readonly Color Fill = new Color(0.45f, 0.7f, 1f, 0.10f);
        private static readonly Color CornerColour = new Color(0.35f, 0.65f, 1f, 1f);
        private static readonly Color HoverColour = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Cut = new Color(0.62f, 0.4f, 0.26f, 0.5f);
        private static readonly Color Raised = new Color(0.9f, 0.8f, 0.55f, 0.45f);
        private static readonly Color Conifer = new Color(0.1f, 0.32f, 0.16f, 0.9f);
        private static readonly Color Broadleaf = new Color(0.22f, 0.46f, 0.16f, 0.9f);
        private const int MostTrees = 4000;
        private const int MostShading = 5000;

        private OverlayRenderSystem overlay;
        private TerrainSystem terrain;
        private NeighbourhoodToolSystem tool;
        private float nextErrorLog;
        private int drawnPlanVersion = -1;
        private readonly List<(float3 Start, float3 End, float Width, Color Colour)> lines = new List<(float3, float3, float, Color)>();
        private readonly List<(float3 Centre, float Diameter, Color Colour)> dots = new List<(float3, float, Color)>();

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
                terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
                tool = World.GetOrCreateSystemManaged<NeighbourhoodToolSystem>();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: the preview failed to start");
                Enabled = false;
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                if (tool == null || !tool.IsActive) return;
                var buffer = overlay.GetBuffer(out JobHandle dependencies);
                dependencies.Complete();
                if (tool.PlanVersion != drawnPlanVersion) Prepare();
                foreach (var (start, end, width, colour) in lines) Line(buffer, start, end, colour, width);
                foreach (var (centre, diameter, colour) in dots) buffer.DrawCircle(colour, centre, diameter);
                DrawArea(buffer);
            }
            catch (Exception exception)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextErrorLog) return;
                nextErrorLog = now + 5f;
                Mod.Logger.Error(exception, "Stepped Streets: drawing the preview failed");
            }
        }

        private void DrawArea(OverlayRenderSystem.Buffer buffer)
        {
            var corners = tool.Corners;
            if (corners.Count == 0 && !tool.Cursor.HasValue) return;
            var heights = terrain.GetHeightData();
            float3 Ground(float2 point)
            {
                float height = TerrainUtils.SampleHeight(ref heights, new float3(point.x, 0f, point.y));
                return new float3(point.x, math.isfinite(height) ? height : 0f, point.y);
            }
            // Sized from the picking reach, which follows the camera: the outline stays a clear line when zoomed out.
            float width = math.clamp(tool.Reach * 0.22f, 2f, 16f);
            int edges = tool.AreaClosed ? corners.Count : corners.Count - 1;
            for (int index = 0; index < edges; index++)
                Line(buffer, Ground(corners[index]), Ground(corners[(index + 1) % corners.Count]), Outline, width);
            var snapped = tool.Snapped;
            if (!tool.AreaClosed && corners.Count > 0 && tool.Cursor.HasValue)
            {
                float3 end = snapped.HasValue ? Ground(snapped.Value) : tool.Cursor.Value;
                Line(buffer, Ground(corners[corners.Count - 1]), end, new Color(Outline.r, Outline.g, Outline.b, 0.6f), width * 0.75f);
            }
            // A light fill until the plan arrives, so the drawn area reads as one shape.
            if (tool.AreaClosed && tool.Plan == null && tool.EarlyRoads == null && corners.Count >= 3) FillArea(buffer, corners, Ground);
            for (int index = 0; index < corners.Count; index++)
            {
                bool hovered = index == tool.HoveredCorner;
                bool closesArea = !tool.AreaClosed && index == 0 && corners.Count >= 3;
                buffer.DrawCircle(hovered || closesArea ? HoverColour : CornerColour, Ground(corners[index]), width * (hovered ? 5f : 3.5f));
            }
            // A ring where the cursor has snapped: the first corner (closing), another corner or a road.
            if (snapped.HasValue)
                buffer.DrawCircle(HoverColour, new Color(1f, 1f, 1f, 0f), width * 0.6f, OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f), Ground(snapped.Value), width * 7f);
        }

        private static void FillArea(OverlayRenderSystem.Buffer buffer, IReadOnlyList<float2> corners, Func<float2, float3> ground)
        {
            // ponytail: a scanline fill of 10 m strips; a mesh would be smoother if the strips show.
            float low = float.MaxValue, high = float.MinValue;
            foreach (var corner in corners) { low = math.min(low, corner.y); high = math.max(high, corner.y); }
            var crossings = new List<float>();
            for (float z = low + 5f; z < high; z += 10f)
            {
                crossings.Clear();
                for (int index = 0; index < corners.Count; index++)
                {
                    float2 a = corners[index], b = corners[(index + 1) % corners.Count];
                    if ((a.y <= z) == (b.y <= z)) continue;
                    crossings.Add(a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                crossings.Sort();
                for (int index = 0; index + 1 < crossings.Count; index += 2)
                    Line(buffer, ground(new float2(crossings[index], z)), ground(new float2(crossings[index + 1], z)), Fill, 10f);
            }
        }

        /// <summary>Works out every shape of the current plan once, so drawing a frame is only a loop.</summary>
        private void Prepare()
        {
            drawnPlanVersion = tool.PlanVersion;
            lines.Clear();
            dots.Clear();
            var plan = tool.Plan ?? tool.EarlyRoads;
            var request = tool.PlannedRequest;
            if (plan == null || request == null) return;

            if (plan.Ground != null)
            {
                // Cut and fill as squares on a grid coarse enough to stay under the drawing budget.
                var field = plan.Ground;
                float step = 8f;
                float width = (field.Columns - 1) * field.Cell, depth = (field.Rows - 1) * field.Cell;
                while (width / step * (depth / step) > MostShading * 4f) step *= 1.5f;
                for (float z = step * 0.5f; z < depth; z += step)
                    for (float x = step * 0.5f; x < width; x += step)
                    {
                        float2 point = field.Origin + new float2(x, z);
                        float change = field.ChangeAt(point);
                        if (math.abs(change) < 0.4f) continue;
                        var colour = change < 0f ? Cut : Raised;
                        colour.a *= math.saturate(math.abs(change) / 2.5f) * 0.7f + 0.3f;
                        float height = field.HeightAt(point);
                        lines.Add((new float3(point.x - step * 0.5f, height, point.y), new float3(point.x + step * 0.5f, height, point.y), step, colour));
                        if (lines.Count > MostShading) break;
                    }
            }

            foreach (var lot in plan.Lots)
            {
                var colour = ZoneColour(lot.Zone);
                var corners = lot.Pad.Corners;
                // A pad is a rectangle; a square-ended, pad-wide line fills it without a mesh.
                lines.Add(((corners[0] + corners[1]) * 0.5f, (corners[3] + corners[2]) * 0.5f, math.distance(corners[0].xz, corners[1].xz),
                    new Color(colour.r, colour.g, colour.b, 0.4f)));
                for (int index = 0; index < 4; index++)
                    lines.Add((corners[index], corners[(index + 1) % 4], 0.8f, new Color(colour.r * 0.6f, colour.g * 0.6f, colour.b * 0.6f, 0.9f)));
            }

            // Service buildings: a solid blue pad with a bright outline and a bar along the front that faces the road.
            var service = new Color(0.25f, 0.5f, 1f, 0.7f);
            foreach (var building in plan.Buildings)
            {
                var corners = building.Pad.Corners;
                lines.Add(((corners[0] + corners[1]) * 0.5f, (corners[3] + corners[2]) * 0.5f, math.distance(corners[0].xz, corners[1].xz), service));
                for (int index = 0; index < 4; index++)
                    lines.Add((corners[index], corners[(index + 1) % 4], 1.2f, new Color(0.75f, 0.88f, 1f, 1f)));
                lines.Add((corners[0], corners[1], 3f, new Color(1f, 1f, 1f, 1f)));
            }

            int stride = math.max(1, plan.Trees.Count / MostTrees);
            for (int index = 0; index < plan.Trees.Count; index += stride)
            {
                var tree = plan.Trees[index];
                dots.Add((tree.Position, 3f, tree.Kind == TreeKind.Conifer ? Conifer : Broadleaf));
            }

            foreach (var road in plan.Roads)
                for (int index = 1; index < road.Points.Length; index++)
                {
                    float3 start = road.Points[index - 1], end = road.Points[index];
                    float run = math.distance(start.xz, end.xz);
                    float grade = run > 0.01f ? math.abs(end.y - start.y) / run : 0f;
                    lines.Add((start, end, request.RoadHalfWidth * 1.6f, GradeColour(grade, request.MaxRoadGrade)));
                }
            foreach (var node in plan.Nodes)
                if (node.ExistingRoadId != 0) dots.Add((node.Position, 8f, new Color(1f, 0.3f, 0.85f, 1f)));
        }

        private static Color GradeColour(float grade, float maximum)
        {
            if (grade <= 0.03f) return new Color(0.82f, 0.86f, 0.92f, 0.9f);
            if (grade <= 0.06f) return new Color(0.98f, 0.85f, 0.3f, 0.9f);
            if (grade <= maximum + 0.003f) return new Color(1f, 0.55f, 0.15f, 0.9f);
            return new Color(1f, 0.15f, 0.15f, 0.95f);
        }

        internal static Color ZoneColour(string zone)
        {
            string name = (zone ?? "").ToLowerInvariant();
            // An unzoned lot (no zones in the mix): levelled ground, no zone colour.
            if (name.Length == 0) return new Color(0.82f, 0.82f, 0.82f);
            if (name.Contains("commercial")) return new Color(0.25f, 0.55f, 0.95f);
            if (name.Contains("office")) return new Color(0.65f, 0.4f, 0.9f);
            if (name.Contains("industrial")) return new Color(0.95f, 0.8f, 0.2f);
            if (name.Contains("medium") || name.Contains("row")) return new Color(0.2f, 0.75f, 0.35f);
            if (name.Contains("high")) return new Color(0.1f, 0.55f, 0.25f);
            return new Color(0.55f, 0.95f, 0.45f);
        }

        private static void Line(OverlayRenderSystem.Buffer buffer, float3 start, float3 end, Color colour, float width) =>
            buffer.DrawLine(colour, colour, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(start, end), width, float2.zero);
    }
}
