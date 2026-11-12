// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SteppedStreets.Core
{
    /// <summary>A named set of saved settings: each setting's name and its value as invariant text.</summary>
    public sealed class Preset
    {
        public string Name = "";
        public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The player's presets, kept as one JSON list: [{"name":"..","Style":"..",..},..], every value a string. Reading is
    /// lenient: it keeps what it read before anything broken, so a damaged setting loses only what follows the damage.
    /// </summary>
    public static class PresetList
    {
        public const int Most = 20;
        public const int LongestName = 40;

        // The preset's own field; settings are named in PascalCase, so none can take it.
        private const string NameField = "name";

        /// <summary>The name trimmed, or null when that leaves nothing or more than <see cref="LongestName"/> characters.</summary>
        public static string CleanName(string name)
        {
            string trimmed = (name ?? "").Trim();
            return trimmed.Length >= 1 && trimmed.Length <= LongestName ? trimmed : null;
        }

        public static Preset Find(List<Preset> presets, string name)
        {
            string clean = CleanName(name);
            return clean == null ? null : presets.Find(preset => string.Equals(preset.Name, clean, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Saves the values under the name, over a preset of the same name in any case (keeping its place), else at the end.
        /// False when the name is unusable or the list is full.
        /// </summary>
        public static bool Save(List<Preset> presets, string name, IDictionary<string, string> values)
        {
            string clean = CleanName(name);
            if (clean == null || values == null) return false;
            var preset = Find(presets, clean);
            if (preset == null)
            {
                if (presets.Count >= Most) return false;
                presets.Add(preset = new Preset());
            }
            preset.Name = clean;
            preset.Values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var value in values)
                if (!string.IsNullOrEmpty(value.Key) && value.Key != NameField && value.Value != null) preset.Values[value.Key] = value.Value;
            return true;
        }

        public static bool Delete(List<Preset> presets, string name)
        {
            var preset = Find(presets, name);
            return preset != null && presets.Remove(preset);
        }

        public static List<Preset> Parse(string json)
        {
            var presets = new List<Preset>();
            var reader = new Reader(json);
            if (!reader.Take('[')) return presets;
            for (int entry = 0; !reader.Take(']'); entry++)
            {
                if (entry > 0 && !reader.Take(',')) break;
                if (!reader.Take('{')) break;
                var preset = new Preset();
                string name = null;
                bool whole = reader.Take('}');
                while (!whole)
                {
                    string key = reader.String();
                    string value = key != null && reader.Take(':') ? reader.String() : null;
                    if (value == null) break;
                    if (key == NameField) name = value;
                    else preset.Values[key] = value;
                    if (reader.Take('}')) whole = true;
                    else if (!reader.Take(',')) break;
                }
                if (!whole) break;
                preset.Name = CleanName(name);
                // A preset without a usable name, or one repeating an earlier name, is left out; the list stops at Most.
                if (preset.Name != null && Find(presets, preset.Name) == null && presets.Count < Most) presets.Add(preset);
            }
            return presets;
        }

        public static string Write(IEnumerable<Preset> presets)
        {
            var json = new StringBuilder("[");
            foreach (var preset in presets)
            {
                if (json.Length > 1) json.Append(',');
                json.Append('{');
                Text(json, NameField);
                json.Append(':');
                Text(json, preset.Name);
                foreach (var value in preset.Values)
                {
                    json.Append(',');
                    Text(json, value.Key);
                    json.Append(':');
                    Text(json, value.Value);
                }
                json.Append('}');
            }
            return json.Append(']').ToString();
        }

        private static void Text(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char character in value ?? "")
            {
                if (character == '"' || character == '\\') json.Append('\\').Append(character);
                else if (character < 32) json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                else json.Append(character);
            }
            json.Append('"');
        }

        /// <summary>Just enough JSON for the list's own shape: punctuation and strings.</summary>
        private sealed class Reader
        {
            private readonly string text;
            private int at;

            public Reader(string text) { this.text = text ?? ""; }

            private void SkipSpace()
            {
                while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
            }

            public bool Take(char character)
            {
                SkipSpace();
                if (at >= text.Length || text[at] != character) return false;
                at++;
                return true;
            }

            /// <summary>The next string with its escapes undone, or null when there is none.</summary>
            public string String()
            {
                if (!Take('"')) return null;
                var value = new StringBuilder();
                while (at < text.Length)
                {
                    char character = text[at++];
                    if (character == '"') return value.ToString();
                    if (character != '\\') { value.Append(character); continue; }
                    if (at >= text.Length) return null;
                    char escaped = text[at++];
                    switch (escaped)
                    {
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'u':
                            if (at + 4 > text.Length || !int.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) return null;
                            value.Append((char)code);
                            at += 4;
                            break;
                        default: value.Append(escaped); break;
                    }
                }
                return null;
            }
        }
    }
}
