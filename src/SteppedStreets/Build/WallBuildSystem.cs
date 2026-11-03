// Placemaker — emitting CreationDefinitions for each plan element (GPL-3.0).
// Net courses follow ParkManager's CreateNetCourseDefinition and GridRoadGenerator's
// CreateGridDefinitions/MakeCoursePos (attach to an existing edge via m_Entity + m_SplitPosition,
// Upgraded composition flags on the definition). Object definitions follow ParkingLotTool's
// "field by field like ObjectToolBaseSystem" ObjectDefinition. Credits: tomkpunkt, Ti4goc,
// KruemelGames (GPL-3.0).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: garden-wall objects only, named prefab lookup, measured piece length, fixed pad heights and the standalone build-tool cadence; no nets, ground snapping, scaling, grouping or audit machinery.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using SteppedStreets.Core.Planning;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace SteppedStreets.Build
{
    public sealed class WallBuildReport
    {
        public int Requested;
        public int Placed;
        public string Summary;
    }

    public sealed partial class WallBuildSystem : GameSystemBase
    {
        private const string WallPrefabName = "EU_Wall02";
        // ponytail: 3 m garden-wall fallback when geometry is absent; replace with an asset-specific lookup if game assets change.
        private const float DefaultPieceLength = 3f;
        private readonly List<Entity> _definitions = new List<Entity>();
        private BuildToolSystem _tool;
        private PrefabSystem _prefabs;
        private Entity _wallPrefab;
        private WallSegment[] _walls;
        private int[] _pieceCounts;
        private float _pieceLength;
        private int _requested;
        private int _placed;
        private int _activationFrame;
        private int _emitFrame = -1;
        private bool _applyRequested;
        private bool _toolActivated;
        private Action<WallBuildReport> _whenDone;

        public bool Busy { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                _tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
                _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            }
            catch (Exception exception)
            {
                Enabled = false;
                Mod.Logger.Error(exception, "Stepped Streets walls: system creation failed");
            }
        }

        public void Begin(IReadOnlyList<WallSegment> walls, Action<WallBuildReport> whenDone)
        {
            if (Busy)
            {
                const string summary = "Stepped Streets walls: already building; request ignored.";
                Mod.Logger.Warn(summary);
                Complete(whenDone, new WallBuildReport { Summary = summary });
                return;
            }

            Busy = true;
            _whenDone = whenDone;
            _requested = _placed = 0;
            _emitFrame = -1;
            _applyRequested = _toolActivated = false;
            try
            {
                if (walls == null) throw new ArgumentNullException(nameof(walls));
                _walls = new WallSegment[walls.Count];
                for (int index = 0; index < walls.Count; index++)
                {
                    var wall = walls[index];
                    if (!math.all(math.isfinite(wall.Start)) || !math.all(math.isfinite(wall.End))
                        || math.abs(wall.End.y - wall.Start.y) > 0.001f
                        || !(math.distance(wall.Start.xz, wall.End.xz) > 0f))
                        throw new ArgumentException($"Wall {index} must have finite, distinct ends at one base height.", nameof(walls));
                    _walls[index] = wall;
                }
                if (_walls.Length == 0) { Finish("no walls requested"); return; }
                if (_tool == null || _prefabs == null || !Enabled)
                    throw new InvalidOperationException("The wall build system did not initialize.");

                // PrefabID -> prefab -> entity follows SurfaceCloneSystem.CreateClone's existing-prefab lookup.
                if (!_prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), WallPrefabName), out var prefab)
                    || !(prefab is StaticObjectPrefab) || !_prefabs.TryGetEntity(prefab, out _wallPrefab))
                {
                    Finish($"garden wall '{WallPrefabName}' is unavailable");
                    return;
                }
                _pieceLength = EntityManager.HasComponent<ObjectGeometryData>(_wallPrefab)
                    ? EntityManager.GetComponentData<ObjectGeometryData>(_wallPrefab).m_Size.z : 0f;
                if (!math.isfinite(_pieceLength) || _pieceLength <= 0f)
                {
                    _pieceLength = DefaultPieceLength;
                    Mod.Logger.Warn($"Stepped Streets walls: '{WallPrefabName}' has no usable ObjectGeometryData.m_Size.z; using {_pieceLength:0.##} m pieces.");
                }

                _pieceCounts = new int[_walls.Length];
                for (int index = 0; index < _walls.Length; index++)
                {
                    float length = math.distance(_walls[index].Start.xz, _walls[index].End.xz);
                    // Keep the final unscaled piece only when at least half of it fits.
                    int count = checked((int)math.floor(length / _pieceLength + 0.5f));
                    _pieceCounts[index] = count;
                    _requested = checked(_requested + count);
                }
                if (_requested == 0) { Finish("all segments are shorter than half a wall piece"); return; }
                _toolActivated = true;
                _tool.Activate();
                _activationFrame = UnityEngine.Time.frameCount;
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets walls: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!Busy) return;
            try
            {
                if (!_tool.IsActive) { _tool.Interrupted = true; Finish("interrupted by a tool change; no further Apply requested"); return; }
                int frame = UnityEngine.Time.frameCount;
                if (_emitFrame < 0)
                {
                    if (_tool.Ready) Emit(frame);
                    else if (frame - _activationFrame >= 8) Finish("build tool did not start within eight frames");
                    return;
                }

                int elapsed = frame - _emitFrame;
                if (!_applyRequested && elapsed >= 2)
                {
                    _tool.Requested = ApplyMode.Apply;
                    _applyRequested = true;
                }
                if (elapsed >= 4)
                    Finish(_tool.LastAppliedFrame >= _emitFrame
                        ? "Apply handed to the game; emitted definitions counted, objects not verified"
                        : "build tool never handed Apply to the game; objects not verified");
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets walls: build failed");
                Finish("build failed: " + exception.Message);
            }
        }

        private void Emit(int frame)
        {
            for (int index = 0; index < _walls.Length; index++)
            {
                var wall = _walls[index];
                float3 direction = math.normalize(new float3(wall.End.x - wall.Start.x, 0f, wall.End.z - wall.Start.z));
                // EU_Wall02 runs along local Z, with its origin at the piece centre.
                var rotation = quaternion.RotateY(math.atan2(direction.x, direction.z));
                for (int piece = 0; piece < _pieceCounts[index]; piece++)
                {
                    float3 position = wall.Start + direction * ((piece + 0.5f) * _pieceLength);
                    var definition = EntityManager.CreateEntity();
                    _definitions.Add(definition);
                    EntityManager.AddComponentData(definition, new CreationDefinition
                    {
                        m_Prefab = _wallPrefab,
                        m_RandomSeed = _placed + 1,
                    });
                    EntityManager.AddComponentData(definition, new ObjectDefinition
                    {
                        m_Position = position,
                        m_LocalPosition = position,
                        m_Rotation = rotation,
                        m_LocalRotation = rotation,
                        m_Scale = new float3(1f),
                        m_Intensity = 1f,
                        m_Age = 0.425f,
                        m_Probability = 100,
                        m_PrefabSubIndex = -1,
                        m_ParentMesh = -1,
                        m_Elevation = 0f,
                    });
                    EntityManager.AddComponent<Updated>(definition);
                    _placed++;
                }
            }
            _emitFrame = frame;
            Mod.Logger.Info($"Stepped Streets walls: emitted {_placed} '{WallPrefabName}' pieces; Apply at +2, cleanup +4 frames.");
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
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets walls: definition cleanup failed"); }
            try { if (_toolActivated) _tool.Restore(); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets walls: previous tool restoration failed"); }
            // ponytail: no verification pass; count the placed objects by owner if walls go missing.
            var report = new WallBuildReport
            {
                Requested = _requested,
                Placed = _placed,
                Summary = $"Stepped Streets walls: emitted {_placed} of {_requested} pieces; {details}.",
            };
            var whenDone = _whenDone;
            _whenDone = null;
            _walls = null;
            _pieceCounts = null;
            _toolActivated = false;
            Busy = false;
            Mod.Logger.Info(report.Summary);
            Complete(whenDone, report);
        }

        private static void Complete(Action<WallBuildReport> whenDone, WallBuildReport report)
        {
            try { whenDone?.Invoke(report); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets walls: completion callback failed"); }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            try
            {
                if (Busy) Finish("system destroyed before the build completed");
                base.OnDestroy();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets walls: system destruction failed");
            }
        }
    }
}
