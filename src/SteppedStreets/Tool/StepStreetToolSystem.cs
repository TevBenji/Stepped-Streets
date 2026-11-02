// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using SteppedStreets.Core.Planning;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;
using static System.FormattableString;

namespace SteppedStreets.Tool
{
    public sealed partial class StepStreetToolSystem : ToolBaseSystem
    {
        private StreetCapture selection;
        private SiteSnapshot snapshot;
        private StepPlan plan;
        private Entity hoveredEdge;
        private float3[] hoveredLine = new float3[0];
        private float plannedBankGrade;
        private StepLook plannedLook;
        private string plannedZoneName;
        private ToolStage stageBeforeClose = ToolStage.Picking;
        private readonly RightClickGesture rightClick = new RightClickGesture();
        private float nextErrorLog;

        public ToolState State { get; } = new ToolState();
        public override string toolID => "Stepped Streets";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;
        internal bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        internal StreetCapture Selection => selection;
        internal float3[] HoveredLine => hoveredLine;
        internal StepPlan Plan => plan;
        internal float PreviewBankGrade => plannedBankGrade;

        [Preserve]
        protected override void OnCreate()
        {
            try { base.OnCreate(); }
            catch (Exception exception)
            {
                Fail(exception, "The street picker could not be created.");
                Enabled = false;
            }
        }

        public void Toggle()
        {
            if (State.Stage == ToolStage.Building) { Message("Still building the last street. Wait until it is done."); return; }
            if (IsActive) Close(); else Open();
        }

        public void Open()
        {
            try
            {
                if (State.Stage == ToolStage.Building) return;
                m_ToolSystem.activeTool = this;
                State.Stage = selection == null ? ToolStage.Picking : stageBeforeClose;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The street picker could not open."); }
        }

        public void Close()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("Still building the last street. Wait until it is done."); return; }
                if (State.Stage != ToolStage.Closed) stageBeforeClose = State.Stage;
                State.Stage = ToolStage.Closed;
                if (IsActive) m_ToolSystem.activeTool = m_DefaultToolSystem;
                ClearHover();
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The street picker could not close."); }
        }

        public void ClearSelection()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("Still building the last street. Wait until it is done."); return; }
                selection = null;
                snapshot = null;
                plan = null;
                State.StreetName = "";
                State.Summary = "";
                State.Messages = new string[0];
                State.Stage = IsActive ? ToolStage.Picking : ToolStage.Closed;
                stageBeforeClose = ToolStage.Picking;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The street selection could not be cleared."); }
        }

        public void SettingsChanged()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("The new settings will apply to the next plan."); return; }
                if (selection != null) Replan();
            }
            catch (Exception exception) { Fail(exception, "The street could not be planned with these settings."); }
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            try
            {
                base.OnStartRunning();
                EnableActions(true);
                if (State.Stage == ToolStage.Closed)
                    State.Stage = selection == null ? ToolStage.Picking : stageBeforeClose;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The street picker could not start."); }
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            try
            {
                applyMode = ApplyMode.None;
                EnableActions(false);
                rightClick.Reset();
                ClearHover();
                // The invisible build tool briefly owns the tool slot while zoning or walls are applied.
                if (State.Stage != ToolStage.Building && State.Stage != ToolStage.Closed)
                {
                    stageBeforeClose = State.Stage;
                    State.Stage = ToolStage.Closed;
                }
                Touch();
                base.OnStopRunning();
            }
            catch (Exception exception) { Fail(exception, "The street picker could not stop cleanly."); }
        }

        public override void InitializeRaycast()
        {
            try
            {
                base.InitializeRaycast();
                m_ToolRaycastSystem.typeMask = TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = Layer.Road;
                m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubElements;
            }
            catch (Exception exception) { LogUpdateError(exception, "Stepped Streets: setting up the road raycast failed"); }
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            try
            {
                applyMode = ApplyMode.None;
                if (!IsActive || State.Stage == ToolStage.Building) return inputDependencies;
                var input = InputManager.instance;
                if (input == null || input.hasInputFieldFocus) { ClearHover(); return inputDependencies; }
                var mouse = Mouse.current;
                var keyboard = Keyboard.current;
                bool overWorld = input.controlOverWorld;
                bool rawRightDown = mouse != null && mouse.rightButton.wasPressedThisFrame;
                bool rightPressed = overWorld && (rawRightDown || Pressed(secondaryApplyAction));
                bool rightHeld = (mouse != null && mouse.rightButton.isPressed) || secondaryApplyAction.IsPressed();
                float2 movement = mouse == null ? float2.zero : (float2)mouse.delta.ReadValue();
                bool rightClicked = rightClick.Update(rightPressed, rightHeld, movement, overWorld, UnityEngine.Time.realtimeSinceStartup);

                bool escape = (Pressed(cancelAction) && !rawRightDown && !rightHeld)
                    || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
                if (escape)
                {
                    if (selection != null) ClearSelection(); else Close();
                    return inputDependencies;
                }
                if (rightClicked) { ClearSelection(); return inputDependencies; }

                Entity edge = Entity.Null;
                if (overWorld && GetRaycastResult(out Entity hitEntity, out RaycastHit hit)
                    && math.all(math.isfinite(hit.m_HitPosition)) && StreetCapture.IsUsableEdge(EntityManager, hitEntity))
                    edge = hitEntity;
                UpdateHover(edge);
                bool leftPressed = overWorld && (Pressed(applyAction) || (mouse != null && mouse.leftButton.wasPressedThisFrame));
                if (leftPressed && edge != Entity.Null)
                {
                    bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                    Select(edge, shift);
                }
            }
            catch (Exception exception)
            {
                LogUpdateError(exception, "Stepped Streets: street picker update failed");
                const string message = "The street picker could not read this street. Try selecting it again.";
                if (State.Stage != ToolStage.Failed || State.Summary != message)
                {
                    State.Stage = ToolStage.Failed;
                    State.Summary = message;
                    Touch();
                }
            }
            return inputDependencies;
        }

        private void Select(Entity edge, bool add)
        {
            var captured = StreetCapture.Capture(EntityManager, edge);
            if (captured == null) { Message("Select a road edge to plan its street."); return; }
            if (add && selection != null)
            {
                captured = selection.Add(EntityManager, captured);
                if (captured == null) { Message("That street doesn't join the selection"); return; }
            }
            selection = captured;
            State.StreetName = StreetName(captured.Aggregate);
            Replan();
        }

        private string StreetName(Entity aggregate)
        {
            try
            {
                return aggregate == Entity.Null ? "" : World.GetOrCreateSystemManaged<NameSystem>().GetRenderedLabelName(aggregate) ?? "";
            }
            catch (Exception exception)
            {
                LogUpdateError(exception, "Stepped Streets: reading the street name failed");
                return "";
            }
        }

        private void Replan()
        {
            plan = null;
            snapshot = null;
            var settings = Mod.Settings ?? throw new InvalidOperationException("Stepped Streets settings are unavailable.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            snapshot = SiteSnapshot.Capture(EntityManager, World, selection, settings.DepthCells);
            var request = new StepPlanRequest
            {
                Centreline = selection.Centreline,
                RoadHalfWidth = selection.HalfWidth,
                Side = settings.Side,
                LotWidthCells = settings.LotWidthCells,
                NarrowestWidthCells = math.min(settings.NarrowestWidthCells, settings.LotWidthCells),
                DepthCells = settings.DepthCells,
                MinimumGrade = settings.MinimumGradePercent / 100f,
                HighestStep = settings.HighestStep,
                BankGrade = settings.BankGrade,
                Look = settings.Look,
                EarthworkLimit = settings.EarthworkLimit,
                GroundHeight = snapshot.GroundHeight,
                Blocked = snapshot.Blocked,
            };
            // ponytail: capture and planning run on the main thread; move planning to Task.Run with latest-request-wins if R10's 250 ms budget is exceeded.
            plan = StepPlanner.Plan(request);
            plannedBankGrade = request.BankGrade;
            plannedLook = request.Look;
            plannedZoneName = settings.ZoneName;
            State.StreetName = StreetName(selection.Aggregate);
            stageBeforeClose = ToolStage.Planned;
            State.Stage = IsActive ? ToolStage.Planned : ToolStage.Closed;
            State.Summary = plan.SummaryLine;
            State.Messages = plan.Messages.ToArray();
            Touch();
            var summary = plan.Summary;
            Log(Invariant($"plan: {summary.LotCount} pads, {plan.Steps.Count} steps of {summary.LowestStep:0.00}-{summary.TallestStep:0.00} m, {summary.EmptyCount} lots left empty, {plan.FlatStretches.Count} flat stretches; settings: side {request.Side}, lots {request.LotWidthCells} cells (narrowest {request.NarrowestWidthCells}) by {request.DepthCells}, minimum grade {settings.MinimumGradePercent}%, highest step {request.HighestStep:0.0} m, bank grade {request.BankGrade:0.00}, look {request.Look}, earthwork limit {request.EarthworkLimit:0.0} m; capture and plan {clock.Elapsed.TotalMilliseconds:0.0} ms"));
            foreach (var message in plan.Messages) Log(message);
        }

        private void UpdateHover(Entity edge)
        {
            float grade = float.NaN;
            if (edge != Entity.Null)
            {
                var curve = EntityManager.GetComponentData<Curve>(edge);
                if (curve.m_Length > 0f) grade = math.abs(curve.m_Bezier.d.y - curve.m_Bezier.a.y) / curve.m_Length * 100f;
                if (edge != hoveredEdge)
                {
                    int segments = math.max(1, (int)math.ceil(curve.m_Length / 4f));
                    hoveredLine = new float3[segments + 1];
                    for (int index = 0; index <= segments; index++)
                        hoveredLine[index] = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, index / (float)segments);
                }
            }
            else if (hoveredLine.Length > 0) hoveredLine = new float3[0];
            bool tooFlat = !float.IsNaN(grade) && grade < (Mod.Settings?.MinimumGradePercent ?? 6);
            if (edge != hoveredEdge || !grade.Equals(State.HoverGradePercent) || tooFlat != State.HoverTooFlat)
            {
                hoveredEdge = edge;
                State.HoverGradePercent = grade;
                State.HoverTooFlat = tooFlat;
                Touch();
            }
        }

        private void ClearHover() => UpdateHover(Entity.Null);

        private static bool Pressed(IProxyAction action) => action != null && action.WasPressedThisFrame();

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            try
            {
                // Entity identifiers and the sampled ground belong to the old city.
                State.Stage = ToolStage.Closed;
                selection = null;
                snapshot = null;
                plan = null;
                State.StreetName = "";
                State.Summary = "";
                State.Messages = new string[0];
                stageBeforeClose = ToolStage.Picking;
                ClearHover();
                rightClick.Reset();
                Touch();
                base.OnGamePreload(purpose, mode);
            }
            catch (Exception exception) { Fail(exception, "The street picker could not reset for the next city."); }
        }

        private void EnableActions(bool enabled)
        {
            applyAction.shouldBeEnabled = enabled;
            secondaryApplyAction.shouldBeEnabled = enabled;
            cancelAction.shouldBeEnabled = enabled;
        }

        private void Message(string message)
        {
            if (State.Messages.Contains(message)) return;
            State.Messages = State.Messages.Concat(new[] { message }).ToArray();
            Touch();
        }

        private void Fail(Exception exception, string message)
        {
            Mod.Logger.Error(exception, "Stepped Streets: " + message);
            State.Stage = ToolStage.Failed;
            stageBeforeClose = ToolStage.Failed;
            State.Summary = message;
            Message(message);
            Touch();
        }

        private void LogUpdateError(Exception exception, string message)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextErrorLog) return;
            nextErrorLog = now + 5f;
            Mod.Logger.Error(exception, message);
        }

        private void Touch() => State.Version++;
        private static void Log(string line) => Mod.Logger.Info("Stepped Streets: " + line);
    }
}
