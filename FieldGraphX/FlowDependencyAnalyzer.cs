using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;

namespace FieldGraphX.Logic
{
    // ──────────────────────────────────────────────────────────────────────────
    // Models
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies what kind of trigger a Cloud Flow has.
    /// </summary>
    public enum TriggerKind
    {
        /// <summary>CDS/Dataverse row-change trigger (When a row is added/modified/deleted).</summary>
        CdsRowChange,
        /// <summary>Manual / instant trigger (button, HTTP request, PowerApps, etc.).</summary>
        Manual,
        /// <summary>Schedule / recurrence trigger.</summary>
        Scheduled,
        /// <summary>Any other connector trigger (e-mail arrived, Teams message, etc.).</summary>
        Other
    }

    /// <summary>
    /// Parsed trigger metadata from a Cloud Flow's clientdata JSON.
    /// </summary>
    public class FlowTriggerInfo
    {
        /// <summary>How this flow is triggered.</summary>
        public TriggerKind Kind { get; set; } = TriggerKind.Other;

        /// <summary>
        /// Logical name of the entity this flow listens on.
        /// Only meaningful when Kind == CdsRowChange.
        /// </summary>
        public string EntityLogicalName { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated filtering attributes.
        /// NULL or empty means the flow fires on every update of the entity (broad trigger).
        /// Only meaningful when Kind == CdsRowChange.
        /// </summary>
        public string FilteringAttributes { get; set; }

        /// <summary>Raw trigger key inside the "triggers" JSON object.</summary>
        public string TriggerKey { get; set; } = string.Empty;

        /// <summary>
        /// True when this is a CDS trigger with no filtering attributes
        /// (fires on every row change of the entity).
        /// </summary>
        public bool IsBroadTrigger =>
            Kind == TriggerKind.CdsRowChange &&
            string.IsNullOrWhiteSpace(FilteringAttributes);

        /// <summary>Individual field names from FilteringAttributes.</summary>
        public IReadOnlyList<string> FilteringFields =>
            (Kind != TriggerKind.CdsRowChange || string.IsNullOrWhiteSpace(FilteringAttributes))
                ? Array.Empty<string>()
                : FilteringAttributes
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => f.Trim().ToLowerInvariant())
                    .ToArray();
    }

    /// <summary>
    /// All fields that a Cloud Flow writes via CDS/Dataverse "Update a row" actions.
    /// </summary>
    public class FlowUpdateInfo
    {
        /// <summary>Entity logical name targeted by the update action.</summary>
        public string EntityLogicalName { get; set; } = string.Empty;

        /// <summary>Field logical names that are explicitly set by this action.</summary>
        public IReadOnlyList<string> UpdatedFields { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Lifecycle status of a Cloud Flow as stored in the workflow table.
    /// </summary>
    public enum FlowStatus
    {
        /// <summary>type=1, statecode=1  – turned on, running normally.</summary>
        Active,
        /// <summary>type=1, statecode=0  – turned off by the owner.</summary>
        Inactive,
        /// <summary>type=2               – never published / still in draft.</summary>
        Draft
    }

    /// <summary>
    /// Full metadata for one Cloud Flow, enriched with dependency info.
    /// </summary>
    public class FlowNode
    {
        public Guid FlowId { get; set; }
        public string FlowName { get; set; } = string.Empty;
        public string FlowUrl { get; set; } = string.Empty;
        /// <summary>Active = on, Inactive = turned off, Draft = never published.</summary>
        public FlowStatus Status { get; set; } = FlowStatus.Active;

        /// <summary>Parsed trigger (null if no CDS trigger found).</summary>
        public FlowTriggerInfo Trigger { get; set; }

        /// <summary>All update actions found inside this flow.</summary>
        public IReadOnlyList<FlowUpdateInfo> UpdateActions { get; set; } = Array.Empty<FlowUpdateInfo>();

        /// <summary>True when the searched field is part of the trigger's filtering attributes.</summary>
        public bool IsSearchedFieldTrigger { get; set; }

        /// <summary>True when the searched field is written by at least one update action.</summary>
        public bool IsSearchedFieldUpdated { get; set; }

        /// <summary>
        /// True when this flow has a broad trigger (no filtering attributes).
        /// Recursion stops at broad-trigger flows.
        /// </summary>
        public bool IsBroadTrigger => Trigger?.IsBroadTrigger ?? false;

        /// <summary>Child nodes in the dependency tree (flows that cause THIS flow to trigger).</summary>
        public List<FlowNode> Children { get; set; } = new List<FlowNode>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // JSON Parser
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responsible only for extracting structured data from a flow's clientdata JSON.
    /// Completely stateless – all methods are pure functions.
    /// </summary>
    public static class FlowJsonParser
    {
        // ── Trigger extraction ─────────────────────────────────────────────────

        /// <summary>
        /// Returns trigger metadata for the FIRST trigger found in the flow JSON.
        /// Always returns a non-null <see cref="FlowTriggerInfo"/>; the Kind property
        /// tells callers whether it is a CDS row-change trigger or something else
        /// (manual, scheduled, other connector).
        ///
        /// Returns null only when clientdata is empty or unparseable.
        /// </summary>
        public static FlowTriggerInfo ParseTrigger(string clientDataJson)
        {
            if (string.IsNullOrWhiteSpace(clientDataJson)) return null;

            try
            {
                var root = JObject.Parse(clientDataJson);
                var triggers = root.SelectToken("properties.definition.triggers") as JObject;
                if (triggers == null) return null;

                foreach (var prop in triggers.Properties())
                {
                    var tv = prop.Value as JObject;
                    if (tv == null) continue;

                    string triggerKey = prop.Name;
                    string triggerType = tv["type"]?.ToString() ?? string.Empty;

                    // ── Manual / instant triggers ──────────────────────────────
                    // type = "Request" covers: manual, PowerApps, HTTP, Teams task
                    // type = "ApiConnectionWebhook" without subscriptionRequest is also manual-ish
                    if (triggerType.Equals("Request", StringComparison.OrdinalIgnoreCase) ||
                        triggerKey.Equals("manual", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FlowTriggerInfo
                        {
                            Kind = TriggerKind.Manual,
                            TriggerKey = triggerKey
                        };
                    }

                    // ── Scheduled / recurrence triggers ───────────────────────
                    if (triggerType.Equals("Recurrence", StringComparison.OrdinalIgnoreCase) ||
                        triggerType.Equals("OpenApiConnection", StringComparison.OrdinalIgnoreCase) &&
                        triggerKey.IndexOf("recurrence", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return new FlowTriggerInfo
                        {
                            Kind = TriggerKind.Scheduled,
                            TriggerKey = triggerKey
                        };
                    }

                    // ── CDS / Dataverse row-change trigger ─────────────────────
                    // These have inputs.parameters with "subscriptionRequest/entityname"
                    var parameters = tv.SelectToken("inputs.parameters") as JObject;
                    if (parameters != null)
                    {
                        var entityName = parameters["subscriptionRequest/entityname"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(entityName))
                        {
                            // filteringattributes: absent = broad trigger
                            var filteringAttrs =
                                parameters["subscriptionRequest/filteringattributes"]?.ToString();

                            // Fallback: some flows store a filterexpression instead
                            if (string.IsNullOrWhiteSpace(filteringAttrs))
                            {
                                var filterExpr =
                                    parameters["subscriptionRequest/filterexpression"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(filterExpr))
                                    filteringAttrs = ExtractFieldFromFilterExpression(filterExpr);
                            }

                            return new FlowTriggerInfo
                            {
                                Kind = TriggerKind.CdsRowChange,
                                TriggerKey = triggerKey,
                                EntityLogicalName = entityName.Trim().ToLowerInvariant(),
                                FilteringAttributes = filteringAttrs
                            };
                        }
                    }

                    // ── Any other connector trigger ────────────────────────────
                    // (e-mail, Teams, SharePoint, etc.) — still return it so the
                    // flow is shown in the tree rather than silently discarded.
                    return new FlowTriggerInfo
                    {
                        Kind = TriggerKind.Other,
                        TriggerKey = triggerKey
                    };
                }
            }
            catch
            {
                // Malformed JSON – caller treats null as "unknown trigger"
            }

            return null;
        }

        // ── Update-action extraction ───────────────────────────────────────────

        /// <summary>
        /// Returns every "Update a row" (or equivalent) action found anywhere in the flow JSON,
        /// including inside branches, loops, and scopes.
        /// </summary>
        public static IReadOnlyList<FlowUpdateInfo> ParseUpdateActions(string clientDataJson)
        {
            if (string.IsNullOrWhiteSpace(clientDataJson)) return Array.Empty<FlowUpdateInfo>();

            try
            {
                var root = JObject.Parse(clientDataJson);
                var actions = root.SelectToken("properties.definition.actions") as JObject;
                if (actions == null) return Array.Empty<FlowUpdateInfo>();

                var results = new List<FlowUpdateInfo>();
                CollectUpdateActions(actions, results);
                return results;
            }
            catch
            {
                return Array.Empty<FlowUpdateInfo>();
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void CollectUpdateActions(JObject actionsObject, List<FlowUpdateInfo> results)
        {
            foreach (var actionProp in actionsObject.Properties())
            {
                var action = actionProp.Value as JObject;
                if (action == null) continue;

                TryExtractUpdateAction(action, results);

                // Recurse into nested action containers: actions, else, cases
                foreach (var nestedContainerToken in new[]
                {
                    action["actions"],
                    action["else"]?["actions"],
                })
                {
                    if (nestedContainerToken is JObject nested)
                        CollectUpdateActions(nested, results);
                }

                // Switch/case branches
                var switchCases = action["cases"] as JObject;
                if (switchCases != null)
                {
                    foreach (var caseProp in switchCases.Properties())
                    {
                        var caseActions = caseProp.Value["actions"] as JObject;
                        if (caseActions != null)
                            CollectUpdateActions(caseActions, results);
                    }
                }

                // Default branch of switch
                var defaultActions = action["default"]?["actions"] as JObject;
                if (defaultActions != null)
                    CollectUpdateActions(defaultActions, results);
            }
        }

        private static void TryExtractUpdateAction(JObject action, List<FlowUpdateInfo> results)
        {
            var parameters = action.SelectToken("inputs.parameters") as JObject;
            if (parameters == null) return;

            // "entityName" is present on CDS Update / Create / Upsert actions.
            // We read it for context but do NOT require it to match — the entity name
            // stored by Power Automate can differ from the Dataverse logical name
            // (e.g. "accounts" vs "account", schema names, etc.).
            var entityName = parameters["entityName"]?.ToString()?.Trim().ToLowerInvariant()
                             ?? string.Empty;

            // Collect every "item/<fieldName>" key – these are the fields being written.
            var updatedFields = parameters
                .Properties()
                .Where(p => p.Name.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name.Substring("item/".Length).Trim().ToLowerInvariant())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            // Also capture OData $select fields being read (useful for trigger detection)
            // but only store fields that are explicitly WRITTEN (item/* pattern).
            if (updatedFields.Count == 0) return;

            results.Add(new FlowUpdateInfo
            {
                EntityLogicalName = entityName,
                UpdatedFields = updatedFields
            });
        }

        /// <summary>
        /// Best-effort extraction of a field name from an OData-style filter expression,
        /// e.g. "statecode eq 0" → "statecode".
        /// </summary>
        private static string ExtractFieldFromFilterExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return null;
            var parts = expr.Trim().Split(' ');
            return parts.Length > 0 ? parts[0].Trim() : null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dependency Analyzer
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full dependency tree for a given entity/field combination.
    ///
    /// Algorithm (backward tracing):
    ///  1. Find all flows that mention <entity> AND <field> in clientdata.
    ///  2. For each such flow: parse trigger + update actions.
    ///  3. Mark whether the searched field is used as a trigger or is being updated.
    ///  4. For every flow that UPDATES the searched field:
    ///       – determine its own trigger entity/field
    ///       – recursively search for flows that update THAT trigger field
    ///         (guarded by a visited-set to prevent infinite loops)
    ///       – stop recursion when a broad trigger is encountered
    /// </summary>
    public class FlowDependencyAnalyzer
    {
        private readonly IOrganizationService _service;
        private readonly string _environmentId;

        public FlowDependencyAnalyzer(IOrganizationService service, string environmentId)
        {
            _service = service;
            _environmentId = environmentId ?? string.Empty;
        }

        // ── Public entry point ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the flat list of all root FlowNodes for the given entity/field.
        /// Each node's .Children property contains the backward dependency chain.
        /// </summary>
        public List<FlowNode> BuildDependencyTree(
            string entityLogicalName,
            string fieldLogicalName)
        {
            if (string.IsNullOrWhiteSpace(entityLogicalName) ||
                string.IsNullOrWhiteSpace(fieldLogicalName))
                return new List<FlowNode>();

            entityLogicalName = entityLogicalName.Trim().ToLowerInvariant();
            fieldLogicalName = fieldLogicalName.Trim().ToLowerInvariant();

            // Global visited set across the entire tree build to avoid duplicate subtrees
            var globalVisited = new HashSet<Guid>();

            return BuildLevel(entityLogicalName, fieldLogicalName, globalVisited);
        }

        // ── Private recursive core ─────────────────────────────────────────────

        private List<FlowNode> BuildLevel(
            string entityName,
            string fieldName,
            HashSet<Guid> visitedInPath)
        {
            var candidateFlows = FetchCandidateFlows(entityName, fieldName);
            var resultNodes = new List<FlowNode>();

            foreach (var rawFlow in candidateFlows)
            {
                var flowId = rawFlow.GetAttributeValue<Guid>("workflowid");

                // ── Loop / duplicate protection ────────────────────────────────
                if (visitedInPath.Contains(flowId))
                    continue;

                var clientData = rawFlow.GetAttributeValue<string>("clientdata");
                var trigger = FlowJsonParser.ParseTrigger(clientData);
                var updates = FlowJsonParser.ParseUpdateActions(clientData);

                // Determine lifecycle status from type + statecode columns
                int typeValue = rawFlow.GetAttributeValue<int>("type");
                var stateCode = rawFlow.GetAttributeValue<OptionSetValue>("statecode");
                int stateCodeValue = stateCode?.Value ?? 1;
                FlowStatus flowStatus =
                    typeValue == 2 ? FlowStatus.Draft :
                    stateCodeValue == 0 ? FlowStatus.Inactive :
                                                            FlowStatus.Active;

                // Only include the flow if it actually uses the entity+field we searched
                bool isTrigger = IsTriggerForField(trigger, entityName, fieldName);
                bool isUpdater = IsUpdaterForField(updates, entityName, fieldName);

                if (!isTrigger && !isUpdater)
                    continue;

                var node = new FlowNode
                {
                    FlowId = flowId,
                    FlowName = rawFlow.GetAttributeValue<string>("name") ?? "(unnamed)",
                    FlowUrl = BuildFlowUrl(flowId),
                    Status = flowStatus,
                    Trigger = trigger,
                    UpdateActions = updates,
                    IsSearchedFieldTrigger = isTrigger,
                    IsSearchedFieldUpdated = isUpdater
                };

                resultNodes.Add(node);

                // ── Recurse only for flows that UPDATE the searched field ───────
                //
                // Key invariant: a flow is only an upstream dependency of THIS node
                // when it updates the EXACT entity+field that THIS node's trigger
                // listens on.
                //
                // Example (your bug):
                //   Searched field  : incident.title
                //   Flow A trigger  : email.importsequencenumber  → sets incident.title  ← isUpdater
                //   Flow B trigger  : email.importsequencenumber  → sets incident.title  ← isUpdater
                //
                //   Recursion asks: "what flows update email.importsequencenumber?"
                //   → FetchCandidateFlows("email","importsequencenumber") returns Flow A + Flow B
                //     because both mention "email" and "importsequencenumber" in clientdata.
                //   → But neither Flow A nor Flow B actually *writes* email.importsequencenumber —
                //     they only READ it as their trigger field.
                //   → IsUpdaterForField(flowA.updates, "email", "importsequencenumber") → FALSE
                //   → So neither becomes a child of the other. ✓
                //
                if (!isUpdater)
                    continue; // only flows that write the field get upstream parents traced

                // Non-CDS triggers (manual, scheduled, other): they have no Dataverse
                // trigger field to trace backwards on — stop here.
                if (trigger == null || trigger.Kind != TriggerKind.CdsRowChange)
                    continue;

                // STOP: broad trigger — flag it but do not recurse further.
                if (trigger.IsBroadTrigger)
                    continue;

                // The trigger field is what THIS flow listens on.
                // We search for flows that UPDATE that field — those are true parents.
                string parentEntity = trigger.EntityLogicalName;
                string parentField = trigger.FilteringFields.FirstOrDefault();

                if (string.IsNullOrWhiteSpace(parentEntity) ||
                    string.IsNullOrWhiteSpace(parentField))
                    continue;

                // Guard: don't recurse if we'd search the same entity+field we
                // already started with — avoids degenerate self-referential loops
                // not caught by the flowId visited-set (different field, same flow chain).
                if (parentEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase) &&
                    parentField.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                visitedInPath.Add(flowId);
                var parentCandidates = BuildLevel(parentEntity, parentField, visitedInPath);
                visitedInPath.Remove(flowId);

                // ── Critical filter ────────────────────────────────────────────
                // Only keep candidates that actually WRITE parentEntity.parentField.
                // Flows that merely READ it (e.g. as their own trigger) are NOT parents.
                var trueParents = parentCandidates
                    .Where(p => IsUpdaterForField(p.UpdateActions, parentEntity, parentField))
                    .ToList();

                node.Children.AddRange(trueParents);
            }

            return resultNodes;
        }

        // ── Dataverse query ────────────────────────────────────────────────────

        /// <summary>
        /// Fetches all Cloud Flows (category=5, any type/statecode) whose clientdata
        /// contains both the entity name and the field name.
        ///
        /// Note: Using LIKE on clientdata is the only viable approach in Dataverse
        /// without a full-text index. We filter precisely in memory after retrieval.
        /// </summary>
        private IReadOnlyList<Entity> FetchCandidateFlows(string entityName, string fieldName)
        {
            var query = new QueryExpression("workflow")
            {
                ColumnSet = new ColumnSet("name", "clientdata", "workflowid", "statecode", "type"),
                TopCount = 5000,
                Criteria =
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions     =
                    {
                        new ConditionExpression("category", ConditionOperator.Equal, 5), // Cloud Flow
                        // No type filter – we fetch Active (1), Inactive (1+statecode=0)
                        // and Draft (2) flows and colour-code them in the UI.
                    },
                    Filters =
                    {
                        new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions     =
                            {
                                new ConditionExpression("clientdata", ConditionOperator.Like, $"%{entityName}%"),
                                new ConditionExpression("clientdata", ConditionOperator.Like, $"%{fieldName}%"),
                            }
                        }
                    }
                }
            };

            return _service.RetrieveMultiple(query).Entities;
        }

        // ── Field-match helpers ────────────────────────────────────────────────

        private static bool IsTriggerForField(
            FlowTriggerInfo trigger,
            string entityName,
            string fieldName)
        {
            if (trigger == null) return false;

            // Non-CDS triggers (manual, scheduled, other connector) never trigger
            // on a specific Dataverse field, so they cannot be "the trigger for this field".
            // They will still appear in the tree because IsUpdaterForField may be true.
            if (trigger.Kind != TriggerKind.CdsRowChange) return false;

            if (!trigger.EntityLogicalName.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                return false;

            // Broad trigger: no filtering attributes means it fires on every update of the entity.
            if (trigger.IsBroadTrigger) return true;

            return trigger.FilteringFields.Any(
                f => f.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsUpdaterForField(
            IReadOnlyList<FlowUpdateInfo> updates,
            string entityName,
            string fieldName)
        {
            return updates.Any(u =>
            {
                // Field must match exactly (the item/* key is always the logical name)
                bool fieldMatches = u.UpdatedFields.Any(
                    f => f.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                if (!fieldMatches) return false;

                // If the update action carries no entity name (some connectors omit it),
                // we trust the upstream LIKE query and count the field match as sufficient.
                if (string.IsNullOrWhiteSpace(u.EntityLogicalName)) return true;

                // Accept exact match OR singular/plural variants PA sometimes stores:
                //   "account" == "accounts",  "contact" == "contacts", etc.
                return EntityNamesMatch(u.EntityLogicalName, entityName);
            });
        }

        /// <summary>
        /// Compares two entity logical names tolerantly.
        /// Power Automate sometimes stores the plural display name ("accounts")
        /// while Dataverse uses the singular logical name ("account").
        /// </summary>
        private static bool EntityNamesMatch(string a, string b)
        {
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;

            // Try adding/removing a trailing 's'
            if ((a + "s").Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals(b + "s", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // ── URL builder ────────────────────────────────────────────────────────

        private string BuildFlowUrl(Guid flowId)
        {
            if (string.IsNullOrWhiteSpace(_environmentId))
                return $"https://make.powerautomate.com/flows/{flowId}/details";

            return $"https://make.powerautomate.com/environments/{_environmentId}/flows/{flowId}/details";
        }
    }
}