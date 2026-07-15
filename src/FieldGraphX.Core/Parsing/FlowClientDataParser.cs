using System;
using System.Collections.Generic;
using System.Linq;
using FieldGraphX.Core.Models;
using Newtonsoft.Json.Linq;

namespace FieldGraphX.Core.Parsing
{
    /// <summary>
    /// Extracts structured data from a cloud flow's clientdata JSON.
    /// Stateless — all methods are pure functions.
    /// </summary>
    public static class FlowClientDataParser
    {
        /// <summary>operationIds of the modern Dataverse connector that write rows.</summary>
        private static readonly Dictionary<string, WriteActionKind> WriteOperationIds =
            new Dictionary<string, WriteActionKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["UpdateRecord"] = WriteActionKind.Update,
                ["CreateRecord"] = WriteActionKind.Create,
                ["UpsertRecord"] = WriteActionKind.Upsert,
                // Legacy CDS (current environment) connector names
                ["UpdateRecordWithOrganization"] = WriteActionKind.Update,
                ["CreateRecordWithOrganization"] = WriteActionKind.Create
            };

        // ── Trigger ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the flow's (single) trigger. Never returns null:
        /// Kind == None when clientdata is empty or unparseable.
        /// </summary>
        public static TriggerInfo ParseTrigger(string clientDataJson)
        {
            if (string.IsNullOrWhiteSpace(clientDataJson))
                return new TriggerInfo();

            JObject root;
            try
            {
                root = JObject.Parse(clientDataJson);
            }
            catch
            {
                return new TriggerInfo();
            }

            if (!(root.SelectToken("properties.definition.triggers") is JObject triggers))
                return new TriggerInfo();

            foreach (var prop in triggers.Properties())
            {
                if (!(prop.Value is JObject trigger)) continue;

                var info = ParseSingleTrigger(prop.Name, trigger);
                if (info != null) return info;
            }

            return new TriggerInfo();
        }

        private static TriggerInfo ParseSingleTrigger(string triggerKey, JObject trigger)
        {
            string triggerType = trigger["type"]?.ToString() ?? string.Empty;

            // Dataverse row-change trigger: identified by subscriptionRequest/entityname,
            // regardless of the exact type string (OpenApiConnectionWebhook today).
            if (trigger.SelectToken("inputs.parameters") is JObject parameters)
            {
                var rawEntity = parameters["subscriptionRequest/entityname"]?.ToString();
                if (!string.IsNullOrWhiteSpace(rawEntity))
                {
                    return new TriggerInfo
                    {
                        Kind = TriggerKind.CdsRowChange,
                        TriggerKey = triggerKey,
                        RawEntityName = rawEntity.Trim().ToLowerInvariant(),
                        ChangeTypes = MapSubscriptionMessage(parameters["subscriptionRequest/message"]),
                        FilteringAttributes = SplitFilteringAttributes(
                            parameters["subscriptionRequest/filteringattributes"]?.ToString()),
                        // A filterexpression is a run condition, NOT a trigger field.
                        // It is kept raw for display and never folded into FilteringAttributes.
                        FilterExpression = NullIfEmpty(
                            parameters["subscriptionRequest/filterexpression"]?.ToString()),
                        Scope = NullIfEmpty(parameters["subscriptionRequest/scope"]?.ToString())
                    };
                }
            }

            if (triggerType.Equals("Request", StringComparison.OrdinalIgnoreCase) ||
                triggerKey.Equals("manual", StringComparison.OrdinalIgnoreCase))
            {
                return new TriggerInfo { Kind = TriggerKind.Manual, TriggerKey = triggerKey };
            }

            if (triggerType.Equals("Recurrence", StringComparison.OrdinalIgnoreCase))
            {
                return new TriggerInfo { Kind = TriggerKind.Scheduled, TriggerKey = triggerKey };
            }

            return new TriggerInfo { Kind = TriggerKind.Other, TriggerKey = triggerKey };
        }

        /// <summary>
        /// Maps subscriptionRequest/message to change-type flags.
        /// 1=Create, 2=Delete, 3=Update, 4=Create|Update, 5=Create|Delete,
        /// 6=Update|Delete, 7=Create|Update|Delete.
        /// Community-documented, not officially specced — kept in one place on purpose.
        /// </summary>
        public static ChangeType MapSubscriptionMessage(JToken messageToken)
        {
            if (messageToken == null) return ChangeType.None;
            if (!int.TryParse(messageToken.ToString(), out var message)) return ChangeType.None;

            switch (message)
            {
                case 1: return ChangeType.Create;
                case 2: return ChangeType.Delete;
                case 3: return ChangeType.Update;
                case 4: return ChangeType.Create | ChangeType.Update;
                case 5: return ChangeType.Create | ChangeType.Delete;
                case 6: return ChangeType.Update | ChangeType.Delete;
                case 7: return ChangeType.Create | ChangeType.Update | ChangeType.Delete;
                default: return ChangeType.None;
            }
        }

        private static IReadOnlyList<string> SplitFilteringAttributes(string filteringAttributes)
        {
            if (string.IsNullOrWhiteSpace(filteringAttributes))
                return Array.Empty<string>();

            return filteringAttributes
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim().ToLowerInvariant())
                .Where(f => f.Length > 0)
                .ToArray();
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // ── Write actions ──────────────────────────────────────────────────────

        /// <summary>
        /// Finds every Dataverse write action (Update/Create/Upsert a row) anywhere in the
        /// flow JSON via a full deep walk — this covers scopes, conditions, loops, switches
        /// and any future container types without enumerating them.
        /// </summary>
        /// <param name="isKnownEntity">
        /// Optional predicate over entity logical names (from metadata). Used to strip
        /// polymorphic lookup suffixes correctly when the entity name itself contains
        /// underscores ("regardingobjectid_msdyn_workorder@odata.bind" → "regardingobjectid").
        /// </param>
        public static IReadOnlyList<UpdateActionInfo> ParseWriteActions(
            string clientDataJson, Func<string, bool> isKnownEntity = null)
        {
            if (string.IsNullOrWhiteSpace(clientDataJson))
                return Array.Empty<UpdateActionInfo>();

            JObject root;
            try
            {
                root = JObject.Parse(clientDataJson);
            }
            catch
            {
                return Array.Empty<UpdateActionInfo>();
            }

            var results = new List<UpdateActionInfo>();
            DeepCollectWriteActions(root, results, isKnownEntity);
            return results;
        }

        private static void DeepCollectWriteActions(
            JToken token, List<UpdateActionInfo> results, Func<string, bool> isKnownEntity)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value is JObject candidate)
                        TryExtractWriteAction(prop.Name, candidate, results, isKnownEntity);

                    DeepCollectWriteActions(prop.Value, results, isKnownEntity);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr)
                    DeepCollectWriteActions(item, results, isKnownEntity);
            }
        }

        private static void TryExtractWriteAction(
            string actionName, JObject action, List<UpdateActionInfo> results,
            Func<string, bool> isKnownEntity)
        {
            if (!(action.SelectToken("inputs.parameters") is JObject parameters)) return;

            var operationId = action.SelectToken("inputs.host.operationId")?.ToString();
            var entityName = parameters["entityName"]?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;

            WriteActionKind kind;
            if (!string.IsNullOrEmpty(operationId))
            {
                // A known operationId decides; unknown operationIds (GetItem, ListRecords,
                // SendEmail, ...) are definitely not writes.
                if (!WriteOperationIds.TryGetValue(operationId, out kind)) return;
            }
            else
            {
                // No operationId: only accept when the node still clearly looks like a
                // Dataverse write (entityName + item/* keys). Avoids false positives from
                // non-Dataverse actions that happen to use "item/" keys.
                if (string.IsNullOrEmpty(entityName)) return;
                kind = WriteActionKind.Unknown;
            }

            var fields = parameters
                .Properties()
                .Where(p => p.Name.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
                .Select(p => NormalizeFieldKey(p.Name.Substring("item/".Length), isKnownEntity))
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fields.Count == 0) return;

            results.Add(new UpdateActionInfo
            {
                ActionName = actionName,
                Kind = kind,
                RawEntityName = entityName,
                Fields = fields
            });
        }

        /// <summary>
        /// Strips the OData binding suffix Power Automate appends to lookup field keys:
        ///   "customerid_account@odata.bind"              → "customerid"
        ///   "regardingobjectid_msdyn_workorder@odata.bind" → "regardingobjectid"
        ///   "title"                                      → "title" (plain field, unchanged)
        ///
        /// The entity-type suffix can itself contain underscores (msdyn_workorder), so a
        /// plain "cut at last underscore" is wrong. With <paramref name="isKnownEntity"/>
        /// (metadata-backed) the longest suffix that is a real entity name is stripped;
        /// without it the last underscore is used as a best-effort fallback.
        /// </summary>
        public static string NormalizeFieldKey(string rawKey, Func<string, bool> isKnownEntity = null)
        {
            if (string.IsNullOrWhiteSpace(rawKey)) return null;

            rawKey = rawKey.Trim().ToLowerInvariant();

            int atIndex = rawKey.IndexOf('@');
            if (atIndex < 0)
                return rawKey;

            string beforeAt = rawKey.Substring(0, atIndex);

            // "@odata.type" style keys carry no field name at all.
            if (beforeAt.Length == 0) return null;

            if (isKnownEntity != null)
            {
                // Walk underscores left to right so the LONGEST entity suffix wins
                // ("someid_new_account": "new_account" is tried before "account").
                for (int i = beforeAt.IndexOf('_'); i > 0; i = beforeAt.IndexOf('_', i + 1))
                {
                    if (isKnownEntity(beforeAt.Substring(i + 1)))
                        return beforeAt.Substring(0, i);
                }
            }

            int lastUnderscore = beforeAt.LastIndexOf('_');
            if (lastUnderscore > 0)
                return beforeAt.Substring(0, lastUnderscore);

            return beforeAt;
        }

        // ── Whole flow ─────────────────────────────────────────────────────────

        /// <summary>Parses one raw workflow row into a fully populated FlowInfo.</summary>
        public static FlowInfo ParseFlow(
            Abstractions.FlowRecord record, Func<string, bool> isKnownEntity = null)
        {
            return new FlowInfo
            {
                WorkflowId = record.WorkflowId,
                WorkflowIdUnique = record.WorkflowIdUnique,
                Name = string.IsNullOrWhiteSpace(record.Name) ? "(unnamed)" : record.Name,
                Status = MapStatus(record.Type, record.StateCode),
                Trigger = ParseTrigger(record.ClientData),
                WriteActions = ParseWriteActions(record.ClientData, isKnownEntity)
            };
        }

        private static FlowStatus MapStatus(int type, int stateCode)
        {
            if (type == 2) return FlowStatus.Draft;
            return stateCode == 0 ? FlowStatus.Inactive : FlowStatus.Active;
        }
    }
}
