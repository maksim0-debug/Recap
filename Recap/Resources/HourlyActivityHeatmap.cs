using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Recap
{
    public class HourlyActivityHeatmap : UserControl
    {
        private Dictionary<int, TimeSpan> _hourlyData;
        private ToolTip _toolTip;
        private int _hoveredHour = -1;

        public Color BaseColor { get; set; } = Color.FromArgb(16, 124, 16);
        public Color LowActivityColor { get; set; } = Color.FromArgb(40, 40, 40);

        public HourlyActivityHeatmap()
        {
            this.DoubleBuffered = true;
            _hourlyData = new Dictionary<int, TimeSpan>();
            _toolTip = new ToolTip();

            this.Paint += OnPaint;
            this.MouseMove += OnMouseMove;
            this.MouseLeave += OnMouseLeave;
        }

        public void SetData(Dictionary<int, TimeSpan> data)
        {
            _hourlyData = data;
            this.Invalidate();
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            if (_hoveredHour != -1)
            {
                _hoveredHour = -1;
                _toolTip.Hide(this);
                this.Invalidate();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            int hour = GetHourAtPoint(e.Location);
            if (hour != _hoveredHour)
            {
                _hoveredHour = hour;
                if (hour != -1 && _hourlyData.ContainsKey(hour))
                {
                    var duration = _hourlyData[hour];
                    string text = Localization.Format("activityTooltip", hour, hour + 1, (int)duration.TotalHours, duration.Minutes);
                    _toolTip.Show(text, this, PointToClient(Cursor.Position).X + 10, PointToClient(Cursor.Position).Y + 10);
                }
                else
                {
                    _toolTip.Hide(this);
                }
                this.Invalidate();
            }
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(this.BackColor);

            int boxSize = GetBoxSize();
            int padding = 4;
            int cols = 6;
            int rows = 4;

            int startX = (this.Width - (cols * (boxSize + padding) - padding)) / 2;
            int startY = (this.Height - (rows * (boxSize + padding) - padding)) / 2;

            double maxMinutes = 60;              
            if (_hourlyData.Count > 0)
            {
                double actualMax = _hourlyData.Values.Max(t => t.TotalMinutes);
                if (actualMax > maxMinutes) maxMinutes = actualMax;
            }

            for (int i = 0; i < 24; i++)
            {
                int col = i % cols;
                int row = i / cols;

                int x = startX + col * (boxSize + padding);
                int y = startY + row * (boxSize + padding);

                Rectangle rect = new Rectangle(x, y, boxSize, boxSize);

                Color color = this.BackColor;
                if (_hourlyData.ContainsKey(i) && maxMinutes > 0)
                {
                    double minutes = _hourlyData[i].TotalMinutes;
                    float intensity = (float)(minutes / maxMinutes);
                    color = InterpolateColor(this.BackColor, BaseColor, intensity);
                }

                using (SolidBrush brush = new SolidBrush(color))
                {
                    e.Graphics.FillRoundedRectangle(brush, rect, 4);
                }

                if (i == _hoveredHour)
                {
                    using (Pen pen = new Pen(this.ForeColor, 2))
                    {
                        e.Graphics.DrawRoundedRectangle(pen, rect, 4);
                    }
                }
                
                string hourLabel = $"{i}";
                TextRenderer.DrawText(e.Graphics, hourLabel, this.Font, rect, this.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private int GetBoxSize()
        {
            int padding = 4;
            int cols = 6;
            int rows = 4;

            int maxW = (this.Width - (cols - 1) * padding) / cols;
            int maxH = (this.Height - (rows - 1) * padding) / rows;

            int size = Math.Min(maxW, maxH);
            return (int)(size * 0.8);
        }

        private int GetHourAtPoint(Point location)
        {
            int boxSize = GetBoxSize();
            int padding = 4;
            int cols = 6;
            int rows = 4;

            int startX = (this.Width - (cols * (boxSize + padding) - padding)) / 2;
            int startY = (this.Height - (rows * (boxSize + padding) - padding)) / 2;

            int adjustedX = location.X - startX;
            int adjustedY = location.Y - startY;

            if (adjustedX < 0 || adjustedY < 0) return -1;

            int col = adjustedX / (boxSize + padding);
            int row = adjustedY / (boxSize + padding);

            if (col >= cols || row >= rows) return -1;

            int hour = row * cols + col;
            if (hour >= 0 && hour < 24) return hour;

            return -1;
        }

        private Color InterpolateColor(Color c1, Color c2, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            int r = (int)(c1.R + (c2.R - c1.R) * t);
            int g = (int)(c1.G + (c2.G - c1.G) * t);
            int b = (int)(c1.B + (c2.B - c1.B) * t);
            return Color.FromArgb(r, g, b);
        }
    }
}
