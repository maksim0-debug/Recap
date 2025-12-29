using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Recap
{
    public class ActivityHeatmap : UserControl
    {
        public event Action<DateTime> DayClicked;

        private Dictionary<DateTime, TimeSpan> _activityData;
        private DateTime _displayMonth;
        private ToolTip _toolTip;
        private int _hoveredDay = -1;

        public Color BaseColor { get; set; } = Color.FromArgb(16, 124, 16);
        public Color LowActivityColor { get; set; } = Color.FromArgb(40, 40, 40);

        public ActivityHeatmap()
        {
            this.DoubleBuffered = true;
            _displayMonth = DateTime.Today;
            _activityData = new Dictionary<DateTime, TimeSpan>();
            _toolTip = new ToolTip();

            this.Paint += OnPaint;
            this.MouseMove += OnMouseMove;
            this.MouseLeave += OnMouseLeave;
            this.MouseClick += OnMouseClick;
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            DateTime? clickedDate = GetDateAtPoint(e.Location);
            if (clickedDate.HasValue)
            {
                DayClicked?.Invoke(clickedDate.Value);
            }
        }

        public void SetDisplayMonth(DateTime month)
        {
            _displayMonth = month;
            this.Invalidate();
        }

        public void SetData(Dictionary<DateTime, TimeSpan> data)
        {
            _activityData = data;
            this.Invalidate();
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            if (_hoveredDay != -1)
            {
                _hoveredDay = -1;
                _toolTip.Hide(this);
                this.Invalidate();
            }
        }

        private DateTime? GetDateAtPoint(Point location)
        {
            int dayBoxSize = GetDayBoxSize();
            if (dayBoxSize <= 0) return null;

            int padding = 4;
            int cols = 7;
            int startX = (this.Width - (cols * (dayBoxSize + padding) - padding)) / 2;
            int startY = (this.Height - (6 * (dayBoxSize + padding) - padding)) / 2;

            int adjustedX = location.X - startX;
            int adjustedY = location.Y - startY;

            if (adjustedX < 0 || adjustedY < 0) return null;

            int col = adjustedX / (dayBoxSize + padding);
            int row = adjustedY / (dayBoxSize + padding);

            if (col >= cols) return null;

            int dayIndex = row * cols + col;

            DateTime firstDayOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            int startDayOfWeek = ((int)firstDayOfMonth.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
            int day = dayIndex - startDayOfWeek + 1;

            if (day > 0 && day <= DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month))
            {
                return new DateTime(_displayMonth.Year, _displayMonth.Month, day);
            }

            return null;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            DateTime? hoveredDate = GetDateAtPoint(e.Location);
            int day = hoveredDate?.Day ?? -1;

            if (_hoveredDay != day)
            {
                _hoveredDay = day;
                if (hoveredDate.HasValue)
                {
                    TimeSpan activity = _activityData.ContainsKey(hoveredDate.Value.Date) ? _activityData[hoveredDate.Value.Date] : TimeSpan.Zero;
                    string tooltipText = $"{hoveredDate.Value.ToLongDateString()}\nАктивность: {activity:hh\\ч\\ mm\\м\\ ss\\с}";
                    _toolTip.Show(tooltipText, this, e.Location.X + 15, e.Location.Y + 15);
                }
                else
                {
                    _toolTip.Hide(this);
                }
                this.Invalidate();
            }
        }

        private int GetDayBoxSize()
        {
            if (this.Width <= 0 || this.Height <= 0) return 0;
            return Math.Min((this.Width - 10) / 7, (this.Height - 10) / 6);
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.BackColor);

            if (_activityData == null) return;

            TimeSpan maxActivity = _activityData.Values.Any() ? _activityData.Values.Max() : TimeSpan.FromHours(1);
            if (maxActivity < TimeSpan.FromHours(1)) maxActivity = TimeSpan.FromHours(1);

            int dayBoxSize = GetDayBoxSize();
            if (dayBoxSize <= 0) return;

            int padding = 4;
            int cols = 7;
            int startX = (this.Width - (cols * (dayBoxSize + padding) - padding)) / 2;
            int startY = (this.Height - (6 * (dayBoxSize + padding) - padding)) / 2;

            DateTime firstDayOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            int startDayOfWeek = ((int)firstDayOfMonth.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;

            for (int day = 1; day <= DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month); day++)
            {
                var currentDate = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
                int dayIndex = startDayOfWeek + day - 1;
                int col = dayIndex % cols;
                int row = dayIndex / cols;

                var rect = new Rectangle(startX + col * (dayBoxSize + padding), startY + row * (dayBoxSize + padding), dayBoxSize, dayBoxSize);

                TimeSpan activity = _activityData.ContainsKey(currentDate.Date) ? _activityData[currentDate.Date] : TimeSpan.Zero;
                double intensity = activity.TotalSeconds / maxActivity.TotalSeconds;
                if (intensity > 1.0) intensity = 1.0;

                Color dayColor = InterpolateColor(this.BackColor, BaseColor, intensity);

                using (var brush = new SolidBrush(dayColor))
                {
                    g.FillRoundedRectangle(brush, rect, 4);
                }

                if (_hoveredDay == day)
                {
                    using (var pen = new Pen(this.ForeColor, 2))
                    {
                        g.DrawRoundedRectangle(pen, rect, 4);
                    }
                }

                TextRenderer.DrawText(g, day.ToString(), this.Font, rect, this.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private Color InterpolateColor(Color color1, Color color2, double fraction)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            int r = (int)(color1.R + (color2.R - color1.R) * fraction);
            int g = (int)(color1.G + (color2.G - color1.G) * fraction);
            int b = (int)(color1.B + (color2.B - color1.B) * fraction);
            return Color.FromArgb(r, g, b);
        }
    }
}