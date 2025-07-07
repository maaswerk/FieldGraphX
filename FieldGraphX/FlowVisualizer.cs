using FieldGraphX.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Activities.Expressions;
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

            if (TriggerFlows == null || TriggerFlows.Count == 0)
                return;

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var positionedFlows = LayoutFlows(TriggerFlows);

            foreach (var flow in positionedFlows)
            {
                // Skalierte Position und Größe
                int x = (int)(flow.X * 320 * zoom + scrollOffset.X);
                int y = (int)(flow.Y * 180 * zoom + scrollOffset.Y);
                int width = (int)(300 * zoom);
                int height = (int)(150 * zoom);

                var rect = new Rectangle(x, y, width, height);

                // Zeichne Rechteck
                g.FillRectangle(flow.Flow.Parents?.Count > 0 ? Brushes.LightGray : Brushes.LightCyan, rect);
                if (flow.Flow.IsFieldUsedAsTrigger && flow.Flow.IsFieldSet)
                {
                    g.FillRectangle(Brushes.LightYellow, rect);
                }
                g.DrawRectangle(Pens.Black, rect);

                // Zeichne Flow-Titel und Infos
                string name = flow.Flow.FlowName ?? "Unnamed Flow";
                string trigger = flow.Flow.Trigger != null
                    ? $"🎯 Trigger: {flow.Flow.Trigger.Entity}.{flow.Flow.Trigger.Field}"
                    : "❌ No Trigger";

                string parentInfo = BuildSimplifiedParentInfo(flow.Flow);
                var statusParts = new List<string>();
                if (flow.Flow.IsFieldUsedAsTrigger) statusParts.Add("🔵 Uses Field as Trigger");
                if (flow.Flow.IsFieldSet) statusParts.Add($"🟢 Sets Field '{flow.Flow.SetField}'");
                string status = string.Join(" | ", statusParts);

                g.DrawString(name, new Font("Segoe UI Emoji", 9 * zoom), Brushes.Black, new RectangleF(x + 5, y + 5, width - 10, height - 20));
                g.DrawString(trigger, new Font("Segoe UI Emoji", 8 * zoom), Brushes.Black, new RectangleF(x + 5, y + 30, width - 10, height - 20));
                g.DrawString(status, new Font("Segoe UI Emoji", 8 * zoom), Brushes.DarkGreen, new RectangleF(x + 5, y + 45, width - 10, height - 20));
                g.DrawString(parentInfo, new Font("Segoe UI Emoji", 8 * zoom), Brushes.Gray, new RectangleF(x + 5, y + 65, width - 10, height - 20));

                Rectangle rectf = new Rectangle(new Point(x + 105, y + 115), new Size((int)(80 * zoom), (int)(30 * zoom)));

                g.FillRectangle(Brushes.LightSkyBlue, rectf);
                g.DrawRectangle(Pens.Black, rectf);

                // Flow-Name zeichnen
                g.DrawString("Open Flow", new Font("Segoe UI Emoji", 8 * zoom), Brushes.Black, rectf.Location);
                flow.Flow.Button = rectf;

                // Zeichne Verbindungslinien
                foreach (var parent in flow.Flow.Parents)
                {
                    var parentFlow = positionedFlows.FirstOrDefault(p => p.Flow == parent);
                    if (parentFlow != null)
                    {
                        var from = new Point(x + width / 2, y);
                        var to = new Point((int)(parentFlow.X * 320 * zoom + scrollOffset.X + width / 2),
                                           (int)(parentFlow.Y * 180 * zoom + scrollOffset.Y + height));
                        DrawArrowHead(g, from, to);
                        g.DrawLine(Pens.Black, from, to);
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
            var headSize = (int)(12 * zoom); // Skalierung der Pfeilspitze
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
                foreach (var flow in TriggerFlows)
                {
                    if (flow.Button is RectangleF rect && rect.Contains(e.Location))
                    {
                        // Klick-Event für das Rechteck
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
                                MessageBox.Show($"Error opening flow: {ex.Message}");
                            }
                        }
                        else
                        {
                            MessageBox.Show("Flow URL is empty or invalid.");
                        }
                        break;
                    }
                }
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
            zoom *= e.Delta > 0 ? 1.1f : 1 / 1.1f;

            // Begrenzung des Zooms
            zoom = Math.Max(0.2f, Math.Min(5.0f, zoom));

            // Maus-zentriertes Scrollen
            var mouse = e.Location;
            scrollOffset.X = (int)(mouse.X - (mouse.X - scrollOffset.X) * (zoom / oldZoom));
            scrollOffset.Y = (int)(mouse.Y - (mouse.Y - scrollOffset.Y) * (zoom / oldZoom));

            Invalidate(); // Neuzeichnen des Controls
        }

        private void UpdateFlowButtons(List<PositionedFlow> positionedFlows)
        {
            // Entferne alte Buttons, die nicht mehr benötigt werden
            foreach (var btn in flowButtons)
            {
                Controls.Remove(btn);
            }
            flowButtons.Clear();

            // Erstelle oder aktualisiere Buttons basierend auf den Flows
            foreach (var positionedFlow in positionedFlows)
            {
                var flow = positionedFlow.Flow;

                var btnFlow = new Button
                {
                    Text = flow.FlowName ?? "Unnamed Flow",
                    Size = new Size(100, 30),
                    BackColor = Color.LightSkyBlue,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(
                        (int)(positionedFlow.X * 320 * zoom + scrollOffset.X),
                        (int)(positionedFlow.Y * 180 * zoom + scrollOffset.Y)
                    )
                };

                btnFlow.Click += (sender, args) =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(flow.FlowUrl))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = flow.FlowUrl,
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            MessageBox.Show("Flow URL is empty or invalid.");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening flow: {ex.Message}");
                    }
                };

                flowButtons.Add(btnFlow);
                Controls.Add(btnFlow);
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
