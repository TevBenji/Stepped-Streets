// Stepped Streets, the hillside neighbourhood generator. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using SteppedStreets.Core.Planning;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Core.Neighbourhood
{
    /// <summary>
    /// Lots along both sides of every new road, laid by the stepped-lot planner (R1–R10), kept off every other road, the
    /// junctions, buildings, water, steep ground and lots already placed; then gaps on steep ground (video tip: give space)
    /// and a zone for each lot from the player's mix.
    /// </summary>
    internal static class LotLayout
    {
        private const float Shoulder = 1.5f;

        /// <summary>How far a pad may sit above or below its street: uphill houses sit higher with a short driveway ramp.</summary>
        internal const float PadLift = 1.5f;

        /// <summary>The existing roads' pieces, each with how far a lot or building keeps from it (half its width and a shoulder).</summary>
        internal static SpatialBuckets<(float2 Start, float2 End, float Clearance)> ExistingRoads(NeighbourhoodRequest request)
        {
            var existing = new SpatialBuckets<(float2, float2, float)>(32f);
            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < (road?.Points?.Length ?? 0); point++)
                {
                    float2 start = road.Points[point - 1].xz, end = road.Points[point].xz;
                    float clearance = road.HalfWidth + Shoulder;
                    existing.Add(math.min(start, end) - clearance, math.max(start, end) + clearance, (start, end, clearance));
                }
            return existing;
        }

        public static List<PlannedLot> Plan(NeighbourhoodPlan plan, List<int> owners, SiteSurvey survey, NeighbourhoodRequest request,
            List<string> messages, CancellationToken cancellation)
        {
            float halfWidth = request.RoadHalfWidth;
            var segments = new SpatialBuckets<int3>(32f);
            float reach = halfWidth + Shoulder + 1f;
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                var points = plan.Roads[road].Points;
                for (int point = 1; point < points.Length; point++)
                    segments.Add(math.min(points[point - 1].xz, points[point].xz) - reach, math.max(points[point - 1].xz, points[point].xz) + reach,
                        new int3(road, point, owners[road]));
            }
            var existing = ExistingRoads(request);
            var keepAway = new SpatialBuckets<(float2 Centre, float Radius)>(32f);
            foreach (var node in plan.Nodes)
            {
                // Lots keep off junctions (the game's junction and its corner radii) and the turning end of a cul-de-sac.
                float radius = node.Degree >= 3 || node.ExistingRoadId != 0 ? halfWidth + 10f : node.DeadEnd ? halfWidth + 4f : 0f;
                if (radius > 0f) keepAway.Add(node.Position.xz - radius, node.Position.xz + radius, (node.Position.xz, radius));
            }
            var occupied = new SpatialBuckets<Pad>(32f);

            var lots = new List<PlannedLot>();
            var reasons = new Dictionary<EmptyLotReason, int>();
            for (int road = 0; road < plan.Roads.Count; road++)
            {
                cancellation.ThrowIfCancellationRequested();
                var planned = plan.Roads[road];
                bool wantsLots = request.Style != NeighbourhoodStyle.TerracedBlocks || planned.Kind == RoadKind.Contour;
                if (!wantsLots || planned.Length < 2 * StepPlanner.CellSize) continue;
                int street = owners[road];

                bool Blocked(float2 point)
                {
                    if (!Poly.Contains(survey.Boundary, point)) return true;
                    if (request.Blocked != null && request.Blocked(point)) return true;
                    if (survey.SlopeAt(point) > request.SteepestBuildable) return true;
                    foreach (var (start, end, clearance) in existing.Near(point))
                        if (Geometry.DistanceToSegment(point, start, end) < clearance) return true;
                    foreach (var item in segments.Near(point))
                    {
                        var points = plan.Roads[item.x].Points;
                        float distance = Geometry.DistanceToSegment(point, points[item.y - 1].xz, points[item.y].xz);
                        // Its own street only stops a lot that cuts into the carriageway on the inside of a bend.
                        if (distance < (item.z == street ? halfWidth - 2f : halfWidth + Shoulder)) return true;
                    }
                    foreach (var (centre, radius) in keepAway.Near(point))
                        if (math.distancesq(point, centre) < radius * radius) return true;
                    foreach (var pad in occupied.Near(point))
                        if (InsidePad(pad, point, 0.3f)) return true;
                    return false;
                }

                StepPlan stepPlan;
                try
                {
                    stepPlan = StepPlanner.Plan(new StepPlanRequest
                    {
                        Centreline = planned.Points,
                        RoadHalfWidth = halfWidth,
                        Side = StreetSide.Both,
                        LotWidthCells = request.LotWidthCells,
                        NarrowestWidthCells = math.clamp(request.NarrowestWidthCells, StepPlanner.MinimumWidthCells, request.LotWidthCells),
                        DepthCells = request.LotDepthCells,
                        // Every stretch gets lots: contour streets are nearly level by design, and they are still graded.
                        MinimumGrade = 0f,
                        HighestStep = request.HighestStep,
                        BankGrade = NeighbourhoodPlanner.EffectiveBankGrade(request),
                        // The pad may still move up to PadLift towards its ground below; the real check follows that.
                        EarthworkLimit = request.LevelLots ? request.EarthworkLimit + PadLift : 1000f,
                        GroundHeight = request.GroundHeight,
                        Blocked = Blocked,
                    });
                }
                catch (ArgumentException) { continue; }
                foreach (var empty in stepPlan.EmptyLots)
                    if (empty.Reason != EmptyLotReason.LeftOver)
                        reasons[empty.Reason] = (reasons.TryGetValue(empty.Reason, out int count) ? count : 0) + 1;

                bool dropNext = false;
                StreetSide side = StreetSide.Both;
                foreach (var pad in stepPlan.Pads)
                {
                    if (pad.Side != side) { side = pad.Side; dropNext = false; }
                    var kept = request.LevelLots ? Settle(pad, request) : pad;
                    if (kept == null) { reasons[EmptyLotReason.TooMuchEarthwork] = (reasons.TryGetValue(EmptyLotReason.TooMuchEarthwork, out int over) ? over : 0) + 1; continue; }
                    int gap = Gap(kept, survey, request, planned.Kind);
                    if (gap > 0)
                    {
                        if (kept.WidthCells - gap >= StepPlanner.MinimumWidthCells) kept = Narrowed(kept, kept.WidthCells - gap);
                        else
                        {
                            // Too narrow to give up a column: leave every second lot empty instead.
                            bool drop = dropNext;
                            dropNext = !dropNext;
                            if (drop) continue;
                        }
                    }
                    var corners = new float2[4];
                    for (int corner = 0; corner < 4; corner++) corners[corner] = kept.Corners[corner].xz;
                    Poly.Bounds(corners, out var low, out var high);
                    occupied.Add(low - 1f, high + 1f, kept);
                    lots.Add(new PlannedLot { Pad = kept, Road = road });
                }
            }
            foreach (var reason in new[] { EmptyLotReason.TooMuchEarthwork, EmptyLotReason.TooSteep })
                if (reasons.TryGetValue(reason, out int count) && count > 0)
                    messages.Add(string.Format(CultureInfo.InvariantCulture, "{0} lots left out: {1}.", count,
                        reason == EmptyLotReason.TooSteep ? "too steep a step for this lot width" : $"more than {request.EarthworkLimit:0.#} m of earthwork"));
            AssignZones(lots, request);
            return lots;
        }

        /// <summary>
        /// Moves a pad up to <see cref="PadLift"/> from its street's height towards its own ground, halving the cut behind an
        /// uphill lot and the fill under a downhill one; a lot still over the earthwork limit tries one cell shallower, then
        /// is left out (null).
        /// </summary>
        internal static Pad Settle(Pad pad, NeighbourhoodRequest request)
        {
            for (int depth = pad.DepthCells; depth >= StepPlanner.MinimumRows; depth--)
            {
                var candidate = depth == pad.DepthCells ? pad : Shallower(pad, depth);
                float sum = 0f, lowest = float.MaxValue, highest = float.MinValue;
                int count = 0;
                foreach (var point in Samples(candidate))
                {
                    float ground = request.GroundHeight(point);
                    sum += ground; count++;
                    lowest = math.min(lowest, ground);
                    highest = math.max(highest, ground);
                }
                float height = math.clamp(sum / count, pad.Height - PadLift, pad.Height + PadLift);
                // The middle of the range keeps the worst cut and the worst fill equal when the limit allows it.
                height = math.clamp((lowest + highest) * 0.5f, math.max(pad.Height - PadLift, height - 1f), math.min(pad.Height + PadLift, height + 1f));
                if (highest - height <= request.EarthworkLimit && height - lowest <= request.EarthworkLimit)
                    return WithHeight(candidate, height);
                if (depth == StepPlanner.MinimumRows) break;
            }
            return null;
        }

        private static IEnumerable<float2> Samples(Pad pad)
        {
            foreach (var corner in pad.Corners) yield return corner.xz;
            for (int row = 0; row < pad.DepthCells; row++)
                for (int column = 0; column < pad.WidthCells; column++) yield return pad.PointAt(column + 0.5f, row + 0.5f);
        }

        private static Pad Shallower(Pad pad, int depth)
        {
            float3 Corner(float column, float row) { var point = pad.PointAt(column, row); return new float3(point.x, pad.Height, point.y); }
            return new Pad
            {
                Side = pad.Side, Height = pad.Height, Start = pad.Start, End = pad.End, WidthCells = pad.WidthCells, DepthCells = depth,
                Corners = new[] { Corner(0, 0), Corner(pad.WidthCells, 0), Corner(pad.WidthCells, depth), Corner(0, depth) },
            };
        }

        private static Pad WithHeight(Pad pad, float height)
        {
            var corners = new float3[4];
            for (int corner = 0; corner < 4; corner++) corners[corner] = new float3(pad.Corners[corner].x, height, pad.Corners[corner].z);
            return new Pad
            {
                Side = pad.Side, Height = height, Start = pad.Start, End = pad.End, WidthCells = pad.WidthCells, DepthCells = pad.DepthCells,
                Corners = corners,
            };
        }

        /// <summary>Empty cells after a lot: Auto from the ground's slope, or the player's fixed choice.</summary>
        private static int Gap(Pad pad, SiteSurvey survey, NeighbourhoodRequest request, RoadKind kind)
        {
            int gap;
            if (request.LotSpacingCells >= 0) gap = request.LotSpacingCells;
            else if (request.Style == NeighbourhoodStyle.TerracedBlocks) gap = 0;
            else
            {
                float slope = survey.SlopeAt(pad.PointAt(pad.WidthCells * 0.5f, pad.DepthCells * 0.5f));
                gap = slope >= 0.75f * request.SteepestBuildable ? 2 : slope >= 0.5f * request.SteepestBuildable ? 1 : 0;
            }
            return kind == RoadKind.Lane ? math.max(gap, 2) : gap;
        }

        /// <summary>The same pad, keeping its first <paramref name="width"/> columns.</summary>
        internal static Pad Narrowed(Pad pad, int width)
        {
            float2 Point(float column, float row) => pad.PointAt(column, row);
            float3 Corner(float column, float row) { var point = Point(column, row); return new float3(point.x, pad.Height, point.y); }
            return new Pad
            {
                Side = pad.Side,
                Height = pad.Height,
                Start = pad.Start,
                End = pad.Start + (pad.End - pad.Start) * width / pad.WidthCells,
                WidthCells = width,
                DepthCells = pad.DepthCells,
                Corners = new[] { Corner(0, 0), Corner(width, 0), Corner(width, pad.DepthCells), Corner(0, pad.DepthCells) },
            };
        }

        internal static bool InsidePad(Pad pad, float2 point, float inset)
        {
            float2 origin = pad.Corners[0].xz;
            float2 along = pad.Corners[1].xz - origin, across = pad.Corners[3].xz - origin;
            float alongLength = math.length(along), acrossLength = math.length(across);
            if (alongLength < 1e-3f || acrossLength < 1e-3f) return false;
            float u = math.dot(point - origin, along / alongLength), v = math.dot(point - origin, across / acrossLength);
            return u > inset && u < alongLength - inset && v > inset && v < acrossLength - inset;
        }

        /// <summary>
        /// One zone per lot from the mix: Random (seeded per lot), Clumps (runs of 3–5 lots) or Even (round robin). With no zones
        /// in the mix the lots are still levelled but left unzoned (zone ""), for the player to zone by hand.
        /// </summary>
        internal static void AssignZones(List<PlannedLot> lots, NeighbourhoodRequest request)
        {
            var mix = new List<ZoneShare>();
            foreach (var share in request.Mix ?? new ZoneShare[0])
                if (share != null && !string.IsNullOrWhiteSpace(share.Zone) && share.Share > 0f && math.isfinite(share.Share)) mix.Add(share);
            if (mix.Count == 0)
            {
                foreach (var lot in lots) lot.Zone = "";
                return;
            }
            float total = 0f;
            foreach (var share in mix) total += share.Share;

            string Weighted(float roll)
            {
                float running = 0f;
                foreach (var share in mix)
                {
                    running += share.Share / total;
                    if (roll < running) return share.Zone;
                }
                return mix[mix.Count - 1].Zone;
            }

            var assigned = new int[mix.Count];
            string clumpZone = null;
            int clumpLeft = 0, clumpRoad = -1;
            StreetSide clumpSide = StreetSide.Both;
            for (int index = 0; index < lots.Count; index++)
            {
                var lot = lots[index];
                switch (request.Pattern)
                {
                    case MixPattern.Random:
                        lot.Zone = Weighted(Geometry.Hash01(request.Seed, index, 7));
                        break;
                    case MixPattern.Even:
                    {
                        // The zone furthest behind its share of the lots so far.
                        int best = 0;
                        float bestDeficit = float.MinValue;
                        for (int zone = 0; zone < mix.Count; zone++)
                        {
                            float deficit = mix[zone].Share / total * (index + 1) - assigned[zone];
                            if (deficit > bestDeficit + 1e-6f) { bestDeficit = deficit; best = zone; }
                        }
                        assigned[best]++;
                        lot.Zone = mix[best].Zone;
                        break;
                    }
                    default:
                        if (clumpLeft <= 0 || lot.Road != clumpRoad || lot.Pad.Side != clumpSide)
                        {
                            clumpZone = Weighted(Geometry.Hash01(request.Seed, index, 11));
                            clumpLeft = 3 + (int)(Geometry.Hash01(request.Seed, index, 13) * 3f);
                            clumpRoad = lot.Road;
                            clumpSide = lot.Pad.Side;
                        }
                        lot.Zone = clumpZone;
                        clumpLeft--;
                        break;
                }
            }
        }
    }
}
