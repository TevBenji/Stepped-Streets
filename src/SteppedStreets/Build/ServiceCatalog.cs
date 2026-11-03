// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace SteppedStreets.Build
{
    /// <summary>
    /// The service buildings a neighbourhood can mix in: the game's unlocked city-service buildings that stand beside a road on
    /// dry ground (police, fire, health, schools, parks, utilities...), not upgrades, one-per-city landmarks or harbours.
    /// </summary>
    internal static class ServiceCatalog
    {
        internal sealed class Building
        {
            public string Name;
            public string Label;
            public string Group;
            public Entity Prefab;
            public int WidthCells, DepthCells;
        }

        // Buildings that need water, a node, a net, or may stand only once in a city: not for a neighbourhood mix.
        private const PlacementFlags Excluded = PlacementFlags.Shoreline | PlacementFlags.Floating | PlacementFlags.Underwater
            | PlacementFlags.Waterway | PlacementFlags.Hovering | PlacementFlags.RoadNode | PlacementFlags.NetObject | PlacementFlags.Unique;

        public static List<Building> List(EntityManager entities, PrefabSystem prefabs, Func<string, string, string> localise)
        {
            var buildings = new List<Building>();
            using (var query = entities.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<ServiceObjectData>(),
                    ComponentType.ReadOnly<PlaceableObjectData>(), ComponentType.ReadOnly<PrefabData>() },
                None = new[] { ComponentType.ReadOnly<ServiceUpgradeData>(), ComponentType.ReadOnly<BuildingExtensionData>(),
                    ComponentType.ReadOnly<Locked>() },
            }))
            using (var list = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in list)
                {
                    var placeable = entities.GetComponentData<PlaceableObjectData>(entity);
                    if ((placeable.m_Flags & PlacementFlags.RoadSide) == 0 || (placeable.m_Flags & Excluded) != 0) continue;
                    var lot = entities.GetComponentData<BuildingData>(entity).m_LotSize;
                    if (lot.x < 1 || lot.y < 1 || lot.x > 20 || lot.y > 20) continue;
                    if (!prefabs.TryGetPrefab<PrefabBase>(entities.GetComponentData<PrefabData>(entity), out var prefab) || prefab == null) continue;
                    string name = prefab.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    string group = "";
                    var service = entities.GetComponentData<ServiceObjectData>(entity).m_Service;
                    if (service != Entity.Null && entities.HasComponent<PrefabData>(service)
                        && prefabs.TryGetPrefab<PrefabBase>(entities.GetComponentData<PrefabData>(service), out var servicePrefab) && servicePrefab != null)
                        group = localise?.Invoke("Services.NAME[" + servicePrefab.name + "]", servicePrefab.name) ?? servicePrefab.name;
                    buildings.Add(new Building
                    {
                        Name = name,
                        Label = localise?.Invoke("Assets.NAME[" + name + "]", name) ?? name,
                        Group = group,
                        Prefab = entity,
                        WidthCells = lot.x,
                        DepthCells = lot.y,
                    });
                }
            buildings.Sort((first, second) => first.Group != second.Group ? string.CompareOrdinal(first.Group, second.Group)
                : string.CompareOrdinal(first.Label, second.Label));
            return buildings;
        }
    }
}
