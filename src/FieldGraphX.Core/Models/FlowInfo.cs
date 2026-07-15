using System;
using System.Collections.Generic;

namespace FieldGraphX.Core.Models
{
    /// <summary>Lifecycle status of a cloud flow (workflow table).</summary>
    public enum FlowStatus
    {
        /// <summary>Turned on and running.</summary>
        Active,
        /// <summary>Turned off by the owner (statecode = 0).</summary>
        Inactive,
        /// <summary>Never published / still a draft (type = 2).</summary>
        Draft
    }

    /// <summary>One fully parsed cloud flow.</summary>
    public sealed class FlowInfo
    {
        public Guid WorkflowId { get; set; }

        /// <summary>workflow.workflowidunique — used for the Power Automate portal URL.</summary>
        public Guid WorkflowIdUnique { get; set; }

        public string Name { get; set; } = string.Empty;

        public FlowStatus Status { get; set; } = FlowStatus.Active;

        /// <summary>Never null; Kind == None when clientdata was missing or unparseable.</summary>
        public TriggerInfo Trigger { get; set; } = new TriggerInfo();

        public IReadOnlyList<UpdateActionInfo> WriteActions { get; set; } =
            Array.Empty<UpdateActionInfo>();
    }
}
