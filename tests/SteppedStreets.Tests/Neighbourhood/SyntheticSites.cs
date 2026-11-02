// Stepped Streets, synthetic hillsides for the generator's tests and gallery. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using SteppedStreets.Core.Neighbourhood;
using Unity.Mathematics;

namespace SteppedStreets.Tests.Neighbourhood
{
    public sealed class SyntheticSite
    {
        public string Name;
        public Func<float2, float> Ground;
        public Func<float2, bool> Blocked;
        public float2[] Boundary;
        public bool Road = true;
        public float RoadZ = -30f;

        public NeighbourhoodRequest Request(Action<NeighbourhoodRequest> adjust = null)
        {
            var request = new NeighbourhoodRequest
            {
                Boundary = Boundary,
                GroundHeight = Ground,
                Blocked = Blocked,
                ExistingRoads = Road ? new[] { ExistingRoadAlong(RoadZ) } : new ExistingRoad[0],
            };
            adjust?.Invoke(request);
            return request;
        }

        /// <summary>A straight existing road running along x below the area, sampled every 4 m like the mod's snapshot.</summary>
        private ExistingRoad ExistingRoadAlong(float z)
        {
            float2 minimum = float.MaxValue, maximum = float.MinValue;
            foreach (var corner in Boundary) { minimum = math.min(minimum, corner); maximum = math.max(maximum, corner); }
            var points = new List<float3>();
            for (float x = minimum.x - 100f; x <= maximum.x + 100f; x += 4f)
                points.Add(new float3(x, Ground(new float2(x, z)), z));
            return new ExistingRoad { Id = 1, Points = points.ToArray(), HalfWidth = 6f };
        }
    }

    public static class SyntheticSites
    {
        private static float2[] Square(float minimum, float maximum) => new[]
        {
            new float2(minimum, minimum), new float2(maximum, minimum), new float2(maximum, maximum), new float2(minimum, maximum),
        };

        public static SyntheticSite Slope(float percent, float size = 800f) => new SyntheticSite
        {
            Name = $"slope-{percent:0}",
            Ground = point => percent / 100f * point.y,
            Boundary = Square(0f, size),
        };

        public static SyntheticSite Cone() => new SyntheticSite
        {
            Name = "cone",
            Ground = point => 70f * math.exp(-math.distancesq(point, new float2(500f, 520f)) / (2f * 300f * 300f)),
            Boundary = Square(0f, 1000f),
            // A farm and a house on the hill: roads and lots keep off them.
            Blocked = point => (point.x > 300f && point.x < 340f && point.y > 600f && point.y < 650f)
                || (point.x > 700f && point.x < 720f && point.y > 300f && point.y < 330f),
        };

        public static SyntheticSite Ridge() => new SyntheticSite
        {
            Name = "ridge",
            Ground = point => 60f * math.exp(-(point.x - 500f) * (point.x - 500f) / (2f * 220f * 220f)) + 0.01f * point.y,
            Boundary = new[] { new float2(0f, 0f), new float2(1000f, 0f), new float2(1000f, 900f), new float2(0f, 900f) },
        };

        public static SyntheticSite Valley() => new SyntheticSite
        {
            Name = "valley",
            Ground = point => 50f * (1f - math.exp(-(point.x - 500f) * (point.x - 500f) / (2f * 250f * 250f))) + 0.02f * point.y,
            Boundary = new[] { new float2(0f, 0f), new float2(1000f, 0f), new float2(1000f, 900f), new float2(0f, 900f) },
            // A river along the valley floor.
            Blocked = point => math.abs(point.x - 500f) < 14f,
        };

        public static SyntheticSite Saddle() => new SyntheticSite
        {
            Name = "saddle",
            Ground = point => 40f + 0.00012f * ((point.x - 500f) * (point.x - 500f) - (point.y - 500f) * (point.y - 500f)),
            Boundary = Square(100f, 900f),
            RoadZ = 70f,
        };

        public static SyntheticSite NoisyHill() => new SyntheticSite
        {
            Name = "noisy-hill",
            Ground = point => 70f * math.exp(-math.distancesq(point, new float2(500f, 520f)) / (2f * 300f * 300f))
                // Bumps and swells like a real map's: short waves the survey smooths out, longer ones the streets must ride.
                + 1.2f * math.sin(point.x / 9f) * math.cos(point.y / 11f) + 0.9f * math.sin((point.x + point.y) / 21f)
                + 0.8f * math.cos(point.x / 31f - point.y / 47f),
            Boundary = new[]
            {
                new float2(40f, 20f), new float2(980f, 60f), new float2(960f, 980f), new float2(520f, 900f), new float2(30f, 960f),
            },
        };

        /// <summary>Every site of the done criteria, with the default settings.</summary>
        public static IEnumerable<SyntheticSite> All()
        {
            yield return Slope(8f);
            yield return Slope(12f);
            yield return Slope(18f);
            yield return Cone();
            yield return Ridge();
            yield return Valley();
            yield return Saddle();
            yield return NoisyHill();
        }

        public static SyntheticSite ByName(string name)
        {
            foreach (var site in All()) if (site.Name == name) return site;
            throw new ArgumentException(name);
        }
    }
}
