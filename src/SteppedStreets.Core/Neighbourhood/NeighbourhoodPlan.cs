// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Contour: a street along the hill, held near level. Street: a layout's main street on flat or gentle ground, which may
    /// climb up to the max road grade. Both carry the junctions that links and connections make. Link: joins two streets.
    /// Connection: joins the network to an existing road. Lane: the Mountain lane.
    /// </summary>
    public enum RoadKind { Contour, Link, Connection, Lane, Street }

    public static class RoadKinds
    {
        /// <summary>A street others join at its points: a contour street or a layout's main street.</summary>
        public static bool IsMain(this RoadKind kind) => kind == RoadKind.Contour || kind == RoadKind.Street;
    }

    public sealed class PlannedNode
    {
        /// <summary>World position at the road surface height.</summary>
        public float3 Position;

        /// <summary>0 for a new node; otherwise the existing edge this node joins (see <see cref="ExistingRoad.Id"/>).</summary>
        public long ExistingRoadId;

        /// <summary>Where on that edge, as its curve parameter (0 and 1 are its end nodes).</summary>
        public float ExistingRoadT;

        /// <summary>Only one new road ends here and it joins nothing: a cul-de-sac.</summary>
        public bool DeadEnd;

        /// <summary>How many new roads end here.</summary>
        public int Degree;

        /// <summary>Half the width of the widest new road ending here, centreline to kerb.</summary>
        public float HalfWidth;
    }

    public sealed class PlannedRoad
    {
        public RoadKind Kind;

        /// <summary>Centreline at the planned surface height, about every 4 m, from StartNode to EndNode.</summary>
        public float3[] Points = new float3[0];

        public int StartNode;
        public int EndNode;

        public float Length;

        /// <summary>The steepest grade along the road, rise over run.</summary>
        public float SteepestGrade;

        /// <summary>A main street or connector: built with the main road type, and its lots can take the main-street zone.</summary>
        public bool Main;

        /// <summary>Half the road's width, centreline to kerb: the main road's for a main street when one is chosen.</summary>
        public float HalfWidth;
    }

    /// <summary>
    /// A footpath through a long block, straight from the kerb of one street to the kerb of the street behind or opposite it,
    /// its ends just short of each kerb so the game joins them to the sidewalks.
    /// </summary>
    public sealed class PlannedPath
    {
        /// <summary>The ends, at the planned height of the street each one meets; the path climbs evenly between them.</summary>
        public float3 Start, End;

        /// <summary>Indexes into <see cref="NeighbourhoodPlan.Roads"/> of the streets the path joins.</summary>
        public int StartRoad, EndRoad;
    }

    public sealed class PlannedLot
    {
        /// <summary>The level pad: corners, height, width and depth in cells.</summary>
        public Pad Pad;

        /// <summary>Index into <see cref="NeighbourhoodPlan.Roads"/> of the road the lot fronts.</summary>
        public int Road;

        public string Zone = "";

        /// <summary>The cells to zone: every cell of a kept lot, front row included.</summary>
        public List<int2> LevelCells = new List<int2>();
    }

    /// <summary>A service building on its level pad, facing its road.</summary>
    public sealed class PlannedBuilding
    {
        public string Name = "";
        public Pad Pad;

        /// <summary>Index into <see cref="NeighbourhoodPlan.Roads"/> of the road the building faces.</summary>
        public int Road;

        /// <summary>The middle of the lot, at the pad height.</summary>
        public float3 Position;

        /// <summary>Unit direction from the back of the lot to its front (the road): the building's forward.</summary>
        public float2 Forward;
    }

    /// <summary>What to plant: trees, then the low greenery and the rocks set among them.</summary>
    public enum TreeKind { Conifer, Broadleaf, Bush, Flower, Rock, Boulder }

    public struct PlannedTree
    {
        public float3 Position;
        public TreeKind Kind;

        /// <summary>A stable number for the build's variation and rotation.</summary>
        public uint Variation;
    }

    public sealed class NeighbourhoodPlan
    {
        public List<PlannedNode> Nodes = new List<PlannedNode>();
        public List<PlannedRoad> Roads = new List<PlannedRoad>();
        public List<PlannedLot> Lots = new List<PlannedLot>();
        public List<PlannedBuilding> Buildings = new List<PlannedBuilding>();
        public List<PlannedTree> Trees = new List<PlannedTree>();
        public List<WallSegment> Walls = new List<WallSegment>();
        public List<PlannedPath> Paths = new List<PlannedPath>();

        /// <summary>The planned ground; null when nothing changes.</summary>
        public GroundField Ground;

        /// <summary>At least one new road reaches an existing road.</summary>
        public bool Connected;

        /// <summary>How many streets the layout planned, and how many of them the road rules then dropped (for the tests).</summary>
        internal int StreetsPlanned, StreetsDropped;

        /// <summary>
        /// At least one road (the area need not touch a road, so a standalone network is buildable too), or, on existing
        /// streets, at least one cell to zone or one building to place.
        /// </summary>
        public bool Buildable => Roads.Count > 0 || ZonedCells > 0 || Buildings.Count > 0;

        public float RoadLength;
        public float SteepestRoadGrade;
        public int ZonedCells;
        public float CutVolume;
        public float FillVolume;
        public float MostEarthwork;

        /// <summary>The share of the area left wild because it is too steep, 0 to 1.</summary>
        public float WildShare;

        /// <summary>Area of the drawn outline in square metres.</summary>
        public float AreaSquareMetres;

        /// <summary>Lots per zone, in the mix's order.</summary>
        public List<KeyValuePair<string, int>> ZoneCounts = new List<KeyValuePair<string, int>>();

        public string SummaryLine = "";
        public List<string> Messages = new List<string>();
        public double Milliseconds;

        /// <summary>Milliseconds per planning stage, for the log: where the time goes on the player's machine.</summary>
        public string Timings = "";
    }
}
