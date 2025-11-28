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

        private Point scrollOffset = Point.Empty;
        private Point? lastMouseDown = null;
        public float zoom = 1.0f;
        public List<FlowUsage> AllFlows { get; set; } = new List<FlowUsage>();

        public FlowVisualizer()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.White);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.TranslateTransform(scrollOffset.X, scrollOffset.Y);
            e.Graphics.ScaleTransform(zoom, zoom);



            if (TriggerFlows == null || TriggerFlows.Count == 0)
                return;

            int baseWidth = 300;
            int baseHeight = 150;
            int spacingX = 20;
            int spacingY = 50;

            var positionedFlows = LayoutFlows(TriggerFlows);

            foreach (var flow in positionedFlows)
            {
                int width = (int)(baseWidth * zoom);
                int height = (int)(baseHeight * zoom);

                // Pixelposition basierend auf hierarchischem Layout
                int x = (int)(flow.X * (baseWidth + spacingX) * zoom + scrollOffset.X);
                int y = (int)((-flow.Y) * (baseHeight + spacingY) * zoom + scrollOffset.Y);

                var rect = new Rectangle(x, y, width, height);

                // Hintergrundfarbe
                Brush bgBrush = flow.Flow.Parents?.Count > 0 ? Brushes.LightGray : Brushes.LightCyan;
                if (flow.Flow.IsFieldUsedAsTrigger && flow.Flow.IsFieldSet)
                    bgBrush = Brushes.LightYellow;

                e.Graphics.FillRectangle(bgBrush, rect);
                e.Graphics.DrawRectangle(Pens.Black, rect);

                // Schriftgrößen
                float titleSize = 9f * zoom;
                float infoSize = 8f * zoom;

                // Flow-Titel und Infos proportional innerhalb der Box
                e.Graphics.DrawString(flow.Flow.FlowName ?? "Unnamed Flow", new Font("Segoe UI Emoji", titleSize, FontStyle.Bold), Brushes.Black,
                    new RectangleF(x + width * 0.05f, y + height * 0.05f, width * 0.9f, height * 0.2f));

                string trigger = flow.Flow.Trigger != null
                    ? $"🎯 Trigger: {flow.Flow.Trigger.Entity}.{flow.Flow.Trigger.Field}"
                    : "❌ No Trigger";
                e.Graphics.DrawString(trigger, new Font("Segoe UI Emoji", infoSize), Brushes.Black,
                    new RectangleF(x + width * 0.05f, y + height * 0.3f, width * 0.9f, height * 0.2f));

                var statusParts = new List<string>();
                if (flow.Flow.IsFieldUsedAsTrigger) statusParts.Add("🔵 Uses Field as Trigger");
                if (flow.Flow.IsFieldSet) statusParts.Add($"🟢 Sets Field '{flow.Flow.SetField}'");
                string status = string.Join(" | ", statusParts);

                e.Graphics.DrawString(status, new Font("Segoe UI Emoji", infoSize), Brushes.DarkGreen,
                    new RectangleF(x + width * 0.05f, y + height * 0.55f, width * 0.9f, height * 0.2f));

                string parentInfo = BuildSimplifiedParentInfo(flow.Flow);
                e.Graphics.DrawString(parentInfo, new Font("Segoe UI Emoji", infoSize), Brushes.Gray,
                    new RectangleF(x + width * 0.05f, y + height * 0.75f, width * 0.9f, height * 0.2f));

                // "Open Flow"-Button
                RectangleF buttonRect = new RectangleF(
                    x + width - 90 * zoom,
                    y + height - 35 * zoom,
                    80 * zoom,
                    30 * zoom
                );
                flow.Flow.Button = buttonRect;
                e.Graphics.FillRectangle(Brushes.LightSkyBlue, buttonRect);
                e.Graphics.DrawRectangle(Pens.Black, buttonRect.X, buttonRect.Y, buttonRect.Width, buttonRect.Height);
                e.Graphics.DrawString("Open Flow", new Font("Segoe UI Emoji", infoSize), Brushes.Black,
                    buttonRect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                var centerX = flow.Flow.Button.X + flow.Flow.Button.Width / 2;
                var centerY = flow.Flow.Button.Y + flow.Flow.Button.Height / 2;
                float radius = 5; // Punktgröße
                e.Graphics.FillEllipse(Brushes.Red, centerX - radius, centerY - radius, radius * 2, radius * 2);
            }

            // Verbindungen zwischen Flows
            foreach (var flow in positionedFlows)
            {
                int x = (int)(flow.X * (baseWidth + spacingX) * zoom + scrollOffset.X);
                int y = (int)((-flow.Y) * (baseHeight + spacingY) * zoom + scrollOffset.Y);
                int width = (int)(baseWidth * zoom);
                int height = (int)(baseHeight * zoom);

                foreach (var parent in flow.Flow.Parents)
                {
                    var parentFlow = positionedFlows.FirstOrDefault(p => p.Flow == parent);
                    if (parentFlow != null)
                    {
                        int parentX = (int)(parentFlow.X * (baseWidth + spacingX) * zoom + scrollOffset.X);
                        int parentY = (int)((-parentFlow.Y) * (baseHeight + spacingY) * zoom + scrollOffset.Y);

                        var from = new Point(x + width / 2, y);
                        var to = new Point(parentX + width / 2, parentY + height);

                        e.Graphics.DrawLine(Pens.Black, from, to);
                        DrawArrowHead(e.Graphics, from, to);
                    }
                }
            }
        }


        private void DrawArrowHead(Graphics g, Point from, Point to)
        {
            int headSize = (int)(12 * zoom);
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            var p1 = new Point(
                (int)(to.X - headSize * Math.Cos(angle - Math.PI / 6)),
                (int)(to.Y - headSize * Math.Sin(angle - Math.PI / 6))
            );
            var p2 = new Point(
                (int)(to.X - headSize * Math.Cos(angle + Math.PI / 6)),
                (int)(to.Y - headSize * Math.Sin(angle + Math.PI / 6))
            );
            g.FillPolygon(Brushes.Black, new[] { to, p1, p2 });
        }

        private List<PositionedFlow> LayoutFlows(List<FlowUsage> triggers)
        {
            var positioned = new List<PositionedFlow>();
            if (triggers == null || triggers.Count == 0)
                return positioned;

            int baseWidth = 300;
            int baseHeight = 150;
            int spacingX = 20;
            int spacingY = 50; // mehr Abstand zwischen Ebenen

            // Dictionary, um Flows nach Tiefe zu gruppieren
            var levels = new Dictionary<int, List<FlowUsage>>();
            var visited = new HashSet<Guid>();

            void Traverse(FlowUsage flow, int depth)
            {
                if (visited.Contains(flow.FlowID)) return;
                visited.Add(flow.FlowID);

                if (!levels.ContainsKey(depth))
                    levels[depth] = new List<FlowUsage>();
                levels[depth].Add(flow);

                foreach (var parent in flow.Parents)
                    Traverse(parent, depth - 1);
            }

            // Start bei allen Trigger-Flows
            foreach (var trigger in triggers)
                Traverse(trigger, 0);

            // Positionen berechnen
            foreach (var kv in levels)
            {
                int depth = kv.Key;
                var flowsAtLevel = kv.Value;

                for (int i = 0; i < flowsAtLevel.Count; i++)
                {
                    positioned.Add(new PositionedFlow
                    {
                        Flow = flowsAtLevel[i],
                        X = i,       // X = Index in der Ebene
                        Y = -depth   // Y = Tiefe (negativ, damit Trigger oben ist)
                    });
                }
            }

            return positioned;
        }



        private string BuildSimplifiedParentInfo(FlowUsage flow)
        {
            if (flow.Parents == null || flow.Parents.Count == 0)
                return "🌱 Root Flow (No Parents)";

            var parentNames = flow.Parents.Take(2).Select(p => p.FlowName ?? "Unknown").ToList();
            string result = $"📋 Parents: {string.Join(", ", parentNames)}";
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
                float mouseX = (e.X  / zoom) - scrollOffset.X;
                float mouseY = (e.Y / zoom) - scrollOffset.Y;
                var mouseInDrawCoords = new PointF(mouseX, mouseY);

                foreach (var flow in LayoutFlows(TriggerFlows))
                {
                    if (flow.Flow.Button.IntersectsWith(new RectangleF(mouseInDrawCoords,new SizeF(10,10))))
                    {
                        if (!string.IsNullOrEmpty(flow.Flow.FlowUrl))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = flow.Flow.FlowUrl,
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
                Invalidate();
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
            zoom = Math.Max(0.2f, Math.Min(5.0f, zoom));

            var mouse = e.Location;
            scrollOffset.X = (int)(mouse.X - (mouse.X - scrollOffset.X) * (zoom / oldZoom));
            scrollOffset.Y = (int)(mouse.Y - (mouse.Y - scrollOffset.Y) * (zoom / oldZoom));

            Invalidate();
        }
    }

    public class PositionedFlow
    {
        public FlowUsage Flow { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}