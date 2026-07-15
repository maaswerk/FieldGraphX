using System;
using System.Collections.Generic;
using FieldGraphX.Core.Models;

namespace FieldGraphX.Core.Analysis
{
    /// <summary>
    /// Builds the global dependency graph for a seed field via breadth-first search
    /// over the <see cref="FlowIndex"/>.
    ///
    /// Downstream: "changing the seed field runs these flows, which set these fields,
    /// which run these flows, …" — Field → (Triggers) → Flow → (Sets) → Field → …
    ///
    /// Upstream: "these flows write the seed field, and are themselves started by these
    /// fields, which are written by these flows, …" — Field ← (Sets) ← Flow ← (Triggers) ← Field ← …
    ///
    /// Nodes are deduplicated globally by id, so cycles simply close a loop in the graph
    /// instead of recursing — there are no per-path visited sets and no cache that can
    /// drop edges.
    /// </summary>
    public sealed class DependencyAnalyzer
    {
        [Flags]
        private enum Direction { Downstream = 1, Upstream = 2, Both = Downstream | Upstream }

        private readonly FlowIndex _index;
        private readonly AnalyzerOptions _options;
        private readonly Action<string> _log;

        public DependencyAnalyzer(FlowIndex index, AnalyzerOptions options = null, Action<string> log = null)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _options = options ?? new AnalyzerOptions();
            _log = log;
        }

        public DependencyGraph Analyze(FieldRef seedField)
        {
            if (seedField.IsEmpty)
                throw new ArgumentException("Seed field must have entity and attribute.", nameof(seedField));

            var graph = new DependencyGraph();
            var seedNode = graph.GetOrAddFieldNode(seedField, 0);
            seedNode.IsSeed = true;
            graph.Seed = seedNode;

            // Per-direction bookkeeping. Downstream additionally tracks the change-type
            // mask a field was reached with: a field first seen via a Create-only writer
            // must be re-expanded if an Update writer reaches it later.
            var downstreamProcessed = new Dictionary<FieldRef, ChangeType>();
            var upstreamProcessed = new HashSet<FieldRef>();

            var queue = new Queue<(FieldRef Field, int Depth, Direction Dir, ChangeType Changes)>();
            // The seed expands both ways. "How is the seed changed" is unknown — the user
            // asks about any change of the field, so Create and Update both count.
            queue.Enqueue((seedField, 0, Direction.Both, ChangeType.Create | ChangeType.Update));

            while (queue.Count > 0)
            {
                var (field, depth, dir, changes) = queue.Dequeue();

                if (depth >= _options.MaxDepth)
                {
                    graph.DepthLimitHit = true;
                    _log?.Invoke($"Depth limit {_options.MaxDepth} reached at {field}");
                    continue;
                }

                if (dir.HasFlag(Direction.Downstream))
                    ExpandDownstream(graph, queue, downstreamProcessed, field, depth, changes);

                if (dir.HasFlag(Direction.Upstream))
                    ExpandUpstream(graph, queue, upstreamProcessed, field, depth);
            }

            _log?.Invoke($"Analysis done: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges");
            return graph;
        }

        /// <summary>Field → flows it triggers → fields those flows set.</summary>
        private void ExpandDownstream(
            DependencyGraph graph,
            Queue<(FieldRef, int, Direction, ChangeType)> queue,
            Dictionary<FieldRef, ChangeType> processed,
            FieldRef field,
            int depth,
            ChangeType incomingChanges)
        {
            // Only expand for change-type bits not handled before.
            processed.TryGetValue(field, out var alreadyProcessed);
            var newBits = incomingChanges & ~alreadyProcessed;
            if (newBits == ChangeType.None) return;
            processed[field] = alreadyProcessed | newBits;

            var fieldNode = graph.GetOrAddFieldNode(field, depth);

            foreach (var flow in _index.TriggeredByField(field))
            {
                if (!IsIncluded(flow)) continue;

                // A write only fires triggers that subscribe to that change type
                // (an Update action never fires a create-only trigger).
                var overlap = flow.Trigger.ChangeTypes & newBits;
                if (flow.Trigger.ChangeTypes != ChangeType.None && overlap == ChangeType.None)
                {
                    _log?.Invoke($"Skip {flow.Name}: trigger change types {flow.Trigger.ChangeTypes} " +
                                 $"don't overlap incoming {newBits} on {field}");
                    continue;
                }

                var flowNode = graph.GetOrAddFlowNode(flow, depth);
                graph.AddEdge(fieldNode.Id, flowNode.Id, EdgeKind.Triggers,
                              overlap == ChangeType.None ? newBits : overlap);

                EnqueueWrittenFields(graph, queue, flow, flowNode, depth);
            }

            if (_options.IncludeBroadTriggers)
            {
                foreach (var flow in _index.BroadTriggersOnEntity(field.EntityLogicalName))
                {
                    if (!IsIncluded(flow)) continue;

                    var flowNode = graph.GetOrAddFlowNode(flow, depth);
                    graph.AddEdge(fieldNode.Id, flowNode.Id, EdgeKind.TriggersBroad,
                                  flow.Trigger.ChangeTypes);

                    // Broad flows are shown but not recursed by default (explosion guard).
                    if (_options.RecurseThroughBroadTriggers)
                        EnqueueWrittenFields(graph, queue, flow, flowNode, depth);
                }
            }
        }

        private void EnqueueWrittenFields(
            DependencyGraph graph,
            Queue<(FieldRef, int, Direction, ChangeType)> queue,
            FlowInfo flow,
            GraphNode flowNode,
            int depth)
        {
            foreach (var action in flow.WriteActions)
            {
                if (string.IsNullOrEmpty(action.EntityLogicalName)) continue;

                foreach (var fieldName in action.Fields)
                {
                    var written = new FieldRef(action.EntityLogicalName, fieldName);
                    var writtenNode = graph.GetOrAddFieldNode(written, depth + 1);
                    graph.AddEdge(flowNode.Id, writtenNode.Id, EdgeKind.Sets, action.EmittedChange);
                    queue.Enqueue((written, depth + 1, Direction.Downstream, action.EmittedChange));
                }
            }
        }

        /// <summary>Field ← flows that set it ← fields those flows trigger on.</summary>
        private void ExpandUpstream(
            DependencyGraph graph,
            Queue<(FieldRef, int, Direction, ChangeType)> queue,
            HashSet<FieldRef> processed,
            FieldRef field,
            int depth)
        {
            if (!processed.Add(field)) return;

            var fieldNode = graph.GetOrAddFieldNode(field, depth);

            foreach (var flow in _index.SettersOfField(field))
            {
                if (!IsIncluded(flow)) continue;

                var flowNode = graph.GetOrAddFlowNode(flow, depth);
                graph.AddEdge(flowNode.Id, fieldNode.Id, EdgeKind.Sets);

                var trigger = flow.Trigger;
                if (trigger.Kind != TriggerKind.CdsRowChange || trigger.IsBroadTrigger)
                {
                    // Manual, scheduled, other-connector and broad-trigger setters are
                    // upstream leaves — their cause is not a specific field.
                    continue;
                }

                foreach (var attribute in trigger.FilteringAttributes)
                {
                    var triggerField = new FieldRef(trigger.EntityLogicalName, attribute);
                    var triggerFieldNode = graph.GetOrAddFieldNode(triggerField, depth + 1);
                    graph.AddEdge(triggerFieldNode.Id, flowNode.Id, EdgeKind.Triggers,
                                  trigger.ChangeTypes);
                    queue.Enqueue((triggerField, depth + 1, Direction.Upstream, ChangeType.None));
                }
            }
        }

        private bool IsIncluded(FlowInfo flow)
        {
            if (flow.Status == FlowStatus.Draft && !_options.IncludeDrafts) return false;
            if (flow.Status == FlowStatus.Inactive && !_options.IncludeInactive) return false;
            return true;
        }
    }
}
