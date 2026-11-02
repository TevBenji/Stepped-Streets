// Stepped Streets, the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    /// <summary>Turns a plan into what the terrain and zoning builds take, with each step built as a planted bank (PRD option A).</summary>
    public static class StepPlanPatches
    {
        /// <summary>The narrowest blend back to the hillside: Placemaker's build bank.</summary>
        public const float NarrowestBank = 4f;

        public const float WidestBank = 24f;

        // The game's marquee takes a cell by its centre, so each quad is pulled in from its cell's edges and never reaches
        // the centre of a neighbour (Placemaker's ZoneGuard.QuadShrink).
        private const float QuadShrink = 1f;

        /// <summary>
        /// One level patch per pad and one sloped patch per step. A step's bank takes Rise ÷ <paramref name="bankGrade"/> metres
        /// of street, half from each pad, and ramps between the two pad heights, so the ground climbs instead of jumping.
        /// </summary>
        public static List<PlannedTerraform> ToPatches(StepPlan plan, float bankGrade)
        {
            // ponytail: trims are not clamped to the pad; the settings' ranges (steps up to 3 m, bank grade from 0.2, pads at
            // least 16 m) keep two trims shorter than any pad. Clamp here if those ranges grow.
            var startTrims = new Dictionary<Pad, float>();
            var endTrims = new Dictionary<Pad, float>();
            foreach (var step in plan.Steps)
            {
                float half = math.abs(step.Rise) / bankGrade * 0.5f;
                endTrims[step.Earlier] = half;
                startTrims[step.Later] = half;
            }

            var patches = new List<PlannedTerraform>();
            var rings = new Dictionary<Pad, float3[]>();
            foreach (var pad in plan.Pads)
            {
                startTrims.TryGetValue(pad, out float startTrim);
                endTrims.TryGetValue(pad, out float endTrim);
                var corners = pad.Corners;
                float3 along = math.normalize(corners[1] - corners[0]);
                var ring = new[]
                {
                    corners[0] + along * startTrim,
                    corners[1] - along * endTrim,
                    corners[2] - along * endTrim,
                    corners[3] + along * startTrim,
                };
                rings[pad] = ring;
                patches.Add(new PlannedTerraform { Ring = ring, TargetHeight = pad.Height, Permanent = true });
            }

            foreach (var step in plan.Steps)
            {
                var earlier = rings[step.Earlier];
                var later = rings[step.Later];
                // Same winding as the pads: the earlier pad's end edge at its height, then the later pad's start edge at its
                // height, so the bank meets each pad exactly along their shared edges.
                patches.Add(new PlannedTerraform
                {
                    Ring = new[] { earlier[1], later[0], later[3], earlier[2] },
                    TargetHeight = (step.Earlier.Height + step.Later.Height) * 0.5f,
                    Permanent = true,
                    Sloped = true,
                });
            }

            return patches;
        }

        /// <summary>
        /// How wide the build's blend back to the hillside must be for the pad corner standing furthest off the ground to
        /// fall away no steeper than <paramref name="bankGrade"/>, kept between 4 and 24 m. <paramref name="groundHeight"/> takes world (x, z).
        /// </summary>
        public static float BankWidth(StepPlan plan, Func<float2, float> groundHeight, float bankGrade)
        {
            float largest = 0f;
            foreach (var pad in plan.Pads)
            {
                foreach (var corner in pad.Corners)
                    largest = math.max(largest, math.abs(pad.Height - groundHeight(corner.xz)));
            }

            return math.clamp(largest / bankGrade, NarrowestBank, WidestBank);
        }

        /// <summary>
        /// One zoning quad in world (x, z) per cell, given as (column, row) the way <see cref="StepPlanner.LevelCells"/> returns
        /// them: the 8 m cell pulled in 1 m on every side.
        /// </summary>
        public static List<float2[]> CellQuads(Pad pad, IEnumerable<int2> cells)
        {
            const float inset = QuadShrink / StepPlanner.CellSize;
            var quads = new List<float2[]>();
            foreach (var cell in cells)
            {
                quads.Add(new[]
                {
                    pad.PointAt(cell.x + inset, cell.y + inset),
                    pad.PointAt(cell.x + 1f - inset, cell.y + inset),
                    pad.PointAt(cell.x + 1f - inset, cell.y + 1f - inset),
                    pad.PointAt(cell.x + inset, cell.y + 1f - inset),
                });
            }

            return quads;
        }
    }
}
