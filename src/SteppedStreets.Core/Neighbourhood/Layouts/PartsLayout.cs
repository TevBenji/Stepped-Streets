// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using System.Threading;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood.Layouts
{
    /// <summary>
    /// The layouts made of parts, each planned with a layout of its own and joined up afterwards: a hybrid (a grid on the side
    /// nearest the city's road, loops beyond) and a polycentric one (several centres, each its own ring neighbourhood).
    /// </summary>
    internal static class PartsLayout
    {
        public static bool Handles(NeighbourhoodStyle style) => style == NeighbourhoodStyle.Hybrid || style == NeighbourhoodStyle.Polycentric;

        /// <summary>The streets of every part; <paramref name="joined"/> says whether any meets the city's road straight on.</summary>
        public static List<Street> Plan(SiteSurvey survey, NeighbourhoodRequest request, out bool joined, CancellationToken cancellation)
        {
            var streets = new List<Street>();
            joined = false;
            var parts = request.Style == NeighbourhoodStyle.Hybrid ? Halves(survey, request) : Centres(survey, request);
            foreach (var (boundary, style) in parts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (boundary.Length < 3 || math.abs(Area(boundary)) < 40000f) continue;
                var part = request.Copy();
                part.Boundary = boundary;
                part.Style = style;
                // The panel offers no junctions choice for these layouts, so a part's grid uses the default, whatever was last set.
                part.Junctions = JunctionPattern.Staggered;
                var partSurvey = new SiteSurvey(part, cancellation);
                bool partJoined;
                var planned = RadialLayout.Handles(style) ? RadialLayout.Plan(partSurvey, part, out partJoined, cancellation)
                    : BranchLayout.Handles(style) ? BranchLayout.Plan(partSurvey, part, out partJoined, cancellation)
                    : GridLayout.Plan(partSurvey, part, out _, out partJoined, cancellation);
                joined |= partJoined;
                // Each part numbers its streets from nought: its joins move up by the streets already taken.
                int first = streets.Count;
                foreach (var street in planned)
                {
                    if (street.Start.Street >= 0) street.Start = Shift(street.Start, first);
                    if (street.End.Street >= 0) street.End = Shift(street.End, first);
                    streets.Add(street);
                }
            }
            return streets;
        }

        private static Attachment Shift(Attachment attachment, int by)
        {
            attachment.Street += by;
            return attachment;
        }

        /// <summary>
        /// The hybrid's two halves, split square to the area's longest way: a rectangular grid on the side nearest the city's
        /// road, loops and lollipops on the far side.
        /// </summary>
        private static List<(float2[] Boundary, NeighbourhoodStyle Style)> Halves(SiteSurvey survey, NeighbourhoodRequest request)
        {
            var boundary = survey.Boundary;
            float2 middle = (survey.BoundaryMinimum + survey.BoundaryMaximum) * 0.5f;
            float2 size = survey.BoundaryMaximum - survey.BoundaryMinimum;
            float2 across = size.x >= size.y ? new float2(1f, 0f) : new float2(0f, 1f);
            // The near side: the half the city's nearest road lies towards.
            float best = float.MaxValue, toward = 1f;
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                foreach (var point in road?.Points ?? new float3[0])
                {
                    float distance = Poly.Contains(boundary, point.xz) ? 0f : Poly.DistanceToBoundary(boundary, point.xz);
                    if (distance < best) { best = distance; toward = math.dot(point.xz - middle, across) >= 0f ? 1f : -1f; }
                }
            var near = Clip(boundary, middle, across * toward);
            var far = Clip(boundary, middle, -across * toward);
            return new List<(float2[], NeighbourhoodStyle)> { (near, NeighbourhoodStyle.RectangularGrid), (far, NeighbourhoodStyle.LoopsAndLollipops) };
        }

        /// <summary>
        /// The polycentric parts: two to four centres, about one per 30 hectares, spread over the area by a few rounds of
        /// k-means; each part is the ground nearer its centre than any other, with a ring layout of its own.
        /// </summary>
        private static List<(float2[] Boundary, NeighbourhoodStyle Style)> Centres(SiteSurvey survey, NeighbourhoodRequest request)
        {
            var boundary = survey.Boundary;
            var points = new List<float2>();
            for (float x = survey.BoundaryMinimum.x; x <= survey.BoundaryMaximum.x; x += 24f)
                for (float y = survey.BoundaryMinimum.y; y <= survey.BoundaryMaximum.y; y += 24f)
                    if (Poly.Contains(boundary, new float2(x, y))) points.Add(new float2(x, y));
            int count = math.clamp((int)math.round(math.abs(Area(boundary)) / 300000f), 2, 4);
            // Start spread along the area's longest way, then settle.
            float2 size = survey.BoundaryMaximum - survey.BoundaryMinimum;
            var centres = new float2[count];
            for (int centre = 0; centre < count; centre++)
            {
                float share = (centre + 0.5f) / count;
                centres[centre] = size.x >= size.y
                    ? new float2(survey.BoundaryMinimum.x + share * size.x, (survey.BoundaryMinimum.y + survey.BoundaryMaximum.y) * 0.5f)
                    : new float2((survey.BoundaryMinimum.x + survey.BoundaryMaximum.x) * 0.5f, survey.BoundaryMinimum.y + share * size.y);
            }
            for (int round = 0; round < 8 && points.Count > 0; round++)
            {
                var sums = new float2[count];
                var counts = new int[count];
                foreach (var point in points)
                {
                    int nearest = 0;
                    for (int centre = 1; centre < count; centre++)
                        if (math.distancesq(point, centres[centre]) < math.distancesq(point, centres[nearest])) nearest = centre;
                    sums[nearest] += point;
                    counts[nearest]++;
                }
                for (int centre = 0; centre < count; centre++) if (counts[centre] > 0) centres[centre] = sums[centre] / counts[centre];
            }
            var styles = new[] { NeighbourhoodStyle.Spiderweb, NeighbourhoodStyle.Rings, NeighbourhoodStyle.Star, NeighbourhoodStyle.Spiderweb };
            var parts = new List<(float2[], NeighbourhoodStyle)>();
            for (int centre = 0; centre < count; centre++)
            {
                var cell = boundary;
                for (int other = 0; other < count; other++)
                {
                    if (other == centre) continue;
                    // Keep the side of the line halfway between the two centres that this centre is on.
                    cell = Clip(cell, (centres[centre] + centres[other]) * 0.5f, math.normalizesafe(centres[other] - centres[centre]) * -1f);
                }
                parts.Add((cell, styles[(centre + (int)(request.Seed % 4)) % styles.Length]));
            }
            return parts;
        }

        /// <summary>The part of a polygon on the side of a line (through <paramref name="point"/>) that <paramref name="keep"/> points to.</summary>
        internal static float2[] Clip(float2[] polygon, float2 point, float2 keep)
        {
            var result = new List<float2>();
            for (int index = 0; index < polygon.Length; index++)
            {
                float2 a = polygon[index], b = polygon[(index + 1) % polygon.Length];
                float sa = math.dot(a - point, keep), sb = math.dot(b - point, keep);
                if (sa >= 0f) result.Add(a);
                if ((sa >= 0f) != (sb >= 0f)) result.Add(math.lerp(a, b, sa / (sa - sb)));
            }
            return result.ToArray();
        }

        private static float Area(float2[] polygon)
        {
            float area = 0f;
            for (int index = 0; index < polygon.Length; index++)
            {
                float2 a = polygon[index], b = polygon[(index + 1) % polygon.Length];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }
    }
}
