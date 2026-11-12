// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>What one end of a planned street joins.</summary>
    internal struct Attachment
    {
        public static readonly Attachment None = new Attachment { Street = -1, Point = -1 };

        /// <summary>The street whose point this end shares, or −1.</summary>
        public int Street;

        public int Point;

        /// <summary>Set when the end joins an existing road instead.</summary>
        public bool Existing;

        public long ExistingId;
        public float ExistingT;

        public bool Joined => Street >= 0 || Existing;
    }

    /// <summary>A street while the layout is planned: an evenly sampled centreline with its height profile.</summary>
    internal sealed class Street
    {
        public RoadKind Kind;
        public List<float2> Points;
        public float[] Heights;

        /// <summary>A contour street that closes on itself; its last point is not repeated.</summary>
        public bool Closed;

        public Attachment Start = Attachment.None, End = Attachment.None;

        /// <summary>Points where another street joins this one.</summary>
        public readonly List<int> Junctions = new List<int>();

        /// <summary>Sharp corners the street turns at (a hexagon's zigzag): the road is cut there, so each piece runs straight.</summary>
        public readonly List<int> Corners = new List<int>();

        /// <summary>Broke a rule: gone for good.</summary>
        public bool Broken;

        /// <summary>Cut off from the kept network; worked out afresh by every prune.</summary>
        public bool Pruned;

        public bool Removed => Broken || Pruned;

        /// <summary>A street the layout's traffic runs along (a trunk, a through street, a connector): see <see cref="RoadNetwork.Assemble"/>.</summary>
        public bool Main;

        public Street(RoadKind kind, List<float2> points, float[] heights)
        {
            Kind = kind;
            Points = points;
            Heights = heights;
        }

        private float[] stations;

        /// <summary>Distance along the street at each point, worked out once: a street's points never change after it is made.</summary>
        public float[] Stations => stations ?? (stations = Geometry.Stations(Points));

        public float Length => Stations[Stations.Length - 1] + (Closed ? math.distance(Points[Points.Count - 1], Points[0]) : 0f);

        public float3 Point3(int index) => new float3(Points[index].x, Heights[index], Points[index].y);

        public float2 Tangent(int index)
        {
            if (!Closed) return Geometry.Tangent(Points, index);
            int count = Points.Count;
            float2 direction = Points[(index + 1) % count] - Points[(index - 1 + count) % count];
            return math.lengthsq(direction) > 1e-9f ? math.normalize(direction) : new float2(1f, 0f);
        }

        /// <summary>Distance along the street between two points (the shorter way round on a ring).</summary>
        public float Between(int first, int second)
        {
            float along = math.abs(Stations[second] - Stations[first]);
            return Closed ? math.min(along, Length - along) : along;
        }
    }
}
