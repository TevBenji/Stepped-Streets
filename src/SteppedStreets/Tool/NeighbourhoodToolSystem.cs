// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Tools;
using SteppedStreets.Build;
using SteppedStreets.Core.Neighbourhood;
using SteppedStreets.Core.Terrain;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;
using static System.FormattableString;

namespace SteppedStreets.Tool
{
    /// <summary>
    /// The neighbourhood tool: the player draws an area on the ground, the planner lays out a hillside neighbourhood in it on
    /// a worker thread, the preview shows it, and Build makes it real. Click places corners; clicking the first corner (or
    /// Enter) closes the area; dragging a corner, or the middle of an edge, reshapes it; right-click removes a corner; Ctrl+Z
    /// undoes; Esc steps back.
    /// </summary>
    public sealed partial class NeighbourhoodToolSystem : ToolBaseSystem
    {
        // About ten times the area of the first version's 2 km square; planning a whole 6 km hillside takes a while.
        private const float LargestSide = 6400f;
        private readonly List<float2> corners = new List<float2>();
        private readonly List<(float2[] Corners, bool Closed)> history = new List<(float2[], bool)>();
        private readonly RightClickGesture rightClick = new RightClickGesture();
        private bool areaClosed;
        private int dragging = -1;
        private float2[] beforeDrag;
        private float2 dragStart;
        private float nextErrorLog;

        // Planning on the worker: the latest request wins.
        private Task<NeighbourhoodPlan> planTask;
        private CancellationTokenSource planCancel;
        private int requestedVersion, taskVersion;
        private SiteSnapshot taskSnapshot;
        private NeighbourhoodRequest taskRequest;
        private SiteSnapshot plannedSnapshot;

        public ToolState State { get; } = new ToolState();
        public override string toolID => "Stepped Streets";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;
        internal bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        internal IReadOnlyList<float2> Corners => corners;
        internal bool AreaClosed => areaClosed;
        internal int HoveredCorner { get; private set; } = -1;
        internal float3? Cursor { get; private set; }

        /// <summary>What the cursor has snapped to, for the preview's ring; null when it is free.</summary>
        internal float2? Snapped { get; private set; }

        /// <summary>World metres the picking and snapping reach this frame; the preview sizes its lines from it.</summary>
        internal float Reach { get; private set; } = 6f;

        // Existing roads near the camera, for snapping corners onto them; rebuilt when the tool opens and after a build.
        private SpatialBuckets<(float2 Start, float2 End)> roadSnap;
        internal NeighbourhoodPlan Plan { get; private set; }

        /// <summary>The roads of the plan still being worked out, shown while its lots and ground are planned.</summary>
        internal NeighbourhoodPlan EarlyRoads { get; private set; }

        private sealed class EarlyRoadsHandOff
        {
            public int Version;
            public NeighbourhoodPlan Roads;
        }

        private EarlyRoadsHandOff earlyFromWorker;
        internal NeighbourhoodRequest PlannedRequest { get; private set; }
        internal int PlanVersion { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            try { base.OnCreate(); }
            catch (Exception exception)
            {
                Fail(exception, "The neighbourhood tool could not be created.");
                Enabled = false;
            }
        }

        public void Toggle()
        {
            if (State.Stage == ToolStage.Building) { Message("Still building. Wait until it is done."); return; }
            if (IsActive) Close(); else Open();
        }

        public void Open()
        {
            try
            {
                if (State.Stage == ToolStage.Building) return;
                m_ToolSystem.activeTool = this;
                RefreshStage();
            }
            catch (Exception exception) { Fail(exception, "The neighbourhood tool could not open."); }
        }

        public void Close()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("Still building. Wait until it is done."); return; }
                State.Stage = ToolStage.Closed;
                if (IsActive) m_ToolSystem.activeTool = m_DefaultToolSystem;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The neighbourhood tool could not close."); }
        }

        /// <summary>Clears the area (undoable) and its plan.</summary>
        public void ClearArea()
        {
            if (State.Stage == ToolStage.Building) return;
            if (corners.Count > 0) Remember();
            corners.Clear();
            areaClosed = false;
            dragging = -1;
            Replan();
        }

        public void CloseArea()
        {
            if (State.Stage == ToolStage.Building || areaClosed) return;
            if (corners.Count < 3) { Message("Place at least three corners first."); return; }
            if (CrossesItself(corners, true)) { Message("The outline crosses itself. Move a corner first."); return; }
            Remember();
            areaClosed = true;
            Replan();
        }

        public void Undo()
        {
            if (State.Stage == ToolStage.Building || history.Count == 0) return;
            var last = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            corners.Clear();
            corners.AddRange(last.Corners);
            areaClosed = last.Closed;
            dragging = -1;
            Replan();
        }

        public void Reroll()
        {
            var settings = Mod.Settings;
            if (settings == null || State.Stage == ToolStage.Building) return;
            // A new seed far from the last, so neighbouring seeds don't look alike.
            settings.Seed = 1 + (int)(math.hash(new uint2((uint)settings.Seed, (uint)Environment.TickCount)) % 99999u);
            settings.ApplyAndSave();
            SettingsChanged();
        }

        public void SettingsChanged()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("The new settings will apply to the next plan."); return; }
                Replan();
            }
            catch (Exception exception) { Fail(exception, "The area could not be planned with these settings."); }
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            try
            {
                base.OnStartRunning();
                EnableActions(true);
                RefreshRoadSnap();
                if (State.Stage == ToolStage.Closed) RefreshStage();
            }
            catch (Exception exception) { Fail(exception, "The neighbourhood tool could not start."); }
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            try
            {
                applyMode = ApplyMode.None;
                EnableActions(false);
                rightClick.Reset();
                dragging = -1;
                Cursor = null;
                HoveredCorner = -1;
                // The invisible build tool briefly owns the tool slot while a build stage applies.
                if (State.Stage != ToolStage.Building) State.Stage = ToolStage.Closed;
                Touch();
                base.OnStopRunning();
            }
            catch (Exception exception) { Fail(exception, "The neighbourhood tool could not stop cleanly."); }
        }

        public override void InitializeRaycast()
        {
            try
            {
                base.InitializeRaycast();
                m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            }
            catch (Exception exception) { LogUpdateError(exception, "Stepped Streets: setting up the terrain raycast failed"); }
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            try
            {
                applyMode = ApplyMode.None;
                AdoptFinishedPlan();
                // The build itself advances from BuildTick, which runs even when another tool is active.
                if (State.Stage == ToolStage.Building) return inputDependencies;
                if (!IsActive) return inputDependencies;
                var input = InputManager.instance;
                if (input == null || input.hasInputFieldFocus) return inputDependencies;
                HandleInput(input);
            }
            catch (Exception exception)
            {
                LogUpdateError(exception, "Stepped Streets: neighbourhood tool update failed");
            }
            return inputDependencies;
        }

        private void HandleInput(InputManager input)
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool overWorld = input.controlOverWorld;
            float3? hit = null;
            if (overWorld && GetRaycastResult(out _, out RaycastHit raycast) && math.all(math.isfinite(raycast.m_HitPosition))) hit = raycast.m_HitPosition;
            Cursor = hit;

            // Picking reach grows with the camera's distance, so corners stay grabbable when zoomed out.
            float reach = 6f;
            var camera = UnityEngine.Camera.main;
            if (hit.HasValue && camera != null)
                reach = math.clamp(math.distance((float3)camera.transform.position, hit.Value) * 0.03f, 4f, 80f);
            Reach = reach;
            float2? snapped = hit.HasValue ? Snap(hit.Value.xz, reach) : (float2?)null;
            // The preview reads these directly and the panel shows neither, so no Touch (a full panel state rebuild).
            Snapped = snapped;
            float2 point = snapped ?? (hit.HasValue ? hit.Value.xz : float2.zero);
            int hovered = -1;
            if (hit.HasValue && dragging < 0)
            {
                float best = reach;
                for (int index = 0; index < corners.Count; index++)
                {
                    float distance = math.distance(corners[index], hit.Value.xz);
                    if (distance <= best) { best = distance; hovered = index; }
                }
            }
            else if (dragging >= 0) hovered = dragging;
            HoveredCorner = hovered;

            bool rawRightDown = mouse != null && mouse.rightButton.wasPressedThisFrame;
            bool rightHeld = (mouse != null && mouse.rightButton.isPressed) || secondaryApplyAction.IsPressed();
            float2 movement = mouse == null ? float2.zero : (float2)mouse.delta.ReadValue();
            bool rightClicked = rightClick.Update(overWorld && (rawRightDown || Pressed(secondaryApplyAction)), rightHeld, movement, overWorld,
                UnityEngine.Time.realtimeSinceStartup);
            bool control = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool escape = (Pressed(cancelAction) && !rawRightDown && !rightHeld) || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);

            if (escape)
            {
                if (dragging >= 0) { RestoreDrag(); return; }
                if (corners.Count > 0) ClearArea(); else Close();
                return;
            }
            if (control && keyboard.zKey.wasPressedThisFrame) { Undo(); return; }
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) { CloseArea(); return; }
            if (rightClicked && dragging < 0) { RemoveCorner(hovered); return; }

            bool leftPressed = overWorld && (Pressed(applyAction) || (mouse != null && mouse.leftButton.wasPressedThisFrame));
            bool leftHeld = mouse != null && mouse.leftButton.isPressed;
            if (dragging >= 0)
            {
                if (hit.HasValue && leftHeld && math.distance(corners[dragging], point) > 0.05f)
                {
                    corners[dragging] = point;
                    Touch();
                }
                if (!leftHeld) EndDrag();
                return;
            }
            if (!leftPressed || !hit.HasValue) return;
            bool closing = !areaClosed && corners.Count >= 3 && snapped.HasValue && math.distance(snapped.Value, corners[0]) < 0.01f;
            if (closing) { CloseArea(); return; }
            if (hovered >= 0)
            {
                StartDrag(hovered);
                return;
            }
            if (!areaClosed)
            {
                // Two corners on one spot would make a zero-length edge.
                if (corners.Count > 0 && math.distance(corners[corners.Count - 1], point) < 1f) return;
                Remember();
                corners.Add(point);
                RefreshStage();
                return;
            }
            // A closed area grows a corner where the player grabs one of its edges.
            for (int index = 0; index < corners.Count; index++)
            {
                float2 start = corners[index], end = corners[(index + 1) % corners.Count];
                if (SiteGeometry.DistanceSquared(hit.Value.xz, start, end) > reach * reach) continue;
                Remember();
                corners.Insert(index + 1, point);
                StartDrag(index + 1, remembered: true);
                return;
            }
        }

        /// <summary>
        /// Where a click at this point lands: on the first corner when it would close the area, on another corner while
        /// dragging, or on the nearest existing road's centreline; null when nothing is within reach.
        /// </summary>
        private float2? Snap(float2 point, float reach)
        {
            if (!areaClosed && corners.Count >= 3 && dragging < 0 && math.distance(point, corners[0]) <= reach * 1.6f) return corners[0];
            if (dragging >= 0)
                for (int index = 0; index < corners.Count; index++)
                    if (index != dragging && math.distance(point, corners[index]) <= reach) return corners[index];
            if (roadSnap == null) return null;
            float best = reach;
            float2? nearest = null;
            foreach (var (start, end) in roadSnap.Within(point, reach))
            {
                float2 direction = end - start;
                float lengthSquared = math.lengthsq(direction);
                float fraction = lengthSquared > 1e-6f ? math.saturate(math.dot(point - start, direction) / lengthSquared) : 0f;
                float2 onRoad = start + direction * fraction;
                float distance = math.distance(point, onRoad);
                // Road ends and junctions win when they are close: corners there line the area up with the street grid.
                if (fraction <= 0.001f || fraction >= 0.999f) distance *= 0.7f;
                if (distance < best) { best = distance; nearest = onRoad; }
            }
            return nearest;
        }

        /// <summary>Collects the ground roads' centrelines in 64 m buckets for snapping.</summary>
        private void RefreshRoadSnap()
        {
            try
            {
                var buckets = new SpatialBuckets<(float2, float2)>(64f);
                using (var query = EntityManager.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<Game.Net.Road>(),
                    Unity.Entities.ComponentType.ReadOnly<Game.Net.Curve>(), Unity.Entities.ComponentType.Exclude<Game.Tools.Temp>(),
                    Unity.Entities.ComponentType.Exclude<Deleted>()))
                using (var edges = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                    foreach (var edge in edges)
                    {
                        var curve = EntityManager.GetComponentData<Game.Net.Curve>(edge);
                        int pieces = math.max(1, (int)math.ceil(curve.m_Length / 12f));
                        float2 previous = curve.m_Bezier.a.xz;
                        for (int piece = 1; piece <= pieces; piece++)
                        {
                            float2 next = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, piece / (float)pieces).xz;
                            // Unpadded: the snap query looks through every bucket within its reach instead.
                            buckets.Add(math.min(previous, next), math.max(previous, next), (previous, next));
                            previous = next;
                        }
                    }
                roadSnap = buckets;
            }
            catch (Exception exception)
            {
                roadSnap = null;
                LogUpdateError(exception, "Stepped Streets: collecting roads for snapping failed");
            }
        }

        private void StartDrag(int corner, bool remembered = false)
        {
            dragging = corner;
            beforeDrag = remembered ? null : corners.ToArray();
            dragStart = corners[corner];
            Touch();
        }

        private void EndDrag()
        {
            int corner = dragging;
            dragging = -1;
            bool moved = math.distance(corners[corner], dragStart) > 0.5f;
            // A corner grown from an edge was remembered when it was inserted, so it replans even if it did not move.
            bool inserted = beforeDrag == null;
            if (!inserted && moved) history.Add((beforeDrag, areaClosed));
            beforeDrag = null;
            if (moved || inserted) Replan();
        }

        private void RestoreDrag()
        {
            if (beforeDrag != null) { corners.Clear(); corners.AddRange(beforeDrag); }
            else if (dragging >= 0 && dragging < corners.Count) corners[dragging] = dragStart;
            dragging = -1;
            beforeDrag = null;
            Touch();
        }

        private void RemoveCorner(int hovered)
        {
            if (corners.Count == 0) return;
            int corner = hovered >= 0 ? hovered : areaClosed ? -1 : corners.Count - 1;
            if (corner < 0) return;
            Remember();
            corners.RemoveAt(corner);
            if (corners.Count < 3) areaClosed = false;
            Replan();
        }

        private void Remember()
        {
            history.Add((corners.ToArray(), areaClosed));
            if (history.Count > 64) history.RemoveAt(0);
        }

        /// <summary>Starts planning the closed area on a worker, or clears the plan when there is no closed area.</summary>
        private void Replan()
        {
            planCancel?.Cancel();
            planCancel = null;
            planTask = null;
            Plan = null;
            EarlyRoads = null;
            PlannedRequest = null;
            PlanVersion++;
            ClearPlanState();
            State.Corners = corners.Count;
            State.AreaClosed = areaClosed;
            State.AreaSquareMetres = corners.Count >= 3 ? math.abs(SignedArea(corners)) : 0f;
            if (!areaClosed || corners.Count < 3) { RefreshStage(); return; }
            // Every edit of a closed area comes through here (drags, removed corners, undo), so one check covers them all.
            if (CrossesItself(corners, true))
            {
                State.Stage = ToolStage.Drawing;
                Message("The outline crosses itself. Move a corner back.");
                Touch();
                return;
            }
            Poly.Bounds(corners.ToArray(), out var low, out var high);
            if (math.any(high - low > LargestSide))
            {
                State.Stage = ToolStage.Drawing;
                Message(Invariant($"The area is too large: keep it within {LargestSide / 1000f:0.0} km on each side."));
                Touch();
                return;
            }
            var settings = Mod.Settings ?? throw new InvalidOperationException("Stepped Streets settings are unavailable.");
            var boundary = corners.ToArray();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var snapshot = SiteSnapshot.Capture(EntityManager, World, boundary);
            var request = BuildRequest(boundary, snapshot, settings);
            double captureMilliseconds = clock.Elapsed.TotalMilliseconds;
            var cancel = new CancellationTokenSource();
            planCancel = cancel;
            taskVersion = ++requestedVersion;
            taskSnapshot = snapshot;
            taskRequest = request;
            taskRoadName = settings.RoadName;
            int version = taskVersion;
            planTask = Task.Run(() => NeighbourhoodPlanner.Plan(request, cancel.Token, roads =>
            {
                // Handed across threads by one reference write; the main thread keeps it only if its request is still current.
                // A superseded plan stays quiet, so it can't overwrite the current plan's roads in the one-slot hand-off.
                if (!cancel.IsCancellationRequested) Volatile.Write(ref earlyFromWorker, new EarlyRoadsHandOff { Version = version, Roads = roads });
            }), cancel.Token);
            State.Stage = ToolStage.Planning;
            Touch();
            Log(Invariant($"planning an area of {State.AreaSquareMetres / 10000f:0.0} ha with {boundary.Length} corners; snapshot {captureMilliseconds:0} ms, {snapshot.ExistingRoads.Length} existing road edges, {snapshot.BuildingCount} buildings"));
        }

        private void AdoptFinishedPlan()
        {
            var handOff = Interlocked.Exchange(ref earlyFromWorker, null);
            if (handOff != null && handOff.Version == requestedVersion && planTask != null && !planTask.IsCompleted)
            {
                var early = handOff.Roads;
                EarlyRoads = early;
                PlannedRequest = taskRequest;
                PlanVersion++;
                State.Summary = Invariant($"{early.RoadLength / 1000f:0.0} km of road planned; laying out the lots and the ground...");
                Touch();
            }
            var task = planTask;
            if (task == null || !task.IsCompleted) return;
            planTask = null;
            if (taskVersion != requestedVersion) return;
            if (task.IsCanceled) return;
            if (task.IsFaulted)
            {
                var error = task.Exception?.GetBaseException();
                if (error is OperationCanceledException) return;
                Mod.Logger.Error(error, "Stepped Streets: planning the area failed");
                EarlyRoads = null;
                PlanVersion++;
                State.Stage = ToolStage.Failed;
                State.Summary = "The area could not be planned. The log has the details.";
                State.Messages = new[] { State.Summary };
                Touch();
                return;
            }
            var plan = task.Result;
            Plan = plan;
            EarlyRoads = null;
            PlannedRequest = taskRequest;
            plannedRoadName = taskRoadName;
            plannedSnapshot = taskSnapshot;
            PlanVersion++;
            State.Summary = plan.SummaryLine;
            State.Messages = plan.Messages.ToArray();
            State.RoadKilometres = plan.RoadLength / 1000f;
            State.Lots = plan.Lots.Count;
            State.ZonedCells = plan.ZonedCells;
            State.EarthCubicMetres = plan.CutVolume + plan.FillVolume;
            State.SteepestRoadPercent = plan.SteepestRoadGrade * 100f;
            State.WildPercent = plan.WildShare * 100f;
            State.Connected = plan.Connected;
            State.Trees = plan.Trees.Count;
            State.ZoneCounts = new List<KeyValuePair<string, int>>(plan.ZoneCounts);
            State.Stage = plan.Buildable ? ToolStage.Planned : ToolStage.Drawing;
            if (!IsActive && State.Stage != ToolStage.Building) State.Stage = ToolStage.Closed;
            Touch();
            Log(Invariant($"plan in {plan.Milliseconds:0} ms ({plan.Timings}): {plan.Roads.Count} roads, {plan.RoadLength / 1000f:0.00} km, {plan.Lots.Count} lots, {plan.ZonedCells} cells, {plan.Trees.Count} trees, {plan.Walls.Count} wall pieces, connected {plan.Connected}; {plan.SummaryLine}"));
            foreach (var message in plan.Messages) Log(message);
        }

        private NeighbourhoodRequest BuildRequest(float2[] boundary, SiteSnapshot snapshot, Settings.SteppedStreetsSettings settings)
        {
            var road = RoadCatalog.Find(EntityManager, World.GetOrCreateSystemManaged<PrefabSystem>(), settings.RoadName);
            return new NeighbourhoodRequest
            {
                Boundary = boundary,
                GroundHeight = snapshot.GroundHeight,
                Blocked = snapshot.Blocked,
                ExistingRoads = snapshot.ExistingRoads,
                Style = settings.Style,
                RoadHalfWidth = road?.HalfWidth ?? 6f,
                BlockDepth = settings.BlockDepth,
                MaxRoadGrade = settings.MaxRoadGradePercent / 100f,
                // The Options page can set these past the mode's limits; the panel can't.
                SteepGround = settings.SteepGround,
                SteepestBuildable = math.min(settings.SteepestBuildablePercent, settings.SteepestBuildableLimit()) / 100f,
                Mix = settings.ZoneShares().ToArray(),
                Pattern = settings.MixPattern,
                LotWidthCells = settings.LotWidthCells,
                NarrowestWidthCells = math.min(settings.NarrowestWidthCells, settings.LotWidthCells),
                LotDepthCells = settings.DepthCells,
                LevelLots = settings.LevelLots,
                EarthworkLimit = math.min(settings.EarthworkLimit, settings.EarthworkCeiling()),
                BankGrade = settings.BankGrade,
                HighestStep = settings.HighestStep,
                LotSpacingCells = settings.LotSpacing,
                Walls = settings.RetainingWalls,
                Trees = settings.Trees,
                TreeDensity = settings.TreeDensityPercent / 100f,
                Seed = (uint)math.max(1, settings.Seed),
            };
        }

        private void RefreshStage()
        {
            State.Corners = corners.Count;
            State.AreaClosed = areaClosed;
            State.AreaSquareMetres = corners.Count >= 3 ? math.abs(SignedArea(corners)) : 0f;
            if (State.Stage == ToolStage.Building) return;
            if (!IsActive) State.Stage = ToolStage.Closed;
            else if (planTask != null) State.Stage = ToolStage.Planning;
            else if (Plan != null && Plan.Buildable) State.Stage = ToolStage.Planned;
            else if (State.Stage != ToolStage.Built && State.Stage != ToolStage.Failed || corners.Count == 0) State.Stage = ToolStage.Drawing;
            if (State.Stage == ToolStage.Drawing && Plan == null)
            {
                State.Summary = "";
                State.Messages = corners.Count == 0
                    ? new[] { "Draw the area on a hillside: click corners, then click the first corner to close it." }
                    : new string[0];
            }
            Touch();
        }

        private void ClearPlanState()
        {
            State.Summary = "";
            State.Messages = new string[0];
            State.RoadKilometres = 0f;
            State.Lots = State.ZonedCells = State.Trees = 0;
            State.EarthCubicMetres = State.SteepestRoadPercent = State.WildPercent = 0f;
            State.Connected = false;
            State.ZoneCounts = new List<KeyValuePair<string, int>>();
            State.Progress = 0f;
        }

        internal static float SignedArea(IReadOnlyList<float2> ring)
        {
            float area = 0f;
            for (int index = 0; index < ring.Count; index++)
            {
                float2 current = ring[index], next = ring[(index + 1) % ring.Count];
                area += current.x * next.y - next.x * current.y;
            }
            return area * 0.5f;
        }

        /// <summary>True when two edges of the outline that are not neighbours cross.</summary>
        internal static bool CrossesItself(IReadOnlyList<float2> ring, bool closed)
        {
            int count = ring.Count;
            int edges = closed ? count : count - 1;
            for (int first = 0; first < edges; first++)
                for (int second = first + 1; second < edges; second++)
                {
                    if (second == first + 1 || (closed && first == 0 && second == count - 1)) continue;
                    if (SegmentsCross(ring[first], ring[(first + 1) % count], ring[second], ring[(second + 1) % count])) return true;
                }
            return false;
        }

        private static bool SegmentsCross(float2 a, float2 b, float2 c, float2 d)
        {
            float Cross(float2 origin, float2 first, float2 second) => (first.x - origin.x) * (second.y - origin.y) - (first.y - origin.y) * (second.x - origin.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            try
            {
                // The sampled ground and entity handles belong to the old city.
                planCancel?.Cancel();
                planTask = null;
                corners.Clear();
                history.Clear();
                areaClosed = false;
                dragging = -1;
                Plan = null;
                EarlyRoads = null;
                PlannedRequest = null;
                plannedSnapshot = null;
                PlanVersion++;
                ResetBuild();
                ClearPlanState();
                State.Stage = ToolStage.Closed;
                rightClick.Reset();
                Touch();
                base.OnGamePreload(purpose, mode);
            }
            catch (Exception exception) { Fail(exception, "The neighbourhood tool could not reset for the next city."); }
        }

        private void EnableActions(bool enabled)
        {
            applyAction.shouldBeEnabled = enabled;
            secondaryApplyAction.shouldBeEnabled = enabled;
            cancelAction.shouldBeEnabled = enabled;
        }

        private static bool Pressed(IProxyAction action) => action != null && action.WasPressedThisFrame();

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
