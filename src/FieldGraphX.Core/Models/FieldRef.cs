using System;

namespace FieldGraphX.Core.Models
{
    /// <summary>
    /// Identifies a single Dataverse field: entity logical name + attribute logical name.
    /// Both parts are stored lowercase so the struct can be used as a dictionary key
    /// with plain equality.
    /// </summary>
    public readonly struct FieldRef : IEquatable<FieldRef>
    {
        public string EntityLogicalName { get; }
        public string AttributeLogicalName { get; }

        public FieldRef(string entityLogicalName, string attributeLogicalName)
        {
            EntityLogicalName = (entityLogicalName ?? string.Empty).Trim().ToLowerInvariant();
            AttributeLogicalName = (attributeLogicalName ?? string.Empty).Trim().ToLowerInvariant();
        }

        public bool IsEmpty =>
            string.IsNullOrEmpty(EntityLogicalName) || string.IsNullOrEmpty(AttributeLogicalName);

        public bool Equals(FieldRef other) =>
            string.Equals(EntityLogicalName, other.EntityLogicalName, StringComparison.Ordinal) &&
            string.Equals(AttributeLogicalName, other.AttributeLogicalName, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is FieldRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((EntityLogicalName?.GetHashCode() ?? 0) * 397) ^
                       (AttributeLogicalName?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() => $"{EntityLogicalName}.{AttributeLogicalName}";
    }
}
