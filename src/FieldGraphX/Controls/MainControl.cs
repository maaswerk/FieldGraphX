using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldGraphX.Core.Abstractions;
using FieldGraphX.Core.Analysis;
using FieldGraphX.Core.Export;
using FieldGraphX.Core.Models;
using FieldGraphX.Services;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace FieldGraphX.Controls
{
    public partial class MainControl : PluginControlBase, IGitHubPlugin
    {
        // ── State ──────────────────────────────────────────────────────────────
        private Settings _settings;
        private string _environmentId = string.Empty;
        private IReadOnlyList<EntityInfo> _entities = Array.Empty<EntityInfo>();
        private EntityNameResolver _resolver = new EntityNameResolver();
        private IReadOnlyList<FlowRecord> _cachedFlows;   // per-session flow cache
        private DependencyGraph _graph;

        // ── UI ─────────────────────────────────────────────────────────────────
        private ToolStrip _toolbar;
        private ToolStripComboBox _cmbEntity;
        private ToolStripComboBox _cmbField;
        private ToolStripButton _btnAnalyze;
        private ToolStripButton _btnRefreshFlows;
        private ToolStripButton _btnExportMermaid;
        private ToolStripComboBox _cmbDepth;
        private ToolStripMenuItem _mnuIncludeBroad;
        private ToolStripMenuItem _mnuRecurseBroad;
        private ToolStripMenuItem _mnuIncludeDrafts;
        private ToolStripMenuItem _mnuIncludeInactive;
        private TreeView _tree;
        private TabControl _tabs;
        private FlowDetailPanel _detailPanel;
        private GraphViewPanel _graphPanel;
        private ListBox _logList;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;

        public string RepositoryName => "FieldGraphX";
        public string UserName => "maaswerk";

        public MainControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Size = new Size(1100, 700);

            BuildToolbar();
            BuildBody();
            BuildStatusStrip();

            Load += (s, e) => LoadSettings();

            ResumeLayout(false);
            PerformLayout();
        }

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

            var btnClose = new ToolStripButton("Close") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnClose.Click += (s, e) => CloseTool();

            _cmbEntity = new ToolStripComboBox("Entity")
            {
                Width = 260,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            _cmbEntity.SelectedIndexChanged += (s, e) => OnEntitySelected();

            _cmbField = new ToolStripComboBox("Field")
            {
                Width = 260,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DropDownStyle = ComboBoxStyle.DropDown
            };

            _btnAnalyze = new ToolStripButton("Analyze") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            _btnAnalyze.Click += (s, e) => ExecuteMethod(RunAnalysis);

            _btnRefreshFlows = new ToolStripButton("Refresh flows")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Flows are cached per session; click to re-fetch them from Dataverse."
            };
            _btnRefreshFlows.Click += (s, e) => { _cachedFlows = null; Log("Flow cache cleared."); };

            _cmbDepth = new ToolStripComboBox("Depth") { Width = 50, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDepth.Items.AddRange(new object[] { "2", "3", "5", "10", "15" });

            var mnuOptions = new ToolStripDropDownButton("Options");
            _mnuIncludeBroad = new ToolStripMenuItem("Show broad triggers (no filtering attributes)")
            { CheckOnClick = true, Checked = true };
            _mnuRecurseBroad = new ToolStripMenuItem("Recurse through broad triggers")
            { CheckOnClick = true, Checked = false };
            _mnuIncludeDrafts = new ToolStripMenuItem("Include draft flows")
            { CheckOnClick = true, Checked = true };
            _mnuIncludeInactive = new ToolStripMenuItem("Include turned-off flows")
            { CheckOnClick = true, Checked = true };
            mnuOptions.DropDownItems.AddRange(new ToolStripItem[]
            { _mnuIncludeBroad, _mnuRecurseBroad, _mnuIncludeDrafts, _mnuIncludeInactive });

            _btnExportMermaid = new ToolStripButton("Copy Mermaid")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Copies the graph as a Mermaid definition (paste into a wiki or mermaid.live).",
                Enabled = false
            };
            _btnExportMermaid.Click += (s, e) => ExportMermaid();

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                btnClose,
                new ToolStripSeparator(),
                new ToolStripLabel("Entity:"), _cmbEntity,
                new ToolStripLabel("Field:"), _cmbField,
                _btnAnalyze,
                new ToolStripSeparator(),
                new ToolStripLabel("Depth:"), _cmbDepth,
                mnuOptions,
                new ToolStripSeparator(),
                _btnRefreshFlows,
                _btnExportMermaid
            });

            Controls.Add(_toolbar);
        }

        private void BuildBody()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 360
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                ShowNodeToolTips = true
            };
            _tree.AfterSelect += (s, e) => OnTreeNodeSelected(e.Node);
            split.Panel1.Controls.Add(_tree);

            _tabs = new TabControl { Dock = DockStyle.Fill };

            var tabDetails = new TabPage("Details");
            _detailPanel = new FlowDetailPanel();
            tabDetails.Controls.Add(_detailPanel);

            var tabGraph = new TabPage("Graph");
            _graphPanel = new GraphViewPanel();
            _graphPanel.NodeSelected += OnGraphNodeSelected;
            _graphPanel.NodeOpened += OnGraphNodeOpened;
            tabGraph.Controls.Add(_graphPanel);

            var tabLog = new TabPage("Log");
            _logList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            tabLog.Controls.Add(_logList);

            _tabs.TabPages.AddRange(new[] { tabDetails, tabGraph, tabLog });
            split.Panel2.Controls.Add(_tabs);

            Controls.Add(split);
            split.BringToFront();
        }

        private void BuildStatusStrip()
        {
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Connect to an organization to start.");
            _statusStrip.Items.Add(_statusLabel);
            Controls.Add(_statusStrip);
        }

        // ── Settings ───────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            if (!SettingsManager.Instance.TryLoad(GetType(), out _settings))
                _settings = new Settings();

            _cmbDepth.SelectedItem = _cmbDepth.Items.Contains(_settings.MaxDepth.ToString())
                ? _settings.MaxDepth.ToString() : "10";
            _mnuIncludeBroad.Checked = _settings.IncludeBroadTriggers;
            _mnuRecurseBroad.Checked = _settings.RecurseThroughBroadTriggers;
            _mnuIncludeDrafts.Checked = _settings.IncludeDrafts;
            _mnuIncludeInactive.Checked = _settings.IncludeInactive;
        }

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            if (_settings != null)
            {
                _settings.LastEntity = _cmbEntity.Text?.Trim() ?? string.Empty;
                _settings.LastField = _cmbField.Text?.Trim() ?? string.Empty;
                _settings.MaxDepth = ParseDepth();
                _settings.IncludeBroadTriggers = _mnuIncludeBroad.Checked;
                _settings.RecurseThroughBroadTriggers = _mnuRecurseBroad.Checked;
                _settings.IncludeDrafts = _mnuIncludeDrafts.Checked;
                _settings.IncludeInactive = _mnuIncludeInactive.Checked;
                SettingsManager.Instance.Save(GetType(), _settings);
            }
            base.ClosingPlugin(info);
        }

        private int ParseDepth() =>
            int.TryParse(_cmbDepth.Text, out var depth) && depth > 0 ? depth : 10;

        // ── Connection / metadata ──────────────────────────────────────────────

        public override void UpdateConnection(
            IOrganizationService newService, ConnectionDetail detail,
            string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            // New org — everything cached from the old one is stale.
            _cachedFlows = null;
            _environmentId = string.Empty;
            LoadMetadata();
        }

        private void LoadMetadata()
        {
            if (Service == null) return;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading entity metadata…",
                Work = (worker, args) =>
                {
                    var metadata = new MetadataRepository(Service);
                    var entities = metadata.LoadEntities();

                    var warnings = new List<string>();
                    var environmentId = new EnvironmentInfoService(Service)
                        .GetEnvironmentId(warnings.Add);

                    args.Result = (entities, environmentId, warnings);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowError("Loading metadata failed", args.Error);
                        return;
                    }

                    var (entities, environmentId, warnings) =
                        ((IReadOnlyList<EntityInfo>, string, List<string>))args.Result;

                    _entities = entities;
                    _environmentId = environmentId;
                    _resolver = new EntityNameResolver(MetadataRepository.BuildNameMap(entities));
                    foreach (var warning in warnings) Log("WARN: " + warning);

                    _cmbEntity.Items.Clear();
                    _cmbEntity.Items.AddRange(entities.Cast<object>().ToArray());

                    if (!string.IsNullOrEmpty(_settings?.LastEntity))
                    {
                        var last = entities.FirstOrDefault(x => x.LogicalName == _settings.LastEntity);
                        if (last != null) _cmbEntity.SelectedItem = last;
                    }

                    SetStatus($"{entities.Count} entities loaded. Pick entity and field, then Analyze.");
                }
            });
        }

        private void OnEntitySelected()
        {
            var entity = _cmbEntity.SelectedItem as EntityInfo;
            if (entity == null || Service == null) return;

            WorkAsync(new WorkAsyncInfo
            {
                Message = $"Loading fields of {entity.LogicalName}…",
                Work = (worker, args) =>
                {
                    args.Result = new MetadataRepository(Service).LoadAttributes(entity.LogicalName);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowError($"Loading fields of {entity.LogicalName} failed", args.Error);
                        return;
                    }

                    var attributes = (IReadOnlyList<AttributeInfo>)args.Result;
                    _cmbField.Items.Clear();
                    _cmbField.Items.AddRange(attributes.Cast<object>().ToArray());

                    if (!string.IsNullOrEmpty(_settings?.LastField))
                    {
                        var last = attributes.FirstOrDefault(a => a.LogicalName == _settings.LastField);
                        if (last != null) _cmbField.SelectedItem = last;
                    }

                    SetStatus($"{attributes.Count} fields loaded for {entity.LogicalName}.");
                }
            });
        }

        // ── Analysis ───────────────────────────────────────────────────────────

        private void RunAnalysis()
        {
            var entityName = (_cmbEntity.SelectedItem as EntityInfo)?.LogicalName
                             ?? _cmbEntity.Text?.Trim().ToLowerInvariant();
            var fieldName = (_cmbField.SelectedItem as AttributeInfo)?.LogicalName
                            ?? _cmbField.Text?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(fieldName))
            {
                MessageBox.Show("Please select an entity and a field first.", "FieldGraphX",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var seed = new FieldRef(entityName, fieldName);
            var options = new AnalyzerOptions
            {
                MaxDepth = ParseDepth(),
                IncludeBroadTriggers = _mnuIncludeBroad.Checked,
                RecurseThroughBroadTriggers = _mnuRecurseBroad.Checked,
                IncludeDrafts = _mnuIncludeDrafts.Checked,
                IncludeInactive = _mnuIncludeInactive.Checked
            };
            var cachedFlows = _cachedFlows;
            var resolver = _resolver;

            _logList.Items.Clear();
            var stopwatch = Stopwatch.StartNew();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Analyzing flow dependencies…",
                Work = (worker, args) =>
                {
                    var flows = cachedFlows;
                    if (flows == null)
                    {
                        var repository = new DataverseFlowRepository(Service);
                        flows = repository.GetAllCloudFlows(
                            count => worker.ReportProgress(0, $"Loading cloud flows… {count}"));
                    }

                    worker.ReportProgress(0, "Building flow index…");
                    var index = FlowIndex.Build(flows, resolver);

                    worker.ReportProgress(0, "Tracing dependencies…");
                    var log = new List<string>();
                    var analyzer = new DependencyAnalyzer(index, options, log.Add);
                    var graph = analyzer.Analyze(seed);

                    args.Result = (flows, graph, VisJsGraphSerializer.Serialize(graph), log);
                },
                ProgressChanged = e => SetWorkingMessage(e.UserState?.ToString() ?? string.Empty),
                PostWorkCallBack = args =>
                {
                    stopwatch.Stop();
                    if (args.Error != null)
                    {
                        ShowError("Analysis failed", args.Error);
                        return;
                    }

                    var (flows, graph, visJson, log) =
                        ((IReadOnlyList<FlowRecord>, DependencyGraph, string, List<string>))args.Result;

                    _cachedFlows = flows;
                    _graph = graph;
                    foreach (var line in log) Log(line);

                    PopulateTree(graph);
                    _graphPanel.ShowGraph(visJson);
                    _btnExportMermaid.Enabled = true;

                    var flowCount = graph.Nodes.Values.Count(n => n.Kind == NodeKind.Flow);
                    var status = $"{flowCount} flow(s), {graph.Edges.Count} dependencies " +
                                 $"({flows.Count} flows scanned in {stopwatch.Elapsed.TotalSeconds:0.0}s)";
                    if (graph.DepthLimitHit) status += " — depth limit reached, result truncated";
                    SetStatus(status);
                }
            });
        }

        // ── Tree projection ────────────────────────────────────────────────────

        private void PopulateTree(DependencyGraph graph)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            var seedId = graph.Seed.Id;
            var root = new TreeNode($"🔍 {graph.Seed.Label}")
            {
                Tag = graph.Seed,
                ToolTipText = "The searched field"
            };

            var downstream = new TreeNode("⬇ Flows triggered by this field");
            foreach (var edge in graph.EdgesFrom(seedId)
                     .Where(e => e.Kind == EdgeKind.Triggers || e.Kind == EdgeKind.TriggersBroad)
                     .OrderBy(e => e.Kind))
            {
                if (graph.Nodes.TryGetValue(edge.ToId, out var flowNode))
                    downstream.Nodes.Add(BuildFlowTreeNode(graph, flowNode, edge,
                        new HashSet<string> { seedId }, downstreamView: true));
            }

            var upstream = new TreeNode("⬆ Flows that set this field");
            foreach (var edge in graph.EdgesTo(seedId).Where(e => e.Kind == EdgeKind.Sets))
            {
                if (graph.Nodes.TryGetValue(edge.FromId, out var flowNode))
                    upstream.Nodes.Add(BuildFlowTreeNode(graph, flowNode, edge,
                        new HashSet<string> { seedId }, downstreamView: false));
            }

            downstream.Text += $" ({downstream.Nodes.Count})";
            upstream.Text += $" ({upstream.Nodes.Count})";

            root.Nodes.Add(downstream);
            root.Nodes.Add(upstream);
            _tree.Nodes.Add(root);

            root.Expand();
            downstream.Expand();
            upstream.Expand();
            _tree.EndUpdate();
        }

        private TreeNode BuildFlowTreeNode(
            DependencyGraph graph, GraphNode flowNode, GraphEdge viaEdge,
            HashSet<string> path, bool downstreamView)
        {
            var flow = flowNode.Flow;
            var text = $"{FlowIcon(flow)} {flow.Name}{StatusSuffix(flow)}";
            if (viaEdge?.Kind == EdgeKind.TriggersBroad)
                text += "  (broad – triggers on every change)";

            var node = new TreeNode(text) { Tag = flowNode, ToolTipText = flow.Name };

            if (!path.Add(flowNode.Id))
            {
                node.Text = $"↺ {flow.Name} (see above)";
                return node;
            }

            if (downstreamView)
            {
                // Fields this flow sets → flows triggered by them.
                foreach (var setsEdge in graph.EdgesFrom(flowNode.Id).Where(e => e.Kind == EdgeKind.Sets))
                {
                    if (!graph.Nodes.TryGetValue(setsEdge.ToId, out var fieldNode)) continue;

                    var consumers = graph.EdgesFrom(fieldNode.Id)
                        .Where(e => e.Kind == EdgeKind.Triggers || e.Kind == EdgeKind.TriggersBroad)
                        .ToList();
                    if (consumers.Count == 0) continue; // fields nobody listens to stay out of the tree

                    var fieldTreeNode = new TreeNode($"🏷 sets {fieldNode.Label}") { Tag = fieldNode };
                    if (path.Add(fieldNode.Id))
                    {
                        foreach (var trigEdge in consumers)
                        {
                            if (graph.Nodes.TryGetValue(trigEdge.ToId, out var childFlow))
                                fieldTreeNode.Nodes.Add(
                                    BuildFlowTreeNode(graph, childFlow, trigEdge, path, true));
                        }
                        path.Remove(fieldNode.Id);
                    }
                    else
                    {
                        fieldTreeNode.Text += " ↺";
                    }
                    node.Nodes.Add(fieldTreeNode);
                }
            }
            else
            {
                // Fields that trigger this flow → flows that set them.
                foreach (var trigEdge in graph.EdgesTo(flowNode.Id)
                         .Where(e => e.Kind == EdgeKind.Triggers))
                {
                    if (!graph.Nodes.TryGetValue(trigEdge.FromId, out var fieldNode)) continue;
                    if (fieldNode.IsSeed) continue;

                    var setters = graph.EdgesTo(fieldNode.Id).Where(e => e.Kind == EdgeKind.Sets).ToList();

                    var fieldTreeNode = new TreeNode($"🏷 triggered by {fieldNode.Label}") { Tag = fieldNode };
                    if (path.Add(fieldNode.Id))
                    {
                        foreach (var setsEdge in setters)
                        {
                            if (graph.Nodes.TryGetValue(setsEdge.FromId, out var parentFlow))
                                fieldTreeNode.Nodes.Add(
                                    BuildFlowTreeNode(graph, parentFlow, setsEdge, path, false));
                        }
                        path.Remove(fieldNode.Id);
                    }
                    else
                    {
                        fieldTreeNode.Text += " ↺";
                    }
                    node.Nodes.Add(fieldTreeNode);
                }
            }

            path.Remove(flowNode.Id);
            return node;
        }

        private static string FlowIcon(FlowInfo flow)
        {
            if (flow.Status != FlowStatus.Active) return "⏸";
            return flow.Trigger.IsBroadTrigger ? "⚠" : "⚡";
        }

        private static string StatusSuffix(FlowInfo flow) =>
            flow.Status == FlowStatus.Draft ? " [Draft]" :
            flow.Status == FlowStatus.Inactive ? " [Off]" : string.Empty;

        // ── Selection sync ─────────────────────────────────────────────────────

        private void OnTreeNodeSelected(TreeNode treeNode)
        {
            if (!(treeNode?.Tag is GraphNode graphNode)) return;

            if (graphNode.Kind == NodeKind.Flow)
                _detailPanel.ShowFlow(graphNode.Flow, _environmentId);
            else if (graphNode.Field.HasValue)
                _detailPanel.ShowField(graphNode.Field.Value, graphNode.IsSeed);

            _graphPanel.SelectNode(graphNode.Id);
        }

        private void OnGraphNodeSelected(string nodeId)
        {
            if (_graph == null || !_graph.Nodes.TryGetValue(nodeId, out var graphNode)) return;

            if (InvokeRequired) { BeginInvoke(new Action(() => ShowNodeDetails(graphNode))); }
            else ShowNodeDetails(graphNode);
        }

        private void ShowNodeDetails(GraphNode graphNode)
        {
            if (graphNode.Kind == NodeKind.Flow)
                _detailPanel.ShowFlow(graphNode.Flow, _environmentId);
            else if (graphNode.Field.HasValue)
                _detailPanel.ShowField(graphNode.Field.Value, graphNode.IsSeed);

            var treeNode = FindTreeNode(_tree.Nodes, graphNode.Id);
            if (treeNode != null)
            {
                _tree.SelectedNode = treeNode;
                treeNode.EnsureVisible();
            }
        }

        private void OnGraphNodeOpened(string nodeId)
        {
            if (_graph == null || !_graph.Nodes.TryGetValue(nodeId, out var graphNode)) return;
            if (graphNode.Kind != NodeKind.Flow) return;

            var url = EnvironmentInfoService.BuildFlowUrl(_environmentId, graphNode.Flow.WorkflowIdUnique);
            try { Process.Start(url); }
            catch (Exception ex) { Log($"WARN: could not open browser: {ex.Message}"); }
        }

        private static TreeNode FindTreeNode(TreeNodeCollection nodes, string graphNodeId)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is GraphNode graphNode && graphNode.Id == graphNodeId) return node;
                var found = FindTreeNode(node.Nodes, graphNodeId);
                if (found != null) return found;
            }
            return null;
        }

        // ── Export / misc ──────────────────────────────────────────────────────

        private void ExportMermaid()
        {
            if (_graph == null) return;
            Clipboard.SetText(MermaidExporter.Export(_graph));
            SetStatus("Mermaid definition copied to clipboard.");
        }

        private void ShowError(string context, Exception error)
        {
            Log($"ERROR: {context}: {error}");
            MessageBox.Show($"{context}:\n{error.Message}", "FieldGraphX",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Log(string message)
        {
            _logList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            _logList.TopIndex = _logList.Items.Count - 1;
        }

        private void SetStatus(string text) => _statusLabel.Text = text;
    }
}
