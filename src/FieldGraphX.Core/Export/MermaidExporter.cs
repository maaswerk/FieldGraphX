using System.Collections.Generic;
using System.Text;
using FieldGraphX.Core.Models;

namespace FieldGraphX.Core.Export
{
    /// <summary>
    /// Exports a <see cref="DependencyGraph"/> as a Mermaid flowchart definition
    /// (for wikis, or rendering on mermaid.live).
    /// </summary>
    public static class MermaidExporter
    {
        public static string Export(DependencyGraph graph)
        {
            var sb = new StringBuilder();
            sb.AppendLine("graph LR");

            var mermaidIds = new Dictionary<string, string>();
            var counter = 0;

            string IdOf(GraphNode node)
            {
                if (!mermaidIds.TryGetValue(node.Id, out var id))
                {
                    id = (node.Kind == NodeKind.Field ? "fld" : "flw") + counter++;
                    mermaidIds[node.Id] = id;
                }
                return id;
            }

            foreach (var node in graph.Nodes.Values)
            {
                var label = Escape(node.Label +
                    (node.Kind == NodeKind.Flow && node.Flow.Status != FlowStatus.Active
                        ? $" [{node.Flow.Status}]" : string.Empty));

                sb.AppendLine(node.Kind == NodeKind.Field
                    ? $"    {IdOf(node)}[\"{label}\"]"
                    : $"    {IdOf(node)}([\"{label}\"])");
            }

            foreach (var edge in graph.Edges)
            {
                if (!graph.Nodes.TryGetValue(edge.FromId, out var from) ||
                    !graph.Nodes.TryGetValue(edge.ToId, out var to))
                    continue;

                var arrow = edge.Kind == EdgeKind.TriggersBroad
                    ? "-. broad .->"
                    : edge.Kind == EdgeKind.Sets ? "-- sets -->" : "-- triggers -->";
                sb.AppendLine($"    {IdOf(from)} {arrow} {IdOf(to)}");
            }

            if (graph.Seed != null && mermaidIds.TryGetValue(graph.Seed.Id, out var seedId))
            {
                sb.AppendLine($"    style {seedId} stroke-width:3px");
            }

            return sb.ToString();
        }

        private static string Escape(string text) =>
            (text ?? string.Empty).Replace("\"", "'").Replace("\n", " ");
    }
}
