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
                    if (!Available(entities, prefabs, entity)) continue;
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

        /// <summary>The small park buildings for pocket parks, by the game's names (2.12.0 game facts).</summary>
        private static readonly string[] ParkNames =
        {
            "PocketPark01", "PocketPark02", "PocketPark03", "PocketPark04", "PocketPark05", "PocketPark06", "PocketPark07", "PocketPark08",
            "PocketPark09", "PocketPark10", "Playground01", "GymPark01", "ChirperPark01", "Playground03", "Playground05", "CityPark05",
            "DogPark01", "CityPark01",
        };

        /// <summary>
        /// The unlocked pocket parks and other small parks with their lot sizes. Read from the game's park buildings rather than
        /// <see cref="List"/>, so a park is found whatever its placement flags.
        /// </summary>
        public static List<Building> Parks(EntityManager entities, PrefabSystem prefabs, Func<string, string, string> localise)
        {
            var parks = new List<Building>();
            using (var query = entities.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkData>(), ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<PrefabData>() },
                None = new[] { ComponentType.ReadOnly<Locked>() },
            }))
            using (var list = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in list)
                {
                    if (!Available(entities, prefabs, entity)) continue;
                    if (!prefabs.TryGetPrefab<PrefabBase>(entities.GetComponentData<PrefabData>(entity), out var prefab) || prefab == null) continue;
                    string name = prefab.name;
                    if (Array.IndexOf(ParkNames, name) < 0) continue;
                    var lot = entities.GetComponentData<BuildingData>(entity).m_LotSize;
                    parks.Add(new Building
                    {
                        Name = name,
                        Label = localise?.Invoke("Assets.NAME[" + name + "]", name) ?? name,
                        Group = "",
                        Prefab = entity,
                        WidthCells = lot.x,
                        DepthCells = lot.y,
                    });
                }
            parks.Sort((first, second) => string.CompareOrdinal(first.Name, second.Name));
            return parks;
        }

        /// <summary>
        /// A building from a DLC or content pack the player doesn't have stays among the game's prefabs, but the game refuses to
        /// place it (owner's in-game test, 3 November 2026): it is left off the list, as the game's own menus grey it out.
        /// </summary>
        private static bool Available(EntityManager entities, PrefabSystem prefabs, Entity building)
        {
            var contents = new[]
            {
                entities.HasComponent<ContentPrerequisiteData>(building) ? entities.GetComponentData<ContentPrerequisiteData>(building).m_ContentPrerequisite : Entity.Null,
                entities.HasComponent<ModPrerequisiteData>(building) ? entities.GetComponentData<ModPrerequisiteData>(building).m_ContentPrerequisite : Entity.Null,
            };
            foreach (var content in contents)
                if (content != Entity.Null && entities.HasComponent<PrefabData>(content)
                    && prefabs.TryGetPrefab<ContentPrefab>(entities.GetComponentData<PrefabData>(content), out var prefab) && prefab != null
                    && !prefab.IsAvailable())
                    return false;
            return true;
        }
    }
}
