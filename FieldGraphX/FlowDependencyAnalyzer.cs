using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;

namespace FieldGraphX.Logic
{
    // Mirror of the UI-side DebugLogLevel – kept here so the analyzer
    // has no dependency on the FieldGraphX UI namespace.
    // Values MUST match FieldGraphX.DebugLogLevel exactly (cast by ordinal).
    internal enum AnalyzerLogLevel
    {
        Info = 0,
        Enter = 1,
        Found = 2,
        Match = 3,
        Recurse = 4,
        Skip = 5,
        Warning = 6
    }
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

        /// <summary>Upstream flows that write the field this flow triggers on.</summary>
        public List<FlowNode> Parents { get; set; } = new List<FlowNode>();
        /// <summary>Downstream flows that this flow causes to run by writing their trigger field.</summary>
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
            // Power Automate appends OData binding suffixes to lookup fields, e.g.:
            //   "item/customerid_account@odata.bind"  → logical name is "customerid"
            //   "item/ownerid_systemuser@odata.bind"  → logical name is "ownerid"
            //   "item/title"                          → logical name is "title"
            // We strip everything from the first underscore-preceded "@odata" or from
            // the "@" character, then also strip any trailing "_<entitytype>" suffix
            // that Power Automate adds before the @odata part.
            var updatedFields = parameters
                .Properties()
                .Where(p => p.Name.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
                .Select(p => NormalizeFieldKey(p.Name.Substring("item/".Length)))
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

        /// <summary>
        /// Strips OData binding suffixes that Power Automate appends to lookup field keys.
        ///
        /// Examples:
        ///   "customerid_account@odata.bind"  → "customerid"
        ///   "ownerid_systemuser@odata.bind"  → "ownerid"
        ///   "regardingobjectid_incident"     → "regardingobjectid"  (no @odata suffix)
        ///   "title"                          → "title"              (plain field, unchanged)
        ///
        /// Pattern: the logical name is everything before the LAST underscore that is
        /// followed by an entity-type name, which itself is followed by "@odata" or end-of-string.
        /// The safest heuristic: if the key contains "@", take everything before the last "_"
        /// that precedes the "@".  If no "@", return as-is (already a plain logical name).
        /// </summary>
        private static string NormalizeFieldKey(string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey)) return rawKey;

            rawKey = rawKey.Trim().ToLowerInvariant();

            // Find the @ character – present on all OData binding suffixes
            int atIndex = rawKey.IndexOf('@');
            if (atIndex < 0)
                return rawKey; // plain field like "title", "statecode" – return unchanged

            // Everything before the @ is e.g. "customerid_account"
            string beforeAt = rawKey.Substring(0, atIndex);

            // Strip the "_<entitytype>" suffix: find the last underscore
            int lastUnderscore = beforeAt.LastIndexOf('_');
            if (lastUnderscore > 0)
                return beforeAt.Substring(0, lastUnderscore); // → "customerid"

            // No underscore found (unusual) – return the part before @
            return beforeAt;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dependency Analyzer
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full dependency tree for a given entity/field combination.
    ///
    /// Algorithm (bidirectional tracing):
    ///
    ///  STEP 1 — Find direct participants
    ///    Fetch all flows whose clientdata mentions entity+field.
    ///    Each flow is classified as:
    ///      • Updater  – has an action that writes entity.field
    ///      • Trigger  – has a CDS trigger that fires when entity.field changes
    ///
    ///  STEP 2 — Backward tracing (upstream parents of updaters)
    ///    For every Updater that has a CDS trigger on entity2.field2:
    ///    search for flows that UPDATE entity2.field2 → those are its parents.
    ///
    ///  STEP 3 — Forward tracing (downstream children of updaters)
    ///    For every Updater: collect all OTHER fields it writes.
    ///    For each written field, search for flows that TRIGGER on that field.
    ///    Those flows are downstream children.
    ///
    ///  Loop protection: a HashSet<Guid> tracks every flowId in the current
    ///  call stack to prevent infinite recursion on circular dependencies.
    /// </summary>
    public class FlowDependencyAnalyzer
    {
        private readonly IOrganizationService _service;
        private readonly string _environmentId;
        // Optional debug callback: (message, level-as-int) → void
        // Null when debug mode is off – checked before every call so there is
        // zero overhead in normal operation.
        private readonly Action<string, int> _log;

        public FlowDependencyAnalyzer(
            IOrganizationService service,
            string environmentId,
            Action<string, int> debugLog = null)
        {
            _service = service;
            _environmentId = environmentId ?? string.Empty;
            _log = debugLog;
        }

        private void Log(string message, AnalyzerLogLevel level = AnalyzerLogLevel.Info)
        {
            _log?.Invoke(message, (int)level);
        }

        // ── Public entry point ─────────────────────────────────────────────────

        // Maximum recursion depth. Prevents runaway traversal in large envs.
        // 6 levels covers: trigger-field → updater → its trigger-field → updater → …
        // which is already a very deep real-world chain.
        private const int MaxDepth = 6;

        public List<FlowNode> BuildDependencyTree(
            string entityLogicalName,
            string fieldLogicalName)
        {
            if (string.IsNullOrWhiteSpace(entityLogicalName) ||
                string.IsNullOrWhiteSpace(fieldLogicalName))
                return new List<FlowNode>();

            entityLogicalName = entityLogicalName.Trim().ToLowerInvariant();
            fieldLogicalName = fieldLogicalName.Trim().ToLowerInvariant();

            Log($"════ BuildDependencyTree START  {entityLogicalName}.{fieldLogicalName} ════",
                AnalyzerLogLevel.Enter);

            // visitedInPath  – guards against circular dependency IN THE CURRENT CALL STACK
            // exploredFields – guards against re-exploring the same (entity.field) pair
            //                  anywhere in the tree, preventing the fan-out explosion where
            //                  every field a flow writes spawns a new independent subtree.
            var visitedInPath = new HashSet<Guid>();
            var exploredFields = new HashSet<string>();

            var result = BuildLevel(entityLogicalName, fieldLogicalName,
                                    visitedInPath, exploredFields, depth: 0);

            Log($"════ BuildDependencyTree END  →  {result.Count} root node(s) ════",
                AnalyzerLogLevel.Match);
            return result;
        }

        // ── Core: build one level of the dependency graph ─────────────────────

        private List<FlowNode> BuildLevel(
            string entityName,
            string fieldName,
            HashSet<Guid> visitedInPath,
            HashSet<string> exploredFields,
            int depth)
        {
            // Hard depth limit – log and bail out cleanly
            if (depth > MaxDepth)
            {
                Log($"  STOP — MaxDepth ({MaxDepth}) reached at {entityName}.{fieldName}",
                    AnalyzerLogLevel.Warning);
                return new List<FlowNode>();
            }

            // Global field-pair deduplication:
            // If we have already fully explored this entity.field combination anywhere
            // in the tree, return empty so we don't re-process the same subtree.
            string fieldKey = $"{entityName}.{fieldName}";
            if (!exploredFields.Add(fieldKey))
            {
                Log($"  SKIP — {fieldKey} already explored globally", AnalyzerLogLevel.Skip);
                return new List<FlowNode>();
            }

            Log($"BuildLevel  entity={entityName}  field={fieldName}  depth={depth}",
                AnalyzerLogLevel.Enter);

            var candidateFlows = FetchCandidateFlows(entityName, fieldName);
            Log($"  Dataverse returned {candidateFlows.Count} candidate flow(s)");
            var resultNodes = new List<FlowNode>();

            foreach (var rawFlow in candidateFlows)
            {
                var flowId = rawFlow.GetAttributeValue<Guid>("workflowid");
                var flowName = rawFlow.GetAttributeValue<string>("name") ?? "(unnamed)";

                if (visitedInPath.Contains(flowId))
                {
                    Log($"  SKIP (already in path): {flowName}", AnalyzerLogLevel.Skip);
                    continue;
                }

                var clientData = rawFlow.GetAttributeValue<string>("clientdata");
                var trigger = FlowJsonParser.ParseTrigger(clientData);
                var updates = FlowJsonParser.ParseUpdateActions(clientData);

                var typeOsv = rawFlow.GetAttributeValue<OptionSetValue>("type");
                var stateCodeOsv = rawFlow.GetAttributeValue<OptionSetValue>("statecode");
                int typeValue = typeOsv?.Value ?? 1;
                int stateCodeValue = stateCodeOsv?.Value ?? 1;
                FlowStatus flowStatus =
                    typeValue == 2 ? FlowStatus.Draft :
                    stateCodeValue == 0 ? FlowStatus.Inactive :
                                          FlowStatus.Active;

                bool isTrigger = IsTriggerForField(trigger, entityName, fieldName);
                bool isUpdater = IsUpdaterForField(updates, entityName, fieldName);

                Log($"  Flow: { flowName} trigger={trigger?.Kind.ToString() ?? "none"}  " +
                    $"isTrigger={isTrigger}  isUpdater={isUpdater}",
                    (isTrigger || isUpdater) ? AnalyzerLogLevel.Found : AnalyzerLogLevel.Skip);

                if (!isTrigger && !isUpdater)
                    continue;

                Log($"    MATCH — adding to results", AnalyzerLogLevel.Match);

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

                if (!isUpdater)
                {
                    Log($"    SKIP recursion for { flowName} — is trigger-only, no outbound edges",
                        AnalyzerLogLevel.Skip);
                    continue;
                }

                visitedInPath.Add(flowId);
                Log($"  Entering recursion for { flowName} (path depth now {visitedInPath.Count})");

                // ── BACKWARD: who causes THIS flow to run? ─────────────────────
                if (trigger == null || trigger.Kind != TriggerKind.CdsRowChange)
                {
                    Log($"    SKIP backward — trigger kind: {trigger?.Kind.ToString() ?? "null"} (no CDS trigger field)",
                        AnalyzerLogLevel.Skip);
                }
                else if (trigger.IsBroadTrigger)
                {
                    Log($"    SKIP backward — { flowName} has broad trigger (no filtering attributes)",
                        AnalyzerLogLevel.Warning);
                }
                else
                {
                    string upEntity = trigger.EntityLogicalName;
                    string upField = trigger.FilteringFields.FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(upEntity) || string.IsNullOrWhiteSpace(upField))
                    {
                        Log($"    SKIP backward — could not extract trigger entity/field from { flowName}",
                            AnalyzerLogLevel.Warning);
                    }
                    else if (EntityNamesMatch(upEntity, entityName) &&
                             upField.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"    SKIP backward — trigger field is same as search field ({upEntity}.{upField})",
                            AnalyzerLogLevel.Skip);
                    }
                    else
                    {
                        Log($"    BACKWARD trace: { flowName} triggers on {upEntity}.{upField}",
                            AnalyzerLogLevel.Recurse);
                        var upstreamCandidates = BuildLevel(upEntity, upField, visitedInPath, exploredFields, depth + 1);
                        Log($"    BACKWARD result: {upstreamCandidates.Count} candidate(s) for {upEntity}.{upField}");

                        foreach (var candidate in upstreamCandidates)
                        {
                            if (IsUpdaterForField(candidate.UpdateActions, upEntity, upField))
                            {
                                Log($"    PARENT found: { candidate.FlowName} writes {upEntity}.{upField}",
                                    AnalyzerLogLevel.Match);
                                node.Parents.Add(candidate);
                            }
                            else
                            {
                                Log($"    NOT a parent: { candidate.FlowName} does not write {upEntity}.{upField}",
                                    AnalyzerLogLevel.Skip);
                            }
                        }
                    }
                }

                // ── FORWARD: what does THIS flow cause to run? ─────────────────
                foreach (var updateAction in updates)
                {
                    string downEntity = updateAction.EntityLogicalName;
                    if (string.IsNullOrWhiteSpace(downEntity))
                    {
                        Log($"    SKIP forward action — no entity name (fields: {string.Join(", ", updateAction.UpdatedFields)})",
                            AnalyzerLogLevel.Skip);
                        continue;
                    }

                    foreach (var writtenField in updateAction.UpdatedFields)
                    {
                        if (EntityNamesMatch(downEntity, entityName) &&
                            writtenField.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"    SKIP forward — {downEntity}.{writtenField} is the searched field itself",
                                AnalyzerLogLevel.Skip);
                            continue;
                        }

                        Log($"    FORWARD trace: { flowName} writes {downEntity}.{writtenField}",
                            AnalyzerLogLevel.Recurse);
                        var downstreamCandidates = BuildLevel(downEntity, writtenField, visitedInPath, exploredFields, depth + 1);
                        Log($"    FORWARD result: {downstreamCandidates.Count} candidate(s) for {downEntity}.{writtenField}");

                        foreach (var candidate in downstreamCandidates)
                        {
                            if (IsTriggerForField(candidate.Trigger, downEntity, writtenField))
                            {
                                if (!node.Children.Any(c => c.FlowId == candidate.FlowId))
                                {
                                    Log($"    CHILD found: { candidate.FlowName} triggers on {downEntity}.{writtenField}",
                                        AnalyzerLogLevel.Match);
                                    node.Children.Add(candidate);
                                }
                                else
                                {
                                    Log($"    CHILD already added: { candidate.FlowName}",
                                        AnalyzerLogLevel.Skip);
                                }
                            }
                            else
                            {
                                Log($"    NOT a child: { candidate.FlowName} does not trigger on {downEntity}.{writtenField}",
                                    AnalyzerLogLevel.Skip);
                            }
                        }
                    }
                }

                visitedInPath.Remove(flowId);
                Log($"  visitedInPath depth after pop: {visitedInPath.Count}");
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

            if (!EntityNamesMatch(trigger.EntityLogicalName, entityName))
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