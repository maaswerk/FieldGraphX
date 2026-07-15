using System;
using System.Collections.Generic;
using System.Threading;

namespace FieldGraphX.Core.Abstractions
{
    /// <summary>
    /// One raw row from the workflow table. Deliberately free of any Dataverse SDK types
    /// so the core library stays netstandard2.0 and unit-testable without a connection.
    /// </summary>
    public sealed class FlowRecord
    {
        public Guid WorkflowId { get; set; }
        public Guid WorkflowIdUnique { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ClientData { get; set; } = string.Empty;
        public int StateCode { get; set; } = 1;
        public int Type { get; set; } = 1;
    }

    /// <summary>Provides all cloud flows of the connected environment.</summary>
    public interface IFlowRepository
    {
        IReadOnlyList<FlowRecord> GetAllCloudFlows(
            Action<int> progress = null,
            CancellationToken cancellationToken = default);
    }
}
