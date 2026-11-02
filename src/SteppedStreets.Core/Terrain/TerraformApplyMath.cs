// Placemaker — terraform brushes, lake water and zoning (GPL-3.0).
// Terraform: the desired ground (Core TerraformRaster) is applied directly with
// TerrainSystem.ApplyBrush (Shift kernel, one generated mask texture per tile) and verified against
// the CPU height readback. Fallback and water adapted from ParkManager's ParkLakeBuild (tomkpunkt,
// GPL-3.0): the game's level brush re-emitted for spots off target, then a constant-level water
// source fills the basin. Zoning follows GridRoadGenerator's Zoning
// definitions (Ti4goc, GPL-3.0): ZoningFlags.Zone|Marquee without Overwrite never repaints cells
// that are already zoned.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: extracted patch inputs and Shift rollback timing from Game/Build/BuildService.Terrain.cs:94-114,404-416; added input validation, snapshot copies and the inverse low-strength curve; removed game and plan types.
// The original header above describes the source file; none of its ParkManager or GridRoadGenerator code is included here.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Terrain
{
    public static class TerraformApplyMath
    {
        /// <summary>Copies patch geometry before a worker uses it; later patches keep their overwrite order.</summary>
        public static List<(float2[] ring, float target, int layer)> PreparePatches(IReadOnlyList<PlannedTerraform> patches,
            out List<float[]> vertexHeights)
        {
            if (patches == null || patches.Count == 0) throw new ArgumentException("No terrain patches supplied.", nameof(patches));
            var items = new List<(float2[] ring, float target, int layer)>();
            vertexHeights = new List<float[]>();
            foreach (var patch in patches)
            {
                if (patch == null || patch.Ring == null || patch.Ring.Length < 3 || !math.isfinite(patch.TargetHeight))
                    throw new ArgumentException("A terrain patch has no finite target or valid ring.", nameof(patches));
                var ring = new float2[patch.Ring.Length];
                for (int index = 0; index < ring.Length; index++)
                {
                    if (!math.all(math.isfinite(patch.Ring[index]))) throw new ArgumentException("A terrain vertex is not finite.", nameof(patches));
                    ring[index] = patch.Ring[index].xz;
                }
                items.Add((ring, patch.TargetHeight, 0));
                vertexHeights.Add(TerraformSurface.VertexHeights(patch));
            }
            return items;
        }

        /// <summary>Reverse a linear Shift strength produced by TerraformRaster.ShiftStrength, using the same mask.</summary>
        public static float ReverseStrength(float appliedStrength, float appliedDeltaTime, float currentDeltaTime)
        {
            float strength = -appliedStrength * math.clamp(appliedDeltaTime, 0.05f, 1f)
                / math.clamp(currentDeltaTime, 0.05f, 1f);
            const float threshold = 0.002f;
            // A slower rollback frame can enter the game's nonlinear range; undo that curve before dispatch.
            return math.abs(strength) < threshold
                ? math.sign(strength) * threshold * math.pow(math.abs(strength) / threshold, 1f / 0.055f)
                : strength;
        }
    }
}
