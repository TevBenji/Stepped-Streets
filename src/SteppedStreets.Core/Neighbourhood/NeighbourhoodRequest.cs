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

        /// <summary>Reads [{"zone":"..","share":n},..], the only shape the panel writes; entries it can't read are skipped.</summary>
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
            if (shares.Count == 0) shares.Add(new ZoneShare { Zone = "Residential Low", Share = 100f });
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
