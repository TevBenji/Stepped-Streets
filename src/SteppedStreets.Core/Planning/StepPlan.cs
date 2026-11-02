// Stepped Streets, the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    /// <summary>The planner's answer for one street, in world coordinates.</summary>
    /// <remarks>Distances along the street are metres on the ground plan, measured along the centreline from its downhill end.</remarks>
    public sealed class StepPlan
    {
        internal readonly List<Pad> pads = new List<Pad>();
        internal readonly List<Step> steps = new List<Step>();
        internal readonly List<EmptyLot> emptyLots = new List<EmptyLot>();
        internal readonly List<FlatStretch> flatStretches = new List<FlatStretch>();
        internal readonly List<string> messages = new List<string>();

        /// <summary>Left side first, each side from the downhill end.</summary>
        public IReadOnlyList<Pad> Pads => pads;

        public IReadOnlyList<Step> Steps => steps;

        public IReadOnlyList<EmptyLot> EmptyLots => emptyLots;

        public IReadOnlyList<FlatStretch> FlatStretches => flatStretches;

        public StepPlanSummary Summary { get; internal set; }

        /// <summary>PRD 5.5's "flatter than" message when the whole street is too gentle to step; otherwise null.</summary>
        public string Message { get; internal set; }

        public string SummaryLine { get; internal set; }

        public IReadOnlyList<string> Messages => messages;
    }

    /// <summary>One lot on its own level pad.</summary>
    public sealed class Pad
    {
        public StreetSide Side { get; internal set; }

        /// <summary>Front at the start, front at the end, back at the end, back at the start; y is the pad height.</summary>
        public IReadOnlyList<float3> Corners { get; internal set; }

        public float Height { get; internal set; }

        public float Start { get; internal set; }

        public float End { get; internal set; }

        public int WidthCells { get; internal set; }

        public int DepthCells { get; internal set; }

        // Keep every change of street grade so an apron follows the road even within a lot.
        internal IReadOnlyList<float2> StreetProfile { get; set; }

        /// <summary>
        /// World x and z of a point given in cells from the pad's front corner at its start: column along the kerb,
        /// row away from the road (row 0 is next to the road).
        /// </summary>
        public float2 PointAt(float column, float row)
        {
            float2 origin = Corners[0].xz;
            return origin
                + (Corners[1].xz - origin) * (column / WidthCells)
                + (Corners[3].xz - origin) * (row / DepthCells);
        }
    }

    /// <summary>The change of height between two neighbouring pads on one side.</summary>
    public sealed class Step
    {
        public StreetSide Side { get; internal set; }

        /// <summary>The later pad's height minus the earlier one's; positive where the street climbs.</summary>
        public float Rise { get; internal set; }

        /// <summary>The kerb end of the edge the two pads share; y is the earlier pad's height.</summary>
        public float3 EdgeFront { get; internal set; }

        /// <summary>The back end of the shared edge; y is the earlier pad's height.</summary>
        public float3 EdgeBack { get; internal set; }

        /// <summary>The pad before the step, nearer the downhill end.</summary>
        public Pad Earlier { get; internal set; }

        public Pad Later { get; internal set; }
    }

    public enum EmptyLotReason
    {
        /// <summary>Even at the narrowest width the step to the previous pad is taller than the highest step.</summary>
        TooSteep,

        /// <summary>The end of the street, shorter than the narrowest lot.</summary>
        LeftOver,

        TooMuchEarthwork,

        Blocked,
    }

    /// <summary>Land beside the street that gets no pad, and why.</summary>
    public sealed class EmptyLot
    {
        public StreetSide Side { get; internal set; }

        /// <summary>Same order as <see cref="Pad.Corners"/>; y is the street's height at the middle of the front.</summary>
        public IReadOnlyList<float3> Corners { get; internal set; }

        public float Start { get; internal set; }

        public float End { get; internal set; }

        public EmptyLotReason Reason { get; internal set; }

        /// <summary>The reason in the preview's words (PRD 5.5).</summary>
        public string Wording => Reason switch
        {
            EmptyLotReason.TooSteep => "too steep for lots this wide",
            EmptyLotReason.TooMuchEarthwork => "too much earthwork",
            EmptyLotReason.Blocked => "blocked by a building, road or water",
            _ => "too short for a lot",
        };
    }

    /// <summary>A stretch left alone because it is flatter than the minimum grade (R1).</summary>
    public sealed class FlatStretch
    {
        public StreetSide Side { get; internal set; }

        public float Start { get; internal set; }

        public float End { get; internal set; }
    }

    /// <summary>The numbers for the preview's summary line (PRD 5.1 step 5).</summary>
    public sealed class StepPlanSummary
    {
        public int LotCount { get; internal set; }

        /// <summary>The smallest step height in metres, ignoring direction; 0 when there are no steps.</summary>
        public float LowestStep { get; internal set; }

        /// <summary>The tallest step height in metres, ignoring direction; 0 when there are no steps.</summary>
        public float TallestStep { get; internal set; }

        /// <summary>All rejected lots. Left-over ends are not lots, so they are not counted.</summary>
        public int EmptyCount { get; internal set; }
    }
}
