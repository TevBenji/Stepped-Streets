// Placemaker — delete definitions, the way the game's bulldozer removes what it takes (GPL-3.0).
// An edge goes as CreationDefinition { m_Original, Delete } with a NetCourse along its own curve between its own nodes; an
// object or building as CreationDefinition { m_Original, Delete } with an ObjectDefinition where it stands (SuiteFixture's
// fixture clearing, BuildService.Owner.cs's clearing of trees and props).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: a standalone system for Remove last neighbourhood that takes a list of entities, 1500 a frame through the invisible build tool, and counts what is gone afterwards.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace SteppedStreets.Build
{
    public sealed class RemoveBuildReport
    {
        public int Requested;
        public int Removed;
        public string Summary = "";
    }

    /// <summary>Removes road edges, objects and buildings through the invisible build tool; their owned parts go with them.</summary>
    public sealed partial class RemoveBuildSystem : GameSystemBase
    {
        private const int PerFrame = 1500;
        private readonly List<Entity> _definitions = new List<Entity>();
        private BuildToolSystem _tool;
        private List<Entity> _targets;
        private int _cursor, _activationFrame, _emittedFrame = -1, _applyFrame = -1;
        private Action<RemoveBuildReport> _whenDone;

        public bool Busy { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
        }

        public void Begin(List<Entity> targets, Action<RemoveBuildReport> whenDone)
        {
            if (Busy) { Mod.Logger.Warn("Stepped Streets removal: Begin ignored while a build is busy."); return; }
            Busy = true;
            _whenDone = whenDone;
            _targets = new List<Entity>(targets ?? new List<Entity>());
            _cursor = 0;
            _emittedFrame = _applyFrame = -1;
            try
            {
                if (_targets.Count == 0) { Finish("nothing to remove"); return; }
                _tool.Activate();
                _activationFrame = UnityEngine.Time.frameCount;
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets removal: could not begin");
                Finish("could not begin: " + exception.Message);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!Busy) return;
            try
            {
                int frame = UnityEngine.Time.frameCount;
                if (!_tool.IsActive && _applyFrame < 0) { _tool.Interrupted = true; Finish("interrupted by a tool change"); return; }
                if (_emittedFrame < 0)
                {
                    if (!_tool.Ready)
                    {
                        if (frame - _activationFrame >= 8) Finish("build tool did not start within eight frames");
                        return;
                    }
                    EmitBatch();
                    if (_cursor >= _targets.Count) _emittedFrame = frame;
                    return;
                }
                if (_applyFrame < 0)
                {
                    if (frame - _emittedFrame < 2) return;
                    _tool.Requested = ApplyMode.Apply;
                    _applyFrame = frame;
                    return;
                }
                if (frame - _applyFrame == 4) DestroyOwnDefinitions();
                if (frame - _applyFrame >= 20) Finish(_tool.LastAppliedFrame >= _applyFrame ? "applied" : "the build tool never handed Apply to the game");
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets removal: failed");
                Finish("failed: " + exception.Message);
            }
        }

        private void EmitBatch()
        {
            int end = math.min(_targets.Count, _cursor + PerFrame);
            for (; _cursor < end; _cursor++)
            {
                var target = _targets[_cursor];
                if (Gone(target) || EntityManager.HasComponent<Temp>(target)) continue;
                if (EntityManager.HasComponent<Edge>(target) && EntityManager.HasComponent<Curve>(target))
                {
                    var curve = EntityManager.GetComponentData<Curve>(target).m_Bezier;
                    var edge = EntityManager.GetComponentData<Edge>(target);
                    var definition = Definition(target);
                    EntityManager.AddComponentData(definition, new NetCourse
                    {
                        m_Curve = curve,
                        m_Length = MathUtils.Length(curve),
                        m_FixedIndex = -1,
                        m_StartPosition = new CoursePos
                        {
                            m_Entity = edge.m_Start, m_Position = curve.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                            m_CourseDelta = 0f, m_ParentMesh = -1,
                        },
                        m_EndPosition = new CoursePos
                        {
                            m_Entity = edge.m_End, m_Position = curve.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                            m_CourseDelta = 1f, m_ParentMesh = -1,
                        },
                    });
                }
                else if (EntityManager.HasComponent<Game.Objects.Transform>(target))
                {
                    var transform = EntityManager.GetComponentData<Game.Objects.Transform>(target);
                    var definition = Definition(target);
                    EntityManager.AddComponentData(definition, new ObjectDefinition
                    {
                        m_Position = transform.m_Position,
                        m_Rotation = transform.m_Rotation,
                        m_LocalPosition = transform.m_Position,
                        m_LocalRotation = transform.m_Rotation,
                        m_ParentMesh = -1,
                        m_Scale = new float3(1f),
                        m_Intensity = 1f,
                        m_PrefabSubIndex = -1,
                    });
                }
            }
        }

        private Entity Definition(Entity original)
        {
            var definition = EntityManager.CreateEntity();
            _definitions.Add(definition);
            EntityManager.AddComponentData(definition, new CreationDefinition { m_Original = original, m_Flags = CreationFlags.Delete });
            EntityManager.AddComponent<Updated>(definition);
            return definition;
        }

        private bool Gone(Entity entity) => !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity);

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
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets removal: definition cleanup failed: " + exception.Message); }
            try { _tool.Restore(); }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets removal: restoring the previous tool failed: " + exception.Message); }
            var report = new RemoveBuildReport { Requested = _targets?.Count ?? 0 };
            try { foreach (var target in _targets ?? new List<Entity>()) if (Gone(target)) report.Removed++; }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets removal: counting what is gone failed: " + exception.Message); }
            report.Summary = $"Stepped Streets removal: {report.Removed} of {report.Requested} gone; {details}.";
            if (report.Removed < report.Requested) Mod.Logger.Warn(report.Summary); else Mod.Logger.Info(report.Summary);
            var whenDone = _whenDone;
            _whenDone = null;
            _targets = null;
            Busy = false;
            try { whenDone?.Invoke(report); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets removal: completion callback failed"); }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Busy) Finish("system destroyed before the removal completed");
            base.OnDestroy();
        }
    }
}
