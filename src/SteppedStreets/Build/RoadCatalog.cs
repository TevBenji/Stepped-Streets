// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace SteppedStreets.Build
{
    /// <summary>The game's road prefabs a neighbourhood can use: ground roads that allow zoning, not highways.</summary>
    internal static class RoadCatalog
    {
        internal sealed class Road
        {
            public string Name;
            public string Label;
            public Entity Prefab;
            public float HalfWidth;
        }

        public static List<Road> List(EntityManager entities, PrefabSystem prefabs, Func<string, string> localise)
        {
            var roads = new List<Road>();
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<RoadData>(), ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<PrefabData>()))
            using (var list = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in list)
                {
                    var road = entities.GetComponentData<RoadData>(entity);
                    var geometry = entities.GetComponentData<NetGeometryData>(entity);
                    if ((road.m_Flags & Game.Prefabs.RoadFlags.EnableZoning) == 0 || (road.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0) continue;
                    if ((geometry.m_Flags & GeometryFlags.RequireElevated) != 0 || !(geometry.m_DefaultWidth > 0f)) continue;
                    if (!prefabs.TryGetPrefab<PrefabBase>(entities.GetComponentData<PrefabData>(entity), out var prefab) || prefab == null) continue;
                    string name = prefab.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    roads.Add(new Road { Name = name, Label = localise?.Invoke(name) ?? name, Prefab = entity, HalfWidth = geometry.m_DefaultWidth * 0.5f });
                }
            roads.Sort((first, second) => first.HalfWidth != second.HalfWidth ? first.HalfWidth.CompareTo(second.HalfWidth)
                : string.CompareOrdinal(first.Name, second.Name));
            return roads;
        }

        /// <summary>The road with this name, else the first whose name contains it, else the narrowest road; null when none exists.</summary>
        public static Road Find(EntityManager entities, PrefabSystem prefabs, string name)
        {
            var roads = List(entities, prefabs, null);
            if (roads.Count == 0) return null;
            foreach (var road in roads) if (string.Equals(road.Name, name, StringComparison.OrdinalIgnoreCase)) return road;
            if (!string.IsNullOrWhiteSpace(name))
                foreach (var road in roads) if (road.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return road;
            return roads[0];
        }

        /// <summary>The game's plain footpath, Pavement Path, else its narrowest footpath; Entity.Null when none exists.</summary>
        /// <remarks>
        /// The footpath list follows Footbridge Builder's PathCatalog (TevBenji, GPL-3.0): pathway prefabs that are no bridge,
        /// not invisible (one left as a dead end crashed the game) and not for bikes only.
        /// </remarks>
        public static Entity FindPath(EntityManager entities, PrefabSystem prefabs)
        {
            Entity best = Entity.Null;
            float narrowest = float.MaxValue;
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<PathwayData>(), ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<PlaceableNetData>(), ComponentType.ReadOnly<PrefabData>()))
            using (var list = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in list)
                {
                    if (entities.HasComponent<BridgeData>(entity)) continue;
                    if (!prefabs.TryGetPrefab<PrefabBase>(entities.GetComponentData<PrefabData>(entity), out var prefab) || prefab == null) continue;
                    string name = prefab.name ?? "";
                    if (name == PathName) return entity;
                    if (name.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0 || name.EndsWith("BikePath", StringComparison.OrdinalIgnoreCase)) continue;
                    float width = entities.GetComponentData<NetGeometryData>(entity).m_DefaultWidth;
                    if (width > 0f && width < narrowest) { narrowest = width; best = entity; }
                }
            return best;
        }

        public const string PathName = "Pavement Path";
    }
}
