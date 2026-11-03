// Placemaker — terraform brushes, lake water and zoning (GPL-3.0).
// Terraform: the desired ground (Core TerraformRaster) is applied directly with
// TerrainSystem.ApplyBrush (Shift kernel, one generated mask texture per tile) and verified against
// the CPU height readback. Fallback and water adapted from ParkManager's ParkLakeBuild (tomkpunkt,
// GPL-3.0): the game's level brush re-emitted for spots off target, then a constant-level water
// source fills the basin. Zoning follows GridRoadGenerator's Zoning
// definitions (Ti4goc, GPL-3.0): ZoningFlags.Zone|Marquee without Overwrite never repaints cells
// that are already zoned.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: extracted zoning only; named prefab lookup, live corner heights, owned definitions, centre-cell verification of the requested zone and a completion report.

using System;
using System.Collections.Generic;
using Colossal.Logging;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace SteppedStreets.Build
{
    public sealed class ZoneBuildReport
    {
        public int Requested;
        public int Zoned;
        public string Summary;
    }

    public sealed partial class ZoneBuildSystem : GameSystemBase
    {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(SteppedStreets)).SetShowsErrorsInUI(false);
        private readonly List<Entity> _definitions = new List<Entity>();
        private BuildToolSystem _tool;
        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private EntityQuery _zonePrefabs;
        private EntityQuery _zoneBlocks;
        private float2[][] _quads;
        private Entity _zonePrefab;
        private ZoneType _zoneType;
        private string _zoneName;
        private Action<ZoneBuildReport> _whenDone;
        private int _activationFrame;
        private int _emitFrame = -1;
        private bool _applyRequested;
        private bool _listedZonePrefabs;

        public bool Busy { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _zonePrefabs = GetEntityQuery(ComponentType.ReadOnly<ZoneData>(), ComponentType.ReadOnly<PrefabData>());
            _zoneBlocks = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        // Each four-corner outline is one 8 m cell in xz, already shrunk 1 m per side by the caller.
        public void Begin(IReadOnlyList<float2[]> quads, string zonePrefabName, Action<ZoneBuildReport> whenDone)
        {
            if (Busy)
            {
                Logger.Warn("Stepped Streets zoning: Begin ignored while a build is busy.");
                return;
            }

            Busy = true;
            _whenDone = whenDone;
            _emitFrame = -1;
            _applyRequested = false;
            _quads = new float2[quads?.Count ?? 0][];
            try
            {
                if (quads == null) throw new ArgumentNullException(nameof(quads));
                for (int index = 0; index < quads.Count; index++)
                {
                    var quad = quads[index];
                    if (quad == null || quad.Length != 4)
                        throw new ArgumentException($"Quad {index} must have four corners.");
                    foreach (var corner in quad)
                        if (!math.all(math.isfinite(corner)))
                            throw new ArgumentException($"Quad {index} has a non-finite corner.");
                    _quads[index] = (float2[])quad.Clone();
                }
                if (_quads.Length == 0) { Finish(0, "no quads requested"); return; }
                _zonePrefab = FindZonePrefab(zonePrefabName);
                if (_zonePrefab == Entity.Null)
                {
                    Finish(0, $"no ZonePrefab matched '{zonePrefabName}'");
                    return;
                }
                _zoneType = EntityManager.GetComponentData<ZoneData>(_zonePrefab).m_ZoneType;
                _tool.Activate();
                _activationFrame = UnityEngine.Time.frameCount;
            }
            catch (Exception exception)
            {
                Finish(0, "could not begin: " + exception.Message);
            }
        }

        private Entity FindZonePrefab(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)
                && _prefabs.TryGetPrefab(new PrefabID(nameof(ZonePrefab), name), out var exact)
                && exact is ZonePrefab && _prefabs.TryGetEntity(exact, out var exactEntity)
                && EntityManager.HasComponent<ZoneData>(exactEntity))
            {
                _zoneName = exact.name;
                return exactEntity;
            }

            Entity matching = Entity.Null;
            var names = new List<string>();
            using (var entities = _zonePrefabs.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    if (!_prefabs.TryGetPrefab<ZonePrefab>(entity, out var prefab) || prefab == null) continue;
                    names.Add(prefab.name);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (string.Equals(prefab.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        _zoneName = prefab.name;
                        return entity;
                    }
                    if (matching == Entity.Null && prefab.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matching = entity;
                        _zoneName = prefab.name;
                    }
                }
            }
            if (matching == Entity.Null && !_listedZonePrefabs)
            {
                _listedZonePrefabs = true;
                names.Sort(StringComparer.Ordinal);
                Logger.Info($"Stepped Streets zoning: available ZonePrefab names ({names.Count}):");
                foreach (var availableName in names) Logger.Info(availableName);
            }
            return matching;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!Busy) return;
            try
            {
                if (!_tool.IsActive) { _tool.Interrupted = true; Finish(0, "interrupted by a tool change; no further Apply requested"); return; }
                int frame = UnityEngine.Time.frameCount;
                if (_emitFrame < 0)
                {
                    if (_tool.Ready) Emit(frame);
                    else if (frame - _activationFrame >= 8) Finish(0, "build tool did not start within eight frames");
                    return;
                }

                int elapsed = frame - _emitFrame;
                if (!_applyRequested && elapsed >= 2)
                {
                    _tool.Requested = ApplyMode.Apply;
                    _applyRequested = true;
                }
                if (elapsed >= 4) DestroyOwnDefinitions();
                // Match BuildService.Terrain.cs: +2 Apply, +4 cleanup, +40 verification, all measured from emit.
                if (elapsed >= 40)
                {
                    if (_tool.LastAppliedFrame < _emitFrame) Finish(0, "build tool never handed Apply to the game");
                    else VerifyZoning();
                }
            }
            catch (Exception exception)
            {
                Finish(0, "build failed: " + exception.Message);
            }
        }

        private void Emit(int frame)
        {
            var heights = _terrain.GetHeightData(true);
            float3 Position(float2 corner)
            {
                float height = TerrainUtils.SampleHeight(ref heights, new float3(corner.x, 0f, corner.y));
                if (!math.isfinite(height)) throw new InvalidOperationException("Terrain returned a non-finite height.");
                return new float3(corner.x, height, corner.y);
            }
            foreach (var quad in _quads)
            {
                var position = new Quad3(Position(quad[0]), Position(quad[1]), Position(quad[2]), Position(quad[3]));
                var definition = EntityManager.CreateEntity();
                _definitions.Add(definition);
                EntityManager.AddComponentData(definition, new CreationDefinition { m_Prefab = _zonePrefab });
                EntityManager.AddComponentData(definition, new Zoning
                {
                    m_Position = position,
                    // Overwrite would repaint zoning the player already chose.
                    m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee
                });
                EntityManager.AddComponent<Updated>(definition);
            }
            _emitFrame = frame;
            Logger.Info($"Stepped Streets zoning: emitted {_quads.Length} quads for '{_zoneName}'; Apply at +2, cleanup +4, verify +40 frames.");
        }

        private enum CellState { Missing, Hidden, Blocked, Empty, OtherZone, Zoned }

        private void VerifyZoning()
        {
            var states = new CellState[_quads.Length];
            var centres = new float2[_quads.Length];
            var blockedCauses = new string[_quads.Length];
            float2 low = float2.zero, high = float2.zero;
            // Each block looks only at the cell centres near it: a 6 km area has hundreds of thousands of them.
            var centreBuckets = new SteppedStreets.Core.Neighbourhood.SpatialBuckets<int>(64f);
            for (int index = 0; index < _quads.Length; index++)
            {
                var quad = _quads[index];
                centres[index] = 0.25f * (quad[0] + quad[1] + quad[2] + quad[3]);
                low = index == 0 ? centres[index] : math.min(low, centres[index]);
                high = index == 0 ? centres[index] : math.max(high, centres[index]);
                centreBuckets.Add(centres[index], centres[index], index);
            }
            // ponytail: scans the city's blocks once, skipping those that can't reach the quads; use Zones.SearchSystem if a
            // huge city still makes this slow.
            using (var entities = _zoneBlocks.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var block = EntityManager.GetComponentData<Block>(entity);
                    // A block reaches at most half its diagonal (8 m cells) from its centre.
                    float reach = math.length((float2)block.m_Size) * 4f + 1f;
                    float2 position = block.m_Position.xz;
                    if (math.any(position < low - reach) || math.any(position > high + reach)) continue;
                    var cells = EntityManager.GetBuffer<Cell>(entity, true);
                    if (cells.Length < block.m_Size.x * block.m_Size.y) continue;
                    foreach (int index in centreBuckets.Within(position, reach))
                    {
                        var cellIndex = ZoneUtils.GetCellIndex(block, centres[index]);
                        if (math.any(cellIndex < 0) || math.any(cellIndex >= block.m_Size)) continue;
                        var cell = cells[cellIndex.x + cellIndex.y * block.m_Size.x];
                        var state = (cell.m_State & CellFlags.Blocked) != 0 ? CellState.Blocked
                            : (cell.m_State & CellFlags.Visible) == 0 ? CellState.Hidden
                            : cell.m_Zone.Equals(_zoneType) ? CellState.Zoned
                            : cell.m_Zone.Equals(ZoneType.None) ? CellState.Empty : CellState.OtherZone;
                        if (state <= states[index]) continue;
                        states[index] = state;
                        if (state == CellState.Blocked) blockedCauses[index] = BlockedCause(block, cells, cellIndex);
                    }
                }
            }
            var counts = new int[6];
            var causes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < states.Length; index++)
            {
                counts[(int)states[index]]++;
                if (states[index] != CellState.Blocked) continue;
                string cause = blockedCauses[index];
                causes.TryGetValue(cause, out int count);
                causes[cause] = count + 1;
            }
            var details = new List<string>();
            foreach (var cause in causes) details.Add($"{cause.Key} x{cause.Value}");
            Finish(counts[(int)CellState.Zoned], $"zone '{_zoneName}'; empty {counts[(int)CellState.Empty]}, "
                + $"other zone kept {counts[(int)CellState.OtherZone]}, blocked {counts[(int)CellState.Blocked]}, "
                + $"hidden {counts[(int)CellState.Hidden]}, not found {counts[(int)CellState.Missing]}"
                + (details.Count == 0 ? "" : "; " + string.Join("; ", details)));
        }

        private static string BlockedCause(Block block, DynamicBuffer<Cell> cells, int2 cellIndex)
        {
            int Depth(int column)
            {
                if (column < 0 || column >= block.m_Size.x) return -1;
                int depth = 0;
                while (depth < block.m_Size.y && (cells[column + depth * block.m_Size.x].m_State & CellFlags.Blocked) == 0) depth++;
                return depth;
            }
            // ponytail: M1 reports depth hints only; add CellBlockJobs obstruction queries when a proven cause is needed.
            return $"possibly depth rule (lone columns can be cut to 0) or city net: column {cellIndex.x}, row {cellIndex.y}, "
                + $"depths left {Depth(cellIndex.x - 1)} / here {Depth(cellIndex.x)} / right {Depth(cellIndex.x + 1)} (-1 is block edge)";
        }

        private void DestroyOwnDefinitions()
        {
            foreach (var definition in _definitions)
                if (EntityManager.Exists(definition) && !EntityManager.HasComponent<Deleted>(definition))
                    EntityManager.AddComponent<Deleted>(definition);
            _definitions.Clear();
        }

        private void Finish(int zoned, string details)
        {
            int requested = _quads?.Length ?? 0;
            try { DestroyOwnDefinitions(); }
            catch (Exception exception) { Logger.Warn("Stepped Streets zoning cleanup failed: " + exception.Message); }
            try { _tool.Restore(); }
            catch (Exception exception) { Logger.Warn("Stepped Streets could not restore the previous tool: " + exception.Message); }
            var report = new ZoneBuildReport
            {
                Requested = requested,
                Zoned = zoned,
                Summary = $"Stepped Streets zoning: zoned {zoned} of {requested} quads; {details}."
            };
            var whenDone = _whenDone;
            _whenDone = null;
            _quads = null;
            Busy = false;
            if (zoned == requested) Logger.Info(report.Summary); else Logger.Warn(report.Summary);
            try { whenDone?.Invoke(report); }
            catch (Exception exception) { Logger.Warn("Stepped Streets zoning completion callback failed: " + exception.Message); }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Busy) Finish(0, "system destroyed before verification");
            base.OnDestroy();
        }
    }
}
