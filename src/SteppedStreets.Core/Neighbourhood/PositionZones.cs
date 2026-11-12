// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using SteppedStreets.Core.Planning;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Zones by a lot's place on its street, over the zone the mix gave it: the corner zone for corner lots, then the main-street
    /// zone for lots on a main street. A corner lot has an end with no lot of its own street beside it, and there another road
    /// meets its kerb corner within reach, crossing its frontage at more than 30°: a cross street at a junction, a side street,
    /// or the city's road a connector joins. Both zones empty leave every lot as the mix zoned it.
    /// </summary>
    internal static class PositionZones
    {
        /// <summary>
        /// How far past another road's kerb a corner lot's kerb corner may be. LotLayout's junction clearance, laid on the 8 m cell
        /// grid, starts the first new lot about 18 m past the kerb of the street it meets; the lot round the corner behind it, and
        /// the next lot along, start at least two cells further. Zone cells on existing streets start within a cell of the kerb.
        /// </summary>
        internal const float CornerReach = 22f;

        // A road this close to the lot's frontage in direction (cos 30°) is its own street going on past a joint, not a second street.
        private const float SameDirection = 0.866f;

        // Lots whose kerb corners are this close stand side by side, so neither end there is the end of the run.
        private const float Touching = 1f;

        // Wider than any road's half width plus the corner reach, so the buckets hold every piece a corner can reach.
        private const float BucketSize = 32f;

        private struct Piece
        {
            public int Road;
            public float2 Start, End, Direction;
            public float HalfWidth;
        }

        /// <summary>New streets: a lot's own road is <see cref="PlannedLot.Road"/>, and it is on a main street when that road is <see cref="PlannedRoad.Main"/>.</summary>
        public static void ForNewStreets(List<PlannedLot> lots, NeighbourhoodPlan plan, NeighbourhoodRequest request)
        {
            if (Unset(request) || lots.Count == 0) return;
            var widths = new List<float>();
            var pieces = new SpatialBuckets<Piece>(BucketSize);
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                float half = plan.Roads[road].Main && request.MainRoadHalfWidth > 0f ? request.MainRoadHalfWidth : request.RoadHalfWidth;
                Add(pieces, road, plan.Roads[road].Points, half);
                widths.Add(half);
            }
            int first = plan.Roads.Count;
            var existing = request.ExistingRoads ?? new ExistingRoad[0];
            for (int index = 0; index < existing.Length; index++)
                if (existing[index]?.Points != null) Add(pieces, first + index, existing[index].Points, existing[index].HalfWidth);
            var own = new int[lots.Count];
            var main = new bool[lots.Count];
            for (int index = 0; index < lots.Count; index++)
            {
                int road = lots[index].Road;
                own[index] = road;
                main[index] = road >= 0 && road < plan.Roads.Count && plan.Roads[road].Main;
            }
            Assign(lots, pieces, own, main, request);
        }

        /// <summary>
        /// Existing streets: a lot's own road is the one along its kerb, and it is on a main street when that road is wider than
        /// the narrowest road the area's lots face.
        /// </summary>
        public static void ForExistingStreets(List<PlannedLot> lots, NeighbourhoodRequest request)
        {
            if (Unset(request) || lots.Count == 0) return;
            var roads = request.ExistingRoads ?? new ExistingRoad[0];
            var pieces = new SpatialBuckets<Piece>(BucketSize);
            for (int index = 0; index < roads.Length; index++)
                if (roads[index]?.Points != null) Add(pieces, index, roads[index].Points, roads[index].HalfWidth);
            var own = new int[lots.Count];
            float narrowest = float.MaxValue;
            for (int index = 0; index < lots.Count; index++)
            {
                own[index] = Facing(lots[index].Pad, pieces);
                if (own[index] >= 0) narrowest = math.min(narrowest, roads[own[index]].HalfWidth);
            }
            var main = new bool[lots.Count];
            // Half a metre: the same road type measured on two edges can differ by a rounding.
            for (int index = 0; index < lots.Count; index++) main[index] = own[index] >= 0 && roads[own[index]].HalfWidth > narrowest + 0.5f;
            Assign(lots, pieces, own, main, request);
        }

        private static bool Unset(NeighbourhoodRequest request) => string.IsNullOrEmpty(request.CornerZone) && string.IsNullOrEmpty(request.MainStreetZone);

        private static void Add(SpatialBuckets<Piece> pieces, int road, float3[] points, float halfWidth)
        {
            float pad = halfWidth + CornerReach;
            for (int point = 1; point < points.Length; point++)
            {
                float2 start = points[point - 1].xz, end = points[point].xz;
                if (math.distancesq(start, end) < 1e-6f) continue;
                pieces.Add(math.min(start, end) - pad, math.max(start, end) + pad, new Piece
                {
                    Road = road, Start = start, End = end, Direction = math.normalize(end - start), HalfWidth = halfWidth,
                });
            }
        }

        /// <summary>The road whose kerb the lot's front runs along: the nearest one in the frontage's direction; −1 when none is near.</summary>
        private static int Facing(Pad pad, SpatialBuckets<Piece> pieces)
        {
            float2 start = pad.Corners[0].xz, end = pad.Corners[1].xz, middle = (start + end) * 0.5f;
            float2 frontage = math.normalizesafe(end - start);
            int best = -1;
            float nearest = float.MaxValue;
            foreach (var piece in pieces.Near(middle))
            {
                if (math.abs(math.dot(piece.Direction, frontage)) < SameDirection) continue;
                float distance = Geometry.DistanceToSegment(middle, piece.Start, piece.End) - piece.HalfWidth;
                if (distance <= CornerReach && distance < nearest) { nearest = distance; best = piece.Road; }
            }
            return best;
        }

        private static void Assign(List<PlannedLot> lots, SpatialBuckets<Piece> pieces, int[] own, bool[] main, NeighbourhoodRequest request)
        {
            var kerbCorners = new SpatialBuckets<int>(BucketSize);
            for (int index = 0; index < lots.Count; index++)
                for (int corner = 0; corner < 2; corner++)
                {
                    float2 point = lots[index].Pad.Corners[corner].xz;
                    kerbCorners.Add(point - Touching, point + Touching, index);
                }
            bool hasCorner = !string.IsNullOrEmpty(request.CornerZone), hasMain = !string.IsNullOrEmpty(request.MainStreetZone);
            for (int index = 0; index < lots.Count; index++)
            {
                if (hasCorner && Corner(index, lots, kerbCorners, pieces, own[index])) lots[index].Zone = request.CornerZone;
                else if (hasMain && main[index]) lots[index].Zone = request.MainStreetZone;
            }
        }

        private static bool Corner(int index, List<PlannedLot> lots, SpatialBuckets<int> kerbCorners, SpatialBuckets<Piece> pieces, int own)
        {
            var corners = lots[index].Pad.Corners;
            float2 frontage = math.normalizesafe(corners[1].xz - corners[0].xz);
            for (int end = 0; end < 2; end++)
            {
                float2 point = corners[end].xz;
                bool beside = false;
                foreach (int other in kerbCorners.Near(point))
                {
                    if (other == index || lots[other].Road != lots[index].Road) continue;
                    var their = lots[other].Pad.Corners;
                    if (math.distance(their[0].xz, point) < Touching || math.distance(their[1].xz, point) < Touching) { beside = true; break; }
                }
                if (beside) continue;
                foreach (var piece in pieces.Near(point))
                {
                    if (piece.Road == own || math.abs(math.dot(piece.Direction, frontage)) >= SameDirection) continue;
                    if (Geometry.DistanceToSegment(point, piece.Start, piece.End) - piece.HalfWidth <= CornerReach) return true;
                }
            }
            return false;
        }
    }
}
