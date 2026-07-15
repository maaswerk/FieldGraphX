using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldGraphX.Core.Models
{
    public enum NodeKind { Field, Flow }

    public enum EdgeKind
    {
        /// <summary>Field → Flow: the field is in the trigger's filtering attributes.</summary>
        Triggers,
        /// <summary>Field → Flow: broad trigger (no filtering attributes) on the field's entity.</summary>
        TriggersBroad,
        /// <summary>Flow → Field: a write action of the flow sets the field.</summary>
        Sets
    }

    public sealed class GraphNode
    {
        public string Id { get; set; } = string.Empty;
        public NodeKind Kind { get; set; }

        /// <summary>Set when Kind == Field.</summary>
        public FieldRef? Field { get; set; }

        /// <summary>Set when Kind == Flow.</summary>
        public FlowInfo Flow { get; set; }

        /// <summary>Minimal BFS distance from the seed field.</summary>
        public int Depth { get; set; }

        public bool IsSeed { get; set; }

        public string Label => Kind == NodeKind.Field ? Field?.ToString() ?? Id : Flow?.Name ?? Id;

        public static string FieldId(FieldRef field) => $"field:{field}";
        public static string FlowId(Guid workflowId) => $"flow:{workflowId:D}";
    }

    public sealed class GraphEdge
    {
        public string FromId { get; set; } = string.Empty;
        public string ToId { get; set; } = string.Empty;
        public EdgeKind Kind { get; set; }

        /// <summary>Change types this edge represents (trigger subscription ∩ writer emission).</summary>
        public ChangeType ChangeTypes { get; set; }
    }

    /// <summary>
    /// The single global result of an analysis: nodes deduplicated by id, plus edges.
    /// Cycles are represented naturally (an edge back to an existing node).
    /// </summary>
    public sealed class DependencyGraph
    {
        private readonly Dictionary<string, GraphNode> _nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        private readonly List<GraphEdge> _edges = new List<GraphEdge>();
        private readonly HashSet<string> _edgeKeys = new HashSet<string>(StringComparer.Ordinal);

        public GraphNode Seed { get; internal set; }

        public IReadOnlyDictionary<string, GraphNode> Nodes => _nodes;
        public IReadOnlyList<GraphEdge> Edges => _edges;

        /// <summary>True when the depth limit stopped the traversal somewhere.</summary>
        public bool DepthLimitHit { get; internal set; }

        internal GraphNode GetOrAddFieldNode(FieldRef field, int depth)
        {
            var id = GraphNode.FieldId(field);
            if (_nodes.TryGetValue(id, out var existing))
            {
                if (depth < existing.Depth) existing.Depth = depth;
                return existing;
            }

            var node = new GraphNode { Id = id, Kind = NodeKind.Field, Field = field, Depth = depth };
            _nodes.Add(id, node);
            return node;
        }

        internal GraphNode GetOrAddFlowNode(FlowInfo flow, int depth)
        {
            var id = GraphNode.FlowId(flow.WorkflowId);
            if (_nodes.TryGetValue(id, out var existing))
            {
                if (depth < existing.Depth) existing.Depth = depth;
                return existing;
            }

            var node = new GraphNode { Id = id, Kind = NodeKind.Flow, Flow = flow, Depth = depth };
            _nodes.Add(id, node);
            return node;
        }

        internal void AddEdge(string fromId, string toId, EdgeKind kind, ChangeType changeTypes = ChangeType.None)
        {
            var key = fromId + "→" + toId + "|" + (int)kind;
            if (!_edgeKeys.Add(key)) return;
            _edges.Add(new GraphEdge { FromId = fromId, ToId = toId, Kind = kind, ChangeTypes = changeTypes });
        }

        public IEnumerable<GraphEdge> EdgesFrom(string nodeId) => _edges.Where(e => e.FromId == nodeId);
        public IEnumerable<GraphEdge> EdgesTo(string nodeId) => _edges.Where(e => e.ToId == nodeId);
    }
}
