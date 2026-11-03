// Placemaker — emitting CreationDefinitions for each plan element (GPL-3.0).
// Net courses follow ParkManager's CreateNetCourseDefinition and GridRoadGenerator's
// CreateGridDefinitions/MakeCoursePos (attach to an existing edge via m_Entity + m_SplitPosition,
// Upgraded composition flags on the definition). Object definitions follow ParkingLotTool's
// "field by field like ObjectToolBaseSystem" ObjectDefinition. Credits: tomkpunkt, Ti4goc,
// KruemelGames (GPL-3.0).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: road courses only, from the neighbourhood plan's roads cut into short cubic pieces at their planned heights; MakeCoursePos reduced to attaching a road end to an existing edge or node; the standalone build-tool cadence, a temporaries count before Apply and a count of the built edges afterwards; no fitting, elevation, fences, objects, surfaces or audit machinery.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace SteppedStreets.Build
{
    public sealed class NetBuildReport
    {
        public int Pieces;
        public int Built;
        public int Refused;
        public string Summary = "";
    }

    public sealed partial class NetBuildSystem : GameSystemBase
    {
        private const float AttachReach = 6f;
        private const int SettleFrames = 3;
        private const int MostWaitFrames = 90;
        private const int VerifyFrames = 30;

        private readonly List<Entity> _definitions = new List<Entity>();
        private readonly List<float2> _pieceMiddles = new List<float2>();
        private BuildToolSystem _tool;
        private TerrainSystem _terrain;
        private EntityQuery _temporaryEdges;
        private EntityQuery _refused;
        private EntityQuery _edges;
        private NeighbourhoodPlan _plan;
        private Entity _prefab;
        private string _roadName;
        private Action<NetBuildReport> _whenDone;
        private int _activationFrame, _emitFrame = -1, _applyFrame = -1, _lastTemporaryCount = -1, _stableSince;
        private int _refusedCount;

        public bool Busy { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _temporaryEdges = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>());
            _refused = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Error>());
            _edges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Road>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        public void Begin(NeighbourhoodPlan plan, string roadName, Action<NetBuildReport> whenDone)
        {
            if (Busy) { Mod.Logger.Warn("Stepped Streets roads: Begin ignored while a build is busy."); return; }
            Busy = true;
            _plan = plan;
            _whenDone = whenDone;
            _emitFrame = _applyFrame = _lastTemporaryCount = -1;
            _refusedCount = 0;
            _pieceMiddles.Clear();
            try
            {
                if (plan == null || plan.Roads.Count == 0) { Finish("no roads planned"); return; }
                var road = RoadCatalog.Find(EntityManager, World.GetOrCreateSystemManaged<PrefabSystem>(), roadName);
                if (road == null) { Finish("no road prefab is available"); return; }
                _prefab = road.Prefab;
                _roadName = road.Name;
                _tool.Activate();
                _activationFrame = UnityEngine.Time.frameCount;
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets roads: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!Busy) return;
            try
            {
                if (!_tool.IsActive && _applyFrame < 0) { _tool.Interrupted = true; Finish("interrupted by a tool change before the roads were applied"); return; }
                int frame = UnityEngine.Time.frameCount;
                if (_emitFrame < 0)
                {
                    if (_tool.Ready) Emit(frame);
                    else if (frame - _activationFrame >= 8) Finish("build tool did not start within eight frames");
                    return;
                }
                if (_applyFrame < 0)
                {
                    // Wait until the game has turned every definition into temporaries (the count stops changing), then Apply.
                    int count = _temporaryEdges.CalculateEntityCount();
                    if (count != _lastTemporaryCount) { _lastTemporaryCount = count; _stableSince = frame; }
                    bool settled = frame - _emitFrame >= 2 && count > 0 && frame - _stableSince >= SettleFrames;
                    if (!settled && frame - _emitFrame < MostWaitFrames) return;
                    _refusedCount = _refused.CalculateEntityCount();
                    if (_refusedCount > 0) Mod.Logger.Warn($"Stepped Streets roads: the game marked {_refusedCount} temporary pieces with an error before Apply.");
                    Mod.Logger.Info($"Stepped Streets roads: {count} temporary edges after {frame - _emitFrame} frames; Apply.");
                    _tool.Requested = ApplyMode.Apply;
                    _applyFrame = frame;
                    return;
                }
                if (frame - _applyFrame == 2) DestroyOwnDefinitions();
                if (frame - _applyFrame >= VerifyFrames) Finish(_tool.LastAppliedFrame >= _applyFrame ? "applied" : "the build tool never handed Apply to the game");
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets roads: build failed");
                Finish("build failed: " + exception.Message);
            }
        }

        private void Emit(int frame)
        {
            int pieceTotal = 0;
            var pieces = new List<(PlannedRoad Road, int First, int Last)>();
            foreach (var road in _plan.Roads)
                foreach (var (first, last) in RoadPieces.Cut(road.Points)) { pieces.Add((road, first, last)); pieceTotal++; }
            // Room in the terrain's road cull list before our nets come (TerrainCullGuard.cs: the CullForCascades crash).
            TerrainCullGuard.Reserve(_terrain, pieceTotal);
            var random = new Unity.Mathematics.Random(0x5EED1234u);
            foreach (var (road, first, last) in pieces)
            {
                var points = road.Points;
                float3 a = points[first], d = points[last];
                float3 startTangent = Tangent(points, first), endTangent = Tangent(points, last);
                float third = math.distance(a, d) / 3f;
                var curve = new Bezier4x3(a, a + startTangent * third, d - endTangent * third, d);
                var start = CoursePosition(a, startTangent, 0f, CoursePosFlags.IsFirst, first == 0 ? _plan.Nodes[road.StartNode] : null);
                var end = CoursePosition(d, endTangent, 1f, CoursePosFlags.IsLast, last == points.Length - 1 ? _plan.Nodes[road.EndNode] : null);
                // An end attached to an existing road snaps onto it: rebuild the curve to the snapped points.
                if (!start.m_Position.Equals(a) || !end.m_Position.Equals(d))
                    curve = new Bezier4x3(start.m_Position, curve.b + (start.m_Position - a), curve.c + (end.m_Position - d), end.m_Position);
                start.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve));
                end.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve));
                var definition = EntityManager.CreateEntity();
                _definitions.Add(definition);
                EntityManager.AddComponentData(definition, new CreationDefinition { m_Prefab = _prefab, m_RandomSeed = random.NextInt() });
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponentData(definition, new NetCourse
                {
                    m_Curve = curve,
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                    m_Elevation = float2.zero,
                    m_StartPosition = start,
                    m_EndPosition = end,
                });
                _pieceMiddles.Add(MathUtils.Position(curve, 0.5f).xz);
            }
            _emitFrame = frame;
            Mod.Logger.Info($"Stepped Streets roads: emitted {pieceTotal} courses for {_plan.Roads.Count} roads as '{_roadName}' in one frame.");
        }

        // The road's direction at a point, from its neighbours; one-sided at the ends, so the pieces of a street that meet at a
        // junction share a direction there and the street runs on smoothly.
        private static float3 Tangent(float3[] points, int index)
        {
            int before = math.max(0, index - 1), after = math.min(points.Length - 1, index + 1);
            return math.normalizesafe(points[after] - points[before], new float3(1f, 0f, 0f));
        }

        private CoursePos CoursePosition(float3 position, float3 tangent, float delta, CoursePosFlags flags, PlannedNode node)
        {
            var course = new CoursePos
            {
                m_Entity = Entity.Null,
                m_Position = position,
                m_Rotation = NetUtils.GetNodeRotation(tangent),
                m_CourseDelta = delta,
                m_Elevation = float2.zero,
                // Roads zone on both sides.
                m_Flags = flags | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                m_ParentMesh = -1,
                m_SplitPosition = 0f,
            };
            if (node == null || node.ExistingRoadId == 0) return course;
            var target = SiteSnapshot.Unpack(node.ExistingRoadId);
            if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target) || !EntityManager.HasComponent<Edge>(target)
                || !EntityManager.HasComponent<Curve>(target))
            {
                Mod.Logger.Warn("Stepped Streets roads: the existing road a connection joins has changed; the connection ends beside it instead.");
                return course;
            }
            var curve = EntityManager.GetComponentData<Curve>(target).m_Bezier;
            float distance = MathUtils.Distance(curve.xz, position.xz, out float t);
            if (distance > AttachReach) return course;
            var edge = EntityManager.GetComponentData<Edge>(target);
            if (t <= 0.01f) { course.m_Entity = edge.m_Start; course.m_SplitPosition = 0f; }
            else if (t >= 0.99f) { course.m_Entity = edge.m_End; course.m_SplitPosition = 1f; }
            else { course.m_Entity = target; course.m_SplitPosition = t; }
            course.m_Position = MathUtils.Position(curve, t);
            return course;
        }

        private int CountBuilt()
        {
            if (_pieceMiddles.Count == 0) return 0;
            var buckets = new Dictionary<long, List<float2>>();
            long Key(float2 point) => ((long)(int)math.floor(point.x / 16f) << 32) ^ (uint)(int)math.floor(point.y / 16f);
            foreach (var middle in _pieceMiddles)
            {
                long key = Key(middle);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<float2>();
                list.Add(middle);
            }
            var found = new HashSet<float2>();
            using (var edges = _edges.ToEntityArray(Allocator.Temp))
                foreach (var edge in edges)
                {
                    float2 middle = MathUtils.Position(EntityManager.GetComponentData<Curve>(edge).m_Bezier, 0.5f).xz;
                    if (!buckets.TryGetValue(Key(middle), out var list)) continue;
                    foreach (var planned in list) if (math.distance(planned, middle) < 3f) found.Add(planned);
                }
            return found.Count;
        }

        private void DestroyOwnDefinitions()
        {
            foreach (var definition in _definitions)
                if (EntityManager.Exists(definition) && !EntityManager.HasComponent<Deleted>(definition))
                    EntityManager.AddComponent<Deleted>(definition);
            _definitions.Clear();
        }

        private void Finish(string details)
        {
            var report = new NetBuildReport { Pieces = _pieceMiddles.Count, Refused = _refusedCount };
            try { report.Built = details == "applied" ? CountBuilt() : 0; }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets roads: counting the built edges failed: " + exception.Message); }
            try { DestroyOwnDefinitions(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets roads: definition cleanup failed: " + exception.Message); }
            try { _tool.Restore(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets roads: restoring the previous tool failed: " + exception.Message); }
            report.Summary = $"Stepped Streets roads: {report.Built} of {report.Pieces} pieces found built, {report.Refused} refused before Apply; {details}.";
            if (report.Built < report.Pieces) Mod.Logger.Warn(report.Summary); else Mod.Logger.Info(report.Summary);
            var whenDone = _whenDone;
            _whenDone = null;
            _plan = null;
            Busy = false;
            try { whenDone?.Invoke(report); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets roads: completion callback failed"); }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Busy) Finish("system destroyed before the build completed");
            base.OnDestroy();
        }
    }
}
