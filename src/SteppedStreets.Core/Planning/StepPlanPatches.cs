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
        /// A level patch per pad, a sloped patch per step, and apron strips meeting the street.
        /// Each bank takes <see cref="BankColumns"/> whole columns
        /// from the uphill end of the pad before the step and ramps from that pad's height to the next pad's, so every pad
        /// keeps whole cells level and meets its bank along a whole edge. A pad whose columns all went to its bank gets no
        /// level patch.
        /// </summary>
        public static List<PlannedTerraform> ToPatches(StepPlan plan, float bankGrade)
        {
            var keptColumns = new Dictionary<Pad, int>();
            var nextPads = new Dictionary<Pad, Pad>();
            foreach (var step in plan.Steps)
            {
                keptColumns[step.Earlier] = step.Earlier.WidthCells - BankColumns(step, bankGrade);
                nextPads[step.Earlier] = step.Later;
            }

            var apronDepths = new Dictionary<Pad, float>();
            foreach (var pad in plan.Pads)
            {
                int kept = keptColumns.TryGetValue(pad, out int columns) ? columns : pad.WidthCells;
                float FrontHeight(float column) => column <= kept ? pad.Height
                    : math.lerp(pad.Height, nextPads[pad].Height, (column - kept) / (pad.WidthCells - kept));
                float largest = math.abs(StreetHeight(pad, kept) - pad.Height);
                foreach (var point in pad.StreetProfile)
                    largest = math.max(largest, math.abs(point.y - FrontHeight(point.x)));
                // ponytail: the specified 8 m apron cap cannot hold offsets above 8 × bankGrade at that slope;
                // reject those lots or allow longer driveways if wider lots need the same slope guarantee.
                apronDepths[pad] = math.clamp(largest / bankGrade, 4f, 8f);
            }

            var patches = new List<PlannedTerraform>();
            foreach (var pad in plan.Pads)
            {
                int kept = keptColumns.TryGetValue(pad, out int columns) ? columns : pad.WidthCells;
                if (kept == 0)
                    continue;
                var (frontStart, backStart) = Across(pad, 0, apronDepths[pad]);
                var (frontEnd, backEnd) = Across(pad, kept, apronDepths[pad]);
                var ring = new[] { frontStart, frontEnd, backEnd, backStart };
                // Uniform vertex heights still make a level pad. Carry its offset into the blend just like the banks
                // and aprons; switching between absolute-height and offset blends tears their outside boundaries.
                patches.Add(new PlannedTerraform { Ring = ring, TargetHeight = pad.Height, Permanent = true, Sloped = true });
            }

            foreach (var step in plan.Steps)
            {
                var (front, back) = Across(step.Earlier, keptColumns[step.Earlier], apronDepths[step.Earlier]);
                var (laterFront, laterBack) = Across(step.Later, 0, apronDepths[step.Later]);
                // Same winding as the pads. The later pad's start edge, not the earlier pad's end edge, closes the bank, so
                // on a bend the bank also fills the wedge between the two pads.
                patches.Add(new PlannedTerraform
                {
                    Ring = new[] { front, laterFront, laterBack, back },
                    TargetHeight = (step.Earlier.Height + step.Later.Height) * 0.5f,
                    Permanent = true,
                    Sloped = true,
                });
            }

            foreach (var pad in plan.Pads)
            {
                int kept = keptColumns.TryGetValue(pad, out int columns) ? columns : pad.WidthCells;
                var boundaries = new SortedSet<float> { kept };
                foreach (var point in pad.StreetProfile)
                    boundaries.Add(point.x);
                float previous = 0f;
                foreach (float column in boundaries)
                {
                    if (column == 0f)
                        continue;
                    float Height(float boundary) => boundary <= kept ? pad.Height
                        : math.lerp(pad.Height, nextPads[pad].Height, (boundary - kept) / (pad.WidthCells - kept));
                    float3 Front(float boundary)
                    {
                        float2 position = pad.PointAt(boundary, 0f);
                        return new float3(position.x, StreetHeight(pad, boundary), position.y);
                    }
                    float3 Back(float boundary)
                    {
                        if (boundary > kept)
                        {
                            // Taper the bank part of an apron to the next pad's depth so a change of lot width has no seam.
                            var bankStart = Across(pad, kept, apronDepths[pad]).Front;
                            var bankEnd = Across(nextPads[pad], 0, apronDepths[nextPads[pad]]).Front;
                            return math.lerp(bankStart, bankEnd, (boundary - kept) / (pad.WidthCells - kept));
                        }
                        float2 position = pad.PointAt(boundary, apronDepths[pad] / StepPlanner.CellSize);
                        return new float3(position.x, Height(boundary), position.y);
                    }
                    patches.Add(new PlannedTerraform
                    {
                        Ring = new[] { Front(previous), Front(column), Back(column), Back(previous) },
                        TargetHeight = (StreetHeight(pad, previous) + StreetHeight(pad, column) + Height(previous) + Height(column)) * 0.25f,
                        Permanent = true,
                        Sloped = true,
                    });
                    previous = column;
                }
            }

            return patches;
        }

        private static float StreetHeight(Pad pad, float column)
        {
            for (int index = 1; index < pad.StreetProfile.Count; index++)
            {
                var previous = pad.StreetProfile[index - 1];
                var next = pad.StreetProfile[index];
                if (column <= next.x)
                    return math.lerp(previous.y, next.y, (column - previous.x) / (next.x - previous.x));
            }
            return pad.StreetProfile[pad.StreetProfile.Count - 1].y;
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
        private static (float3 Front, float3 Back) Across(Pad pad, int column, float apronDepth)
        {
            float fraction = column / (float)pad.WidthCells;
            float3 front = math.lerp(pad.Corners[0], pad.Corners[1], fraction);
            return (front + (pad.Corners[3] - pad.Corners[0]) * (apronDepth / (pad.DepthCells * StepPlanner.CellSize)),
                math.lerp(pad.Corners[3], pad.Corners[2], fraction));
        }

        /// <summary>
        /// A blend width whose sampled smoothstep profiles stay within the bank grade, clamped to 4–24 m.
        /// Samples extend out from every pad corner, counting the hillside's continued rise or fall under the blend.
        /// </summary>
        public static float BankWidth(StepPlan plan, Func<float2, float> groundHeight, float bankGrade)
        {
            float largestOffset = 0f;
            float groundGrade = 0f;
            foreach (var pad in plan.Pads)
            {
                for (int index = 0; index < pad.Corners.Count; index++)
                {
                    float2 corner = pad.Corners[index].xz;
                    float2 first = math.normalize(corner - pad.Corners[(index + 1) % 4].xz);
                    float2 second = math.normalize(corner - pad.Corners[(index + 3) % 4].xz);
                    float offset = math.abs(pad.Height - groundHeight(corner));
                    if (!math.isfinite(offset))
                        return WidestBank;
                    largestOffset = math.max(largestOffset, offset);
                    // ponytail: corner rays at 0.5 m intervals cover the snapshot's smooth hills, not arbitrary cliffs
                    // between samples; use a shared grade-limited field if rough sites need a continuous slope guarantee.
                    foreach (var direction in new[] { first, second, math.normalize(first + second) })
                    {
                        float previous = groundHeight(corner);
                        for (float distance = 0.5f; distance <= WidestBank; distance += 0.5f)
                        {
                            float height = groundHeight(corner + direction * distance);
                            float grade = math.abs(height - previous) / 0.5f;
                            if (!math.isfinite(grade))
                                return WidestBank;
                            groundGrade = math.max(groundGrade, grade);
                            previous = height;
                        }
                    }
                }
            }

            // Sloped raster items carry a fixed height offset into the hillside. The ground's own grade uses part of
            // the slope budget; the remaining part takes the offset's smoothstep, whose peak derivative is 1.5.
            float remainingGrade = bankGrade - groundGrade;
            // ponytail: the specified 24 m cap can exhaust the slope budget on extreme sites; a shared terrain field
            // must release pad area if those sites need the same grade guarantee.
            return remainingGrade <= 0f ? WidestBank
                : math.clamp(1.5f * largestOffset / remainingGrade, NarrowestBank, WidestBank);
        }

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
