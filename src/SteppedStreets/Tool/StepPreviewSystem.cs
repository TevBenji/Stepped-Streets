// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Rendering;
using SteppedStreets.Core.Planning;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace SteppedStreets.Tool
{
    public sealed partial class StepPreviewSystem : GameSystemBase
    {
        private static readonly Color SelectionColor = new Color(0.65f, 0.85f, 1f, 0.95f);
        private static readonly Color PadFill = new Color(0.3f, 0.65f, 1f, 0.12f);
        private static readonly Color BankColor = new Color(0.18f, 0.38f, 0.5f, 0.95f);
        private static readonly Color EmptyColor = new Color(1f, 0.3f, 0.12f, 0.95f);
        private OverlayRenderSystem overlay;
        private StepStreetToolSystem tool;
        private float nextErrorLog;

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
                tool = World.GetOrCreateSystemManaged<StepStreetToolSystem>();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: the street preview failed to start");
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
                DrawLine(buffer, tool.HoveredLine, tool.State.HoverTooFlat ? Color.gray : Color.white, 1f);
                if (tool.Selection != null) DrawLine(buffer, tool.Selection.Centreline, SelectionColor, 1.4f);
                var plan = tool.Plan;
                if (plan == null) return;
                foreach (var pad in plan.Pads)
                {
                    var corners = pad.Corners;
                    // A pad is a rectangle; a square-ended, pad-wide line fills it without a mesh or temporary game entities.
                    Line(buffer, (corners[0] + corners[1]) * 0.5f, (corners[3] + corners[2]) * 0.5f,
                        PadFill, math.distance(corners[0].xz, corners[1].xz));
                    Outline(buffer, corners, SelectionColor);
                }
                foreach (var step in plan.Steps)
                {
                    var pad = step.Earlier;
                    float fraction = (pad.WidthCells - StepPlanPatches.BankColumns(step, tool.PreviewBankGrade)) / (float)pad.WidthCells;
                    var corners = pad.Corners;
                    float3 front = math.lerp(corners[0], corners[1], fraction);
                    float3 back = math.lerp(corners[3], corners[2], fraction);
                    Outline(buffer, new[] { front, step.Later.Corners[0], step.Later.Corners[3], back }, BankColor);
                    Line(buffer, step.EdgeFront, step.EdgeBack, BankColor, 1.1f);
                }
                foreach (var empty in plan.EmptyLots) Outline(buffer, empty.Corners, EmptyColor);
            }
            catch (Exception exception)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextErrorLog) return;
                nextErrorLog = now + 5f;
                Mod.Logger.Error(exception, "Stepped Streets: drawing the street preview failed");
            }
        }

        private static void Outline(OverlayRenderSystem.Buffer buffer, IReadOnlyList<float3> corners, Color color)
        {
            for (int index = 0; index < corners.Count; index++)
                Line(buffer, corners[index], corners[(index + 1) % corners.Count], color, 0.5f);
        }

        private static void DrawLine(OverlayRenderSystem.Buffer buffer, IReadOnlyList<float3> points, Color color, float width)
        {
            for (int index = 1; index < points.Count; index++) Line(buffer, points[index - 1], points[index], color, width);
        }

        private static void Line(OverlayRenderSystem.Buffer buffer, float3 start, float3 end, Color color, float width) =>
            buffer.DrawLine(color, color, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(start, end), width, float2.zero);
    }
}
