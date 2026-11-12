// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

        // Grid layouts, for flat or gentle ground (appended, so saved settings keep their meaning).

        /// <summary>Straight streets at right angles, blocks as long as they are deep.</summary>
        SquareGrid,

        /// <summary>Straight streets at right angles, long blocks.</summary>
        RectangularGrid,

        /// <summary>A grid with varied street spacing and block lengths.</summary>
        IrregularGrid,

        /// <summary>Long blocks whose cross streets lean, so the blocks are parallelograms.</summary>
        SkewedGrid,

        /// <summary>Parallel streets with gaps: some cross streets left out and some streets broken, so T-junctions and L-turns.</summary>
        FragmentedGrid,

        /// <summary>A rectangular grid crossed by two diagonal avenues.</summary>
        DiagonalGrid,

        /// <summary>A grid whose streets curve in gentle waves, every junction still near square.</summary>
        CurvedGrid,

        /// <summary>Curving parallel streets with gaps: a curved grid broken like the fragmented one.</summary>
        WarpedGrid,

        /// <summary>A rectangular grid with four diagonal avenues radiating from a block at its centre.</summary>
        RadialGrid,

        /// <summary>Streets radiating from a small ring at the centre like the spokes of a wheel, twice as many past a boulevard ring.</summary>
        Star,

        /// <summary>Concentric ring streets joined by staggered radial streets.</summary>
        Rings,

        /// <summary>Concentric rings crossed by radial streets that run straight from the centre outwards.</summary>
        Spiderweb,

        /// <summary>Rings and radial streets spreading from a point on the area's edge, like an open fan.</summary>
        Fan,

        /// <summary>A trunk street from the city's road with straight side streets off both sides.</summary>
        Spine,

        /// <summary>Rails running in from the city's road, joined by rungs one block apart.</summary>
        Ladder,

        /// <summary>A trunk along one side of the area with side streets off one side only, like the teeth of a comb.</summary>
        Comb,

        /// <summary>A trunk with side streets slanting off it in pairs, like the bones of a fish.</summary>
        Fishbone,

        /// <summary>A trunk with limbs off it and twigs off the limbs: a branching hierarchy of streets.</summary>
        Tree,

        /// <summary>Trunks with short dead-end streets off both sides.</summary>
        CulDeSacs,

        /// <summary>Trunks with loop streets off both sides, each leaving and rejoining its trunk.</summary>
        Loops,

        /// <summary>Trunks with loops and lollipops in turn.</summary>
        LoopsAndLollipops,

        /// <summary>Trunks with lollipops off both sides: short sticks ending in small ring streets.</summary>
        Lollipops,

        /// <summary>Streets in three directions 60° apart, so every block is a triangle.</summary>
        TriangularMesh,

        /// <summary>A honeycomb of streets: hexagonal blocks, every junction a three-way one at 120°.</summary>
        Hexagonal,

        /// <summary>Larger hexagons, each split into three diamonds by streets from its centre.</summary>
        HexagonalSpokes,

        /// <summary>A main road along the crest of a ridge, with streets following the contours down both sides.</summary>
        Ridge,

        /// <summary>A main road along the valley floor, with streets following the contours up both sides.</summary>
        Valley,

        /// <summary>Streets running parallel to the shore, joined by streets down to the water.</summary>
        Coastal,

        /// <summary>One road winding back and forth across the area, with a cross street every few blocks.</summary>
        Winding,

        /// <summary>An irregular mesh: streets that wave their own ways, cross streets at varied angles, blocks all different.</summary>
        OrganicMesh,

        /// <summary>A grid in one part of the area and loops and cul-de-sacs in the other.</summary>
        Hybrid,

        /// <summary>Several small centres, each its own neighbourhood of rings and radial streets.</summary>
        Polycentric,

        /// <summary>Big blocks ringed by through streets, with only dead-end streets inside them.</summary>
        Superblock,

        /// <summary>Superblocks whose short cul-de-sacs leave a green space in the middle of each block.</summary>
        Radburn,

        /// <summary>A grid of through streets with loops and crescents inside each block.</summary>
        FusedGrid,

        /// <summary>A grid whose streets are closed in the middle of every other block, so they carry no through traffic.</summary>
        FilteredGrid,

        /// <summary>Superblocks of three by three blocks whose inner streets turn back to the edge, round a central square.</summary>
        BarcelonaSuperblock,
    }

    public static class NeighbourhoodStyles
    {
        /// <summary>The hillside layouts, which follow the contours and so need a slope.</summary>
        public static bool FollowsContours(this NeighbourhoodStyle style) => style == NeighbourhoodStyle.ContourSuburb
            || style == NeighbourhoodStyle.TerracedBlocks || style == NeighbourhoodStyle.MountainLane
            || style == NeighbourhoodStyle.Ridge || style == NeighbourhoodStyle.Valley;

        /// <summary>The layouts whose cross streets can meet as crossroads, staggered or traffic-optimised.</summary>
        public static bool UsesJunctions(this NeighbourhoodStyle style) => Layouts.GridLayout.Handles(style) && !Layouts.GridLayout.FixedJunctions(style);
    }

    /// <summary>How the cross streets of a grid meet its main streets.</summary>
    public enum JunctionPattern
    {
        /// <summary>Cross streets line up: four-way junctions.</summary>
        Crossroads,

        /// <summary>Cross streets offset like brickwork: every junction a T.</summary>
        Staggered,

        /// <summary>Staggered T-junctions for the local streets, and one through road that is the only road meeting the city's.</summary>
        TrafficOptimised,
    }

    public enum MixPattern { Random, Clumps, Even }

    public sealed class ZoneShare
    {
        public string Zone = "";

        /// <summary>Any positive weight; the planner normalises the shares.</summary>
        public float Share = 1f;

        // One {"zone":"..","share":n} object at a time; a regex always moves forward, so a typo can't loop.
        private static readonly Regex ZoneObject = new Regex(@"\{[^{}]*\}", RegexOptions.CultureInvariant);
        private static readonly Regex ZoneField = new Regex(@"""zone""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);
        private static readonly Regex ShareField = new Regex(@"""share""\s*:\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)", RegexOptions.CultureInvariant);
        private static readonly Regex Escape = new Regex(@"\\(.)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads [{"zone":"..","share":n},..], the only shape the panel writes; entries it can't read are skipped. An empty list
        /// ("[]") means no zoning; anything else that yields no zone (a damaged setting) falls back to Residential Low.
        /// </summary>
        public static List<ZoneShare> ParseList(string json)
        {
            var shares = new List<ZoneShare>();
            foreach (Match entry in ZoneObject.Matches(json ?? ""))
            {
                var zone = ZoneField.Match(entry.Value);
                if (!zone.Success) continue;
                string name = Escape.Replace(zone.Groups[1].Value, "$1");
                float share = 1f;
                var shareMatch = ShareField.Match(entry.Value);
                if (shareMatch.Success && !float.TryParse(shareMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out share)) continue;
                if (name.Length > 0 && share > 0f && !float.IsInfinity(share)) shares.Add(new ZoneShare { Zone = name, Share = share });
            }
            if (shares.Count == 0 && (json ?? "").Trim() != "[]") shares.Add(new ZoneShare { Zone = "Residential Low", Share = 100f });
            return shares;
        }

        public static string WriteList(IEnumerable<ZoneShare> shares)
        {
            var json = new StringBuilder("[");
            foreach (var share in shares)
            {
                if (json.Length > 1) json.Append(',');
                json.Append("{\"zone\":\"").Append((share.Zone ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\",\"share\":").Append(share.Share.ToString("0.##", CultureInfo.InvariantCulture)).Append('}');
            }
            return json.Append(']').ToString();
        }
    }

    /// <summary>A service building the player mixed in (a police station, a clinic, a park...), and how many.</summary>
    public sealed class ServiceRequest
    {
        /// <summary>The game's prefab name; the planner only echoes it.</summary>
        public string Name = "";

        /// <summary>The building's lot in cells, along the kerb and back from it.</summary>
        public int WidthCells = 4;
        public int DepthCells = 4;

        public int Count = 1;

        private static readonly Regex Entry = new Regex(@"\{[^{}]*\}", RegexOptions.CultureInvariant);
        private static readonly Regex NameField = new Regex(@"""name""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);
        private static readonly Regex CountField = new Regex(@"""count""\s*:\s*([0-9]+)", RegexOptions.CultureInvariant);
        private static readonly Regex Escape = new Regex(@"\\(.)", RegexOptions.CultureInvariant);

        /// <summary>Reads [{"name":"..","count":n},..] as the panel writes it (sizes come from the game); unreadable entries are skipped.</summary>
        public static List<ServiceRequest> ParseList(string json)
        {
            var services = new List<ServiceRequest>();
            foreach (Match entry in Entry.Matches(json ?? ""))
            {
                var name = NameField.Match(entry.Value);
                if (!name.Success) continue;
                var count = CountField.Match(entry.Value);
                int value = count.Success && int.TryParse(count.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
                string text = Escape.Replace(name.Groups[1].Value, "$1");
                if (text.Length > 0 && value > 0 && !services.Exists(service => service.Name == text))
                    services.Add(new ServiceRequest { Name = text, Count = Math.Min(value, MostEach) });
            }
            return services;
        }

        public static string WriteList(IEnumerable<ServiceRequest> services)
        {
            var json = new StringBuilder("[");
            foreach (var service in services)
            {
                if (json.Length > 1) json.Append(',');
                json.Append("{\"name\":\"").Append((service.Name ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\",\"count\":").Append(service.Count.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            return json.Append(']').ToString();
        }

        /// <summary>The most of one building a plan places.</summary>
        public const int MostEach = 20;
    }

    /// <summary>One existing road edge near the area, to connect to.</summary>
    public sealed class ExistingRoad
    {
        /// <summary>The mod's handle for the game edge; the planner only echoes it.</summary>
        public long Id;

        /// <summary>The edge's curve sampled at even curve parameter t = i / (n − 1), at the road surface height.</summary>
        public float3[] Points = new float3[0];

        public float HalfWidth = 6f;

        /// <summary>
        /// How far along the edge from either end a new road may join it. An edge ends at a node of the player's network,
        /// often a junction; a new junction closer than this crowds it and the game's shapes for the two overlap (owner's
        /// in-game test of 2.1.1, 3 November 2026).
        /// </summary>
        public const float EndClearance = 20f;

        /// <summary>Whether a point a share of the way along one of the edge's segments is clear of both its ends.</summary>
        public bool ClearOfEnds(int segment, float share)
        {
            float along = 0f, total = 0f;
            for (int index = 1; index < Points.Length; index++)
            {
                float length = math.distance(Points[index - 1].xz, Points[index].xz);
                if (index - 1 < segment) along += length;
                else if (index - 1 == segment) along += length * share;
                total += length;
            }
            return along >= EndClearance && total - along >= EndClearance;
        }
    }

    /// <summary>One free cell of one of the game's zone blocks near the area, for zoning streets that already exist.</summary>
    public struct ZoneCell
    {
        /// <summary>The block it belongs to: the same number for every cell of one block.</summary>
        public int Block;

        /// <summary>Column along the road, and row back from it (row 0 is against the road).</summary>
        public int Column, Row;

        public float2 Centre;

        /// <summary>The block's height, for drawing the lot: nothing is levelled on existing streets.</summary>
        public float Height;

        /// <summary>The block's direction in (x, z): a unit vector from the back of the block towards its road.</summary>
        public float2 Forward;
    }

    /// <summary>The drawn area, the site and the player's settings.</summary>
    public sealed class NeighbourhoodRequest
    {
        /// <summary>The same request, its arrays shared: for planning one part of the area with a layout of its own.</summary>
        public NeighbourhoodRequest Copy() => (NeighbourhoodRequest)MemberwiseClone();

        /// <summary>The drawn area in world (x, z), either winding.</summary>
        public float2[] Boundary = new float2[0];

        /// <summary>Raw ground height at world (x, z).</summary>
        public Func<float2, float> GroundHeight;

        /// <summary>True on buildings and water; existing roads come separately. Null means nothing is blocked.</summary>
        public Func<float2, bool> Blocked;

        /// <summary>True on water alone, for the coastal layout's shore. Null means no water.</summary>
        public Func<float2, bool> Water;

        /// <summary>Existing road edges within 400 m of the area (the connection's reach); may be empty.</summary>
        public ExistingRoad[] ExistingRoads = new ExistingRoad[0];

        public NeighbourhoodStyle Style = NeighbourhoodStyle.ContourSuburb;

        /// <summary>Zone the streets already in the area from <see cref="ZoneCells"/>: no new roads, no ground changes, no trees.</summary>
        public bool ExistingStreets;

        /// <summary>The free cells of the game's zone blocks near the area; only filled for <see cref="ExistingStreets"/>.</summary>
        public ZoneCell[] ZoneCells = new ZoneCell[0];

        /// <summary>Half the new road's width, centreline to kerb.</summary>
        public float RoadHalfWidth = 6f;

        /// <summary>Road spacing on the ground between neighbouring contour streets, in metres.</summary>
        public float BlockDepth = 90f;

        /// <summary>The steepest grade any new road may have (rise over run).</summary>
        public float MaxRoadGrade = 0.08f;

        /// <summary>Smoothed ground steeper than this gets no roads or lots and is planted as forest.</summary>
        public float SteepestBuildable = 0.15f;

        /// <summary>
        /// Steep hills and mountains: buildable ground up to 60 % instead of 40 %, links that may switch back in hairpins with
        /// a tighter radius, and banks that reach further to meet steeply rising ground. Off unless the player turns it on.
        /// </summary>
        public bool SteepGround;

        /// <summary>About how far apart links between two contour streets are, along them (hill towns keep connectors few).</summary>
        public float LinkSpacing = 300f;

        public ZoneShare[] Mix = { new ZoneShare { Zone = "Residential Low", Share = 1f } };

        /// <summary>Service buildings to place beside the new roads, each on a level pad of its own.</summary>
        public ServiceRequest[] Services = new ServiceRequest[0];
        public MixPattern Pattern = MixPattern.Clumps;

        public int LotWidthCells = 3;
        public int NarrowestWidthCells = 3;
        public int LotDepthCells = 4;

        /// <summary>Grade the lots into level pads with banks. Off: only the roads are graded and every lot cell is zoned.</summary>
        public bool LevelLots = true;

        /// <summary>Change the terrain at all. Off: no grading, roads follow the ground as the game builds them, lots aren't levelled.</summary>
        public bool ShapeGround = true;

        /// <summary>How a grid's cross streets meet its main streets.</summary>
        public JunctionPattern Junctions = JunctionPattern.Staggered;

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

        /// <summary>Half width of the road for main streets and connectors; 0 uses <see cref="RoadHalfWidth"/> for every street.</summary>
        public float MainRoadHalfWidth;

        /// <summary>Paths for people on foot through long blocks, from street to street.</summary>
        public bool Footpaths;

        /// <summary>Frontage too short for a lot becomes a small park instead of grass.</summary>
        public bool PocketParks;

        /// <summary>Existing streets: grade each lot into a level pad, as new streets do. Off unless asked: it changes the ground.</summary>
        public bool LevelExistingLots;

        /// <summary>The zone for corner lots; empty takes the mix.</summary>
        public string CornerZone = "";

        /// <summary>The zone for lots on a main street; empty takes the mix.</summary>
        public string MainStreetZone = "";
    }
}
