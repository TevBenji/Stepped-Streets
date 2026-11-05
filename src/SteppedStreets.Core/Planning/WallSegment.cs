// Stepped Streets, garden walls. Original work by TevBenji, 2026-11. GPL-3.0.
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    /// <summary>A wall's base in world coordinates, with the bank on its right in the (x, z) plan.</summary>
    public struct WallSegment
    {
        public float3 Start;
        public float3 End;
    }
}
