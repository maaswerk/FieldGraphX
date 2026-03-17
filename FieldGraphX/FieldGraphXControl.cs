// ============================================================================
//  FieldGraphXControl  –  Main plugin UI
//  Layout: 30 % TreeView  |  70 % Detail panel
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldGraphX.Logic;
using McTools.Xrm.Connection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace FieldGraphX
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Designer-free control  (no .designer.cs required for the new layout)
    // ──────────────────────────────────────────────────────────────────────────
    public class FieldGraphXControl : PluginControlBase
    {
        // ── Settings ──────────────────────────────────────────────────────────
        private Settings _settings;

        // ── Service helpers ───────────────────────────────────────────────────
        private InfoLoader _infoLoader;
        private string _environmentId = string.Empty;

        // ── UI components ─────────────────────────────────────────────────────
        private ToolStrip _toolStrip;
        private ToolStripButton _tsbClose;
        private ToolStripSeparator _tsSep1;
        private ToolStripButton _tsbAnalyze;
        private ToolStripLabel _tslEntity;
        private ToolStripComboBox _tscEntity;
        private ToolStripLabel _tslField;
        private ToolStripComboBox _tscField;

        private SplitContainer _split;          // 30 / 70

        // Left pane
        private TreeView _tree;
        private Label _lblTreeHeader;

        // Right pane
        private Panel _detailPanel;
        private Label _lblDetailHeader;
        private Label _lblFlowName;
        private Label _lblTrigger;
        private Label _lblStatus;
        private RichTextBox _rtbDetails;
        private Button _btnOpenFlow;

        // ── State ─────────────────────────────────────────────────────────────
        private FlowNode _selectedNode;

        // ── Progress tracking ─────────────────────────────────────────────────
        private System.Windows.Forms.Timer _elapsedTimer;
        private DateTime _analysisStartTime;
        private ProgressBar _progressBar;
        private Label _lblProgress;

        // ── Colors ────────────────────────────────────────────────────────────
        // Debug mode
        private ToolStripButton _tsbDebug;
        private Panel _debugPanel;
        private RichTextBox _rtbDebugLog;
        private bool _debugMode = false;

        private static readonly Color ColorBroadTrigger = Color.FromArgb(200, 50, 50);   // red
        private static readonly Color ColorUpdater = Color.FromArgb(34, 139, 34);   // green
        private static readonly Color ColorTrigger = Color.FromArgb(30, 100, 180);  // blue
        private static readonly Color ColorBoth = Color.FromArgb(160, 100, 0);   // amber
        private static readonly Color ColorPanelBg = Color.FromArgb(245, 247, 250);
        private static readonly Color ColorInactive = Color.FromArgb(140, 140, 140);  // gray
        private static readonly Color ColorDraft = Color.FromArgb(160, 130, 50);   // amber-gray
        private static readonly Color ColorHeaderBg = Color.FromArgb(60, 80, 120);

        // ══════════════════════════════════════════════════════════════════════
        public FieldGraphXControl()
        {
            BuildUI();
        }

        // ──────────────────────────────────────────────────────────────────────
        //  UI construction (replaces InitializeComponent)
        // ──────────────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = ColorPanelBg;

            // ── ToolStrip ──────────────────────────────────────────────────────
            _toolStrip = new ToolStrip { BackColor = ColorHeaderBg, GripStyle = ToolStripGripStyle.Hidden };

            _tsbClose = new ToolStripButton("Close Tool")
            {
                ForeColor = Color.White,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            _tsbClose.Click += (s, e) => CloseTool();

            _tsSep1 = new ToolStripSeparator();

            _tslEntity = new ToolStripLabel("Entity:") { ForeColor = Color.White };
            _tscEntity = new ToolStripComboBox
            {
                AutoSize = false,
                Width = 160,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            _tscEntity.SelectedIndexChanged += OnEntitySelectionChanged;

            _tslField = new ToolStripLabel("  Field:") { ForeColor = Color.White };
            _tscField = new ToolStripComboBox
            {
                AutoSize = false,
                Width = 160,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };

            _tsbAnalyze = new ToolStripButton("  ▶  Analyze")
            {
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                BackColor = Color.FromArgb(0, 120, 215)
            };
            _tsbAnalyze.Click += OnAnalyzeClicked;

            _tsbDebug = new ToolStripButton("🐛 Debug")
            {
                ForeColor = Color.White,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                CheckOnClick = true,
                ToolTipText = "Toggle debug log panel"
            };
            _tsbDebug.CheckedChanged += OnDebugToggled;

            _toolStrip.Items.AddRange(new ToolStripItem[]
            {
                _tsbClose, _tsSep1,
                _tslEntity, _tscEntity,
                _tslField,  _tscField,
                new ToolStripSeparator(),
                _tsbAnalyze,
                new ToolStripSeparator(),
                _tsbDebug
            });

            // ── SplitContainer ─────────────────────────────────────────────────
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                BackColor = Color.FromArgb(200, 210, 230),
            };

            // ── LEFT PANE – TreeView ───────────────────────────────────────────
            _lblTreeHeader = new Label
            {
                Text = "Dependency Tree",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorHeaderBg,
                ForeColor = Color.White
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                Font = new Font("Segoe UI", 9),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                DrawMode = TreeViewDrawMode.OwnerDrawText
            };
            _tree.DrawNode += OnTreeDrawNode;
            _tree.AfterSelect += OnTreeNodeSelected;

            var leftContainer = new Panel { Dock = DockStyle.Fill };
            leftContainer.Controls.Add(_tree);
            leftContainer.Controls.Add(_lblTreeHeader);

            _split.Panel1.Controls.Add(leftContainer);

            // ── RIGHT PANE – Detail panel ──────────────────────────────────────
            _detailPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(0)
            };

            _lblDetailHeader = new Label
            {
                Text = "Flow Details",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorHeaderBg,
                ForeColor = Color.White
            };

            // Summary strip below the header
            var summaryStrip = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(235, 240, 250),
                Padding = new Padding(12, 8, 8, 4)
            };

            _lblFlowName = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = ColorHeaderBg,
                Text = "(select a flow in the tree)"
            };
            _lblTrigger = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.DimGray,
                Text = ""
            };
            _lblStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.Gray,
                Text = ""
            };

            summaryStrip.Controls.Add(_lblStatus);
            summaryStrip.Controls.Add(_lblTrigger);
            summaryStrip.Controls.Add(_lblFlowName);

            // "Open Flow" button
            _btnOpenFlow = new Button
            {
                Text = "Open Flow in Power Automate  ↗",
                Dock = DockStyle.Bottom,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _btnOpenFlow.FlatAppearance.BorderSize = 0;
            _btnOpenFlow.Click += OnOpenFlowClicked;

            // Rich text box for full details
            _rtbDetails = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            _detailPanel.Controls.Add(_rtbDetails);
            _detailPanel.Controls.Add(summaryStrip);
            _detailPanel.Controls.Add(_lblDetailHeader);
            _detailPanel.Controls.Add(_btnOpenFlow);

            _split.Panel2.Controls.Add(_detailPanel);

            // ── Progress bar panel (sits between toolbar and split) ───────────
            var progressPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(230, 235, 245),
                Padding = new Padding(6, 4, 6, 4),
                Visible = false  // hidden until an analysis starts
            };
            // name it so we can find it later
            progressPanel.Name = "progressPanel";

            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Dock = DockStyle.Fill,
                MarqueeAnimationSpeed = 25
            };

            _lblProgress = new Label
            {
                Dock = DockStyle.Right,
                Width = 220,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(60, 80, 120),
                Text = ""
            };

            progressPanel.Controls.Add(_progressBar);
            progressPanel.Controls.Add(_lblProgress);

            // ── Elapsed timer ──────────────────────────────────────────────────
            _elapsedTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _elapsedTimer.Tick += OnElapsedTimerTick;

            // ── Debug log panel (docked to bottom, hidden by default) ──────────
            _debugPanel = new Panel
            {
                Name = "debugPanel",
                Dock = DockStyle.Bottom,
                Height = 180,
                Visible = false,
                BackColor = Color.FromArgb(18, 18, 18),
                Padding = new Padding(0)
            };

            var debugHeader = new Label
            {
                Text = "  🐛 Debug Log",
                Dock = DockStyle.Top,
                Height = 22,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.FromArgb(180, 220, 255),
                Font = new Font("Segoe UI", 8, FontStyle.Bold)
            };

            var btnClearLog = new Button
            {
                Text = "Clear",
                Dock = DockStyle.Top,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 7)
            };
            btnClearLog.FlatAppearance.BorderSize = 0;
            btnClearLog.Click += (s, e) => _rtbDebugLog.Clear();

            _rtbDebugLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.FromArgb(200, 255, 200),
                Font = new Font("Consolas", 8),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            _debugPanel.Controls.Add(_rtbDebugLog);
            _debugPanel.Controls.Add(btnClearLog);
            _debugPanel.Controls.Add(debugHeader);

            // ── Assemble ───────────────────────────────────────────────────────
            this.Controls.Add(_split);
            this.Controls.Add(_debugPanel);    // DockStyle.Bottom
            this.Controls.Add(progressPanel);  // DockStyle.Top stacks above _split
            this.Controls.Add(_toolStrip);
            this.OnCloseTool += OnCloseToolHandler;
            this.Load += OnLoad;

            // Set initial splitter: 30 % left, 70 % right
            // (actual ratio is applied after the control is sized, see OnLoad)
            this.ResumeLayout(false);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────────────────────────────

        private void OnLoad(object sender, EventArgs e)
        {
            // Restore or create settings
            if (!SettingsManager.Instance.TryLoad(GetType(), out _settings))
            {
                _settings = new Settings();
                LogWarning("Settings not found – creating new settings file.");
            }

            // Apply initial splitter ratio after layout
            _split.SplitterDistance = (int)(_split.Width * 0.30);

            // Load entity / field lists
            if (Service == null) return;

            _infoLoader = new InfoLoader(Service);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading entities…",
                Work = (w, ev) =>
                {
                    var entities = _infoLoader.LoadEntities();
                    var envId = FetchEnvironmentId();
                    ev.Result = (entities, envId);
                },
                PostWorkCallBack = ev =>
                {
                    var (entities, envId) = ((List<string>, string))ev.Result;
                    _environmentId = envId;
                    _tscEntity.Items.Clear();
                    _tscEntity.Items.AddRange(entities.Cast<object>().ToArray());
                    if (_tscEntity.Items.Count > 0) _tscEntity.SelectedIndex = 0;
                }
            });
        }

        private void OnCloseToolHandler(object sender, EventArgs e)
        {
            _elapsedTimer.Stop();
            _elapsedTimer.Dispose();
            SettingsManager.Instance.Save(GetType(), _settings);
        }

        public override void UpdateConnection(
            IOrganizationService newService,
            ConnectionDetail detail,
            string actionName,
            object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (_settings != null && detail != null)
                _settings.LastUsedOrganizationWebappUrl = detail.WebApplicationUrl;

            // Reload with new connection
            _infoLoader = new InfoLoader(Service);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_split != null && _split.Width > 10)
                _split.SplitterDistance = (int)(_split.Width * 0.30);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  ToolStrip events
        // ──────────────────────────────────────────────────────────────────────

        private void OnEntitySelectionChanged(object sender, EventArgs e)
        {
            var entity = _tscEntity.Text?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(entity) || _infoLoader == null) return;

            _tscField.Items.Clear();
            var fields = _infoLoader.LoadFields(entity);
            _tscField.Items.AddRange(fields.Cast<object>().ToArray());
            if (_tscField.Items.Count > 0) _tscField.SelectedIndex = 0;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Progress helpers
        // ──────────────────────────────────────────────────────────────────────

        // ──────────────────────────────────────────────────────────────────────
        //  Debug mode
        // ──────────────────────────────────────────────────────────────────────

        private void OnDebugToggled(object sender, EventArgs e)
        {
            _debugMode = _tsbDebug.Checked;
            _debugPanel.Visible = _debugMode;

            _tsbDebug.ForeColor = _debugMode
                ? Color.FromArgb(255, 220, 80)   // amber when active
                : Color.White;

            if (_debugMode)
            {
                DebugLog("── Debug mode enabled ──────────────────────────────");
                DebugLog("Logs will appear here while analysis runs.");
                DebugLog("Colors:  white = entering level  |  cyan = flow found");
                DebugLog("         green = match  |  yellow = recurse  |  gray = skip");
                DebugLog("────────────────────────────────────────────────────");
            }
        }

        /// <summary>
        /// Thread-safe: can be called from the background worker thread.
        /// Appends a timestamped, color-coded line to the debug log.
        /// </summary>
        private void DebugLog(string message, DebugLogLevel level = DebugLogLevel.Info)
        {
            if (!_debugMode) return;

            // Always marshal back to the UI thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, DebugLogLevel>(DebugLog), message, level);
                return;
            }

            Color color;
            string prefix;
            switch (level)
            {
                case DebugLogLevel.Enter:
                    color = Color.White;
                    prefix = "→ ";
                    break;
                case DebugLogLevel.Found:
                    color = Color.FromArgb(100, 220, 255);  // cyan
                    prefix = "  ✓ ";
                    break;
                case DebugLogLevel.Match:
                    color = Color.FromArgb(100, 255, 140);  // green
                    prefix = "  ★ ";
                    break;
                case DebugLogLevel.Recurse:
                    color = Color.FromArgb(255, 220, 80);   // yellow
                    prefix = "  ↳ ";
                    break;
                case DebugLogLevel.Skip:
                    color = Color.FromArgb(130, 130, 130);  // gray
                    prefix = "  ✗ ";
                    break;
                case DebugLogLevel.Warning:
                    color = Color.FromArgb(255, 120, 80);   // orange
                    prefix = "  ⚠ ";
                    break;
                default:
                    color = Color.FromArgb(200, 255, 200);
                    prefix = "    ";
                    break;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{timestamp}] {prefix}{message}\r\n";

            _rtbDebugLog.SelectionStart = _rtbDebugLog.TextLength;
            _rtbDebugLog.SelectionLength = 0;
            _rtbDebugLog.SelectionColor = color;
            _rtbDebugLog.AppendText(line);
            _rtbDebugLog.SelectionColor = _rtbDebugLog.ForeColor;
            _rtbDebugLog.ScrollToCaret();
        }

        private void StartProgress(string entity, string field)
        {
            _analysisStartTime = DateTime.UtcNow;

            // Show the progress panel
            var panel = Controls["progressPanel"] as Panel;
            if (panel != null) panel.Visible = true;

            _lblProgress.Text = $"Analyzing {entity}.{field}  |  0s elapsed";
            _tsbAnalyze.Enabled = false;
            _elapsedTimer.Start();
        }

        private void StopProgress(int flowCount)
        {
            _elapsedTimer.Stop();

            var elapsed = DateTime.UtcNow - _analysisStartTime;
            string timeStr = elapsed.TotalSeconds < 60
                ? $"{elapsed.TotalSeconds:F1}s"
                : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

            _lblProgress.Text = flowCount > 0
                ? $"Done – {flowCount} flow(s) found in {timeStr}"
                : $"Done – no flows found  ({timeStr})";

            // Hide the marquee, keep the label visible as a summary
            _progressBar.Visible = false;
            _tsbAnalyze.Enabled = true;
        }

        private void OnElapsedTimerTick(object sender, EventArgs e)
        {
            var elapsed = DateTime.UtcNow - _analysisStartTime;

            // Build a gentle "still working" message that changes every tick
            string spinner;
            switch ((int)(elapsed.TotalSeconds * 2) % 4)
            {
                case 0: spinner = "⠋"; break;
                case 1: spinner = "⠙"; break;
                case 2: spinner = "⠸"; break;
                default: spinner = "⠴"; break;
            }

            string timeStr = elapsed.TotalSeconds < 60
                ? $"{elapsed.TotalSeconds:F0}s"
                : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

            // Rough estimate: Dataverse LIKE queries typically take 1-3s each.
            // We don't know the total but we can show a helpful note after thresholds.
            string hint;
            if (elapsed.TotalSeconds < 5) hint = "Fetching flows…";
            else if (elapsed.TotalSeconds < 15) hint = "Parsing flow definitions…";
            else if (elapsed.TotalSeconds < 30) hint = "Tracing dependencies…";
            else if (elapsed.TotalSeconds < 60) hint = "Deep dependency chain detected…";
            else hint = "Large environment – still working…";

            _lblProgress.Text = $"{spinner} {hint}  |  {timeStr} elapsed";
        }

        private void OnAnalyzeClicked(object sender, EventArgs e)
        {
            string entity = _tscEntity.Text?.Trim().ToLowerInvariant();
            string field = _tscField.Text?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(field))
            {
                MessageBox.Show("Please select an entity and a field.", "FieldGraphX",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _tree.Nodes.Clear();
            ClearDetailPanel();

            // Reset progress bar visibility for a fresh run
            _progressBar.Visible = true;
            StartProgress(entity, field);

            WorkAsync(new WorkAsyncInfo
            {
                Message = string.Empty,   // suppress the default XTB overlay
                Work = (w, ev) =>
                {
                    // Build a thread-safe debug logger that marshals to the UI thread
                    Action<string, int> debugCallback = _debugMode
                        ? (msg, lvl) => DebugLog(msg, (DebugLogLevel)lvl)
                        : (Action<string, int>)null;

                    var analyzer = new FlowDependencyAnalyzer(Service, _environmentId, debugCallback);
                    ev.Result = analyzer.BuildDependencyTree(entity, field);
                },
                PostWorkCallBack = ev =>
                {
                    if (ev.Error != null)
                    {
                        StopProgress(0);
                        MessageBox.Show($"Analysis failed:\n{ev.Error.Message}", "FieldGraphX",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var nodes = ev.Result as List<FlowNode>;
                    int count = nodes?.Count ?? 0;
                    StopProgress(count);

                    if (count == 0)
                    {
                        MessageBox.Show("No Cloud Flows found for this entity/field combination.",
                                        "FieldGraphX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    PopulateTree(nodes, entity, field);
                }
            });
        }

        // ──────────────────────────────────────────────────────────────────────
        //  TreeView population
        // ──────────────────────────────────────────────────────────────────────

        private void PopulateTree(List<FlowNode> nodes, string entity, string field)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            var rootTn = new TreeNode($"🔍 {entity}.{field}  ({nodes.Count} flow(s))")
            {
                NodeFont = new Font(_tree.Font, FontStyle.Bold),
                ForeColor = ColorHeaderBg
            };

            foreach (var node in nodes)
                rootTn.Nodes.Add(BuildTreeNode(node));

            _tree.Nodes.Add(rootTn);
            rootTn.Expand();
            ExpandFirstLevel(rootTn);

            _tree.EndUpdate();
        }

        private TreeNode BuildTreeNode(FlowNode flow)
        {
            string icon = GetIcon(flow);
            var tn = new TreeNode($"{icon} {flow.FlowName}")
            {
                Tag = flow,
                ForeColor = GetNodeColor(flow)
            };

            if (flow.IsBroadTrigger)
            {
                tn.Text += "  ⚠ Broad Trigger";
                // Recursion already stopped inside the analyzer; children list is empty
            }

            foreach (var child in flow.Children)
                tn.Nodes.Add(BuildTreeNode(child));

            return tn;
        }

        private static string GetIcon(FlowNode flow)
        {
            // Lifecycle status takes visual priority over functional role
            if (flow.Status == Logic.FlowStatus.Inactive) return "⚫";
            if (flow.Status == Logic.FlowStatus.Draft) return "✏";
            if (flow.IsBroadTrigger) return "🔴";
            if (flow.Trigger?.Kind == Logic.TriggerKind.Manual) return "🟣";
            if (flow.Trigger?.Kind == Logic.TriggerKind.Scheduled) return "🕐";
            if (flow.IsSearchedFieldTrigger && flow.IsSearchedFieldUpdated) return "🟠";
            if (flow.IsSearchedFieldUpdated) return "🟢";
            if (flow.IsSearchedFieldTrigger) return "🔵";
            return "⬜";
        }

        private static Color GetNodeColor(FlowNode flow)
        {
            if (flow.Status == Logic.FlowStatus.Inactive) return ColorInactive;
            if (flow.Status == Logic.FlowStatus.Draft) return ColorDraft;
            if (flow.IsBroadTrigger) return ColorBroadTrigger;
            if (flow.Trigger?.Kind == Logic.TriggerKind.Manual) return Color.DarkOrange;
            if (flow.Trigger?.Kind == Logic.TriggerKind.Scheduled) return Color.SlateBlue;
            if (flow.IsSearchedFieldTrigger && flow.IsSearchedFieldUpdated) return ColorBoth;
            if (flow.IsSearchedFieldUpdated) return ColorUpdater;
            if (flow.IsSearchedFieldTrigger) return ColorTrigger;
            return Color.Black;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Custom TreeView drawing (preserves ForeColor per node)
        // ──────────────────────────────────────────────────────────────────────

        private void OnTreeDrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            // Use the node's ForeColor; fall back to the tree's default
            var color = e.Node.ForeColor == Color.Empty ? _tree.ForeColor : e.Node.ForeColor;
            var font = e.Node.NodeFont ?? _tree.Font;

            // Highlight selected node
            if ((e.State & TreeNodeStates.Selected) != 0)
            {
                e.Graphics.FillRectangle(
                    new SolidBrush(Color.FromArgb(220, 230, 245)), e.Bounds);
            }

            if (e.Bounds.Width > 0)
                TextRenderer.DrawText(e.Graphics, e.Node.Text, font, e.Bounds, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Detail panel
        // ──────────────────────────────────────────────────────────────────────

        private void OnTreeNodeSelected(object sender, TreeViewEventArgs e)
        {
            _selectedNode = e.Node?.Tag as FlowNode;
            ShowDetails(_selectedNode);
        }

        private void ShowDetails(FlowNode flow)
        {
            if (flow == null)
            {
                ClearDetailPanel();
                return;
            }

            // Summary strip
            _lblFlowName.Text = flow.FlowName;
            if (flow.Status == Logic.FlowStatus.Inactive)
                _lblFlowName.ForeColor = ColorInactive;
            else if (flow.Status == Logic.FlowStatus.Draft)
                _lblFlowName.ForeColor = ColorDraft;
            else
                _lblFlowName.ForeColor = ColorHeaderBg;

            if (flow.Trigger != null)
            {
                string triggerDesc;
                Color triggerColor;

                switch (flow.Trigger.Kind)
                {
                    case Logic.TriggerKind.CdsRowChange:
                        string fields = flow.Trigger.IsBroadTrigger
                            ? "(all fields – broad trigger)"
                            : flow.Trigger.FilteringAttributes ?? "(unknown)";
                        triggerDesc = $"Dataverse trigger: {flow.Trigger.EntityLogicalName}  ›  {fields}";
                        triggerColor = flow.IsBroadTrigger ? ColorBroadTrigger : ColorTrigger;
                        break;
                    case Logic.TriggerKind.Manual:
                        triggerDesc = $"Manual / Instant trigger  ({flow.Trigger.TriggerKey})";
                        triggerColor = Color.DarkOrange;
                        break;
                    case Logic.TriggerKind.Scheduled:
                        triggerDesc = $"Scheduled / Recurrence trigger  ({flow.Trigger.TriggerKey})";
                        triggerColor = Color.SlateBlue;
                        break;
                    default:
                        triggerDesc = $"Other trigger: {flow.Trigger.TriggerKey}";
                        triggerColor = Color.Gray;
                        break;
                }

                _lblTrigger.Text = triggerDesc;
                _lblTrigger.ForeColor = triggerColor;
            }
            else
            {
                _lblTrigger.Text = "Trigger could not be parsed";
                _lblTrigger.ForeColor = Color.Gray;
            }

            var statusParts = new List<string>();
            // Lifecycle badge first
            if (flow.Status == Logic.FlowStatus.Inactive)
                statusParts.Add("⚫ Inactive (turned off)");
            else if (flow.Status == Logic.FlowStatus.Draft)
                statusParts.Add("✏ Draft (never published)");
            else
                statusParts.Add("● Active");
            if (flow.IsSearchedFieldTrigger) statusParts.Add("Uses field as Trigger");
            if (flow.IsSearchedFieldUpdated) statusParts.Add("Updates the field");
            if (flow.IsBroadTrigger) statusParts.Add("⚠ Broad Trigger – no filtering attributes");
            _lblStatus.Text = string.Join("  |  ", statusParts.Where(s => s.Length > 0));
            if (flow.Status == Logic.FlowStatus.Inactive)
                _lblStatus.ForeColor = ColorInactive;
            else if (flow.Status == Logic.FlowStatus.Draft)
                _lblStatus.ForeColor = ColorDraft;
            else
                _lblStatus.ForeColor = Color.DarkGreen;

            // Rich detail text
            _rtbDetails.Clear();

            AppendLine(_rtbDetails, "FLOW DETAILS", ColorHeaderBg, bold: true);
            AppendLine(_rtbDetails, $"Name:    {flow.FlowName}");
            Color statusColor;
            if (flow.Status == Logic.FlowStatus.Inactive)
                statusColor = ColorInactive;
            else if (flow.Status == Logic.FlowStatus.Draft)
                statusColor = ColorDraft;
            else
                statusColor = ColorUpdater;
            AppendLine(_rtbDetails, $"Status:  {flow.Status}", statusColor);
            AppendLine(_rtbDetails, $"Flow ID: {flow.FlowId}");
            AppendLine(_rtbDetails, $"URL:     {flow.FlowUrl}", Color.MediumBlue);
            _rtbDetails.AppendText("\n");

            AppendLine(_rtbDetails, "TRIGGER", ColorHeaderBg, bold: true);
            if (flow.Trigger != null)
            {
                AppendLine(_rtbDetails, $"  Kind:  {flow.Trigger.Kind}");
                if (flow.Trigger.Kind == Logic.TriggerKind.CdsRowChange)
                {
                    AppendLine(_rtbDetails, $"  Entity:  {flow.Trigger.EntityLogicalName}");
                    if (flow.Trigger.IsBroadTrigger)
                        AppendLine(_rtbDetails, "  Fields:  (broad trigger – all fields)", ColorBroadTrigger);
                    else
                        AppendLine(_rtbDetails, $"  Fields:  {flow.Trigger.FilteringAttributes}");
                }
                else
                {
                    AppendLine(_rtbDetails, $"  Key:  {flow.Trigger.TriggerKey}", Color.Gray);
                }
            }
            else
            {
                AppendLine(_rtbDetails, "  Trigger could not be parsed.", Color.Gray);
            }
            _rtbDetails.AppendText("\n");

            AppendLine(_rtbDetails, "UPDATE ACTIONS", ColorHeaderBg, bold: true);
            if (flow.UpdateActions.Count == 0)
            {
                AppendLine(_rtbDetails, "  No update actions detected.", Color.Gray);
            }
            else
            {
                foreach (var ua in flow.UpdateActions)
                {
                    AppendLine(_rtbDetails,
                        $"  Entity: {ua.EntityLogicalName}  →  Fields: {string.Join(", ", ua.UpdatedFields)}",
                        ColorUpdater);
                }
            }
            _rtbDetails.AppendText("\n");

            if (flow.Children.Count > 0)
            {
                AppendLine(_rtbDetails, $"UPSTREAM DEPENDENCIES  ({flow.Children.Count})", ColorHeaderBg, bold: true);
                foreach (var child in flow.Children)
                    AppendLine(_rtbDetails, $"  ↑ {child.FlowName}", Color.DarkSlateGray);
            }

            // Enable "Open Flow" button only when a valid URL exists
            _btnOpenFlow.Enabled = !string.IsNullOrWhiteSpace(flow.FlowUrl);
        }

        private void ClearDetailPanel()
        {
            _lblFlowName.Text = "(select a flow in the tree)";
            _lblTrigger.Text = "";
            _lblStatus.Text = "";
            _rtbDetails.Clear();
            _btnOpenFlow.Enabled = false;
            _selectedNode = null;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  "Open Flow" button
        // ──────────────────────────────────────────────────────────────────────

        private void OnOpenFlowClicked(object sender, EventArgs e)
        {
            if (_selectedNode == null || string.IsNullOrWhiteSpace(_selectedNode.FlowUrl))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _selectedNode.FlowUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the URL:\n{ex.Message}", "FieldGraphX",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────────

        private string FetchEnvironmentId()
        {
            try
            {
                var response = (RetrieveCurrentOrganizationResponse)
                    Service.Execute(new RetrieveCurrentOrganizationRequest());
                return response.Detail.EnvironmentId.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ExpandFirstLevel(TreeNode node)
        {
            foreach (TreeNode child in node.Nodes)
                child.Expand();
        }

        private static void AppendLine(
            RichTextBox rtb,
            string text,
            Color? color = null,
            bool bold = false)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color ?? Color.Black;
            rtb.SelectionFont = bold
                ? new Font(rtb.Font, FontStyle.Bold)
                : rtb.Font;
            rtb.AppendText(text + "\n");
            rtb.SelectionColor = Color.Black;
        }
    }

    internal enum DebugLogLevel
    {
        Info,
        Enter,
        Found,
        Match,
        Recurse,
        Skip,
        Warning
    }
}