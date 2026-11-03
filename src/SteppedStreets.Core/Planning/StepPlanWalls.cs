// Stepped Streets, bank foot walls. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    /// <summary>A wall's base in world coordinates, with the bank on its right in the (x, z) plan.</summary>
    public struct WallSegment
    {
        public float3 Start;
        public float3 End;
    }

    public static class StepPlanWalls
    {
        /// <summary>One wall along the foot of each bank on its lower pad, leaving the driveway apron clear.</summary>
        public static List<WallSegment> Segments(StepPlan plan, float bankGrade)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!math.isfinite(bankGrade) || bankGrade <= 0f) throw new ArgumentOutOfRangeException(nameof(bankGrade));

            var walls = new List<WallSegment>();
            foreach (var step in plan.Steps)
            {
                var pad = step.Earlier;
                int column = pad.WidthCells - StepPlanPatches.BankColumns(step, bankGrade);
                // ponytail: fixed 4 m setback matching W1's narrowest apron; use the planned apron depth if wall ends need to follow it.
                float2 front = pad.PointAt(column, 4f / StepPlanner.CellSize);
                float2 back = pad.PointAt(column, pad.DepthCells);
                float2 direction = back - front;
                float2 uphill = pad.Corners[1].xz - pad.Corners[0].xz;
                // Geometry rather than StreetSide also handles a centreline supplied from its uphill end.
                bool frontFirst = math.dot(new float2(direction.y, -direction.x), uphill) > 0f;
                walls.Add(new WallSegment
                {
                    Start = new float3(frontFirst ? front.x : back.x, pad.Height, frontFirst ? front.y : back.y),
                    End = new float3(frontFirst ? back.x : front.x, pad.Height, frontFirst ? back.y : front.y),
                });
            }
            return walls;
        }
    }
}
