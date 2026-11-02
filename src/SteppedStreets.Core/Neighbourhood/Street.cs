// Stepped Streets, the hillside neighbourhood generator. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
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

        public bool Removed;

        public Street(RoadKind kind, List<float2> points, float[] heights)
        {
            Kind = kind;
            Points = points;
            Heights = heights;
        }

        public float Length => Geometry.Length(Points) + (Closed ? math.distance(Points[Points.Count - 1], Points[0]) : 0f);

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
            var stations = Geometry.Stations(Points);
            float along = math.abs(stations[second] - stations[first]);
            return Closed ? math.min(along, Length - along) : along;
        }
    }
}
