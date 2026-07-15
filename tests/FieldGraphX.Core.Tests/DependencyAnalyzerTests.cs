using System.Linq;
using FieldGraphX.Core.Abstractions;
using FieldGraphX.Core.Analysis;
using FieldGraphX.Core.Models;
using Xunit;

namespace FieldGraphX.Core.Tests
{
    public class DependencyAnalyzerTests
    {
        private static readonly EntityNameResolver Resolver = new EntityNameResolver(
            new System.Collections.Generic.Dictionary<string, string>
            {
                ["msdyn_workorders"] = "msdyn_workorder",
                ["accounts"] = "account",
                ["incidents"] = "incident"
            });

        private static DependencyGraph Analyze(
            FieldRef seed, AnalyzerOptions options = null, params FlowRecord[] records)
        {
            var index = FlowIndex.Build(records, Resolver);
            return new DependencyAnalyzer(index, options).Analyze(seed);
        }

        private static string FlowNodeId(string name) =>
            GraphNode.FlowId(TestData.DeterministicGuid(name));

        // ── Direct level: the user's core scenario ─────────────────────────────

        [Fact]
        public void FindsSettersAndTriggeredFlows_ForTheSeedField()
        {
            var setter = TestData.Record("Setter",
                TestData.ClientData(writes: new[] { ("msdyn_workorders", new[] { "msdyn_name" }, "UpdateRecord") }));
            var triggered = TestData.Record("Triggered",
                TestData.ClientData(triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name"));
            var unrelated = TestData.Record("Unrelated",
                TestData.ClientData(triggerEntity: "account", filteringAttributes: "name"));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null,
                                setter, triggered, unrelated);

            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("Setter")));
            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("Triggered")));
            Assert.False(graph.Nodes.ContainsKey(FlowNodeId("Unrelated")));

            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Sets && e.FromId == FlowNodeId("Setter") && e.ToId == graph.Seed.Id);
            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Triggers && e.FromId == graph.Seed.Id && e.ToId == FlowNodeId("Triggered"));
        }

        [Fact]
        public void BroadTriggerFlows_AreReported_MarkedAsBroad()
        {
            // Flow with NO filtering attributes on the seed entity: fires on every change,
            // must appear for any searched field of that entity.
            var broad = TestData.Record("BroadFlow",
                TestData.ClientData(triggerEntity: "msdyn_workorder", message: 4));

            var graph = Analyze(new FieldRef("msdyn_workorder", "anyfield"), null, broad);

            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("BroadFlow")));
            var edge = Assert.Single(graph.Edges, e => e.ToId == FlowNodeId("BroadFlow"));
            Assert.Equal(EdgeKind.TriggersBroad, edge.Kind);
        }

        [Fact]
        public void BroadTriggerFlows_CanBeExcluded()
        {
            var broad = TestData.Record("BroadFlow",
                TestData.ClientData(triggerEntity: "msdyn_workorder", message: 4));

            var graph = Analyze(new FieldRef("msdyn_workorder", "anyfield"),
                new AnalyzerOptions { IncludeBroadTriggers = false }, broad);

            Assert.False(graph.Nodes.ContainsKey(FlowNodeId("BroadFlow")));
        }

        // ── Recursive chain ────────────────────────────────────────────────────

        [Fact]
        public void DownstreamChain_IsFollowedAcrossEntities()
        {
            // seed msdyn_workorder.msdyn_name → FlowB (sets account.name) → FlowC (triggers on account.name)
            var flowB = TestData.Record("FlowB",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name",
                    writes: new[] { ("accounts", new[] { "name" }, "UpdateRecord") }));
            var flowC = TestData.Record("FlowC",
                TestData.ClientData(triggerEntity: "account", filteringAttributes: "name"));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null, flowB, flowC);

            var accountName = GraphNode.FieldId(new FieldRef("account", "name"));
            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Sets && e.FromId == FlowNodeId("FlowB") && e.ToId == accountName);
            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Triggers && e.FromId == accountName && e.ToId == FlowNodeId("FlowC"));
        }

        [Fact]
        public void UpstreamChain_IsFollowed()
        {
            // FlowA (triggers on incident.title, sets workorder.msdyn_name) ← seed
            // FlowZ (sets incident.title) ← FlowA's trigger field
            var flowA = TestData.Record("FlowA",
                TestData.ClientData(
                    triggerEntity: "incident", filteringAttributes: "title",
                    writes: new[] { ("msdyn_workorders", new[] { "msdyn_name" }, "UpdateRecord") }));
            var flowZ = TestData.Record("FlowZ",
                TestData.ClientData(writes: new[] { ("incidents", new[] { "title" }, "UpdateRecord") }));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null, flowA, flowZ);

            var incidentTitle = GraphNode.FieldId(new FieldRef("incident", "title"));
            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Sets && e.FromId == FlowNodeId("FlowA") && e.ToId == graph.Seed.Id);
            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Triggers && e.FromId == incidentTitle && e.ToId == FlowNodeId("FlowA"));
            Assert.Contains(graph.Edges, e =>
                e.Kind == EdgeKind.Sets && e.FromId == FlowNodeId("FlowZ") && e.ToId == incidentTitle);
        }

        [Fact]
        public void Cycle_ClosesLoopInsteadOfLoopingForever()
        {
            // FlowA: triggers on wo.a, sets wo.b.  FlowB: triggers on wo.b, sets wo.a.
            var flowA = TestData.Record("CycleA",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "a",
                    writes: new[] { ("msdyn_workorders", new[] { "b" }, "UpdateRecord") }));
            var flowB = TestData.Record("CycleB",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "b",
                    writes: new[] { ("msdyn_workorders", new[] { "a" }, "UpdateRecord") }));

            var graph = Analyze(new FieldRef("msdyn_workorder", "a"), null, flowA, flowB);

            // Both flows and both fields present exactly once; the cycle is a closed loop.
            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("CycleA")));
            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("CycleB")));
            Assert.Equal(4, graph.Nodes.Count); // wo.a, wo.b, CycleA, CycleB
            Assert.False(graph.DepthLimitHit);
        }

        [Fact]
        public void Diamond_TwoSettersOfSameField_BothKeepTheirEdges()
        {
            // Regression for the old per-path cache that dropped sibling edges:
            // two flows set incident.title; a third triggers on it. All edges must exist.
            var setter1 = TestData.Record("Setter1",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name",
                    writes: new[] { ("incidents", new[] { "title" }, "UpdateRecord") }));
            var setter2 = TestData.Record("Setter2",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name",
                    writes: new[] { ("incidents", new[] { "title" }, "UpdateRecord") }));
            var consumer = TestData.Record("Consumer",
                TestData.ClientData(triggerEntity: "incident", filteringAttributes: "title"));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null,
                                setter1, setter2, consumer);

            var incidentTitle = GraphNode.FieldId(new FieldRef("incident", "title"));
            Assert.Contains(graph.Edges, e => e.FromId == FlowNodeId("Setter1") && e.ToId == incidentTitle);
            Assert.Contains(graph.Edges, e => e.FromId == FlowNodeId("Setter2") && e.ToId == incidentTitle);
            Assert.Contains(graph.Edges, e => e.FromId == incidentTitle && e.ToId == FlowNodeId("Consumer"));
        }

        [Fact]
        public void DepthLimit_StopsTraversal_AndIsReported()
        {
            // Chain: f0 → flow0 → f1 → flow1 → f2 → ... with MaxDepth 2.
            var records = Enumerable.Range(0, 5).Select(i =>
                TestData.Record($"Chain{i}",
                    TestData.ClientData(
                        triggerEntity: "msdyn_workorder", filteringAttributes: $"f{i}",
                        writes: new[] { ("msdyn_workorders", new[] { $"f{i + 1}" }, "UpdateRecord") })))
                .ToArray();

            var graph = Analyze(new FieldRef("msdyn_workorder", "f0"),
                new AnalyzerOptions { MaxDepth = 2 }, records);

            Assert.True(graph.DepthLimitHit);
            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("Chain0")));
            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("Chain1")));
            Assert.False(graph.Nodes.ContainsKey(FlowNodeId("Chain3")));
        }

        // ── Change-type matching ───────────────────────────────────────────────

        [Fact]
        public void UpdateWriter_DoesNotFire_CreateOnlyTrigger()
        {
            // FlowB updates account.name; FlowC triggers only on CREATE of account.name.
            var flowB = TestData.Record("Writer",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name",
                    writes: new[] { ("accounts", new[] { "name" }, "UpdateRecord") }));
            var flowC = TestData.Record("CreateOnly",
                TestData.ClientData(triggerEntity: "account", filteringAttributes: "name", message: 1));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null, flowB, flowC);

            Assert.False(graph.Nodes.ContainsKey(FlowNodeId("CreateOnly")));
        }

        [Fact]
        public void CreateWriter_Fires_CreateTrigger()
        {
            var flowB = TestData.Record("Creator",
                TestData.ClientData(
                    triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name",
                    writes: new[] { ("accounts", new[] { "name" }, "CreateRecord") }));
            var flowC = TestData.Record("CreateOnly",
                TestData.ClientData(triggerEntity: "account", filteringAttributes: "name", message: 1));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null, flowB, flowC);

            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("CreateOnly")));
        }

        // ── Status filtering ───────────────────────────────────────────────────

        [Fact]
        public void DraftsAndInactiveFlows_CanBeExcluded()
        {
            var draft = TestData.Record("DraftFlow",
                TestData.ClientData(triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name"),
                type: 2);
            var off = TestData.Record("OffFlow",
                TestData.ClientData(triggerEntity: "msdyn_workorder", filteringAttributes: "msdyn_name"),
                stateCode: 0);

            var all = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null, draft, off);
            Assert.True(all.Nodes.ContainsKey(FlowNodeId("DraftFlow")));
            Assert.True(all.Nodes.ContainsKey(FlowNodeId("OffFlow")));

            var filtered = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"),
                new AnalyzerOptions { IncludeDrafts = false, IncludeInactive = false }, draft, off);
            Assert.False(filtered.Nodes.ContainsKey(FlowNodeId("DraftFlow")));
            Assert.False(filtered.Nodes.ContainsKey(FlowNodeId("OffFlow")));
        }

        // ── Entity set name resolution ─────────────────────────────────────────

        [Fact]
        public void PluralEntitySetNames_InActions_MatchSingularSeedEntity()
        {
            // Action stores "msdyn_workorders" (EntitySetName); the user searches
            // "msdyn_workorder" (logical name). Must match via the resolver.
            var setter = TestData.Record("PluralSetter",
                TestData.ClientData(writes: new[] { ("msdyn_workorders", new[] { "msdyn_name" }, "UpdateRecord") }));

            var graph = Analyze(new FieldRef("msdyn_workorder", "msdyn_name"), null, setter);

            Assert.True(graph.Nodes.ContainsKey(FlowNodeId("PluralSetter")));
        }
    }
}
