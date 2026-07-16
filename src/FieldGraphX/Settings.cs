namespace FieldGraphX
{
    /// <summary>Persisted via XrmToolBox SettingsManager on load/close of the tool.</summary>
    public class Settings
    {
        public string LastEntity { get; set; } = string.Empty;
        public string LastField { get; set; } = string.Empty;
        public int MaxDepth { get; set; } = 10;
        public bool IncludeDrafts { get; set; } = true;
        public bool IncludeInactive { get; set; } = true;
        public bool IncludeBroadTriggers { get; set; } = true;
        public bool RecurseThroughBroadTriggers { get; set; }
    }
}
