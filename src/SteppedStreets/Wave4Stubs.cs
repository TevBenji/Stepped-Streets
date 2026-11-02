// WAVE 4 STUB: the lead deletes this file at merge.
using System;
using System.Collections.Generic;
using Game;
using SteppedStreets.Core.Planning;

namespace SteppedStreets.Build
{
    public sealed class WallBuildReport { public int Requested; public int Placed; public string Summary; }
    public sealed partial class WallBuildSystem : GameSystemBase
    {
        public bool Busy { get; }
        public void Begin(IReadOnlyList<WallSegment> walls, Action<WallBuildReport> whenDone)
        {
            whenDone?.Invoke(new WallBuildReport { Requested = walls.Count, Summary = "Wall build is a Wave 4 stub." });
        }
        protected override void OnUpdate() { }
    }
}
