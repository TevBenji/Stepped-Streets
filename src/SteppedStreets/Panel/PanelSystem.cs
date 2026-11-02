// Placemaker — UI bindings, exactly as UI/src/contract.ts defines them (group "Placemaker").
//
// Six values, each a ValueBinding<string> carrying JSON (never string[] — that broke mod load in
// PLT; RESEARCH-NOTES rule 22). The UI parses them (JSON.parse). Values are rebuilt only when
// their inputs changed (version stamps) and pushed only when the JSON actually differs
// (ValueBinding compares) — not ~100 bindings per frame like GRG.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: one Stepped Streets state value, validated settings triggers and a one-time zone catalog.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using SteppedStreets.Core.Planning;
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
        private StepStreetToolSystem _tool;
        private PrefabSystem _prefabs;
        private EntityQuery _zoneQuery;
        private ValueBinding<string> _state;
        private readonly SortedDictionary<string, string> _zones = new SortedDictionary<string, string>(StringComparer.Ordinal);
        private string _zonesJson = "[]";
        private bool _zonesBuilt;
        private bool _published;
        private bool _updateFailed;
        private int _version;
        private SettingsSnapshot _settings;

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                _tool = World.GetOrCreateSystemManaged<StepStreetToolSystem>();
                _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
                _zoneQuery = GetEntityQuery(ComponentType.ReadOnly<ZoneData>(), ComponentType.ReadOnly<PrefabData>());
                AddBinding(_state = new ValueBinding<string>(Group, "state", "{}"));
                Trigger("toggleTool", () => _tool.Toggle());
                Trigger("close", () => _tool.Close());
                Trigger("clearSelection", () => _tool.ClearSelection());
                Trigger("build", () => { if (_tool.State.Stage == ToolStage.Planned) _tool.Build(); });
                Trigger<string>("setSide", value => ChangeSettings(settings =>
                {
                    switch (value)
                    {
                        case "Both": settings.Side = StreetSide.Both; return true;
                        case "Left": settings.Side = StreetSide.Left; return true;
                        case "Right": settings.Side = StreetSide.Right; return true;
                        default: return false;
                    }
                }));
                Trigger<string>("setZone", value => ChangeSettings(settings =>
                {
                    if (value == null || !_zones.ContainsKey(value)) return false;
                    settings.ZoneName = value;
                    return true;
                }));
                Trigger<string>("setLook", value => ChangeSettings(settings =>
                {
                    switch (value)
                    {
                        case "PlantedBank": settings.Look = StepLook.PlantedBank; return true;
                        case "LowWall": settings.Look = StepLook.LowWall; return true;
                        default: return false;
                    }
                }));
                NumberTrigger("setLotWidth", 2, 6, (settings, value) => settings.LotWidthCells = (int)Math.Round(value, MidpointRounding.AwayFromZero));
                NumberTrigger("setNarrowest", 2, 6, (settings, value) => settings.NarrowestWidthCells = (int)Math.Round(value, MidpointRounding.AwayFromZero));
                NumberTrigger("setDepth", 2, 6, (settings, value) => settings.DepthCells = (int)Math.Round(value, MidpointRounding.AwayFromZero));
                NumberTrigger("setMinimumGrade", 4, 12, (settings, value) => settings.MinimumGradePercent = (int)Math.Round(value, MidpointRounding.AwayFromZero));
                NumberTrigger("setHighestStep", 0.5, 5, (settings, value) => settings.HighestStep = (float)value);
                NumberTrigger("setBankGrade", 0.2, 0.5, (settings, value) => settings.BankGrade = (float)value);
                NumberTrigger("setEarthworkLimit", 1, 8, (settings, value) => settings.EarthworkLimit = (float)value);
            }
            catch (Exception exception)
            {
                Enabled = false;
                Mod.Logger.Error(exception, "Stepped Streets: panel initialization failed");
            }
        }

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

        private void ChangeSettings(Func<SteppedStreetsSettings, bool> write)
        {
            var settings = Mod.Settings;
            if (settings == null || _tool.State.Stage == ToolStage.Building || !write(settings)) return;
            // Replanning still happens if saving fails, so the visible settings cannot leave a stale preview buildable.
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
                bool catalogChanged = BuildZones();
                var snapshot = new SettingsSnapshot(settings);
                var state = _tool.State;
                if (_published && !catalogChanged && _version == state.Version && _settings.SameAs(snapshot)) return;
                _state.Update(StateJson(state, snapshot));
                _version = state.Version;
                _settings = snapshot;
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

        private bool BuildZones()
        {
            if (_zonesBuilt || _zoneQuery.CalculateEntityCount() == 0) return false;
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            using (var entities = _zoneQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var prefab = _prefabs.GetPrefab<ZonePrefab>(EntityManager.GetComponentData<PrefabData>(entity));
                    if (prefab == null || string.IsNullOrEmpty(prefab.name)) continue;
                    string name = prefab.name;
                    string label = name;
                    if (dictionary != null && dictionary.TryGetValue("Assets.NAME[" + name + "]", out var localized) && !string.IsNullOrEmpty(localized))
                        label = localized;
                    _zones[name] = label;
                }
            }
            if (_zones.Count == 0) return false;
            var json = new StringBuilder("[");
            foreach (var zone in _zones)
            {
                if (json.Length > 1) json.Append(',');
                json.Append("{\"name\":");
                String(json, zone.Key);
                json.Append(",\"label\":");
                String(json, zone.Value);
                json.Append('}');
            }
            _zonesJson = json.Append(']').ToString();
            _zonesBuilt = true;
            return true;
        }

        private string StateJson(ToolState state, SettingsSnapshot settings)
        {
            var json = new StringBuilder("{\"stage\":");
            String(json, state.Stage.ToString());
            json.Append(",\"streetName\":"); String(json, state.StreetName);
            json.Append(",\"hoverGradePercent\":").Append(Number(state.HoverGradePercent));
            json.Append(",\"hoverTooFlat\":").Append(state.HoverTooFlat ? "true" : "false");
            json.Append(",\"summary\":"); String(json, state.Summary);
            json.Append(",\"messages\":[");
            var messages = state.Messages ?? Array.Empty<string>();
            for (int index = 0; index < messages.Length; index++)
            {
                if (index > 0) json.Append(',');
                String(json, messages[index]);
            }
            json.Append("],\"settings\":{\"side\":"); String(json, settings.Side.ToString());
            json.Append(",\"zone\":"); String(json, settings.Zone);
            json.Append(",\"lotWidthCells\":").Append(settings.LotWidth.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"narrowestWidthCells\":").Append(settings.Narrowest.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"depthCells\":").Append(settings.Depth.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"look\":"); String(json, settings.Look.ToString());
            json.Append(",\"minimumGradePercent\":").Append(settings.MinimumGrade.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"highestStep\":").Append(Number(settings.HighestStep));
            json.Append(",\"bankGrade\":").Append(Number(settings.BankGrade));
            json.Append(",\"earthworkLimit\":").Append(Number(settings.EarthworkLimit));
            return json.Append("},\"zones\":").Append(_zonesJson).Append('}').ToString();
        }

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

        private readonly struct SettingsSnapshot
        {
            public readonly StreetSide Side;
            public readonly string Zone;
            public readonly int LotWidth, Narrowest, Depth, MinimumGrade;
            public readonly StepLook Look;
            public readonly float HighestStep, BankGrade, EarthworkLimit;

            public SettingsSnapshot(SteppedStreetsSettings settings)
            {
                Side = settings.Side; Zone = settings.ZoneName;
                LotWidth = settings.LotWidthCells; Narrowest = settings.NarrowestWidthCells;
                Depth = settings.DepthCells; MinimumGrade = settings.MinimumGradePercent;
                Look = settings.Look; HighestStep = settings.HighestStep;
                BankGrade = settings.BankGrade; EarthworkLimit = settings.EarthworkLimit;
            }

            public bool SameAs(SettingsSnapshot other) => Side == other.Side && Zone == other.Zone
                && LotWidth == other.LotWidth && Narrowest == other.Narrowest && Depth == other.Depth
                && MinimumGrade == other.MinimumGrade && Look == other.Look && HighestStep.Equals(other.HighestStep)
                && BankGrade.Equals(other.BankGrade) && EarthworkLimit.Equals(other.EarthworkLimit);
        }
    }
}
