// Placemaker — polygon toolkit used by every layer.
//
// The public API of Poly / SurfaceAcceptance / EarClip is used by other workstreams: signatures and
// behaviour contracts are stable, new members are added freely. Conventions everywhere:
//   * rings are implicitly closed (last point != first), XZ plane as float2 (x right, z up),
//   * counter-clockwise = positive area = outer ring; clockwise = hole,
//   * nothing throws on bad geometry: degenerate input gives an empty result.
//
// Poly is split over several files: Poly.cs (basics, Clean, IsSimple), Poly.Ops.cs (booleans, offset,
// split, clip, holes), Poly.Shape.cs (simplify, smooth, resample, fill runs, pole of inaccessibility).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: extracted Contains/ContainsD from Core/Geometry/Poly.cs:174-204 and Bounds/DistanceToBoundary from :243-263; namespace changed; partial class removed; helper parameter and local names expanded.

using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Terrain
{
    public static class Poly
    {
        public static bool Contains(IReadOnlyList<float2> ring, float2 point) => ContainsD(ring, point);

        public static bool Contains(float2[] ring, float2 point) => ContainsD(ring, point);

        /// <summary>The same for an array (read directly, not through the list interface).</summary>
        internal static bool ContainsD(float2[] ring, double2 point)
        {
            bool inside = false;
            int number = ring.Length;
            for (int index = 0, previousIndex = number - 1; index < number; previousIndex = index++)
            {
                double2 start = ring[index], end = ring[previousIndex];
                if ((start.y > point.y) != (end.y > point.y) && point.x < (end.x - start.x) * (point.y - start.y) / (end.y - start.y) + start.x)
                    inside = !inside;
            }
            return inside;
        }

        internal static bool ContainsD(IReadOnlyList<float2> ring, double2 point)
        {
            if (ring is float2[] array) return ContainsD(array, point);
            bool inside = false;
            int number = ring.Count;
            for (int index = 0, previousIndex = number - 1; index < number; previousIndex = index++)
            {
                double2 start = ring[index], end = ring[previousIndex];
                if ((start.y > point.y) != (end.y > point.y) && point.x < (end.x - start.x) * (point.y - start.y) / (end.y - start.y) + start.x)
                    inside = !inside;
            }
            return inside;
        }

        public static void Bounds(IReadOnlyList<float2> ring, out float2 minimum, out float2 maximum)
        {
            if (ring is float2[] array) { Bounds(array, out minimum, out maximum); return; }
            minimum = new float2(float.MaxValue); maximum = new float2(float.MinValue);
            foreach (var point in ring) { minimum = math.min(minimum, point); maximum = math.max(maximum, point); }
        }

        /// <summary>The same for an array (no enumerator made).</summary>
        public static void Bounds(float2[] ring, out float2 minimum, out float2 maximum)
        {
            minimum = new float2(float.MaxValue); maximum = new float2(float.MinValue);
            for (int index = 0; index < ring.Length; index++) { minimum = math.min(minimum, ring[index]); maximum = math.max(maximum, ring[index]); }
        }

        public static float DistanceToBoundary(IReadOnlyList<float2> ring, float2 point)
        {
            float best = float.MaxValue;
            for (int index = 0; index < ring.Count; index++)
                best = math.min(best, math.sqrt(ReservedSpace.DistSqToSegment(point, ring[index], ring[(index + 1) % ring.Count])));
            return best;
        }
    }

    // Placemaker — engine-free contracts shared by every area generator.
    //
    // Everything in Core/ is pure .NET + Unity.Mathematics: no Game.*, Colossal.*, Unity.Entities.
    // That keeps generators runnable off the main thread and testable without the game.
    // The game layer (Game/) turns an AreaPlan into CreationDefinitions; generators never
    // touch ECS.
    // Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: extracted ReservedSpace.DistSqToSegment and PointInPolygon from Core/Contracts.cs:363-381 into a static helper; namespace changed; helper parameter and local names expanded.
    internal static class ReservedSpace
    {
        internal static float DistSqToSegment(float2 point, float2 start, float2 end)
        {
            var segment = end - start;
            float lengthSquared = math.lengthsq(segment);
            float fraction = lengthSquared > 1e-9f ? math.saturate(math.dot(point - start, segment) / lengthSquared) : 0f;
            return math.distancesq(point, start + segment * fraction);
        }

        internal static bool PointInPolygon(float2 point, float2[] polygon)
        {
            bool inside = false;
            for (int index = 0, previousIndex = polygon.Length - 1; index < polygon.Length; previousIndex = index++)
            {
                if (((polygon[index].y > point.y) != (polygon[previousIndex].y > point.y)) &&
                    (point.x < (polygon[previousIndex].x - polygon[index].x) * (point.y - polygon[index].y) / (polygon[previousIndex].y - polygon[index].y) + polygon[index].x))
                    inside = !inside;
            }
            return inside;
        }
    }

    // Placemaker — brush dab placement for terraforming (GPL-3.0).
    // Adapted from ParkManager's ParkLakePlanner.ExcavationDabs/WaterAnchor (tomkpunkt, GPL-3.0).
    // Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: extracted TerraformDabs.DistanceToBoundary from Core/Build/TerraformDabs.cs:131-137 only; namespace changed; helper parameter and local names expanded.
    public static class TerraformDabs
    {
        public static float DistanceToBoundary(float2[] ring, float2 point)
        {
            float best = float.MaxValue;
            for (int index = 0; index < ring.Length; index++)
                best = math.min(best, math.sqrt(ReservedSpace.DistSqToSegment(point, ring[index], ring[(index + 1) % ring.Length])));
            return best;
        }
    }
}
