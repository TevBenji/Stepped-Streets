// WAVE 4 STUB: the lead deletes this file at merge.
using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;

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
    public sealed partial class StepStreetToolSystem : ToolBaseSystem
    {
        public ToolState State { get; } = new ToolState();
        public override string toolID => "Stepped Streets";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;
        protected override JobHandle OnUpdate(JobHandle inputDependencies) => inputDependencies;
        public void Toggle() { }
        public void Open() { }
        public void Close() { }
        public void ClearSelection() { }
        public void Build() { }
        public void SettingsChanged() { }
    }
}
