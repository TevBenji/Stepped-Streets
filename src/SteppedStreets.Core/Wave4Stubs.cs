// WAVE 4 STUB: the lead deletes this file at merge.
using System.Collections.Generic;
using Unity.Mathematics;

namespace SteppedStreets.Core.Planning
{
    public enum StepLook { PlantedBank, LowWall }
    public struct WallSegment { public float3 Start; public float3 End; }
    public static class StepPlanWalls
    {
        public static List<WallSegment> Segments(StepPlan plan, float bankGrade) => new List<WallSegment>();
    }
}
