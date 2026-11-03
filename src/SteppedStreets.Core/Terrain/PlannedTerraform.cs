// Placemaker — engine-free contracts shared by every area generator.
//
// Everything in Core/ is pure .NET + Unity.Mathematics: no Game.*, Colossal.*, Unity.Entities.
// That keeps generators runnable off the main thread and testable without the game.
// The game layer (Game/) turns an AreaPlan into CreationDefinitions; generators never
// touch ECS.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: extracted TerraformKind and PlannedTerraform from Core/Contracts.cs:849-872; namespace changed; water fields and Apron removed.

using System;
using Unity.Mathematics;

namespace SteppedStreets.Core.Terrain
{
    public enum TerraformKind { Level = 0, Lower = 1, Raise = 2 }

    public sealed class PlannedTerraform
    {
        public float3[] Ring = Array.Empty<float3>();   // footprint
        public TerraformKind Kind;
        public float TargetHeight;
        /// <summary>True = permanent change the player must be warned about (e.g. lake basin).</summary>
        public bool Permanent;
        /// <summary>
        /// (s20, appended) A sloped patch: the target ground is the surface through the ring's vertex heights
        /// (Ring[i].y, mean-value interpolation — bilinear-like on a quad, exact on a triangle, linear along every
        /// edge), not one flat level. Sculpted terrain (Common/Sculpt) emits a grid of sloped quads whose shared
        /// corners carry the same height, so the result is continuous: no terraced steps. TargetHeight stays the
        /// centre height for legacy readers. See Core/Build/TerraformSurface.
        /// </summary>
        public bool Sloped;
    }
}
