// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Game.Input;
using Game.Modding;
using Game.Settings;
using SteppedStreets.Core.Planning;

namespace SteppedStreets.Settings
{
    /// <summary>The spike's settings, under Stepped Streets in the game's Options. Property names are save format: never rename them.</summary>
    [FileLocation("ModsSettings/SteppedStreets/SteppedStreets")]
    [SettingsUIKeyboardAction(StepStreetAction, ActionType.Button)]
    public sealed class SteppedStreetsSettings : ModSetting
    {
        public const string StepStreetAction = "StepStreet";

        private const string Section = "Main";
        private const string Group = "Spike";

        public SteppedStreetsSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(Section, Group)]
        public StreetSide Side { get; set; }

        [SettingsUISlider(min = 2, max = 6, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int LotWidthCells { get; set; }

        [SettingsUISlider(min = 2, max = 6, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int NarrowestWidthCells { get; set; }

        [SettingsUISlider(min = 2, max = 6, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(Section, Group)]
        public int DepthCells { get; set; }

        [SettingsUISlider(min = 4, max = 12, step = 1, scalarMultiplier = 1, unit = Game.UI.Unit.kPercentage)]
        [SettingsUISection(Section, Group)]
        public int MinimumGradePercent { get; set; }

        [SettingsUISlider(min = 0.5f, max = 3f, step = 0.1f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 1)]
        [SettingsUISection(Section, Group)]
        public float HighestStep { get; set; }

        [SettingsUISlider(min = 0.2f, max = 0.5f, step = 0.05f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 2)]
        [SettingsUISection(Section, Group)]
        public float BankGrade { get; set; }

        [SettingsUITextInput]
        [SettingsUISection(Section, Group)]
        public string ZoneName { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.S, StepStreetAction, ctrl: true, shift: true)]
        [SettingsUISection(Section, Group)]
        public ProxyBinding StepStreetBinding { get; set; }

        public override void SetDefaults()
        {
            Side = StreetSide.Both;
            LotWidthCells = 3;
            NarrowestWidthCells = 2;
            DepthCells = 3;
            MinimumGradePercent = 6;
            HighestStep = 2f;
            // Placemaker's sloped car parks settled on 0.4 after curtains at 0.46.
            BankGrade = 0.4f;
            ZoneName = "Residential Low";
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
                [GetBindingMapLocaleID()] = "Stepped Streets",
                [GetBindingKeyLocaleID(StepStreetAction)] = "Step the selected street",
                [GetEnumValueLocaleID(StreetSide.Both)] = "Both",
                [GetEnumValueLocaleID(StreetSide.Left)] = "Left",
                [GetEnumValueLocaleID(StreetSide.Right)] = "Right",
            };
            void Label(string property, string label, string description)
            {
                labels[GetOptionLabelLocaleID(property)] = label;
                labels[GetOptionDescLocaleID(property)] = description;
            }

            Label(nameof(Side), "Side", "Which side of the street gets lots, as the game draws the street.");
            Label(nameof(LotWidthCells), "Lot width (cells)", "Cells along the kerb per pad. 4 or 6 pairs two lots on one pad.");
            Label(nameof(NarrowestWidthCells), "Narrowest width (cells)", "How far a pad may narrow to keep its step under the highest step.");
            Label(nameof(DepthCells), "Depth (cells)", "Cells back from the road.");
            Label(nameof(MinimumGradePercent), "Minimum grade", "Stretches flatter than this are left alone.");
            Label(nameof(HighestStep), "Highest step (m)", "The tallest step allowed between two neighbouring pads.");
            Label(nameof(BankGrade), "Bank grade", "Rise over run of the bank between two pads; Placemaker saw curtains at 0.46.");
            Label(nameof(ZoneName), "Zone", "The zone to paint on level cells, by name (for example Residential Low).");
            Label(nameof(StepStreetBinding), "Step the selected street", "Steps the street selected with the game's select tool.");
            return new MemorySource(labels);
        }
    }
}
