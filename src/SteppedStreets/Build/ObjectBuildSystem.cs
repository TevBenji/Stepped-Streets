// Placemaker — emitting CreationDefinitions for each plan element (GPL-3.0).
// Net courses follow ParkManager's CreateNetCourseDefinition and GridRoadGenerator's
// CreateGridDefinitions/MakeCoursePos (attach to an existing edge via m_Entity + m_SplitPosition,
// Upgraded composition flags on the definition). Object definitions follow ParkingLotTool's
// "field by field like ObjectToolBaseSystem" ObjectDefinition. Credits: tomkpunkt, Ti4goc,
// KruemelGames (GPL-3.0).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: trees (prefabs chosen by name from the game's trees) and service buildings only, live ground heights, batches of 1500 definitions per frame and the standalone build-tool cadence; no nets, scaling, grouping or audit machinery.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SteppedStreets.Core.Neighbourhood;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace SteppedStreets.Build
{
    public sealed class ObjectBuildReport
    {
        public int Requested;
        public int Placed;
        public string Summary = "";
    }

    /// <summary>Places trees or buildings as object definitions through the invisible build tool, 1500 a frame.</summary>
    public sealed partial class ObjectBuildSystem : GameSystemBase
    {
        /// <summary>One object to place: a building faces <see cref="Rotation"/>; a tree's age gives its growth stage.</summary>
        public struct Placement
        {
            public Entity Prefab;
            public float3 Position;
            public quaternion Rotation;
            public int Seed;
            public float Age;
        }

        private const int PerFrame = 1500;
        private static readonly string[] Conifers = { "spruce", "pine", "fir", "conifer", "cedar" };
        private static readonly string[] Broadleaves = { "oak", "birch", "alder", "maple", "linden", "beech", "poplar", "elm", "ash", "chestnut", "plane", "willow" };
        // The game's tree growth stages (ToolUtils.GetRandomAge): teen, adult and elderly look settled; saplings do not.
        private static readonly float[] Ages = { 0.17500001f, 0.425f, 0.77500004f };

        private readonly List<Entity> _definitions = new List<Entity>();
        private BuildToolSystem _tool;
        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private List<Placement> _objects;
        private string _what = "trees";
        private List<Entity> _conifers, _broadleaves;
        private int _cursor, _activationFrame, _emittedFrame = -1, _applyFrame = -1, _placed;
        private Action<ObjectBuildReport> _whenDone;

        public bool Busy { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        public void Begin(List<PlannedTree> trees, Action<ObjectBuildReport> whenDone)
        {
            if (!Start("trees", whenDone)) return;
            try
            {
                if (trees == null || trees.Count == 0) { Finish("no trees planned"); return; }
                FindPrefabs();
                if (_conifers.Count == 0 && _broadleaves.Count == 0) { Finish("no tree prefabs found"); return; }
                foreach (var tree in trees)
                {
                    var choices = tree.Kind == TreeKind.Conifer ? _conifers : _broadleaves;
                    _objects.Add(new Placement
                    {
                        Prefab = choices[(int)(tree.Variation % (uint)choices.Count)],
                        Position = tree.Position,
                        Rotation = quaternion.RotateY((tree.Variation >> 8) % 360u * math.PI / 180f),
                        Seed = (int)(tree.Variation & 0x7FFFFFFF),
                        Age = Ages[(tree.Variation >> 4) % (uint)Ages.Length],
                    });
                }
                Activate();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets trees: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        /// <summary>Places buildings; each stands at its position on the live ground, facing its rotation.</summary>
        public void BeginBuildings(List<Placement> buildings, Action<ObjectBuildReport> whenDone)
        {
            if (!Start("buildings", whenDone)) return;
            try
            {
                if (buildings == null || buildings.Count == 0) { Finish("no buildings planned"); return; }
                _objects.AddRange(buildings);
                Activate();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets buildings: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        private bool Start(string what, Action<ObjectBuildReport> whenDone)
        {
            if (Busy) { Mod.Logger.Warn("Stepped Streets " + what + ": Begin ignored while a build is busy."); return false; }
            Busy = true;
            _what = what;
            _whenDone = whenDone;
            _objects = new List<Placement>();
            _cursor = _placed = 0;
            _emittedFrame = _applyFrame = -1;
            return true;
        }

        private void Activate()
        {
            _tool.Activate();
            _activationFrame = UnityEngine.Time.frameCount;
        }

        private void FindPrefabs()
        {
            _conifers = new List<Entity>();
            _broadleaves = new List<Entity>();
            var other = new List<(string, Entity)>();
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TreeData>(), ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<ObjectGeometryData>()))
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                var named = new List<(string Name, Entity Prefab)>();
                foreach (var entity in entities)
                    if (_prefabs.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabData>(entity), out var prefab) && prefab != null)
                        named.Add((prefab.name ?? "", entity));
                // Sorted so the same city always picks the same trees.
                named.Sort((first, second) => string.CompareOrdinal(first.Name, second.Name));
                foreach (var (name, prefab) in named)
                {
                    string lower = name.ToLowerInvariant();
                    if (lower.Contains("dead") || lower.Contains("stump") || lower.Contains("bush") || lower.Contains("hedge")) continue;
                    if (Array.Exists(Conifers, word => lower.Contains(word))) _conifers.Add(prefab);
                    else if (Array.Exists(Broadleaves, word => lower.Contains(word))) _broadleaves.Add(prefab);
                    else other.Add((name, prefab));
                }
            }
            if (_conifers.Count == 0) foreach (var (_, prefab) in other) _conifers.Add(prefab);
            if (_broadleaves.Count == 0) _broadleaves.AddRange(_conifers);
            if (_conifers.Count == 0) _conifers.AddRange(_broadleaves);
            Mod.Logger.Info($"Stepped Streets trees: {_conifers.Count} conifer and {_broadleaves.Count} broadleaf prefabs.");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!Busy) return;
            try
            {
                int frame = UnityEngine.Time.frameCount;
                if (!_tool.IsActive && _applyFrame < 0) { Finish("interrupted by a tool change"); return; }
                if (_emittedFrame < 0)
                {
                    if (!_tool.Ready)
                    {
                        if (frame - _activationFrame >= 8) Finish("build tool did not start within eight frames");
                        return;
                    }
                    EmitBatch();
                    if (_cursor >= _objects.Count) _emittedFrame = frame;
                    return;
                }
                if (_applyFrame < 0)
                {
                    if (frame - _emittedFrame < 2) return;
                    _tool.Requested = ApplyMode.Apply;
                    _applyFrame = frame;
                    return;
                }
                if (frame - _applyFrame >= 3)
                    Finish(_tool.LastAppliedFrame >= _applyFrame ? "Apply handed to the game; definitions counted, objects not verified" : "the build tool never handed Apply to the game");
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets " + _what + ": build failed");
                Finish("build failed: " + exception.Message);
            }
        }

        private void EmitBatch()
        {
            var heights = _terrain.GetHeightData(true);
            int end = math.min(_objects.Count, _cursor + PerFrame);
            for (; _cursor < end; _cursor++)
            {
                var item = _objects[_cursor];
                float height = TerrainUtils.SampleHeight(ref heights, item.Position);
                var position = new float3(item.Position.x, math.isfinite(height) ? height : item.Position.y, item.Position.z);
                var rotation = item.Rotation;
                var definition = EntityManager.CreateEntity();
                _definitions.Add(definition);
                EntityManager.AddComponentData(definition, new CreationDefinition { m_Prefab = item.Prefab, m_RandomSeed = item.Seed });
                EntityManager.AddComponentData(definition, new ObjectDefinition
                {
                    m_Position = position,
                    m_LocalPosition = position,
                    m_Rotation = rotation,
                    m_LocalRotation = rotation,
                    m_Scale = new float3(1f),
                    m_Intensity = 1f,
                    m_Age = item.Age,
                    m_Probability = 100,
                    m_PrefabSubIndex = -1,
                    m_ParentMesh = -1,
                    m_Elevation = 0f,
                });
                EntityManager.AddComponent<Updated>(definition);
                _placed++;
            }
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
            try { DestroyOwnDefinitions(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets " + _what + ": definition cleanup failed: " + exception.Message); }
            try { _tool.Restore(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets " + _what + ": restoring the previous tool failed: " + exception.Message); }
            var report = new ObjectBuildReport
            {
                Requested = _objects?.Count ?? 0,
                Placed = _placed,
                Summary = $"Stepped Streets {_what}: emitted {_placed} of {_objects?.Count ?? 0}; {details}.",
            };
            Mod.Logger.Info(report.Summary);
            var whenDone = _whenDone;
            _whenDone = null;
            _objects = null;
            Busy = false;
            try { whenDone?.Invoke(report); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets " + _what + ": completion callback failed"); }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Busy) Finish("system destroyed before the build completed");
            base.OnDestroy();
        }
    }
}
