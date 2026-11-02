// Placemaker — terraform brushes, lake water and zoning (GPL-3.0).
// Terraform: the desired ground (Core TerraformRaster) is applied directly with
// TerrainSystem.ApplyBrush (Shift kernel, one generated mask texture per tile) and verified against
// the CPU height readback. Fallback and water adapted from ParkManager's ParkLakeBuild (tomkpunkt,
// GPL-3.0): the game's level brush re-emitted for spots off target, then a constant-level water
// source fills the basin. Zoning follows GridRoadGenerator's Zoning
// definitions (Ti4goc, GPL-3.0): ZoningFlags.Zone|Marquee without Overwrite never repaints cells
// that are already zoned.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: standalone terrain apply system and report; removed dabs, water, zoning, pits, debug hooks and plan/group types; retained the original height copy for curtains and corrected low-strength rollback.
// The original header above describes the source file; none of its ParkManager or GridRoadGenerator code is included here.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Colossal.Logging;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SteppedStreets.Core.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace SteppedStreets.Build
{
    public sealed class TerrainBuildReport
    {
        // Inside texels only: at most 2% over 0.3 m, and none over 0.75 m.
        public bool Reached;
        public int Passes;
        public float WorstError;
        public float FractionOff;
        public int Curtains;
        public double Milliseconds;
        public string Summary;
    }

    public sealed partial class TerrainBuildSystem : GameSystemBase
    {
        private const float RasterCell = 1.75f;
        private const float Tolerance = 0.3f;
        private const float AllowedOffFraction = 0.02f;
        private const int MaximumPasses = 4;
        private const int WaitFrames = 6;
        private const int MaximumStaleWaits = 3;
        private const int TileTexels = 1024;
        private const int MaximumRasterCells = 1 << 20;
        private const double MaximumSeconds = 6.0;
        private const float ShiftToolStrength = 2000f;

        private static readonly ILog Logger = LogManager.GetLogger(nameof(SteppedStreets)).SetShowsErrorsInUI(false);
        private static int s_shiftOrientation = -1;

        private struct AppliedBrush
        {
            public Texture2D Texture;
            public Brush Brush;
            public Bounds2 Area;
        }

        private TerrainSystem _terrain;
        private TerrainHeightData _liveHeights;
        private TerrainHeightData _originalHeights;
        private TerraformRaster _raster;
        private Task<TerraformRaster> _rasterJob;
        private Action<TerrainBuildReport> _whenDone;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly List<AppliedBrush> _applied = new List<AppliedBrush>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private int _frame, _waitUntil, _passes, _orientation, _orientationTries, _staleWaits;
        private float _lastRms, _appliedDeltaTime, _rmsBeforeRevert;
        private bool _awaitingPass, _reverting, _verified;
        private string _failureAfterRevert;

        public bool Busy { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        /// <summary>Starts a terrain build; completion is delivered once on the calling game thread.</summary>
        public void Begin(IReadOnlyList<PlannedTerraform> patches, float bankWidth, Action<TerrainBuildReport> whenDone)
        {
            if (Busy)
            {
                Logger.Warn("Stepped Streets terrain: Begin ignored while a build is busy.");
                return;
            }

            _clock.Restart();
            Busy = true;
            _whenDone = whenDone;
            _verified = _awaitingPass = _reverting = false;
            _failureAfterRevert = null;
            _frame = _waitUntil = _passes = _orientationTries = _staleWaits = 0;
            _orientation = s_shiftOrientation >= 0 ? s_shiftOrientation : 0;
            _raster = null;

            try
            {
                if (!math.isfinite(bankWidth) || bankWidth < 0f) throw new ArgumentOutOfRangeException(nameof(bankWidth));
                var items = TerraformApplyMath.PreparePatches(patches, out var vertexHeights);
                _originalHeights = CopyHeights();
                var original = _originalHeights;
                // The worker owns no live game data; the copied arrays stay alive until it finishes and curtains are measured.
                _rasterJob = Task.Run(() => TerraformRaster.Build(items, RasterCell, bankWidth, MaximumRasterCells,
                    vertexHeights, position => SampleHeight(ref original, position)));
            }
            catch (Exception exception)
            {
                Finish(exception.Message, preventSuccess: true);
            }
        }

        /// <summary>Uses the verified target after success, otherwise freshly read live terrain.</summary>
        public float HeightAt(float2 position)
        {
            ReadLiveHeights();
            if (_verified && _raster != null && _raster.TrySample(position, LiveHeight, out float height)) return height;
            return LiveHeight(position);
        }

        protected override void OnUpdate()
        {
            if (!Busy) return;
            _frame++;
            try
            {
                if (_rasterJob != null)
                {
                    if (_clock.Elapsed.TotalSeconds >= MaximumSeconds) { Finish("raster preparation time limit", preventSuccess: true); return; }
                    if (!_rasterJob.IsCompleted) return;
                    var job = _rasterJob;
                    _rasterJob = null;
                    _raster = job.GetAwaiter().GetResult();
                    if (_raster == null) { Finish("no raster", preventSuccess: true); return; }
                    Logger.Info($"Stepped Streets terrain: raster {_raster.Width}x{_raster.Height} @ {_raster.Cell:F2} m.");
                }
                if (_frame < _waitUntil) return;

                // The deadline stops new passes; an outstanding calibration rollback must settle before the final live measurement.
                ReadLiveHeights();
                var deltas = _raster.Deltas(LiveHeight, Tolerance, out var residual);
                if (_reverting)
                {
                    if (WaitForReadback(residual.Rms, _rmsBeforeRevert)) return;
                    _reverting = false;
                    DestroyTextures();
                    if (_failureAfterRevert != null) { Finish(_failureAfterRevert, preventSuccess: true); return; }
                    _orientation = (_orientation + 1) & 3;
                }
                else if (_awaitingPass)
                {
                    if (WaitForReadback(residual.Rms, _lastRms)) return;
                    _awaitingPass = false;
                    bool improved = residual.Rms < _lastRms * 0.8f;
                    if (!improved && s_shiftOrientation < 0)
                    {
                        _orientationTries++;
                        // ponytail: four failed mirrors stop this spike; add Placemaker's credited dabs fallback if game tests need it.
                        string failure = _orientationTries >= 4 ? "four brush orientations failed calibration"
                            : _clock.Elapsed.TotalSeconds >= MaximumSeconds ? "time limit during calibration" : null;
                        BeginRevert(residual.Rms, failure);
                        return;
                    }
                    if (improved && s_shiftOrientation < 0)
                    {
                        s_shiftOrientation = _orientation;
                        Logger.Info($"Stepped Streets terrain: brush orientation {_orientation} confirmed.");
                    }
                    DestroyTextures();
                }

                Logger.Info($"Stepped Streets terrain pass {_passes}: {residual.InsideOff}/{residual.Inside} inside texels off, worst {residual.InsideMaxAbs:F2} m, rms {residual.Rms:F2} m.");
                if (residual.Inside == 0) { Finish("no inside texels", preventSuccess: true); return; }
                if (residual.Done(Tolerance, AllowedOffFraction)) { Finish("target measured"); return; }
                if (_clock.Elapsed.TotalSeconds >= MaximumSeconds) { Finish("time limit"); return; }
                if (_passes >= MaximumPasses) { Finish("pass limit"); return; }

                _lastRms = residual.Rms;
                _passes++;
                int applications = ApplyShiftPass(deltas);
                if (applications == 0) { Finish("no brush changes above minimum"); return; }
                _awaitingPass = true;
                _waitUntil = _frame + WaitFrames;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Stepped Streets terrain apply failed.");
                if (!_reverting && s_shiftOrientation < 0 && _applied.Count > 0)
                {
                    try { BeginRevert(_lastRms, exception.Message); return; }
                    catch (Exception rollbackException) { Logger.Error(rollbackException, "Stepped Streets terrain rollback failed."); }
                }
                Finish(exception.Message, preventSuccess: true);
            }
        }

        private bool WaitForReadback(float currentRms, float previousRms)
        {
            if (_clock.Elapsed.TotalSeconds < MaximumSeconds && _staleWaits < MaximumStaleWaits
                && math.abs(currentRms - previousRms) < 0.01f * math.max(0.05f, previousRms))
            {
                _staleWaits++;
                _waitUntil = _frame + WaitFrames;
                return true;
            }
            _staleWaits = 0;
            return false;
        }

        private void BeginRevert(float currentRms, string failure)
        {
            Logger.Info($"Stepped Streets terrain: reverting orientation {_orientation}, rms {_lastRms:F2} -> {currentRms:F2} m.");
            _awaitingPass = false;
            _reverting = true;
            _rmsBeforeRevert = currentRms;
            _failureAfterRevert = failure;
            _staleWaits = 0;
            foreach (var applied in _applied)
            {
                var brush = applied.Brush;
                brush.m_Strength = TerraformApplyMath.ReverseStrength(brush.m_Strength, _appliedDeltaTime, UnityEngine.Time.unscaledDeltaTime);
                _terrain.ApplyBrush(TerraformingType.Shift, applied.Area, brush, applied.Texture);
            }
            _applied.Clear();
            _waitUntil = _frame + WaitFrames;
        }

        private int ApplyShiftPass(float[] deltas)
        {
            bool flipX = (_orientation & 1) != 0, flipY = (_orientation & 2) != 0;
            int applications = 0;
            _appliedDeltaTime = UnityEngine.Time.unscaledDeltaTime;
            foreach (var tile in _raster.Tiles(TileTexels))
            {
                float metres = TerraformRaster.Masks(deltas, _raster.Width, _raster.Height, tile, flipX, flipY,
                    Tolerance * 0.25f, out var raise, out var lower, out bool anyRaise, out bool anyLower);
                if (metres <= 0f) continue;
                float strength = TerraformRaster.ShiftStrength(metres, _appliedDeltaTime, ShiftToolStrength, out float maskScale);
                _raster.TileWorld(tile, out var center, out float size);
                if (anyRaise) { ApplyShift(raise, tile.Size, maskScale, center, size, strength); applications++; }
                if (anyLower) { ApplyShift(lower, tile.Size, maskScale, center, size, -strength); applications++; }
            }
            return applications;
        }

        private void ApplyShift(float[] mask, int texels, float maskScale, float2 center, float size, float strength)
        {
            var texture = MaskTexture(mask, texels, maskScale);
            float height = LiveHeight(center);
            var brush = new Brush
            {
                m_Tool = Entity.Null, // The game's default Shift tool speed is 2000.
                m_Position = new float3(center.x, height, center.y),
                m_Target = new float3(center.x, height, center.y),
                m_Start = new float3(center.x, height, center.y),
                m_Angle = 0f, m_Size = size, m_Strength = strength, m_Opacity = 1f
            };
            var area = new Bounds2(center - size * 0.5f, center + size * 0.5f);
            _terrain.ApplyBrush(TerraformingType.Shift, area, brush, texture);
            _applied.Add(new AppliedBrush { Texture = texture, Brush = brush, Area = area });
        }

        private Texture2D MaskTexture(float[] mask, int texels, float scale)
        {
            var texture = new Texture2D(texels, texels, TextureFormat.RGBAHalf, false, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SteppedStreetsMask"
            };
            _textures.Add(texture);
            var pixels = new Color[mask.Length];
            for (int index = 0; index < pixels.Length; index++)
            {
                float value = mask[index] * scale;
                pixels[index] = new Color(value, value, value, value);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private TerrainHeightData CopyHeights()
        {
            ReadLiveHeights();
            NativeArray<ushort> heights = default, downscaledHeights = default;
            try
            {
                heights = new NativeArray<ushort>(_liveHeights.heights, Allocator.Persistent);
                downscaledHeights = _liveHeights.downscaledHeights.IsCreated
                    ? new NativeArray<ushort>(_liveHeights.downscaledHeights, Allocator.Persistent)
                    : new NativeArray<ushort>(0, Allocator.Persistent);
                return new TerrainHeightData(heights, downscaledHeights, _liveHeights.resolution,
                    _liveHeights.scale, _liveHeights.offset, _liveHeights.hasBackdrop);
            }
            catch
            {
                if (heights.IsCreated) heights.Dispose();
                if (downscaledHeights.IsCreated) downscaledHeights.Dispose();
                throw;
            }
        }

        private void ReadLiveHeights()
        {
            _liveHeights = _terrain.GetHeightData(true);
            if (!_liveHeights.isCreated) throw new InvalidOperationException("Live terrain heights are unavailable.");
        }

        private float LiveHeight(float2 position) => SampleHeight(ref _liveHeights, position);

        private static float SampleHeight(ref TerrainHeightData data, float2 position)
        {
            float height = TerrainUtils.SampleHeight(ref data, new float3(position.x, 0f, position.y));
            if (!math.isfinite(height)) throw new InvalidOperationException("A terrain height is not finite.");
            return height;
        }

        private void Finish(string reason, bool preventSuccess = false, bool measure = true)
        {
            var report = new TerrainBuildReport { Passes = _passes, WorstError = float.PositiveInfinity, FractionOff = 1f };
            float curtainMaximum = 0f;
            try
            {
                if (measure && _raster != null && _originalHeights.isCreated)
                {
                    ReadLiveHeights();
                    _raster.Deltas(LiveHeight, Tolerance, out var residual);
                    report.WorstError = residual.InsideMaxAbs;
                    report.FractionOff = residual.Inside > 0 ? (float)residual.InsideOff / residual.Inside : 1f;
                    report.Reached = !preventSuccess && residual.Inside > 0 && residual.Done(Tolerance, AllowedOffFraction);
                    var original = _originalHeights;
                    var curtains = TerrainMetrics.Curtains(LiveHeight, position => SampleHeight(ref original, position),
                        _raster.Origin, _raster.Origin + new float2(_raster.Width, _raster.Height) * _raster.Cell);
                    report.Curtains = curtains.Count;
                    curtainMaximum = curtains.Max;
                }
            }
            catch (Exception exception)
            {
                report.Reached = false;
                reason += "; final measurement failed: " + exception.Message;
                Logger.Error(exception, "Stepped Streets terrain final measurement failed.");
            }

            DestroyTextures();
            ReleaseOriginalHeights();
            _verified = report.Reached;
            if (!_verified) _raster = null;
            Busy = false;
            _clock.Stop();
            report.Milliseconds = _clock.Elapsed.TotalMilliseconds;
            report.Summary = string.Format(CultureInfo.InvariantCulture,
                "Stepped Streets terrain: reached={0}, passes={1}, worst={2:F3} m, fractionOff={3:F4}, curtains={4} (max {5:F3} m), milliseconds={6:F1}; {7}.",
                report.Reached, report.Passes, report.WorstError, report.FractionOff, report.Curtains, curtainMaximum, report.Milliseconds, reason);
            if (report.Reached) Logger.Info(report.Summary); else Logger.Warn(report.Summary);
            var callback = _whenDone;
            _whenDone = null;
            try { callback?.Invoke(report); }
            catch (Exception exception) { Logger.Error(exception, "Stepped Streets terrain completion callback failed."); }
        }

        private void DestroyTextures()
        {
            foreach (var texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
            _textures.Clear();
            _applied.Clear();
        }

        private void ReleaseOriginalHeights()
        {
            var original = _originalHeights;
            var job = _rasterJob;
            _originalHeights = default;
            _rasterJob = null;
            if (job != null)
            {
                // A timed-out worker may still be reading the copy; it must release it after its last sample.
                job.ContinueWith(completed => { var observedException = completed.Exception; DisposeHeights(original); }, TaskScheduler.Default);
            }
            else DisposeHeights(original);
        }

        private static void DisposeHeights(TerrainHeightData data)
        {
            if (data.heights.IsCreated) data.heights.Dispose();
            if (data.downscaledHeights.IsCreated) data.downscaledHeights.Dispose();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            if (Busy) Finish("game reloading", preventSuccess: true, measure: false);
            _verified = false;
            _raster = null;
            base.OnGamePreload(purpose, mode);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Busy) Finish("system destroyed", preventSuccess: true, measure: false);
            base.OnDestroy();
        }
    }
}
