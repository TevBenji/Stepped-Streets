using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using SteppedStreets.Settings;
using SteppedStreets.Tool;

namespace SteppedStreets
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Logger = LogManager.GetLogger(nameof(SteppedStreets)).SetShowsErrorsInUI(false);

        internal static SteppedStreetsSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Logger.Info("Stepped Streets loading");

            // Each area registers inside its own try/catch, so a broken one is logged and never takes the game down.
            try
            {
                Settings = new SteppedStreetsSettings(this);
                AssetDatabase.global.LoadSettings(nameof(SteppedStreets), Settings, new SteppedStreetsSettings(this));
                Settings.RegisterInOptionsUI();
                // Before any GetAction: the actions come from the binding properties.
                Settings.RegisterKeyBindings();
                GameManager.instance.localizationManager.AddSource("en-US", Settings.Labels());
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets: the settings failed to load");
            }

            try { updateSystem.UpdateAt<Build.BuildToolSystem>(SystemUpdatePhase.ToolUpdate); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets build tool registration failed"); }
            try { updateSystem.UpdateBefore<Build.ZoneBuildSystem, Build.BuildToolSystem>(SystemUpdatePhase.ToolUpdate); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets zoning system registration failed"); }
            // The road and object builders hand their Apply to the build tool in the same frame, so they update just before it.
            try { updateSystem.UpdateBefore<Build.NetBuildSystem, Build.BuildToolSystem>(SystemUpdatePhase.ToolUpdate); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets road builder registration failed"); }
            try { updateSystem.UpdateBefore<Build.ObjectBuildSystem, Build.BuildToolSystem>(SystemUpdatePhase.ToolUpdate); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets object builder registration failed"); }
            try
            {
                updateSystem.UpdateAt<Build.TerrainBuildSystem>(SystemUpdatePhase.ToolUpdate);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets terrain registration failed.");
            }

            try
            {
                updateSystem.UpdateAt<NeighbourhoodToolSystem>(SystemUpdatePhase.ToolUpdate);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets: the neighbourhood tool failed to register");
            }

            try
            {
                updateSystem.UpdateBefore<Build.WallBuildSystem, Build.BuildToolSystem>(SystemUpdatePhase.ToolUpdate);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets wall system registration failed");
            }
            try { updateSystem.UpdateAt<StepStreetHotkeySystem>(SystemUpdatePhase.ToolUpdate); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets: the tool hotkey failed to register"); }

            try { updateSystem.UpdateAt<NeighbourhoodPreviewSystem>(SystemUpdatePhase.Rendering); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets: the preview failed to register"); }

            try
            {
                updateSystem.UpdateAt<Panel.PanelSystem>(SystemUpdatePhase.UIUpdate);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets: the panel bindings failed to register");
            }
        }

        public void OnDispose()
        {
            try
            {
                Settings?.UnregisterInOptionsUI();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets: removing the settings failed");
            }

            Settings = null;
        }
    }
}
