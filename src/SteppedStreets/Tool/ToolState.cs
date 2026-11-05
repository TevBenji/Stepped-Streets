// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;

namespace SteppedStreets.Tool
{
    /// <summary>
    /// Closed: the tool is not active. Drawing: corners are being placed (or the area has no plan yet). Planning: the worker is
    /// planning the closed area. Planned: a plan is shown and can be built. Building, Built, Failed: the build and its result.
    /// </summary>
    public enum ToolStage { Closed, Drawing, Planning, Planned, Building, Built, Failed }

    /// <summary>What the panel shows; the tool bumps <see cref="Version"/> whenever any of it changes.</summary>
    public sealed class ToolState
    {
        public ToolStage Stage;
        public int Corners;
        public bool AreaClosed;
        public float AreaSquareMetres;
        public string Summary = "";
        public string[] Messages = new string[0];

        // The plan's numbers, for the summary card (zero when there is no plan).
        public float RoadKilometres;
        public int Lots;
        public int ZonedCells;
        public float EarthCubicMetres;
        public float SteepestRoadPercent;
        public float WildPercent;
        public bool Connected;
        public int Trees;
        public List<KeyValuePair<string, int>> ZoneCounts = new List<KeyValuePair<string, int>>();

        /// <summary>Build progress, 0 to 1, while building.</summary>
        public float Progress;

        public int Version;

        /// <summary>The panel's "Build this neighbourhood?" dialog is open.</summary>
        public bool Confirming;

        /// <summary>The planned service buildings by prefab name.</summary>
        public List<KeyValuePair<string, int>> ServiceCounts = new List<KeyValuePair<string, int>>();
    }
}
