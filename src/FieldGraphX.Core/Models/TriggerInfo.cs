using System;
using System.Collections.Generic;

namespace FieldGraphX.Core.Models
{
    /// <summary>How a cloud flow is started.</summary>
    public enum TriggerKind
    {
        /// <summary>clientdata missing or unparseable.</summary>
        None,
        /// <summary>Dataverse row-change trigger (when a row is added/modified/deleted).</summary>
        CdsRowChange,
        /// <summary>Manual / instant trigger (button, HTTP request, PowerApps, ...).</summary>
        Manual,
        /// <summary>Recurrence / schedule trigger.</summary>
        Scheduled,
        /// <summary>Any other connector trigger (mail arrived, Teams message, ...).</summary>
        Other
    }

    /// <summary>Row change types a Dataverse trigger subscribes to, or a write action emits.</summary>
    [Flags]
    public enum ChangeType
    {
        None = 0,
        Create = 1,
        Update = 2,
        Delete = 4
    }

    /// <summary>Parsed trigger metadata from a flow's clientdata JSON.</summary>
    public sealed class TriggerInfo
    {
        public TriggerKind Kind { get; set; } = TriggerKind.None;

        /// <summary>JSON key of the trigger node, e.g. "When_a_row_is_modified".</summary>
        public string TriggerKey { get; set; } = string.Empty;

        /// <summary>Entity name exactly as stored in clientdata (may be a plural EntitySetName).</summary>
        public string RawEntityName { get; set; } = string.Empty;

        /// <summary>Entity logical name after resolution via <see cref="Analysis.IEntityNameResolver"/>.</summary>
        public string EntityLogicalName { get; set; } = string.Empty;

        /// <summary>Which row changes fire this trigger (from subscriptionRequest/message).</summary>
        public ChangeType ChangeTypes { get; set; } = ChangeType.None;

        /// <summary>Parsed, trimmed, lowercased filtering attributes. Empty = broad trigger.</summary>
        public IReadOnlyList<string> FilteringAttributes { get; set; } = Array.Empty<string>();

        /// <summary>Raw OData filter expression (run condition), if present. Informational only.</summary>
        public string FilterExpression { get; set; }

        /// <summary>subscriptionRequest/scope raw value (informational).</summary>
        public string Scope { get; set; }

        /// <summary>
        /// True for a Dataverse trigger without filtering attributes: it fires on EVERY
        /// change of the entity, so it must be reported for any searched field of that entity.
        /// </summary>
        public bool IsBroadTrigger =>
            Kind == TriggerKind.CdsRowChange && FilteringAttributes.Count == 0;
    }
}
