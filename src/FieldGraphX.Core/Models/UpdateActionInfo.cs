using System;
using System.Collections.Generic;

namespace FieldGraphX.Core.Models
{
    /// <summary>Kind of Dataverse write action, derived from inputs.host.operationId.</summary>
    public enum WriteActionKind
    {
        /// <summary>operationId was absent — treated as a potential create or update.</summary>
        Unknown,
        Update,
        Create,
        Upsert
    }

    /// <summary>One Dataverse write action (Update/Create/Upsert a row) found in a flow.</summary>
    public sealed class UpdateActionInfo
    {
        /// <summary>JSON key of the action node (shown in the detail panel).</summary>
        public string ActionName { get; set; } = string.Empty;

        public WriteActionKind Kind { get; set; } = WriteActionKind.Unknown;

        /// <summary>Entity name exactly as stored in clientdata (usually a plural EntitySetName).</summary>
        public string RawEntityName { get; set; } = string.Empty;

        /// <summary>Entity logical name after resolution via <see cref="Analysis.IEntityNameResolver"/>.</summary>
        public string EntityLogicalName { get; set; } = string.Empty;

        /// <summary>Normalized field logical names written by this action.</summary>
        public IReadOnlyList<string> Fields { get; set; } = Array.Empty<string>();

        /// <summary>Which trigger change types this action can fire on other flows.</summary>
        public ChangeType EmittedChange
        {
            get
            {
                switch (Kind)
                {
                    case WriteActionKind.Create: return ChangeType.Create;
                    case WriteActionKind.Update: return ChangeType.Update;
                    default: return ChangeType.Create | ChangeType.Update;
                }
            }
        }
    }
}
