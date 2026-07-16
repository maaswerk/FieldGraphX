using System;
using System.Collections.Generic;

namespace FieldGraphX.Core.Analysis
{
    /// <summary>
    /// Maps any entity name found in clientdata (logical name, EntitySetName or
    /// LogicalCollectionName — Power Automate stores the plural set name on actions)
    /// to the entity logical name.
    /// </summary>
    public interface IEntityNameResolver
    {
        /// <summary>Returns the logical name, or the input (lowercased) when unknown.</summary>
        string ToLogicalName(string anyEntityName);

        /// <summary>
        /// True when the name is a known entity logical name (from metadata). Used by the
        /// parser to strip polymorphic lookup suffixes ("…_msdyn_workorder@odata.bind").
        /// </summary>
        bool IsKnownLogicalName(string logicalName);
    }

    /// <summary>
    /// Dictionary-backed resolver. The plugin fills the map from Dataverse metadata
    /// (EntitySetName / LogicalCollectionName → LogicalName). For names not in the map
    /// a conservative plural heuristic is applied (…ies→y, …es→∅, …s→∅) so the analyzer
    /// still works when metadata is unavailable (e.g. in unit tests).
    /// </summary>
    public sealed class EntityNameResolver : IEntityNameResolver
    {
        private readonly Dictionary<string, string> _map;
        private readonly HashSet<string> _knownLogicalNames;

        public EntityNameResolver(
            IEnumerable<KeyValuePair<string, string>> collectionToLogicalName = null)
        {
            _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _knownLogicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (collectionToLogicalName == null) return;

            foreach (var pair in collectionToLogicalName)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;
                _map[pair.Key.Trim()] = pair.Value.Trim().ToLowerInvariant();
                _knownLogicalNames.Add(pair.Value.Trim());
            }
        }

        public bool IsKnownLogicalName(string logicalName) =>
            !string.IsNullOrWhiteSpace(logicalName) && _knownLogicalNames.Contains(logicalName.Trim());

        public string ToLogicalName(string anyEntityName)
        {
            if (string.IsNullOrWhiteSpace(anyEntityName)) return string.Empty;

            var name = anyEntityName.Trim().ToLowerInvariant();

            if (_knownLogicalNames.Contains(name)) return name;
            if (_map.TryGetValue(name, out var logical)) return logical;

            return HeuristicSingular(name);
        }

        /// <summary>
        /// Fallback for names missing from metadata. Handles the standard OData
        /// pluralisation rules: opportunities→opportunity, addresses→address, accounts→account.
        /// </summary>
        private static string HeuristicSingular(string name)
        {
            if (name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 3)
                return name.Substring(0, name.Length - 3) + "y";

            // "...ses"/"...xes"/"...zes"/"...ches"/"...shes" → strip "es"
            if ((name.EndsWith("ses", StringComparison.Ordinal) ||
                 name.EndsWith("xes", StringComparison.Ordinal) ||
                 name.EndsWith("zes", StringComparison.Ordinal) ||
                 name.EndsWith("ches", StringComparison.Ordinal) ||
                 name.EndsWith("shes", StringComparison.Ordinal)) && name.Length > 3)
                return name.Substring(0, name.Length - 2);

            // Plain "...s" plural — but never mangle words that end in "ss" ("address").
            if (name.EndsWith("s", StringComparison.Ordinal) &&
                !name.EndsWith("ss", StringComparison.Ordinal) && name.Length > 1)
                return name.Substring(0, name.Length - 1);

            return name;
        }
    }
}
