// Stepped Streets, the hillside neighbourhood generator. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    public enum NeighbourhoodStyle
    {
        /// <summary>Contour streets with S-curve links, every lot on its own pad (the default).</summary>
        ContourSuburb,

        /// <summary>Straighter, level contour streets, so each block side is one terrace; links carry no lots.</summary>
        TerracedBlocks,

        /// <summary>One winding road that climbs the hill with hairpins, sparse lots, forest elsewhere.</summary>
        MountainLane,
    }

    public enum MixPattern { Random, Clumps, Even }

    public sealed class ZoneShare
    {
        public string Zone = "";

        /// <summary>Any positive weight; the planner normalises the shares.</summary>
        public float Share = 1f;
    }

    /// <summary>One existing road edge near the area, to connect to.</summary>
    public sealed class ExistingRoad
    {
        /// <summary>The mod's handle for the game edge; the planner only echoes it.</summary>
        public long Id;

        /// <summary>The edge's curve sampled at even curve parameter t = i / (n − 1), at the road surface height.</summary>
        public float3[] Points = new float3[0];

        public float HalfWidth = 6f;
    }

    /// <summary>The drawn area, the site and the player's settings.</summary>
    public sealed class NeighbourhoodRequest
    {
        /// <summary>The drawn area in world (x, z), either winding.</summary>
        public float2[] Boundary = new float2[0];

        /// <summary>Raw ground height at world (x, z).</summary>
        public Func<float2, float> GroundHeight;

        /// <summary>True on buildings and water; existing roads come separately. Null means nothing is blocked.</summary>
        public Func<float2, bool> Blocked;

        /// <summary>Existing road edges within 400 m of the area (the connection's reach); may be empty.</summary>
        public ExistingRoad[] ExistingRoads = new ExistingRoad[0];

        public NeighbourhoodStyle Style = NeighbourhoodStyle.ContourSuburb;

        /// <summary>Half the new road's width, centreline to kerb.</summary>
        public float RoadHalfWidth = 6f;

        /// <summary>Road spacing on the ground between neighbouring contour streets, in metres.</summary>
        public float BlockDepth = 90f;

        /// <summary>The steepest grade any new road may have (rise over run).</summary>
        public float MaxRoadGrade = 0.08f;

        /// <summary>Smoothed ground steeper than this gets no roads or lots and is planted as forest.</summary>
        public float SteepestBuildable = 0.15f;

        /// <summary>About how far apart links between two contour streets are, along them (hill towns keep connectors few).</summary>
        public float LinkSpacing = 300f;

        public ZoneShare[] Mix = { new ZoneShare { Zone = "Residential Low", Share = 1f } };
        public MixPattern Pattern = MixPattern.Clumps;

        public int LotWidthCells = 3;
        public int NarrowestWidthCells = 3;
        public int LotDepthCells = 3;

        /// <summary>Grade the lots into level pads with banks. Off: only the roads are graded and every lot cell is zoned.</summary>
        public bool LevelLots = true;

        /// <summary>The most cut or fill, in metres, under a lot or a road.</summary>
        public float EarthworkLimit = 4f;

        /// <summary>Rise over run of the steepest bank the grading may make.</summary>
        public float BankGrade = 0.4f;

        /// <summary>The tallest step between neighbouring pads along a climbing road.</summary>
        public float HighestStep = 3.2f;

        /// <summary>Empty cells between lots: −1 decides from the ground's slope (Auto), otherwise 0, 1 or 2.</summary>
        public int LotSpacingCells = -1;

        /// <summary>Garden walls along road shoulders where the cut is tall enough.</summary>
        public bool Walls;

        public bool Trees = true;

        /// <summary>0.1 to 1: how dense the planted trees are.</summary>
        public float TreeDensity = 0.5f;

        /// <summary>The same request and seed give the same plan; another seed gives another layout.</summary>
        public uint Seed = 1;
    }
}
