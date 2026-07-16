namespace FieldGraphX.Core.Analysis
{
    public sealed class AnalyzerOptions
    {
        /// <summary>Maximum BFS distance from the seed field.</summary>
        public int MaxDepth { get; set; } = 10;

        /// <summary>Include flows that were never published.</summary>
        public bool IncludeDrafts { get; set; } = true;

        /// <summary>Include flows that are turned off.</summary>
        public bool IncludeInactive { get; set; } = true;

        /// <summary>
        /// Show flows whose Dataverse trigger has no filtering attributes (they fire on
        /// EVERY change of the entity, so they are affected by the searched field too).
        /// </summary>
        public bool IncludeBroadTriggers { get; set; } = true;

        /// <summary>
        /// Follow the write actions of broad-trigger flows further downstream.
        /// Off by default: on entities with many broad flows this explodes the graph.
        /// </summary>
        public bool RecurseThroughBroadTriggers { get; set; }
    }
}
