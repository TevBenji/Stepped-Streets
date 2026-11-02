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
                var input = InputManager.instance;
                if (hotkey != null && hotkey.WasPerformedThisFrame() && input != null && !input.hasInputFieldFocus)
                    World.GetOrCreateSystemManaged<NeighbourhoodToolSystem>().Toggle();
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
