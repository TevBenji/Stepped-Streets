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

        // The game's marquee takes a cell by its centre, so each quad is pulled in from its cells' edges and never reaches
        // the centre of a neighbour (Placemaker's ZoneGuard.QuadShrink).
        private const float QuadShrink = 1f;

        /// <summary>
        /// One level patch per pad and one sloped patch per step. Each bank takes <see cref="BankColumns"/> whole columns
        /// from the uphill end of the pad before the step and ramps from that pad's height to the next pad's, so every pad
        /// keeps whole cells level and meets its bank along a whole edge. A pad whose columns all went to its bank gets no
        /// level patch.
        /// </summary>
        public static List<PlannedTerraform> ToPatches(StepPlan plan, float bankGrade)
        {
            var keptColumns = new Dictionary<Pad, int>();
            foreach (var step in plan.Steps)
                keptColumns[step.Earlier] = step.Earlier.WidthCells - BankColumns(step, bankGrade);

            var patches = new List<PlannedTerraform>();
            foreach (var pad in plan.Pads)
            {
                int kept = keptColumns.TryGetValue(pad, out int columns) ? columns : pad.WidthCells;
                if (kept == 0)
                    continue;
                var corners = pad.Corners;
                var ring = new[] { corners[0], corners[1], corners[2], corners[3] };
                if (kept < pad.WidthCells)
                    (ring[1], ring[2]) = Across(pad, kept);
                patches.Add(new PlannedTerraform { Ring = ring, TargetHeight = pad.Height, Permanent = true });
            }

            foreach (var step in plan.Steps)
            {
                var (front, back) = Across(step.Earlier, keptColumns[step.Earlier]);
                var later = step.Later.Corners;
                // Same winding as the pads. The later pad's start edge, not the earlier pad's end edge, closes the bank, so
                // on a bend the bank also fills the wedge between the two pads.
                patches.Add(new PlannedTerraform
                {
                    Ring = new[] { front, later[0], later[3], back },
                    TargetHeight = (step.Earlier.Height + step.Later.Height) * 0.5f,
                    Permanent = true,
                    Sloped = true,
                });
            }

            return patches;
        }

        /// <summary>
        /// How many whole 8 m columns a step's bank takes from the uphill end of the pad before it: enough to be no steeper
        /// than <paramref name="bankGrade"/>, at least one, at most the whole pad.
        /// </summary>
        public static int BankColumns(Step step, float bankGrade)
        {
            // Less the planner's step tolerance, so a step it allows at exactly the highest step does not round up a column.
            float columns = (math.abs(step.Rise) - StepPlanner.StepTolerance) / (StepPlanner.CellSize * bankGrade);
            return math.clamp((int)math.ceil(columns), 1, step.Earlier.WidthCells);
        }

        /// <summary>
        /// The tallest step after which a pad <paramref name="narrowestWidthCells"/> wide still keeps the two zoned columns
        /// the game's lone-column rule needs beside its bank; 0 when no step leaves it that.
        /// </summary>
        public static float HighestStepFor(int narrowestWidthCells, float bankGrade) =>
            math.max(0f, (narrowestWidthCells - StepPlanner.MinimumWidthCells) * StepPlanner.CellSize * bankGrade);

        // The line across the pad at a column boundary, front then back, at the pad's height.
        private static (float3 Front, float3 Back) Across(Pad pad, int column)
        {
            float fraction = column / (float)pad.WidthCells;
            return (math.lerp(pad.Corners[0], pad.Corners[1], fraction), math.lerp(pad.Corners[3], pad.Corners[2], fraction));
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

            // The raster blends with a smoothstep, whose steepest point is 1.5 times its average grade. Without this factor a
            // 15 % street shows sheets (TerrainMetrics) at its pad corners.
            return math.clamp(SmoothstepPeak * largest / bankGrade, NarrowestBank, WidestBank);
        }

        private const float SmoothstepPeak = 1.5f;

        /// <summary>
        /// One zoning quad in world (x, z) per run of neighbouring cells within a row, given as (column, row) the way
        /// <see cref="StepPlanner.LevelCells"/> returns them: the run pulled in 1 m on every side. The game's zone cells sit
        /// on each road edge's own 8 m grid, not the pad's, and its marquee takes every one of them whose centre lies inside
        /// the quad. A quad per run takes every game cell centred on the run however the two grids line up; a quad per pad
        /// cell could miss its game cell or catch the neighbour's.
        /// </summary>
        public static List<float2[]> CellQuads(Pad pad, IEnumerable<int2> cells)
        {
            const float inset = QuadShrink / StepPlanner.CellSize;
            var level = new HashSet<int2>(cells);
            var quads = new List<float2[]>();
            for (int row = 0; row < pad.DepthCells; row++)
            {
                for (int column = 0; column < pad.WidthCells; column++)
                {
                    if (!level.Contains(new int2(column, row)))
                        continue;
                    int first = column;
                    while (level.Contains(new int2(column + 1, row)))
                        column++;
                    quads.Add(new[]
                    {
                        pad.PointAt(first + inset, row + inset),
                        pad.PointAt(column + 1f - inset, row + inset),
                        pad.PointAt(column + 1f - inset, row + 1f - inset),
                        pad.PointAt(first + inset, row + 1f - inset),
                    });
                }
            }

            return quads;
        }
    }
}
