// Temporary: the lead deletes this file when merging wave 3; the real systems come from Codex A and Codex B.
using System;
using System.Collections.Generic;
using Game;
using SteppedStreets.Core.Terrain;
using Unity.Mathematics;

namespace SteppedStreets.Build
{
    public sealed class TerrainBuildReport
    {
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
        public bool Busy { get; }

        public void Begin(IReadOnlyList<PlannedTerraform> patches, float bankWidth, Action<TerrainBuildReport> whenDone)
        {
            Mod.Logger.Info("Stepped Streets: terrain build stub");
            whenDone(new TerrainBuildReport { Summary = "stub" });
        }

        public float HeightAt(float2 position) => 0f;

        protected override void OnUpdate()
        {
        }
    }

    public sealed class ZoneBuildReport
    {
        public int Requested;
        public int Zoned;
        public string Summary;
    }

    public sealed partial class ZoneBuildSystem : GameSystemBase
    {
        public bool Busy { get; }

        public void Begin(IReadOnlyList<float2[]> quads, string zonePrefabName, Action<ZoneBuildReport> whenDone)
        {
            Mod.Logger.Info("Stepped Streets: zone build stub");
            whenDone(new ZoneBuildReport { Requested = quads.Count, Summary = "stub" });
        }

        protected override void OnUpdate()
        {
        }
    }
}
