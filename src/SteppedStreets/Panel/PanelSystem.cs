// Placemaker — UI bindings, exactly as UI/src/contract.ts defines them (group "Placemaker").
//
// Six values, each a ValueBinding<string> carrying JSON (never string[] — that broke mod load in
// PLT; RESEARCH-NOTES rule 22). The UI parses them (JSON.parse). Values are rebuilt only when
// their inputs changed (version stamps) and pushed only when the JSON actually differs
// (ValueBinding compares) — not ~100 bindings per frame like GRG.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one Stepped Streets state value for the neighbourhood tool, validated settings triggers, and one-time zone and road catalogues.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using SteppedStreets.Build;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Settings;
using SteppedStreets.Tool;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SteppedStreets.Panel
{
    public sealed partial class PanelSystem : UISystemBase
    {
        private const string Group = "SteppedStreets";
        private NeighbourhoodToolSystem _tool;
        private PrefabSystem _prefabs;
        private ImageSystem _images;
        private readonly Dictionary<string, string> _zoneIcons = new Dictionary<string, string>(StringComparer.Ordinal);
        private EntityQuery _zoneQuery;
        private ValueBinding<string> _state;
        private readonly SortedDictionary<string, string> _zones = new SortedDictionary<string, string>(StringComparer.Ordinal);
        private string _zonesJson = "[]", _roadsJson = "[]", _servicesJson = "[]";
        private readonly HashSet<string> _services = new HashSet<string>(StringComparer.Ordinal);
        private bool _servicesBuilt;
        private bool _zonesBuilt, _roadsBuilt, _published, _updateFailed;
        private int _version;
        private string _settingsJson = "";

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                _tool = World.GetOrCreateSystemManaged<NeighbourhoodToolSystem>();
                _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
                _images = World.GetOrCreateSystemManaged<ImageSystem>();
                _zoneQuery = GetEntityQuery(ComponentType.ReadOnly<ZoneData>(), ComponentType.ReadOnly<PrefabData>());
                AddBinding(_state = new ValueBinding<string>(Group, "state", "{}"));
                Trigger("toggleTool", () => _tool.Toggle());
                Trigger("close", () => _tool.Close());
                Trigger("clearArea", () => _tool.ClearArea());
                Trigger("closeArea", () => _tool.CloseArea());
                Trigger("undo", () => _tool.Undo());
                Trigger("build", () => { if (_tool.State.Stage == ToolStage.Planned) _tool.Build(); });
                Trigger("reroll", () => _tool.Reroll());
                Trigger<string>("setStyle", value => ChangeSettings(settings =>
                {
                    if (!Enum.TryParse(value, out NeighbourhoodStyle style) || !Enum.IsDefined(typeof(NeighbourhoodStyle), style)) return false;
                    settings.Style = style;
                    return true;
                }));
                Trigger<string>("setJunctions", value => ChangeSettings(settings =>
                {
                    if (!Enum.TryParse(value, out JunctionPattern pattern) || !Enum.IsDefined(typeof(JunctionPattern), pattern)) return false;
                    settings.Junctions = pattern;
                    return true;
                }));
                Trigger<string>("setMixPattern", value => ChangeSettings(settings =>
                {
                    if (!Enum.TryParse(value, out MixPattern pattern) || !Enum.IsDefined(typeof(MixPattern), pattern)) return false;
                    settings.MixPattern = pattern;
                    return true;
                }));
                Trigger<string>("setRoad", value => ChangeSettings(settings =>
                {
                    if (string.IsNullOrWhiteSpace(value) || value.Length > 200) return false;
                    settings.RoadName = value;
                    return true;
                }));
                Trigger<string>("setZoneMix", value => ChangeSettings(settings =>
                {
                    if (value == null || value.Length > 4000) return false;
                    var shares = ZoneShare.ParseList(value);
                    // Only zones the game has; the rest would zone nothing. An empty list is fine (no zoning), but a list whose
                    // every zone is unknown is refused rather than read as "no zoning".
                    int sent = shares.Count;
                    if (_zones.Count > 0) shares.RemoveAll(share => !_zones.ContainsKey(share.Zone));
                    if (sent > 0 && shares.Count == 0) return false;
                    settings.ZoneMix = ZoneShare.WriteList(shares);
                    return true;
                }));
                Trigger<string>("setServiceMix", value => ChangeSettings(settings =>
                {
                    if (value == null || value.Length > 4000) return false;
                    var services = ServiceRequest.ParseList(value);
                    // Only buildings the game offers; an empty list is fine (no services).
                    if (_services.Count > 0) services.RemoveAll(service => !_services.Contains(service.Name));
                    settings.ServiceMix = ServiceRequest.WriteList(services);
                    return true;
                }));
                NumberTrigger("setBlockDepth", 60, 200, (settings, value) => settings.BlockDepth = (int)Math.Round(value / 5.0) * 5);
                NumberTrigger("setMaxRoadGrade", 4, 12, (settings, value) => settings.MaxRoadGradePercent = Whole(value));
                NumberTrigger("setSteepestBuildable", 8, 60, (settings, value) => settings.SteepestBuildablePercent = Math.Min(Whole(value), settings.SteepestBuildableLimit()));
                NumberTrigger("setLotWidth", 2, 6, (settings, value) => settings.LotWidthCells = Whole(value));
                NumberTrigger("setLotDepth", 2, 6, (settings, value) => settings.DepthCells = Whole(value));
                NumberTrigger("setEarthworkLimit", 1, 12, (settings, value) => settings.EarthworkLimit = Math.Min((float)(Math.Round(value * 2.0) / 2.0), settings.EarthworkCeiling()));
                NumberTrigger("setBankGrade", 20, 50, (settings, value) => settings.BankGrade = (float)(Math.Round(value / 5.0) * 5.0 / 100.0));
                NumberTrigger("setLotSpacing", -1, 2, (settings, value) => settings.LotSpacing = Whole(value));
                NumberTrigger("setTreeDensity", 10, 100, (settings, value) => settings.TreeDensityPercent = (int)Math.Round(value / 5.0) * 5);
                NumberTrigger("setSeed", 1, 99999, (settings, value) => settings.Seed = Whole(value));
                BoolTrigger("setLevelLots", (settings, value) => settings.LevelLots = value);
                BoolTrigger("setRetainingWalls", (settings, value) => settings.RetainingWalls = value);
                BoolTrigger("setTrees", (settings, value) => settings.Trees = value);
                BoolTrigger("setSteepGround", (settings, value) => settings.SetSteepGround(value));
                BoolTrigger("setShapeGround", (settings, value) => settings.ShapeGround = value);
            }
            catch (Exception exception)
            {
                Enabled = false;
                Mod.Logger.Error(exception, "Stepped Streets: panel initialization failed");
            }
        }

        private static int Whole(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        private void Trigger(string name, Action action) =>
            AddBinding(new TriggerBinding(Group, name, () => Safe(name, action)));

        private void Trigger<T>(string name, Action<T> action) =>
            AddBinding(new TriggerBinding<T>(Group, name, value => Safe(name, () => action(value))));

        private static void Safe(string name, Action action)
        {
            try { action(); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets: panel trigger '" + name + "' failed"); }
        }

        private void NumberTrigger(string name, double minimum, double maximum, Action<SteppedStreetsSettings, double> write)
        {
            Trigger<double>(name, value => ChangeSettings(settings =>
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return false;
                write(settings, Math.Max(minimum, Math.Min(maximum, value)));
                return true;
            }));
        }

        private void BoolTrigger(string name, Action<SteppedStreetsSettings, bool> write) =>
            Trigger<bool>(name, value => ChangeSettings(settings => { write(settings, value); return true; }));

        private void ChangeSettings(Func<SteppedStreetsSettings, bool> write)
        {
            var settings = Mod.Settings;
            if (settings == null || _tool.State.Stage == ToolStage.Building || !write(settings)) return;
            // Replanning still happens if saving fails, so the visible settings never leave a stale plan buildable.
            try { settings.ApplyAndSave(); }
            finally { _tool.SettingsChanged(); }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                base.OnUpdate();
                var settings = Mod.Settings;
                if (settings == null || _state == null || _tool == null) return;
                bool catalogueChanged = BuildZones() | BuildRoads() | BuildServices();
                var state = _tool.State;
                bool current = _published && !catalogueChanged && _version == state.Version;
                // The panel is hidden while the tool is closed and opening it moves the version, so until then there is nothing to rebuild.
                if (current && state.Stage == ToolStage.Closed) return;
                string settingsJson = SettingsJson(settings);
                if (current && settingsJson == _settingsJson) return;
                _state.Update(StateJson(state, settingsJson));
                _version = state.Version;
                _settingsJson = settingsJson;
                _published = true;
                _updateFailed = false;
            }
            catch (Exception exception)
            {
                // A repeated failure must remain visible in the log without flooding it every frame.
                if (!_updateFailed) Mod.Logger.Error(exception, "Stepped Streets: panel update failed");
                _updateFailed = true;
            }
        }

        private string Localised(string name) => Localised("Assets.NAME[" + name + "]", name);

        internal static string Localised(string key, string fallback)
        {
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            return dictionary != null && dictionary.TryGetValue(key, out var localised) && !string.IsNullOrEmpty(localised) ? localised : fallback;
        }

        /// <summary>
        /// The service buildings, read again each time the tool opens: milestones unlock more as the city grows.
        /// </summary>
        private bool BuildServices()
        {
            bool open = _tool.State.Stage != ToolStage.Closed;
            if (!open) { _servicesBuilt = false; return false; }
            if (_servicesBuilt) return false;
            _servicesBuilt = true;
            var buildings = ServiceCatalog.List(EntityManager, _prefabs, Localised);
            _services.Clear();
            var json = new StringBuilder("[");
            foreach (var building in buildings)
            {
                if (!_services.Add(building.Name)) continue;
                if (json.Length > 1) json.Append(',');
                json.Append("{\"name\":"); String(json, building.Name);
                json.Append(",\"label\":"); String(json, building.Label);
                json.Append(",\"group\":"); String(json, building.Group);
                Icon(json, building.Prefab);
                json.Append(",\"width\":").Append(Integer(building.WidthCells));
                json.Append(",\"depth\":").Append(Integer(building.DepthCells)).Append('}');
            }
            string next = json.Append(']').ToString();
            if (next == _servicesJson) return false;
            _servicesJson = next;
            return true;
        }

        /// <summary>The game's own menu icon for a prefab (its group's when it has none), or "" when there is neither.</summary>
        private string IconOf(Entity prefab)
        {
            try { return _images?.GetIconOrGroupIcon(prefab) ?? ""; }
            catch (Exception exception) { Mod.Logger.Warn("Stepped Streets: reading an icon failed: " + exception.Message); return ""; }
        }

        private void Icon(StringBuilder json, Entity prefab)
        {
            string icon = IconOf(prefab);
            if (string.IsNullOrEmpty(icon)) return;
            json.Append(",\"icon\":"); String(json, icon);
        }

        private bool BuildZones()
        {
            if (_zonesBuilt || _zoneQuery.CalculateEntityCount() == 0) return false;
            using (var entities = _zoneQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    // "Unzoned" is a zone prefab too, but nothing grows on it.
                    if (EntityManager.GetComponentData<ZoneData>(entity).m_AreaType == Game.Zones.AreaType.None) continue;
                    var prefab = _prefabs.GetPrefab<ZonePrefab>(EntityManager.GetComponentData<PrefabData>(entity));
                    if (prefab == null || string.IsNullOrEmpty(prefab.name)) continue;
                    _zones[prefab.name] = Localised(prefab.name);
                    _zoneIcons[prefab.name] = IconOf(entity);
                }
            if (_zones.Count == 0) return false;
            var json = new StringBuilder("[");
            foreach (var zone in _zones)
            {
                if (json.Length > 1) json.Append(',');
                json.Append("{\"name\":"); String(json, zone.Key);
                json.Append(",\"label\":"); String(json, zone.Value);
                if (_zoneIcons.TryGetValue(zone.Key, out var icon) && !string.IsNullOrEmpty(icon)) { json.Append(",\"icon\":"); String(json, icon); }
                json.Append('}');
            }
            _zonesJson = json.Append(']').ToString();
            _zonesBuilt = true;
            RepairZoneMix();
            return true;
        }

        /// <summary>
        /// A saved mix may name zones the game doesn't have or can't grow (an earlier version offered Unzoned, or an asset pack
        /// was removed): those rows go, and a mix left empty by that falls back to Residential Low. A mix the player emptied
        /// on purpose (no zoning) has no rows to remove and stays empty.
        /// </summary>
        private void RepairZoneMix()
        {
            var settings = Mod.Settings;
            if (settings == null) return;
            var shares = settings.ZoneShares();
            int before = shares.Count;
            shares.RemoveAll(share => !_zones.ContainsKey(share.Zone));
            if (shares.Count == before) return;
            if (shares.Count == 0)
            {
                string fallback = _zones.ContainsKey("Residential Low") ? "Residential Low"
                    : _zones.Keys.FirstOrDefault(name => name.IndexOf("Residential", StringComparison.OrdinalIgnoreCase) >= 0) ?? _zones.Keys.First();
                shares.Add(new ZoneShare { Zone = fallback, Share = 100f });
            }
            Mod.Logger.Info("Stepped Streets: the saved zone mix named zones that can't grow; now " + ZoneShare.WriteList(shares));
            settings.ZoneMix = ZoneShare.WriteList(shares);
            try { settings.ApplyAndSave(); }
            finally { _tool.SettingsChanged(); }
        }

        private bool BuildRoads()
        {
            if (_roadsBuilt) return false;
            var roads = RoadCatalog.List(EntityManager, _prefabs, Localised);
            if (roads.Count == 0) return false;
            var json = new StringBuilder("[");
            foreach (var road in roads)
            {
                if (json.Length > 1) json.Append(',');
                json.Append("{\"name\":"); String(json, road.Name);
                json.Append(",\"label\":"); String(json, road.Label);
                json.Append('}');
            }
            _roadsJson = json.Append(']').ToString();
            _roadsBuilt = true;
            return true;
        }

        // ponytail: rebuilt every frame (a few hundred bytes; the zone mix parse is cached); add a settings version if it shows up.
        private static string SettingsJson(SteppedStreetsSettings settings)
        {
            var json = new StringBuilder("{\"style\":");
            String(json, settings.Style.ToString());
            json.Append(",\"road\":"); String(json, settings.RoadName);
            json.Append(",\"blockDepth\":").Append(Integer(settings.BlockDepth));
            json.Append(",\"maxRoadGradePercent\":").Append(Integer(settings.MaxRoadGradePercent));
            json.Append(",\"steepestBuildablePercent\":").Append(Integer(settings.SteepestBuildablePercent));
            json.Append(",\"steepGround\":").Append(settings.SteepGround ? "true" : "false");
            json.Append(",\"shapeGround\":").Append(settings.ShapeGround ? "true" : "false");
            json.Append(",\"junctions\":"); String(json, settings.Junctions.ToString());
            json.Append(",\"zoneMix\":[");
            var shares = settings.ZoneShares();
            for (int index = 0; index < shares.Count; index++)
            {
                if (index > 0) json.Append(',');
                json.Append("{\"zone\":"); String(json, shares[index].Zone);
                json.Append(",\"share\":").Append(Number(shares[index].Share)).Append('}');
            }
            json.Append("],\"serviceMix\":[");
            var services = ServiceRequest.ParseList(settings.ServiceMix);
            for (int index = 0; index < services.Count; index++)
            {
                if (index > 0) json.Append(',');
                json.Append("{\"name\":"); String(json, services[index].Name);
                json.Append(",\"count\":").Append(Integer(services[index].Count)).Append('}');
            }
            json.Append("],\"mixPattern\":"); String(json, settings.MixPattern.ToString());
            json.Append(",\"lotWidthCells\":").Append(Integer(settings.LotWidthCells));
            json.Append(",\"lotDepthCells\":").Append(Integer(settings.DepthCells));
            json.Append(",\"levelLots\":").Append(settings.LevelLots ? "true" : "false");
            json.Append(",\"earthworkLimit\":").Append(Number(settings.EarthworkLimit));
            json.Append(",\"bankGradePercent\":").Append(Integer((int)Math.Round(settings.BankGrade * 100f)));
            json.Append(",\"lotSpacing\":").Append(Integer(settings.LotSpacing));
            json.Append(",\"retainingWalls\":").Append(settings.RetainingWalls ? "true" : "false");
            json.Append(",\"trees\":").Append(settings.Trees ? "true" : "false");
            json.Append(",\"treeDensityPercent\":").Append(Integer(settings.TreeDensityPercent));
            json.Append(",\"seed\":").Append(Integer(settings.Seed));
            return json.Append('}').ToString();
        }

        private string StateJson(ToolState state, string settingsJson)
        {
            var json = new StringBuilder("{\"stage\":");
            String(json, state.Stage.ToString());
            json.Append(",\"corners\":").Append(Integer(state.Corners));
            json.Append(",\"areaClosed\":").Append(state.AreaClosed ? "true" : "false");
            json.Append(",\"areaSquareMetres\":").Append(Number(state.AreaSquareMetres));
            json.Append(",\"summary\":"); String(json, state.Summary);
            json.Append(",\"messages\":[");
            var messages = state.Messages ?? Array.Empty<string>();
            for (int index = 0; index < messages.Length; index++)
            {
                if (index > 0) json.Append(',');
                String(json, messages[index]);
            }
            json.Append("],\"stats\":{\"roadKilometres\":").Append(Number(state.RoadKilometres));
            json.Append(",\"lots\":").Append(Integer(state.Lots));
            json.Append(",\"zonedCells\":").Append(Integer(state.ZonedCells));
            json.Append(",\"earthCubicMetres\":").Append(Number(state.EarthCubicMetres));
            json.Append(",\"steepestRoadPercent\":").Append(Number(state.SteepestRoadPercent));
            json.Append(",\"wildPercent\":").Append(Number(state.WildPercent));
            json.Append(",\"connected\":").Append(state.Connected ? "true" : "false");
            json.Append(",\"trees\":").Append(Integer(state.Trees));
            json.Append("},\"zoneCounts\":[");
            for (int index = 0; index < state.ZoneCounts.Count; index++)
            {
                if (index > 0) json.Append(',');
                json.Append("{\"zone\":"); String(json, state.ZoneCounts[index].Key);
                json.Append(",\"lots\":").Append(Integer(state.ZoneCounts[index].Value)).Append('}');
            }
            json.Append("],\"serviceCounts\":[");
            for (int index = 0; index < state.ServiceCounts.Count; index++)
            {
                if (index > 0) json.Append(',');
                json.Append("{\"name\":"); String(json, state.ServiceCounts[index].Key);
                json.Append(",\"count\":").Append(Integer(state.ServiceCounts[index].Value)).Append('}');
            }
            json.Append("],\"progress\":").Append(Number(state.Progress));
            json.Append(",\"settings\":").Append(settingsJson);
            return json.Append(",\"zones\":").Append(_zonesJson).Append(",\"roads\":").Append(_roadsJson)
                .Append(",\"services\":").Append(_servicesJson).Append('}').ToString();
        }

        private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Number(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? "null" : value.ToString("R", CultureInfo.InvariantCulture);

        private static void String(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char character in value ?? "")
            {
                switch (character)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    default:
                        if (character < 32 || char.IsSurrogate(character) || character == '\u2028' || character == '\u2029')
                            json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else json.Append(character);
                        break;
                }
            }
            json.Append('"');
        }
    }
}
