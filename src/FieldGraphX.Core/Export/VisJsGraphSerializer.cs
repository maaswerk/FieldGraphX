using System.Linq;
using FieldGraphX.Core.Models;
using Newtonsoft.Json.Linq;

namespace FieldGraphX.Core.Export
{
    /// <summary>
    /// Serializes a <see cref="DependencyGraph"/> into the nodes/edges JSON shape
    /// consumed by vis-network in the embedded graph.html.
    /// </summary>
    public static class VisJsGraphSerializer
    {
        public static string Serialize(DependencyGraph graph)
        {
            var nodes = new JArray();
            foreach (var node in graph.Nodes.Values)
            {
                nodes.Add(new JObject
                {
                    ["id"] = node.Id,
                    ["label"] = BuildLabel(node),
                    ["shape"] = node.Kind == NodeKind.Field ? "box" : "ellipse",
                    ["group"] = GroupOf(node),
                    ["title"] = BuildTooltip(node)
                });
            }

            var edges = new JArray();
            foreach (var edge in graph.Edges)
            {
                edges.Add(new JObject
                {
                    ["from"] = edge.FromId,
                    ["to"] = edge.ToId,
                    ["arrows"] = "to",
                    ["dashes"] = edge.Kind == EdgeKind.TriggersBroad,
                    ["label"] = EdgeLabel(edge)
                });
            }

            return new JObject { ["nodes"] = nodes, ["edges"] = edges }
                .ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildLabel(GraphNode node)
        {
            if (node.Kind == NodeKind.Field) return node.Label;

            var flow = node.Flow;
            var suffix =
                flow.Status == FlowStatus.Draft ? "\n[Draft]" :
                flow.Status == FlowStatus.Inactive ? "\n[Off]" : string.Empty;
            return flow.Name + suffix;
        }

        private static string GroupOf(GraphNode node)
        {
            if (node.Kind == NodeKind.Field)
                return node.IsSeed ? "seed" : "field";

            var flow = node.Flow;
            if (flow.Status != FlowStatus.Active) return "flowInactive";
            if (flow.Trigger.IsBroadTrigger) return "flowBroad";
            return "flow";
        }

        private static string BuildTooltip(GraphNode node)
        {
            if (node.Kind == NodeKind.Field)
                return node.IsSeed ? $"Searched field: {node.Label}" : $"Field: {node.Label}";

            var flow = node.Flow;
            var trigger = flow.Trigger;
            var triggerText =
                trigger.Kind == TriggerKind.CdsRowChange
                    ? trigger.IsBroadTrigger
                        ? $"Dataverse ({trigger.EntityLogicalName}, BROAD — no filtering attributes)"
                        : $"Dataverse ({trigger.EntityLogicalName}: {string.Join(", ", trigger.FilteringAttributes)})"
                    : trigger.Kind.ToString();

            var writes = flow.WriteActions.Count == 0
                ? "none"
                : string.Join("; ", flow.WriteActions.Select(
                      a => $"{a.EntityLogicalName}: {string.Join(", ", a.Fields)}"));

            return $"{flow.Name}\nStatus: {flow.Status}\nTrigger: {triggerText}\nWrites: {writes}";
        }

        private static string EdgeLabel(GraphEdge edge)
        {
            switch (edge.Kind)
            {
                case EdgeKind.Sets: return "sets";
                case EdgeKind.TriggersBroad: return "triggers (broad)";
                default: return "triggers";
            }
        }
    }
}
