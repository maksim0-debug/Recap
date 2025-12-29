using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Recap
{
    public class DarkListBox : ListBox
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public IconManager IconManager { get; set; }
        public bool ShowFrameCount { get; set; }   

        private ToolTip _toolTip;
        private Timer _tooltipTimer;
        private int _lastTooltipIndex = -1;
        private string _currentTooltipText;

        public DarkListBox()
        {
            this.DrawMode = DrawMode.OwnerDrawFixed;
            this.BorderStyle = BorderStyle.None;
            this.ItemHeight = 24;

            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.UserPaint |
                          ControlStyles.AllPaintingInWmPaint, true);

            _toolTip = new ToolTip();
            _toolTip.OwnerDraw = true;
            _toolTip.Draw += OnToolTipDraw;
            _toolTip.Popup += OnToolTipPopup;
            _toolTip.UseAnimation = true;
            _toolTip.UseFading = true;

            _tooltipTimer = new Timer();
            _tooltipTimer.Interval = AdvancedSettings.Instance.TooltipDelayMs;    
            _tooltipTimer.Tick += OnTooltipTimerTick;
        }

        private void OnTooltipTimerTick(object sender, EventArgs e)
        {
            _tooltipTimer.Stop();
            if (_lastTooltipIndex >= 0 && _lastTooltipIndex < Items.Count)
            {
                var item = Items[_lastTooltipIndex] as FilterItem;
                if (item != null)
                {
                    _currentTooltipText = item.DisplayName;
                    Point cursor = this.PointToClient(Cursor.Position);
                    _toolTip.Show(_currentTooltipText, this, cursor.X + 10, cursor.Y + 20, 5000);
                }
            }
        }

        private void OnToolTipDraw(object sender, DrawToolTipEventArgs e)
        {
            using (var brush = new SolidBrush(Color.FromArgb(40, 40, 40)))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            using (var pen = new Pen(Color.FromArgb(80, 80, 80)))
            {
                e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            Rectangle textRect = e.Bounds;
            textRect.Inflate(-6, -6);  
            
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, this.Font, textRect, Color.White, 
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        private void OnToolTipPopup(object sender, PopupEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentTooltipText)) return;

            Size proposedSize = new Size(400, int.MaxValue);
            Size textSize = TextRenderer.MeasureText(_currentTooltipText, this.Font, proposedSize, TextFormatFlags.WordBreak);
            
            e.ToolTipSize = new Size(textSize.Width + 20, textSize.Height + 14);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = this.IndexFromPoint(e.Location);
            if (index != _lastTooltipIndex)
            {
                _lastTooltipIndex = index;
                _tooltipTimer.Stop();
                _toolTip.Hide(this);

                if (index >= 0 && index < Items.Count)
                {
                    _tooltipTimer.Start();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tooltipTimer.Stop();
            _toolTip.Hide(this);
            _lastTooltipIndex = -1;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!this.DesignMode)
            {
                SetWindowTheme(this.Handle, "explorer", null);
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(this.BackColor))
            {
                e.Graphics.FillRectangle(brush, e.ClipRectangle);
            }

            for (int i = 0; i < Items.Count; i++)
            {
                var rect = GetItemRectangle(i);
                if (e.ClipRectangle.IntersectsWith(rect))
                {
                    DrawListBoxItem(e.Graphics, i, rect);
                }
            }
        }

        private void DrawListBoxItem(Graphics g, int index, Rectangle bounds)
        {
            if (index < 0 || index >= Items.Count) return;

            var item = Items[index] as FilterItem;
            if (item == null) return;

            bool isSelected = (SelectedIndex == index);

            Color backColor = isSelected ? Color.FromArgb(0, 120, 215) : this.BackColor;  
            using (var brush = new SolidBrush(backColor))
            {
                g.FillRectangle(brush, bounds);
            }

            int indent = item.Level * 6;

            Color foreColor = isSelected ? Color.White : this.ForeColor;
            if (item.HasChildren)
            {
                string arrow = item.IsExpanded ? "▼" : "▶";

                int arrowY = bounds.Y + (bounds.Height - 10) / 2;

                using (var brush = new SolidBrush(Color.Gray))
                {
                    g.DrawString(arrow, new Font(this.Font.FontFamily, 7), brush, bounds.X + 1 + (item.Level * 6), arrowY + 1);
                }
            }

            int iconW, iconH;

            if (item.IsVideo || item.Level == 2)
            {
                iconW = 26;
                iconH = 19;
            }
            else if (item.Level == 0)
            {
                iconW = 16;
                iconH = 16;
            }
            else
            {
                iconW = 13;
                iconH = 13;
            }

            int iconX = bounds.X + 12 + indent;
            int iconY = bounds.Y + (bounds.Height - iconH) / 2;

            if (IconManager != null)
            {
                Image icon = IconManager.GetIcon(item.RawName);
                if (icon != null)
                {
                    var oldMode = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    g.DrawImage(icon, iconX, iconY, iconW, iconH);

                    g.InterpolationMode = oldMode;
                }
            }

            string rightText;
            if (ShowFrameCount)
            {
                rightText = $"{item.FrameCount} f";
            }
            else
            {
                rightText = FrameHelper.FormatDuration(item.DurationMs);
            }

            Size rightTextSize = TextRenderer.MeasureText(g, rightText, this.Font);
            int rightSpaceForTime = rightTextSize.Width + 10;   

            int textGap = 4;                 
            int textX = iconX + iconW + textGap;
            int maxTextWidth = bounds.Width - textX - rightSpaceForTime;

            if (maxTextWidth < 10) maxTextWidth = 10;

            var nameRect = new Rectangle(textX, bounds.Y, maxTextWidth, bounds.Height);

            TextRenderer.DrawText(g, item.DisplayName, this.Font, nameRect, foreColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var countRect = new Rectangle(bounds.Right - rightSpaceForTime, bounds.Y, rightSpaceForTime - 5, bounds.Height);

            Color timeColor = isSelected ? Color.White : Color.Gray;

            TextRenderer.DrawText(g, rightText, this.Font, countRect, timeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        protected override void OnDrawItem(DrawItemEventArgs e) { }
    }
}