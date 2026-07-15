using System;
using System.Collections.Generic;
using FieldGraphX.Core.Abstractions;
using FieldGraphX.Core.Models;
using FieldGraphX.Core.Parsing;

namespace FieldGraphX.Core.Analysis
{
    /// <summary>
    /// In-memory index over all parsed flows of an environment. Built once per analysis;
    /// afterwards every lookup the analyzer needs is a dictionary access — recursion does
    /// not hit Dataverse again.
    /// </summary>
    public sealed class FlowIndex
    {
        private static readonly IReadOnlyList<FlowInfo> EmptyFlows = Array.Empty<FlowInfo>();

        private readonly Dictionary<FieldRef, List<FlowInfo>> _triggeredByField =
            new Dictionary<FieldRef, List<FlowInfo>>();
        private readonly Dictionary<string, List<FlowInfo>> _broadTriggersByEntity =
            new Dictionary<string, List<FlowInfo>>(StringComparer.Ordinal);
        private readonly Dictionary<FieldRef, List<FlowInfo>> _settersByField =
            new Dictionary<FieldRef, List<FlowInfo>>();

        public IReadOnlyList<FlowInfo> AllFlows { get; }

        private FlowIndex(IReadOnlyList<FlowInfo> allFlows)
        {
            AllFlows = allFlows;
        }

        /// <summary>Parses all records, resolves entity names, and builds the lookups.</summary>
        public static FlowIndex Build(
            IEnumerable<FlowRecord> records,
            IEntityNameResolver resolver)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));

            var flows = new List<FlowInfo>();
            var seenIds = new HashSet<Guid>();

            foreach (var record in records)
            {
                if (record == null || !seenIds.Add(record.WorkflowId)) continue;

                var flow = FlowClientDataParser.ParseFlow(record, resolver.IsKnownLogicalName);

                if (flow.Trigger.Kind == TriggerKind.CdsRowChange)
                    flow.Trigger.EntityLogicalName = resolver.ToLogicalName(flow.Trigger.RawEntityName);

                foreach (var action in flow.WriteActions)
                    action.EntityLogicalName = resolver.ToLogicalName(action.RawEntityName);

                flows.Add(flow);
            }

            var index = new FlowIndex(flows);

            foreach (var flow in flows)
            {
                var trigger = flow.Trigger;
                if (trigger.Kind == TriggerKind.CdsRowChange &&
                    !string.IsNullOrEmpty(trigger.EntityLogicalName))
                {
                    if (trigger.IsBroadTrigger)
                    {
                        AddTo(index._broadTriggersByEntity, trigger.EntityLogicalName, flow);
                    }
                    else
                    {
                        foreach (var attribute in trigger.FilteringAttributes)
                            AddTo(index._triggeredByField,
                                  new FieldRef(trigger.EntityLogicalName, attribute), flow);
                    }
                }

                foreach (var action in flow.WriteActions)
                {
                    if (string.IsNullOrEmpty(action.EntityLogicalName)) continue;
                    foreach (var field in action.Fields)
                        AddTo(index._settersByField,
                              new FieldRef(action.EntityLogicalName, field), flow);
                }
            }

            return index;
        }

        public IReadOnlyList<FlowInfo> TriggeredByField(FieldRef field) =>
            _triggeredByField.TryGetValue(field, out var flows) ? flows : EmptyFlows;

        public IReadOnlyList<FlowInfo> BroadTriggersOnEntity(string entityLogicalName) =>
            _broadTriggersByEntity.TryGetValue(entityLogicalName ?? string.Empty, out var flows)
                ? (IReadOnlyList<FlowInfo>)flows : EmptyFlows;

        public IReadOnlyList<FlowInfo> SettersOfField(FieldRef field) =>
            _settersByField.TryGetValue(field, out var flows) ? flows : EmptyFlows;

        private static void AddTo<TKey>(Dictionary<TKey, List<FlowInfo>> map, TKey key, FlowInfo flow)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<FlowInfo>();
                map.Add(key, list);
            }
            if (!list.Contains(flow)) list.Add(flow);
        }
    }
}
