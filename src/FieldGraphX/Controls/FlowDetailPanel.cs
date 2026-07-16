using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldGraphX.Core.Models;
using FieldGraphX.Services;

namespace FieldGraphX.Controls
{
    /// <summary>Shows the details of the selected graph node (flow or field).</summary>
    public sealed class FlowDetailPanel : UserControl
    {
        private readonly RichTextBox _text;
        private readonly LinkLabel _openLink;
        private string _currentUrl;

        public FlowDetailPanel()
        {
            Dock = DockStyle.Fill;

            _openLink = new LinkLabel
            {
                Dock = DockStyle.Bottom,
                Text = "Open in Power Automate",
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                Visible = false
            };
            _openLink.LinkClicked += (s, e) => OpenCurrentUrl();

            _text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                Text = "Select a node to see its details."
            };

            Controls.Add(_text);
            Controls.Add(_openLink);
        }

        public void ShowField(FieldRef field, bool isSeed)
        {
            _currentUrl = null;
            _openLink.Visible = false;

            _text.Clear();
            AppendLine(isSeed ? "SEARCHED FIELD" : "FIELD", Color.DarkSlateBlue, bold: true);
            AppendLine(field.ToString(), bold: true);
            AppendLine("");
            AppendLine($"Entity:    {field.EntityLogicalName}");
            AppendLine($"Attribute: {field.AttributeLogicalName}");
        }

        public void ShowFlow(FlowInfo flow, string environmentId)
        {
            _currentUrl = EnvironmentInfoService.BuildFlowUrl(environmentId, flow.WorkflowIdUnique);
            _openLink.Visible = true;

            _text.Clear();
            AppendLine("FLOW", Color.DarkGreen, bold: true);
            AppendLine(flow.Name, bold: true);
            AppendLine($"Status: {flow.Status}",
                flow.Status == FlowStatus.Active ? Color.DarkGreen : Color.Gray);
            AppendLine("");

            AppendLine("TRIGGER", Color.DarkSlateBlue, bold: true);
            var trigger = flow.Trigger;
            switch (trigger.Kind)
            {
                case TriggerKind.CdsRowChange:
                    AppendLine($"Dataverse: {DescribeChanges(trigger.ChangeTypes)} of '{trigger.EntityLogicalName}'");
                    if (trigger.IsBroadTrigger)
                    {
                        AppendLine("BROAD TRIGGER — no filtering attributes: fires on EVERY change of the entity!",
                            Color.Firebrick, bold: true);
                    }
                    else
                    {
                        AppendLine($"Filtering attributes: {string.Join(", ", trigger.FilteringAttributes)}");
                    }
                    if (!string.IsNullOrEmpty(trigger.FilterExpression))
                        AppendLine($"Row filter (run condition): {trigger.FilterExpression}", Color.DimGray);
                    if (!string.IsNullOrEmpty(trigger.Scope))
                        AppendLine($"Scope: {trigger.Scope}", Color.DimGray);
                    break;
                case TriggerKind.Manual:
                    AppendLine("Manual / instant trigger");
                    break;
                case TriggerKind.Scheduled:
                    AppendLine("Scheduled / recurrence trigger");
                    break;
                case TriggerKind.None:
                    AppendLine("(clientdata missing or not parseable)", Color.Gray);
                    break;
                default:
                    AppendLine($"Other connector trigger ({trigger.TriggerKey})");
                    break;
            }
            AppendLine("");

            AppendLine("WRITE ACTIONS", Color.DarkSlateBlue, bold: true);
            if (flow.WriteActions.Count == 0)
            {
                AppendLine("(none found)", Color.Gray);
            }
            else
            {
                foreach (var action in flow.WriteActions)
                {
                    AppendLine($"{action.ActionName}  [{action.Kind}]  →  {action.EntityLogicalName}", bold: true);
                    AppendLine($"   {string.Join(", ", action.Fields)}");
                }
            }
        }

        public void Clear()
        {
            _currentUrl = null;
            _openLink.Visible = false;
            _text.Clear();
            _text.Text = "Select a node to see its details.";
        }

        private void OpenCurrentUrl()
        {
            if (string.IsNullOrEmpty(_currentUrl)) return;
            try
            {
                Process.Start(_currentUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the browser: {ex.Message}", "FieldGraphX",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string DescribeChanges(ChangeType changes)
        {
            if (changes == ChangeType.None) return "change";
            var parts = new System.Collections.Generic.List<string>();
            if (changes.HasFlag(ChangeType.Create)) parts.Add("create");
            if (changes.HasFlag(ChangeType.Update)) parts.Add("update");
            if (changes.HasFlag(ChangeType.Delete)) parts.Add("delete");
            return string.Join("/", parts);
        }

        private void AppendLine(string text, Color? color = null, bool bold = false)
        {
            _text.SelectionStart = _text.TextLength;
            _text.SelectionLength = 0;
            _text.SelectionColor = color ?? Color.Black;
            _text.SelectionFont = bold ? new Font(_text.Font, FontStyle.Bold) : _text.Font;
            _text.AppendText(text + Environment.NewLine);
            _text.SelectionColor = Color.Black;
        }
    }
}
