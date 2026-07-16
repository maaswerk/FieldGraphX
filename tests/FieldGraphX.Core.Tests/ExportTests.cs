using FieldGraphX.Core.Analysis;
using FieldGraphX.Core.Export;
using FieldGraphX.Core.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FieldGraphX.Core.Tests
{
    public class ExportTests
    {
        private static DependencyGraph SampleGraph()
        {
            var resolver = new EntityNameResolver();
            var setter = TestData.Record("Setter \"quoted\" name",
                TestData.ClientData(writes: new[] { ("msdyn_workorders", new[] { "msdyn_name" }, "UpdateRecord") }));
            var broad = TestData.Record("Broad",
                TestData.ClientData(triggerEntity: "msdyn_workorder", message: 4));

            var index = FlowIndex.Build(new[] { setter, broad }, resolver);
            return new DependencyAnalyzer(index).Analyze(new FieldRef("msdyn_workorder", "msdyn_name"));
        }

        [Fact]
        public void VisJs_ProducesValidJson_WithNodesAndEdges()
        {
            var json = VisJsGraphSerializer.Serialize(SampleGraph());
            var parsed = JObject.Parse(json);

            var nodes = (JArray)parsed["nodes"];
            var edges = (JArray)parsed["edges"];

            Assert.True(nodes.Count >= 3); // seed field + 2 flows
            Assert.True(edges.Count >= 2);

            // Seed node is present and grouped as seed.
            Assert.Contains(nodes, n => (string)n["group"] == "seed");
            // Broad edge is dashed.
            Assert.Contains(edges, e => (bool)e["dashes"]);
        }

        [Fact]
        public void Mermaid_ContainsAllNodesAndBroadEdges()
        {
            var mermaid = MermaidExporter.Export(SampleGraph());

            Assert.StartsWith("graph LR", mermaid);
            Assert.Contains("msdyn_workorder.msdyn_name", mermaid);
            Assert.Contains("-. broad .->", mermaid);
            // Quotes in flow names must not break the syntax.
            Assert.DoesNotContain("\"Setter \"quoted\"", mermaid);
        }
    }
}
