// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using Game;
using Game.Input;
using SteppedStreets.Settings;
using UnityEngine.Scripting;

namespace SteppedStreets.Tool
{
    // ToolBaseSystem stops updating when another tool is active, so opening needs an always-running listener.
    public sealed partial class StepStreetHotkeySystem : GameSystemBase
    {
        private ProxyAction hotkey;
        private NeighbourhoodToolSystem tool;
        private float nextErrorLog;

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                if (hotkey == null)
                {
                    hotkey = Mod.Settings?.GetAction(SteppedStreetsSettings.StepStreetAction);
                    if (hotkey != null) hotkey.shouldBeEnabled = true;
                }
                if (tool == null) tool = World.GetOrCreateSystemManaged<NeighbourhoodToolSystem>();
                // The build has to keep going when the player switches tools mid-build, and this system always runs.
                tool.BuildTick();
                var input = InputManager.instance;
                if (hotkey != null && hotkey.WasPerformedThisFrame() && input != null && !input.hasInputFieldFocus) tool.Toggle();
            }
            catch (Exception exception)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextErrorLog) return;
                nextErrorLog = now + 5f;
                Mod.Logger.Error(exception, "Stepped Streets: street picker hotkey failed");
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            try
            {
                if (hotkey != null) hotkey.shouldBeEnabled = false;
                base.OnDestroy();
            }
            catch (Exception exception) { Mod.Logger.Error(exception, "Stepped Streets: removing the street picker hotkey failed"); }
        }
    }
}
