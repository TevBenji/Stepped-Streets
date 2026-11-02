// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Game;
using Game.Input;
using Game.UI.InGame;
using SteppedStreets.Build;
using SteppedStreets.Core.Planning;
using SteppedStreets.Settings;
using Unity.Mathematics;
using static System.FormattableString;

namespace SteppedStreets.Spike
{
    /// <summary>
    /// The M1 spike trigger: the hotkey steps the street selected in the game's info panel. It plans the lots, builds the
    /// ground, then zones the cells that came out level, and logs each stage with its numbers. No tool, panel or preview.
    /// </summary>
    public sealed partial class StepSelectedStreetSystem : GameSystemBase
    {
        // R8: a cell counts as level when the built ground sits within this of its pad, the terrain build's own tolerance.
        private const float LevelTolerance = 0.3f;

        private SelectedInfoUISystem selectedInfo;
        private TerrainBuildSystem terrainBuild;
        private ZoneBuildSystem zoneBuild;
        private ProxyAction hotkey;

        protected override void OnCreate()
        {
            base.OnCreate();
            try
            {
                selectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
                terrainBuild = World.GetOrCreateSystemManaged<TerrainBuildSystem>();
                zoneBuild = World.GetOrCreateSystemManaged<ZoneBuildSystem>();
                hotkey = Mod.Settings?.GetAction(SteppedStreetsSettings.StepStreetAction);
                if (hotkey != null)
                    hotkey.shouldBeEnabled = true;
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: the spike trigger failed to start");
                hotkey = null;
            }

            if (hotkey == null)
            {
                Mod.Logger.Error("Stepped Streets: no hotkey, so the spike trigger is off.");
                Enabled = false;
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (hotkey.WasPerformedThisFrame())
                    StepSelectedStreet();
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: stepping the selected street failed");
            }
        }

        private void StepSelectedStreet()
        {
            if (terrainBuild.Busy || zoneBuild.Busy)
            {
                Log("still building the last street; press the hotkey again when it is done.");
                return;
            }

            var street = SelectedStreet.Capture(EntityManager, selectedInfo.selectedEntity);
            if (street == null)
                return;

            var settings = Mod.Settings;
            var request = new StepPlanRequest
            {
                Centreline = street.Centreline,
                RoadHalfWidth = street.HalfWidth,
                Side = settings.Side,
                LotWidthCells = settings.LotWidthCells,
                // The planner refuses a narrowest width above the lot width; the two sliders can cross.
                NarrowestWidthCells = math.min(settings.NarrowestWidthCells, settings.LotWidthCells),
                DepthCells = settings.DepthCells,
                MinimumGrade = settings.MinimumGradePercent / 100f,
                HighestStep = settings.HighestStep,
            };
            var plan = StepPlanner.Plan(request);
            var summary = plan.Summary;
            Log(Invariant(
                $"plan: {summary.LotCount} pads, {plan.Steps.Count} steps of {summary.LowestStep:0.00}-{summary.TallestStep:0.00} m, {summary.EmptyCount} lots left empty (too steep), {plan.FlatStretches.Count} flat stretches; settings: side {request.Side}, lots {request.LotWidthCells} cells (narrowest {request.NarrowestWidthCells}) by {request.DepthCells}, minimum grade {settings.MinimumGradePercent}%, highest step {request.HighestStep:0.0} m, bank grade {settings.BankGrade:0.00}"));
            if (plan.Message != null)
                Log(plan.Message);
            if (plan.Pads.Count == 0)
                return;

            var patches = StepPlanPatches.ToPatches(plan, settings.BankGrade);
            float bankWidth = StepPlanPatches.BankWidth(plan, terrainBuild.HeightAt, settings.BankGrade);
            Log(Invariant($"terrain: {patches.Count} patches ({plan.Pads.Count} level pads, {plan.Steps.Count} sloped banks), bank width {bankWidth:0.0} m"));
            string zoneName = settings.ZoneName;
            terrainBuild.Begin(patches, bankWidth, report => ZoneLevelCells(plan, zoneName, report));
        }

        private void ZoneLevelCells(StepPlan plan, string zoneName, TerrainBuildReport report)
        {
            try
            {
                Log("terrain: " + report.Summary);
                var quads = new List<float2[]>();
                foreach (var pad in plan.Pads)
                {
                    var cells = StepPlanner.LevelCells(pad, terrainBuild.HeightAt, LevelTolerance);
                    Log(Invariant(
                        $"pad {pad.Side} {pad.Start:0}-{pad.End:0} m along, height {pad.Height:0.00} m: {cells.Count} of {pad.WidthCells * pad.DepthCells} cells level"));
                    quads.AddRange(StepPlanPatches.CellQuads(pad, cells));
                }

                if (quads.Count == 0)
                {
                    Log("zoning: no cell came out level, nothing to zone.");
                    return;
                }

                Log(Invariant($"zoning: {quads.Count} level cells as {zoneName}"));
                zoneBuild.Begin(quads, zoneName, zoneReport => Log("zoning: " + zoneReport.Summary));
            }
            catch (Exception exception)
            {
                Mod.Logger.Error(exception, "Stepped Streets: zoning the level cells failed");
            }
        }

        private static void Log(string line) => Mod.Logger.Info("Stepped Streets: " + line);
    }
}
