// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Game.Input;
using Game.Modding;
using Game.Settings;
using SteppedStreets.Core.Neighbourhood;

namespace SteppedStreets.Settings
{
    /// <summary>
    /// Options for the neighbourhood tool. Property names are save format: never rename them. LotWidthCells, NarrowestWidthCells,
    /// DepthCells, HighestStep, BankGrade, EarthworkLimit and StepStreetBinding keep their meaning from the street tool; the
    /// street tool's Side, MinimumGradePercent, ZoneName and Look are gone with it.
    /// </summary>
    [FileLocation("ModsSettings/SteppedStreets/SteppedStreets")]
    [SettingsUIKeyboardAction(StepStreetAction, ActionType.Button)]
    public sealed class SteppedStreetsSettings : ModSetting
    {
        public const string StepStreetAction = "StepStreet";
        public const string DefaultRoad = "Small Road";
        public const string DefaultZoneMix = "[{\"zone\":\"Residential Low\",\"share\":100}]";

        private const string Section = "Main";
        private const string Group = "Neighbourhood";

        public SteppedStreetsSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(Section, Group)]
        public NeighbourhoodStyle Style { get; set; }

        [SettingsUITextInput]
        [SettingsUISection(Section, Group)]
        public string RoadName { get; set; }

        [SettingsUISlider(min = 60, max = 200, step = 5, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int BlockDepth { get; set; }

        [SettingsUISlider(min = 4, max = 12, step = 1, scalarMultiplier = 1, unit = Game.UI.Unit.kPercentage)]
        [SettingsUISection(Section, Group)]
        public int MaxRoadGradePercent { get; set; }

        [SettingsUISlider(min = 8, max = 30, step = 1, scalarMultiplier = 1, unit = Game.UI.Unit.kPercentage)]
        [SettingsUISection(Section, Group)]
        public int SteepestBuildablePercent { get; set; }

        [SettingsUITextInput]
        [SettingsUISection(Section, Group)]
        public string ZoneMix { get; set; }

        [SettingsUISection(Section, Group)]
        public MixPattern MixPattern { get; set; }

        [SettingsUISlider(min = 2, max = 6, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int LotWidthCells { get; set; }

        [SettingsUISlider(min = 2, max = 6, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int NarrowestWidthCells { get; set; }

        [SettingsUISlider(min = 2, max = 6, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int DepthCells { get; set; }

        [SettingsUISection(Section, Group)]
        public bool LevelLots { get; set; }

        [SettingsUISlider(min = 1f, max = 8f, step = 0.5f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 1)]
        [SettingsUISection(Section, Group)]
        public float EarthworkLimit { get; set; }

        [SettingsUISlider(min = 0.2f, max = 0.5f, step = 0.05f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 2)]
        [SettingsUISection(Section, Group)]
        public float BankGrade { get; set; }

        [SettingsUISlider(min = 0.5f, max = 5f, step = 0.1f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 1)]
        [SettingsUISection(Section, Group)]
        public float HighestStep { get; set; }

        /// <summary>Empty cells between lots: −1 is Auto (from the ground's slope), otherwise 0, 1 or 2.</summary>
        [SettingsUISlider(min = -1, max = 2, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int LotSpacing { get; set; }

        [SettingsUISection(Section, Group)]
        public bool RetainingWalls { get; set; }

        [SettingsUISection(Section, Group)]
        public bool Trees { get; set; }

        [SettingsUISlider(min = 10, max = 100, step = 5, scalarMultiplier = 1, unit = Game.UI.Unit.kPercentage)]
        [SettingsUISection(Section, Group)]
        public int TreeDensityPercent { get; set; }

        [SettingsUISlider(min = 1, max = 99999, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int Seed { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.S, StepStreetAction, ctrl: true, shift: true)]
        [SettingsUISection(Section, Group)]
        public ProxyBinding StepStreetBinding { get; set; }

        public override void SetDefaults()
        {
            Style = NeighbourhoodStyle.ContourSuburb;
            RoadName = DefaultRoad;
            BlockDepth = 90;
            MaxRoadGradePercent = 8;
            SteepestBuildablePercent = 15;
            ZoneMix = DefaultZoneMix;
            MixPattern = MixPattern.Clumps;
            LotWidthCells = 3;
            NarrowestWidthCells = 2;
            DepthCells = 3;
            LevelLots = true;
            EarthworkLimit = 4f;
            // Placemaker's sloped car parks settled on 0.4 after curtains at 0.46.
            BankGrade = 0.4f;
            HighestStep = 3.2f;
            LotSpacing = -1;
            RetainingWalls = false;
            Trees = true;
            TreeDensityPercent = 50;
            Seed = 1;
        }

        /// <summary>The zone mix rows, read leniently: a broken string falls back to the default mix.</summary>
        public List<ZoneShare> ZoneShares() => ParseZoneMix(ZoneMix);

        public static List<ZoneShare> ParseZoneMix(string json)
        {
            var shares = new List<ZoneShare>();
            try
            {
                // ponytail: a tiny reader for [{"zone":"..","share":n},..], the only shape the panel writes; use a JSON
                // library if the mix grows more fields.
                int index = 0;
                string text = json ?? "";
                while ((index = text.IndexOf("\"zone\"", index, StringComparison.Ordinal)) >= 0)
                {
                    int open = text.IndexOf('"', text.IndexOf(':', index) + 1);
                    var zone = new StringBuilder();
                    int position = open + 1;
                    for (; position < text.Length && text[position] != '"'; position++)
                    {
                        if (text[position] == '\\' && position + 1 < text.Length) position++;
                        zone.Append(text[position]);
                    }
                    int shareKey = text.IndexOf("\"share\"", position, StringComparison.Ordinal);
                    int next = text.IndexOf("\"zone\"", position, StringComparison.Ordinal);
                    float share = 1f;
                    if (shareKey >= 0 && (next < 0 || shareKey < next))
                    {
                        int start = text.IndexOf(':', shareKey) + 1, end = start;
                        while (end < text.Length && "0123456789.-eE+ ".IndexOf(text[end]) >= 0) end++;
                        float.TryParse(text.Substring(start, end - start).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out share);
                    }
                    if (zone.Length > 0 && share > 0f && !float.IsNaN(share) && !float.IsInfinity(share))
                        shares.Add(new ZoneShare { Zone = zone.ToString(), Share = share });
                    index = position;
                }
            }
            catch (Exception exception)
            {
                Mod.Logger.Warn("Stepped Streets: the zone mix could not be read (" + exception.Message + "); using the default.");
                shares.Clear();
            }
            if (shares.Count == 0) shares.Add(new ZoneShare { Zone = "Residential Low", Share = 100f });
            return shares;
        }

        public static string WriteZoneMix(IEnumerable<ZoneShare> shares)
        {
            var json = new StringBuilder("[");
            foreach (var share in shares)
            {
                if (json.Length > 1) json.Append(',');
                json.Append("{\"zone\":\"").Append((share.Zone ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\",\"share\":").Append(share.Share.ToString("0.##", CultureInfo.InvariantCulture)).Append('}');
            }
            return json.Append(']').ToString();
        }

        /// <summary>
        /// English labels for the Options page. The game builds its label ids from type and property names, so without these
        /// the page shows the raw ids.
        /// </summary>
        public MemorySource Labels()
        {
            var labels = new Dictionary<string, string>
            {
                [GetSettingsLocaleID()] = "Stepped Streets",
                [GetOptionTabLocaleID(Section)] = "Stepped Streets",
                [GetOptionGroupLocaleID(Group)] = "Hillside neighbourhood",
                [GetBindingMapLocaleID()] = "Stepped Streets",
                [GetBindingKeyLocaleID(StepStreetAction)] = "Open Stepped Streets",
                [GetEnumValueLocaleID(NeighbourhoodStyle.ContourSuburb)] = "Contour suburb",
                [GetEnumValueLocaleID(NeighbourhoodStyle.TerracedBlocks)] = "Terraced blocks",
                [GetEnumValueLocaleID(NeighbourhoodStyle.MountainLane)] = "Mountain lane",
                [GetEnumValueLocaleID(MixPattern.Random)] = "Random",
                [GetEnumValueLocaleID(MixPattern.Clumps)] = "Clumps",
                [GetEnumValueLocaleID(MixPattern.Even)] = "Even",
            };
            void Label(string property, string label, string description)
            {
                labels[GetOptionLabelLocaleID(property)] = label;
                labels[GetOptionDescLocaleID(property)] = description;
            }

            Label(nameof(Style), "Style", "Contour suburb, terraced blocks or one winding mountain lane.");
            Label(nameof(RoadName), "Road type", "The road prefab for new streets, by name (for example Small Road).");
            Label(nameof(BlockDepth), "Block depth (m)", "Road spacing on the ground between neighbouring contour streets.");
            Label(nameof(MaxRoadGradePercent), "Max road grade", "No new road is steeper than this.");
            Label(nameof(SteepestBuildablePercent), "Steepest buildable ground", "Steeper ground gets no roads or lots and is planted as forest.");
            Label(nameof(ZoneMix), "Zone mix", "The zones and their shares, as the panel writes them.");
            Label(nameof(MixPattern), "Mix mode", "Random per lot, clumps of neighbouring lots, or an even split.");
            Label(nameof(LotWidthCells), "Lot width (cells)", "Cells along the kerb per lot.");
            Label(nameof(NarrowestWidthCells), "Narrowest width (cells)", "How far a lot may narrow on a climbing road to keep its step low.");
            Label(nameof(DepthCells), "Lot depth (cells)", "Cells back from the road.");
            Label(nameof(LevelLots), "Level the lots", "Grade each lot into a level pad with soft banks before zoning.");
            Label(nameof(EarthworkLimit), "Most earth (m)", "A lot that needs more cut or fill than this is left out.");
            Label(nameof(BankGrade), "Steepest bank", "Rise over run of the steepest bank the grading makes; 0.4 is 40%.");
            Label(nameof(HighestStep), "Highest step (m)", "The tallest step between neighbouring lots along a climbing road.");
            Label(nameof(LotSpacing), "Space between lots", "-1 decides from the slope (Auto); otherwise 0, 1 or 2 empty cells.");
            Label(nameof(RetainingWalls), "Retaining walls", "The game's garden wall where the cut beside a road is tall enough.");
            Label(nameof(Trees), "Trees", "Forest on steep ground, trees on banks and in gaps.");
            Label(nameof(TreeDensityPercent), "Tree density", "How many trees to plant.");
            Label(nameof(Seed), "Seed", "The same seed gives the same layout; Reroll picks another.");
            Label(nameof(StepStreetBinding), "Open Stepped Streets", "Opens or closes the Stepped Streets tool.");
            return new MemorySource(labels);
        }
    }
}
