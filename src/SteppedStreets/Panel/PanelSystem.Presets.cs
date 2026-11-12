// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.Settings;
using SteppedStreets.Core;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Settings;
using SteppedStreets.Tool;

namespace SteppedStreets.Panel
{
    /// <summary>
    /// Presets: every plan setting, the crosswalks and the street trees, saved under a name and put back in one go; never the
    /// panel's fold or place. A loaded value is checked as its own trigger would check it, and zones, services and roads the
    /// game no longer has fall back as saved settings do.
    /// </summary>
    public sealed partial class PanelSystem
    {
        private static readonly HashSet<string> NotInPresets = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SteppedStreetsSettings.PanelCollapsed), nameof(SteppedStreetsSettings.PanelLeft), nameof(SteppedStreetsSettings.PanelTop),
            nameof(SteppedStreetsSettings.Presets),
        };

        private void AddPresetTriggers()
        {
            Trigger<string>("savePreset", name => SavePreset(name));
            Trigger<string>("loadPreset", name => LoadPreset(name));
            Trigger<string>("deletePreset", name => DeletePreset(name));
        }

        /// <summary>The settings a preset holds: the saved values of the settings class, settings added later included.</summary>
        internal static IEnumerable<PropertyInfo> PresetProperties() =>
            typeof(SteppedStreetsSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                    && (property.PropertyType.IsPrimitive || property.PropertyType.IsEnum || property.PropertyType == typeof(string))
                    && !NotInPresets.Contains(property.Name));

        internal bool SavePreset(string name)
        {
            var settings = Mod.Settings;
            if (settings == null) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in PresetProperties())
            {
                object value = property.GetValue(settings);
                values[property.Name] = value is float number ? number.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
            var presets = PresetList.Parse(settings.Presets);
            if (!PresetList.Save(presets, name, values)) return false;
            SavePresets(settings, presets);
            return true;
        }

        internal bool DeletePreset(string name)
        {
            var settings = Mod.Settings;
            if (settings == null) return false;
            var presets = PresetList.Parse(settings.Presets);
            if (!PresetList.Delete(presets, name)) return false;
            SavePresets(settings, presets);
            return true;
        }

        /// <summary>Puts a preset's settings back, saves them and plans the area again; refused while building, like any setting.</summary>
        internal bool LoadPreset(string name)
        {
            var settings = Mod.Settings;
            if (settings == null || _tool.State.Stage == ToolStage.Building) return false;
            var preset = PresetList.Find(PresetList.Parse(settings.Presets), name);
            if (preset == null) return false;
            foreach (var property in PresetProperties())
                if (preset.Values.TryGetValue(property.Name, out var text) && Read(property, text, out object value)) property.SetValue(settings, value);
            // The steep-ground limits follow the loaded switch, as the panel's triggers keep them.
            settings.SteepestBuildablePercent = Math.Min(settings.SteepestBuildablePercent, settings.SteepestBuildableLimit());
            settings.EarthworkLimit = Math.Min(settings.EarthworkLimit, settings.EarthworkCeiling());
            settings.ServiceMix = ServiceRequest.WriteList(ServiceRequest.ParseList(settings.ServiceMix)
                .Where(service => _services.Count == 0 || _services.Contains(service.Name)));
            if (!ZoneOrMix(settings.CornerZone)) settings.CornerZone = "";
            if (!ZoneOrMix(settings.MainStreetZone)) settings.MainStreetZone = "";
            // ponytail: RepairZoneMix saves and re-plans itself when it drops a zone, so that case plans twice; harmless, as
            // the second plan replaces the first.
            if (_zonesBuilt) RepairZoneMix();
            try { settings.ApplyAndSave(); }
            finally { _tool.SettingsChanged(); }
            return true;
        }

        /// <summary>One saved value as its setting's type, kept within the setting's slider; false when it doesn't read.</summary>
        private static bool Read(PropertyInfo property, string text, out object value)
        {
            value = null;
            var type = property.PropertyType;
            if (type == typeof(string))
            {
                // A road must be named, and every text stays the size its trigger allows.
                int longest = property.Name == nameof(SteppedStreetsSettings.RoadName) || property.Name == nameof(SteppedStreetsSettings.MainRoadName) ? 200 : 4000;
                if (text.Length > longest || property.Name == nameof(SteppedStreetsSettings.RoadName) && string.IsNullOrWhiteSpace(text)) return false;
                value = text;
                return true;
            }
            if (type == typeof(bool))
            {
                if (!bool.TryParse(text, out bool flag)) return false;
                value = flag;
                return true;
            }
            if (type.IsEnum)
            {
                // Names only: a number would parse to any value.
                if (text.Length == 0 || char.IsDigit(text[0]) || text[0] == '-') return false;
                try { value = Enum.Parse(type, text); }
                catch (ArgumentException) { return false; }
                return Enum.IsDefined(type, value);
            }
            if (type != typeof(int) && type != typeof(float)) return false;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number)) return false;
            var slider = property.GetCustomAttribute<SettingsUISliderAttribute>();
            if (slider != null)
            {
                number = Math.Max(slider.min, Math.Min(slider.max, number));
                if (slider.step > 0f) number = slider.min + Math.Round((number - slider.min) / slider.step) * slider.step;
            }
            value = type == typeof(int) ? (object)(int)Math.Round(number, MidpointRounding.AwayFromZero) : (float)number;
            return true;
        }

        private void SavePresets(SteppedStreetsSettings settings, List<Preset> presets)
        {
            settings.Presets = PresetList.Write(presets);
            settings.ApplyAndSave();
            // The panel state isn't rebuilt for a hidden setting: this publishes the new list.
            _published = false;
        }

        private static string PresetNamesJson(SteppedStreetsSettings settings)
        {
            var json = new StringBuilder("[");
            foreach (var preset in PresetList.Parse(settings.Presets))
            {
                if (json.Length > 1) json.Append(',');
                String(json, preset.Name);
            }
            return json.Append(']').ToString();
        }

        /// <summary>Saved corner and main-street zones the game no longer has (an asset pack removed) go back to the mix.</summary>
        private void RepairPositionZones()
        {
            var settings = Mod.Settings;
            if (settings == null || ZoneOrMix(settings.CornerZone) && ZoneOrMix(settings.MainStreetZone)) return;
            if (!ZoneOrMix(settings.CornerZone)) settings.CornerZone = "";
            if (!ZoneOrMix(settings.MainStreetZone)) settings.MainStreetZone = "";
            Mod.Logger.Info("Stepped Streets: a saved corner or main-street zone isn't in the game now; it takes the mix.");
            try { settings.ApplyAndSave(); }
            finally { _tool.SettingsChanged(); }
        }
    }
}
