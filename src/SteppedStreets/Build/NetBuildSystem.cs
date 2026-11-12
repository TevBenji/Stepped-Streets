// Placemaker — emitting CreationDefinitions for each plan element (GPL-3.0).
// Net courses follow ParkManager's CreateNetCourseDefinition and GridRoadGenerator's
// CreateGridDefinitions/MakeCoursePos (attach to an existing edge via m_Entity + m_SplitPosition,
// Upgraded composition flags on the definition). Object definitions follow ParkingLotTool's
// "field by field like ObjectToolBaseSystem" ObjectDefinition. Credits: tomkpunkt, Ti4goc,
// KruemelGames (GPL-3.0).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: road courses only, from the neighbourhood plan's roads cut into short cubic pieces at their planned heights; MakeCoursePos reduced to attaching a road end to an existing edge or node; the standalone build-tool cadence, a temporaries count before Apply and a count of the built edges afterwards; the crosswalk-removal upgrade on every piece when crosswalks are off; no fitting, elevation, fences, objects, surfaces or audit machinery. 2.12.0: main streets with the main road, the street planting upgrade on every piece, footpath courses after the roads, and the planting upgrade on existing streets built the way the game's upgrade tool builds it (Footbridge Builder's NetDefinitions.RemoveCrosswalk, TevBenji, GPL-3.0).

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
using SteppedStreets.Settings;
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

        /// <summary>Footpath pieces planned, and found built after the roads.</summary>
        public int PathPieces, PathsBuilt;

        /// <summary>Street planting on existing streets: edges left as they were, to keep a bike lane or wide sidewalk grass would take.</summary>
        public int Skipped;

        public string Summary = "";
    }

    public sealed partial class NetBuildSystem : GameSystemBase
    {
        private const float AttachReach = 6f;
        private const int SettleFrames = 3;
        private const int MostWaitFrames = 90;
        private const int VerifyFrames = 30;
        // Footpaths go in pieces no longer than this; the game takes a Pavement Path edge up to 200 m.
        private const float MostPathPiece = 100f;

        /// <summary>What one run builds: the plan's roads (then its footpaths, once the roads are there), or the planting on existing streets.</summary>
        private enum Work { Roads, Paths, Planting }

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
        private bool _crosswalks;
        private Work _work;
        // Entity.Null: main streets are built with the street road.
        private Entity _mainPrefab;
        private string _mainRoadName;
        private Entity _pathPrefab;
        private CompositionFlags _planting;
        private bool _grass;
        private float2[] _area;
        private readonly List<Entity> _plantedEdges = new List<Entity>();
        private int _skipped, _roadPieces, _roadsBuilt;
        private EntityQuery _anyEdges, _streets;
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
            _anyEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // The player's own streets: a building's roads (an Owner) are part of the building.
            _streets = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Composition>(),
                    ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
        }

        /// <summary>
        /// The plan's roads, main streets with <paramref name="mainRoadName"/> when one is chosen, then its footpaths. The
        /// crosswalk and street planting settings are read now, as they don't change the plan.
        /// </summary>
        public void Begin(NeighbourhoodPlan plan, string roadName, string mainRoadName, Action<NetBuildReport> whenDone)
        {
            if (!Start(Work.Roads, whenDone)) return;
            _plan = plan;
            try
            {
                if (plan == null || plan.Roads.Count == 0) { Finish("no roads planned"); return; }
                var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
                var road = RoadCatalog.Find(EntityManager, prefabs, roadName);
                if (road == null) { Finish("no road prefab is available"); return; }
                _prefab = road.Prefab;
                _roadName = road.Name;
                // Found the same way as when the plan took its width.
                var main = string.IsNullOrEmpty(mainRoadName) ? null : RoadCatalog.Find(EntityManager, prefabs, mainRoadName);
                _mainPrefab = main?.Prefab ?? Entity.Null;
                _mainRoadName = main?.Name;
                _pathPrefab = plan.Paths.Count > 0 ? RoadCatalog.FindPath(EntityManager, prefabs) : Entity.Null;
                if (plan.Paths.Count > 0 && _pathPrefab == Entity.Null) Mod.Logger.Warn("Stepped Streets roads: no footpath prefab is available; the footpaths are left out.");
                _crosswalks = Mod.Settings?.Crosswalks ?? true;
                var planting = Mod.Settings?.StreetTrees ?? StreetPlanting.None;
                _planting = Planting(planting);
                _grass = planting == StreetPlanting.Grass;
                Activate();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets roads: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        /// <summary>
        /// Use existing: the street planting on the player's streets whose middle lies in the area, as the game's own grass or
        /// tree upgrade. Highways, bridges and tunnels are left alone; so, for grass, is a street with a bike lane or wide
        /// sidewalk, which the game's grass upgrade would take away.
        /// </summary>
        public void BeginPlanting(float2[] area, StreetPlanting planting, Action<NetBuildReport> whenDone)
        {
            if (!Start(Work.Planting, whenDone)) return;
            try
            {
                _area = area;
                _planting = Planting(planting);
                _grass = planting == StreetPlanting.Grass;
                if (planting == StreetPlanting.None || area == null || area.Length < 3) { Finish("nothing to plant"); return; }
                Activate();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets street planting: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        private bool Start(Work work, Action<NetBuildReport> whenDone)
        {
            if (Busy) { Mod.Logger.Warn("Stepped Streets roads: Begin ignored while a build is busy."); return false; }
            Busy = true;
            _work = work;
            _whenDone = whenDone;
            _plan = null;
            _emitFrame = _applyFrame = _lastTemporaryCount = -1;
            _refusedCount = _skipped = _roadPieces = _roadsBuilt = 0;
            _pathPrefab = Entity.Null;
            _pieceMiddles.Clear();
            _plantedEdges.Clear();
            return true;
        }

        private void Activate()
        {
            _tool.Activate();
            _activationFrame = UnityEngine.Time.frameCount;
        }

        /// <summary>
        /// The game's grass or tree upgrade as composition flags (its upgrade prefabs' set flags, docs/2.12.0-GAME-FACTS.md), on
        /// both sides and in the middle, so a divided road gets its median planted too.
        /// </summary>
        internal static CompositionFlags Planting(StreetPlanting planting) =>
            planting == StreetPlanting.Trees ? new CompositionFlags
            {
                m_General = CompositionFlags.General.SecondaryMiddleBeautification,
                m_Left = CompositionFlags.Side.SecondaryBeautification,
                m_Right = CompositionFlags.Side.SecondaryBeautification,
            }
            : planting == StreetPlanting.Grass ? new CompositionFlags
            {
                m_General = CompositionFlags.General.PrimaryMiddleBeautification,
                m_Left = CompositionFlags.Side.PrimaryBeautification,
                m_Right = CompositionFlags.Side.PrimaryBeautification,
            }
            : default;

        private static CompositionFlags Union(CompositionFlags first, CompositionFlags second) => new CompositionFlags
        {
            m_General = first.m_General | second.m_General,
            m_Left = first.m_Left | second.m_Left,
            m_Right = first.m_Right | second.m_Right,
        };

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
                    if (_tool.Ready)
                    {
                        int emitted = _work == Work.Roads ? Emit(frame) : _work == Work.Paths ? EmitPaths(frame) : EmitPlanting(frame);
                        if (emitted == 0) Finish(_work == Work.Planting ? "no street in the area to plant" : "nothing to build");
                    }
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
                    int refused = _refused.CalculateEntityCount();
                    _refusedCount += refused;
                    if (refused > 0) Mod.Logger.Warn($"Stepped Streets roads: the game marked {refused} temporary pieces with an error before Apply.");
                    Mod.Logger.Info($"Stepped Streets roads: {count} temporary edges after {frame - _emitFrame} frames; Apply.");
                    _tool.Requested = ApplyMode.Apply;
                    _applyFrame = frame;
                    return;
                }
                if (frame - _applyFrame == 2) DestroyOwnDefinitions();
                if (frame - _applyFrame < VerifyFrames) return;
                bool applied = _tool.LastAppliedFrame >= _applyFrame;
                if (applied && _work == Work.Roads && _pathPrefab != Entity.Null) StartPaths();
                else Finish(applied ? "applied" : "the build tool never handed Apply to the game");
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets roads: build failed");
                Finish("build failed: " + exception.Message);
            }
        }

        /// <summary>
        /// The footpaths once the roads are built, with the build tool still active: a path's end finds the sidewalk it joins
        /// only on a road that is really there.
        /// </summary>
        private void StartPaths()
        {
            _roadPieces = _pieceMiddles.Count;
            _roadsBuilt = CountBuilt(_edges);
            if (_roadsBuilt == 0) { Finish("applied"); return; }
            _work = Work.Paths;
            _pieceMiddles.Clear();
            _emitFrame = _applyFrame = _lastTemporaryCount = -1;
        }

        private int Emit(int frame)
        {
            int pieceTotal = 0;
            var pieces = new List<(PlannedRoad Road, float3[] Points, int First, int Last)>();
            foreach (var road in _plan.Roads)
            {
                var cut = RoadPieces.Cut(road.Points, out var cutPoints);
                foreach (var (first, last) in cut) { pieces.Add((road, cutPoints, first, last)); pieceTotal++; }
            }
            // Room in the terrain's road cull list before our nets come (TerrainCullGuard.cs: the CullForCascades crash).
            TerrainCullGuard.Reserve(_terrain, pieceTotal);
            var random = new Unity.Mathematics.Random(0x5EED1234u);
            foreach (var (road, points, first, last) in pieces)
            {
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
                var prefab = road.Main && _mainPrefab != Entity.Null ? _mainPrefab : _prefab;
                EntityManager.AddComponentData(definition, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = random.NextInt() });
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
                // The game copies Upgraded from a definition onto the edges it builds (CourseSplitSystem), the same flag the
                // crosswalk upgrade tool sets. Both sides, because which side holds an end's crosswalk depends on the node's
                // composition; the joints between pieces are two-way nodes with no crosswalk, so only the junctions change.
                // The street planting goes the same way, as the grass or tree upgrade would.
                var flags = _planting;
                if (!_crosswalks)
                    flags = Union(flags, new CompositionFlags
                    {
                        m_Left = CompositionFlags.Side.RemoveCrosswalk,
                        m_Right = CompositionFlags.Side.RemoveCrosswalk,
                    });
                if (!flags.Equals(default(CompositionFlags))) EntityManager.AddComponentData(definition, new Upgraded { m_Flags = flags });
                _pieceMiddles.Add(MathUtils.Position(curve, 0.5f).xz);
            }
            _emitFrame = frame;
            int mainRoads = 0;
            foreach (var road in _plan.Roads) if (road.Main) mainRoads++;
            Mod.Logger.Info($"Stepped Streets roads: emitted {pieceTotal} courses for {_plan.Roads.Count} roads as '{_roadName}'"
                + (_mainPrefab != Entity.Null ? $", {mainRoads} main streets as '{_mainRoadName}'" : "") + $" in one frame{(_crosswalks ? "" : ", without crosswalks")}"
                + (_planting.Equals(default(CompositionFlags)) ? "" : _grass ? ", with grass" : ", with trees") + ".");
            return pieceTotal;
        }

        /// <summary>
        /// Each footpath as straight pieces at the planned heights (the ground was graded to them), its two ends free: a path
        /// end just short of a kerb and facing it joins the sidewalk with no crossing (Footbridge Builder's GAME-FACTS 3).
        /// </summary>
        private int EmitPaths(int frame)
        {
            var random = new Unity.Mathematics.Random(0x5EED4321u);
            var pieces = new List<(float3 Start, float3 End)>();
            foreach (var path in _plan.Paths)
            {
                int count = math.max(1, (int)math.ceil(math.distance(path.Start, path.End) / MostPathPiece));
                for (int piece = 0; piece < count; piece++)
                    pieces.Add((math.lerp(path.Start, path.End, piece / (float)count), math.lerp(path.Start, path.End, (piece + 1) / (float)count)));
            }
            TerrainCullGuard.Reserve(_terrain, pieces.Count);
            foreach (var (a, d) in pieces)
            {
                var curve = new Bezier4x3(a, math.lerp(a, d, 1f / 3f), math.lerp(a, d, 2f / 3f), d);
                var direction = math.normalizesafe(d - a, new float3(1f, 0f, 0f));
                var definition = EntityManager.CreateEntity();
                _definitions.Add(definition);
                EntityManager.AddComponentData(definition, new CreationDefinition { m_Prefab = _pathPrefab, m_RandomSeed = random.NextInt() });
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponentData(definition, new NetCourse
                {
                    m_Curve = curve,
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                    m_Elevation = float2.zero,
                    m_StartPosition = PathEnd(a, direction, 0f, CoursePosFlags.IsFirst),
                    m_EndPosition = PathEnd(d, direction, 1f, CoursePosFlags.IsLast),
                });
                _pieceMiddles.Add(MathUtils.Position(curve, 0.5f).xz);
            }
            _emitFrame = frame;
            Mod.Logger.Info($"Stepped Streets roads: emitted {pieces.Count} footpath courses for {_plan.Paths.Count} paths.");
            return pieces.Count;
        }

        private static CoursePos PathEnd(float3 position, float3 direction, float delta, CoursePosFlags flags) => new CoursePos
        {
            m_Entity = Entity.Null,
            m_Position = position,
            m_Rotation = NetUtils.GetNodeRotation(direction),
            m_CourseDelta = delta,
            m_Elevation = float2.zero,
            m_Flags = flags,
            m_ParentMesh = -1,
            m_SplitPosition = 0f,
        };

        /// <summary>
        /// One upgrade definition per street in the area that the planting changes, the way the game's upgrade tool makes them
        /// (Footbridge Builder's NetDefinitions.RemoveCrosswalk): the edge as its original, its full new flag set, its own course
        /// and the Upgrade flag. The game keeps each edge and changes its flags.
        /// </summary>
        private int EmitPlanting(int frame)
        {
            using (var edges = _streets.ToEntityArray(Allocator.Temp))
                foreach (var edge in edges)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
                    if (!EntityManager.HasComponent<RoadData>(prefab) || (EntityManager.GetComponentData<RoadData>(prefab).m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0) continue;
                    var curve = EntityManager.GetComponentData<Curve>(edge).m_Bezier;
                    if (!Core.Terrain.Poly.Contains(_area, MathUtils.Position(curve, 0.5f).xz)) continue;
                    var composition = EntityManager.GetComponentData<Composition>(edge);
                    if (EntityManager.HasComponent<NetCompositionData>(composition.m_Edge)
                        && (EntityManager.GetComponentData<NetCompositionData>(composition.m_Edge).m_Flags.m_General
                            & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) continue;
                    var flags = EntityManager.HasComponent<Upgraded>(edge) ? EntityManager.GetComponentData<Upgraded>(edge).m_Flags : default;
                    // The game's grass upgrade takes out a wide median, wide sidewalks and the side lane: the player's own
                    // upgrades stay, and that street keeps no grass.
                    if (_grass && ((flags.m_General & CompositionFlags.General.WideMedian) != 0
                        || ((flags.m_Left | flags.m_Right) & (CompositionFlags.Side.WideSidewalk | CompositionFlags.Side.SecondaryLane)) != 0))
                    {
                        _skipped++;
                        continue;
                    }
                    var planted = Union(flags, _planting);
                    if (planted.Equals(flags)) continue;
                    _definitions.Add(Upgrade(edge, curve, prefab, planted));
                    _plantedEdges.Add(edge);
                }
            _emitFrame = frame;
            Mod.Logger.Info($"Stepped Streets street planting: {_plantedEdges.Count} streets to plant with {(_grass ? "grass" : "trees")}, {_skipped} kept as they are for their bike lanes or wide sidewalks.");
            return _plantedEdges.Count;
        }

        private Entity Upgrade(Entity edge, Bezier4x3 curve, Entity prefab, CompositionFlags flags)
        {
            var ends = EntityManager.GetComponentData<Edge>(edge);
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Original = edge,
                m_Prefab = prefab,
                m_Flags = CreationFlags.Align | CreationFlags.SubElevation | CreationFlags.Upgrade | CreationFlags.Parent,
            });
            EntityManager.AddComponentData(definition, new Upgraded { m_Flags = flags });
            var replacements = EntityManager.AddBuffer<SubReplacement>(definition);
            if (EntityManager.HasBuffer<SubReplacement>(edge))
                foreach (var replacement in EntityManager.GetBuffer<SubReplacement>(edge, true)) replacements.Add(replacement);
            EntityManager.AddComponentData(definition, new NetCourse
            {
                m_Curve = curve,
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = EntityManager.HasComponent<Fixed>(edge) ? EntityManager.GetComponentData<Fixed>(edge).m_Index : -1,
                m_StartPosition = new CoursePos
                {
                    m_Entity = ends.m_Start, m_Position = curve.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                    m_CourseDelta = 0f, m_Flags = CoursePosFlags.IsFirst, m_ParentMesh = -1,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = ends.m_End, m_Position = curve.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                    m_CourseDelta = 1f, m_Flags = CoursePosFlags.IsLast, m_ParentMesh = -1,
                },
            });
            EntityManager.AddComponent<Updated>(definition);
            return definition;
        }

        /// <summary>How many of the streets the planting was for now carry it.</summary>
        private int CountPlanted()
        {
            int planted = 0;
            foreach (var edge in _plantedEdges)
            {
                if (!EntityManager.Exists(edge) || !EntityManager.HasComponent<Upgraded>(edge)) continue;
                var flags = EntityManager.GetComponentData<Upgraded>(edge).m_Flags;
                if (Union(flags, _planting).Equals(flags)) planted++;
            }
            return planted;
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

        private int CountBuilt(EntityQuery query)
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
            using (var edges = query.ToEntityArray(Allocator.Temp))
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
            var report = new NetBuildReport { Refused = _refusedCount, Skipped = _skipped };
            bool applied = details == "applied";
            try
            {
                switch (_work)
                {
                    case Work.Roads:
                        report.Pieces = _pieceMiddles.Count;
                        report.Built = applied ? CountBuilt(_edges) : 0;
                        break;
                    case Work.Paths:
                        report.Pieces = _roadPieces;
                        report.Built = _roadsBuilt;
                        report.PathPieces = _pieceMiddles.Count;
                        // A path piece is no road, so any edge counts.
                        report.PathsBuilt = applied ? CountBuilt(_anyEdges) : 0;
                        break;
                    default:
                        report.Pieces = _plantedEdges.Count;
                        report.Built = applied ? CountPlanted() : 0;
                        break;
                }
            }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets roads: counting the built edges failed: " + exception.Message); }
            try { DestroyOwnDefinitions(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets roads: definition cleanup failed: " + exception.Message); }
            try { _tool.Restore(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets roads: restoring the previous tool failed: " + exception.Message); }
            report.Summary = _work == Work.Planting
                ? $"Stepped Streets street planting: {report.Built} of {report.Pieces} streets planted, {report.Skipped} kept for their bike lanes or wide sidewalks, {report.Refused} refused before Apply; {details}."
                : $"Stepped Streets roads: {report.Built} of {report.Pieces} pieces found built"
                    + (_work == Work.Paths ? $", {report.PathsBuilt} of {report.PathPieces} footpath pieces" : "") + $", {report.Refused} refused before Apply; {details}.";
            if (report.Built < report.Pieces || report.PathsBuilt < report.PathPieces) Mod.Logger.Warn(report.Summary); else Mod.Logger.Info(report.Summary);
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
