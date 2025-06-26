using FieldGraphX.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FieldGraphX
{
    public class FlowVisualizer : Control
    {
        public List<FlowUsage> TriggerFlows { get; set; } = new List<FlowUsage>();

        private readonly List<Button> flowButtons = new List<Button>();

        private Point scrollOffset = Point.Empty;
        private Point? lastMouseDown = null;
        private float zoom = 1.0f;

        public FlowVisualizer()
        {
            this.DoubleBuffered = true;
            this.Size = new Size(1494, 469); // fallback Größe
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.White);
            this.Controls.Clear();

            if (TriggerFlows == null || TriggerFlows.Count == 0)
                return;

            var g = e.Graphics;
            var emojiFont = new Font("Segoe UI Emoji", 9 * zoom);
            var positioned = LayoutFlows(TriggerFlows);

            // Zeichne Flows
            foreach (var p in positioned)
            {
                int x = (int)(p.X * 320 * zoom + scrollOffset.X);
                int y = (int)(p.Y * 180 * zoom + scrollOffset.Y);
                int width = (int)(300 * zoom);
                int height = (int)(150 * zoom);
                Rectangle rect = new Rectangle(x, y, width, height);
                g.DrawRectangle(Pens.Black, rect);

                var flow = p.Flow;

                // Flow-Titel & Infos
                string name = flow.FlowName ?? "Unnamed Flow";
                string trigger = flow.Trigger != null
                    ? $"🎯 Trigger: {flow.Trigger.Entity}.{flow.Trigger.Field}"
                    : "❌ No Trigger";

                if(flow.Parents == null || flow.Parents.Count == 0)
                {
                    g.FillRectangle(Brushes.LightCyan, rect);
                }
                else
                {
                    g.FillRectangle(Brushes.LightGray, rect);
                }
                    

                string parentInfo = BuildSimplifiedParentInfo(flow);
                var statusParts = new List<string>();
                if (flow.IsFieldUsedAsTrigger) statusParts.Add("🔵 Uses Field as Trigger");
                if (flow.IsFieldSet) statusParts.Add("🟢 Sets Field");
                string status = string.Join(" | ", statusParts);

                // Text mit automatischem Umbruch
                g.DrawString(name, emojiFont, Brushes.Black, new RectangleF(x + 5, y + 5, 290, 20));
                g.DrawString(trigger, emojiFont, Brushes.DarkSlateGray, new RectangleF(x + 5, y + 25, 290, 20));
                g.DrawString(status, emojiFont, Brushes.DarkGreen, new RectangleF(x + 5, y + 45, 290, 20));
                g.DrawString(parentInfo, emojiFont, Brushes.Gray, new RectangleF(x + 5, y + 65, 290, 30));

                // Button „Open Flow“
                var btnOpen = new Button();
                btnOpen.Text = "Open Flow";
                btnOpen.Size = new Size((int)(80 * zoom), (int)(30 * zoom));
                btnOpen.Font = new Font(Font.FontFamily, 8 * zoom); // optional Font-Scaling
                btnOpen.Location = new Point(x + 105 , y + 115);
                btnOpen.BackColor = Color.LightSkyBlue;
                btnOpen.FlatStyle = FlatStyle.Flat;

                // Sicherstellen, dass Click-Event korrekt arbeitet
                btnOpen.Click += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(flow.FlowUrl))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = flow.FlowUrl,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Could not open flow: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show("No URL available for this flow.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };
                flowButtons.Add(btnOpen);
                this.Controls.Add(btnOpen);
            }

            // Pfeile zeichnen (Parent → Kind)
            foreach (var p in positioned)
            {
                foreach (var parent in p.Flow.Parents)
                {
                    var parentPos = positioned.FirstOrDefault(x => x.Flow == parent);
                    if (parentPos != null)
                    {
                        var from = new Point(parentPos.X * 320 + 150 + scrollOffset.X, parentPos.Y * 180 + 150 + scrollOffset.Y);
                        var to = new Point(p.X * 320 + 150 + scrollOffset.X, p.Y * 180 + scrollOffset.Y);
                        g.DrawLine(Pens.Black, from, to);
                        DrawArrowHead(g, from, to);
                    }
                }
            }
        }


        private void OpenFlowButton_Click(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.Tag is FlowUsage flow)
            {
                if (!string.IsNullOrEmpty(flow.FlowUrl))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = flow.FlowUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not open flow: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("No URL available for this flow.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void DrawArrowHead(Graphics g, Point from, Point to)
        {
            var headSize = 6;
            var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            var p1 = new Point(
                (int)(to.X - headSize * Math.Cos(angle - Math.PI / 6)),
                (int)(to.Y - headSize * Math.Sin(angle - Math.PI / 6)));
            var p2 = new Point(
                (int)(to.X - headSize * Math.Cos(angle + Math.PI / 6)),
                (int)(to.Y - headSize * Math.Sin(angle + Math.PI / 6)));

            g.FillPolygon(Brushes.Black, new[] { to, p1, p2 });
        }

        private List<PositionedFlow> LayoutFlows(List<FlowUsage> triggers)
        {
            var positioned = new List<PositionedFlow>();
            var visited = new HashSet<Guid>();
            int currentX = 0;

            void Recurse(FlowUsage flow, int depth)
            {
                if (visited.Contains(flow.FlowID)) return;
                visited.Add(flow.FlowID);

                positioned.Add(new PositionedFlow
                {
                    Flow = flow,
                    X = currentX++,
                    Y = depth
                });

                foreach (var parent in flow.Parents)
                {
                    Recurse(parent, depth - 1);
                }
            }

            foreach (var trigger in triggers)
            {
                Recurse(trigger, 2); // mittlere Y-Ebene
            }

            return positioned;
        }

        private string BuildSimplifiedParentInfo(FlowUsage flow)
        {
            if (flow.Parents == null || flow.Parents.Count == 0)
                return "🌱 Root Flow (No Parents)";

            var parentNames = flow.Parents.Take(2).Select(p => p.FlowName ?? "Unknown").ToList();
            var result = $"📋 Parents: {string.Join(", ", parentNames)}";

            if (flow.Parents.Count > 2)
                result += $" (+{flow.Parents.Count - 2} more)";

            return result;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                lastMouseDown = e.Location;
                Cursor = Cursors.Hand;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (lastMouseDown.HasValue)
            {
                int dx = e.X - lastMouseDown.Value.X;
                int dy = e.Y - lastMouseDown.Value.Y;
                scrollOffset.X += dx;
                scrollOffset.Y += dy;
                lastMouseDown = e.Location;
                Invalidate(); // neu zeichnen
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                lastMouseDown = null;
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float oldZoom = zoom;
            if (e.Delta > 0)
                zoom *= 1.1f; // Zoom in
            else
                zoom /= 1.1f; // Zoom out

            // Begrenzung (optional)
            zoom = Math.Max(0.2f, Math.Min(zoom, 5.0f));

            // Optional: Maus-zentriertes Scrollen
            var mouse = e.Location;
            scrollOffset.X = (int)(mouse.X - (mouse.X - scrollOffset.X) * (zoom / oldZoom));
            scrollOffset.Y = (int)(mouse.Y - (mouse.Y - scrollOffset.Y) * (zoom / oldZoom));

            UpdateButtonPositions(); // falls Buttons verwendet werden
            Invalidate();
        }

        private void UpdateButtonPositions()
        {
            foreach (var kvp in flowButtons)
            {
                var button = kvp;

                // Rechne Position und Größe mit Zoom
                int x = (int)((button.Location.X * 320 * zoom )+ (scrollOffset.X + 10 * zoom));
                int y = (int)((button.Location.Y * 180 * zoom) + (scrollOffset.Y + 10 * zoom));
                int width = (int)(80 * zoom);
                int height = (int)(30 * zoom);

                button.Location = new Point(x, y);
                button.Size = new Size(width, height);

                // Optional: Schriftgröße skalieren (mit Limit)
                float fontSize = Math.Max(6.0f, 8.0f * zoom);
                button.Font = new Font(Font.FontFamily, fontSize); // neu zeichnen
            }
        }

    }

    public class PositionedFlow
    {
        public FlowUsage Flow { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
