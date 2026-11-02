// Stepped Streets, the stepped-lot planner. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    /// <summary>Which side of the street gets lots, as seen travelling the centreline in the order it was given.</summary>
    public enum StreetSide
    {
        Left,
        Right,
        Both,
    }

    /// <summary>One street to plan, and the player's settings for it.</summary>
    public sealed class StepPlanRequest
    {
        /// <summary>The street's centreline as the game's net gives it (x, height, z), in either direction.</summary>
        public IReadOnlyList<float3> Centreline { get; set; }

        /// <summary>Metres from the centreline to the kerb, where the lots start.</summary>
        public float RoadHalfWidth { get; set; }

        public StreetSide Side { get; set; } = StreetSide.Both;

        /// <summary>Lot width in 8 m cells along the kerb.</summary>
        public int LotWidthCells { get; set; }

        /// <summary>The narrowest lot the zone permits; equal to <see cref="LotWidthCells"/> means no narrowing.</summary>
        public int NarrowestWidthCells { get; set; }

        /// <summary>Lot depth in 8 m cells away from the road.</summary>
        public int DepthCells { get; set; }

        /// <summary>Stretches flatter than this grade (rise over run) are left alone. PRD 5.4 default: 6 %.</summary>
        public float MinimumGrade { get; set; } = 0.06f;

        /// <summary>The tallest step, in metres, allowed between two neighbouring pads.</summary>
        public float HighestStep { get; set; }

        // WAVE 4 STUB: the lead takes W1's version of this file at merge.
        public float BankGrade { get; set; } = 0.4f;
        public StepLook Look { get; set; } = StepLook.PlantedBank;
        public float EarthworkLimit { get; set; } = 4f;
        public Func<float2, float> GroundHeight { get; set; }
        public Func<float2, bool> Blocked { get; set; }

        /// <summary>How far, in metres, the ground as built may sit off a pad's height and still count as level (R8).</summary>
        public float LevelTolerance { get; set; } = 0.3f;
    }
}
