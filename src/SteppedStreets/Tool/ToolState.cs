// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
namespace SteppedStreets.Tool
{
    public enum ToolStage { Closed, Picking, Planned, Building, Built, Failed }

    public sealed class ToolState
    {
        public ToolStage Stage;
        public string StreetName = "";
        public float HoverGradePercent = float.NaN;
        public bool HoverTooFlat;
        public string Summary = "";
        public string[] Messages = new string[0];
        public int Version;
    }
}
