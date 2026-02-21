using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class TimelinePreviewManager : IDisposable
    {
        private readonly FrameRepository _frameRepository;
        private readonly IconManager _iconManager;
        private readonly PreviewPopup _previewPopup;

        private int PreviewWidth => AdvancedSettings.Instance.PreviewWidth;
        private int PreviewHeight => AdvancedSettings.Instance.PreviewHeight;

        private MiniFrame? _nextFrameToLoad = null;
        private bool _isWorkerRunning = false;
        private readonly object _workerLock = new object();

        public TimelinePreviewManager(FrameRepository frameRepository, IconManager iconManager)
        {
            _frameRepository = frameRepository;
            _iconManager = iconManager;
            _previewPopup = new PreviewPopup(PreviewWidth, PreviewHeight);
        }

        public void Show(MiniFrame frame, Control trackBar, int mouseX)
        {
            if (_previewPopup.IsDisposed) return;

            var location = CalculatePopupPosition(trackBar, mouseX);
            _previewPopup.Location = location;

            if (!_previewPopup.Visible)
            {
                _previewPopup.ShowInactive();
            }

            lock (_workerLock)
            {
                _nextFrameToLoad = frame;

                if (!_isWorkerRunning)
                {
                    _isWorkerRunning = true;
                    Task.Run(ProcessQueueAsync);
                }
            }
        }

        public void Hide()
        {
            if (!_previewPopup.IsDisposed)
            {
                _previewPopup.Hide();
            }

            lock (_workerLock)
            {
                _nextFrameToLoad = null;
            }
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                MiniFrame currentMiniFrame;

                lock (_workerLock)
                {
                    if (_nextFrameToLoad == null)
                    {
                        _isWorkerRunning = false;
                        return;
                    }

                    currentMiniFrame = _nextFrameToLoad.Value;
                    _nextFrameToLoad = null;
                }

                var currentFrame = _frameRepository.GetFrameIndex(currentMiniFrame.TimestampTicks);
                if (currentFrame.TimestampTicks == 0) continue;   

                Bitmap thumb = null;
                try
                {
                    byte[] imgBytes = _frameRepository.GetFrameData(currentFrame);

                    if (imgBytes != null && imgBytes.Length > 0)
                    {
                        using (var ms = new MemoryStream(imgBytes))
                        using (var fullBmp = (Bitmap)Image.FromStream(ms, false, false))
                        {
                            thumb = new Bitmap(PreviewWidth, PreviewHeight);
                            using (var g = Graphics.FromImage(thumb))
                            {
                                g.CompositingMode = CompositingMode.SourceCopy;
                                g.InterpolationMode = InterpolationMode.Bilinear;
                                g.DrawImage(fullBmp, 0, 0, PreviewWidth, PreviewHeight);
                            }
                        }
                    }
                }
                catch { }

                if (_previewPopup.IsDisposed)
                {
                    thumb?.Dispose();
                    return;
                }

                Image icon = _iconManager.GetIcon(currentFrame.AppName);

                string timeStr = currentFrame.GetTime().ToString("HH:mm:ss");

                string displayName = CleanAppName(currentFrame.AppName);
                
                if (displayName.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    displayName = displayName.Substring(4);

                string text = displayName;        

                try
                {
                    if (_previewPopup.IsHandleCreated)
                    {
                        _previewPopup.Invoke((MethodInvoker)(() =>
                        {
                            if (!_previewPopup.IsDisposed)
                                _previewPopup.UpdateContent(thumb, icon, text, timeStr);
                            else
                                thumb?.Dispose();
                        }));
                    }
                    else
                    {
                        thumb?.Dispose();
                    }
                }
                catch
                {
                    thumb?.Dispose();
                }
            }
        }

        private string CleanAppName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "";

            var parts = rawName.Split('|');
            string displayName = "";

            if (parts.Length >= 3 && parts[1].Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            {
                string title = parts[2];
                int vIndex = title.LastIndexOf(" [v=");
                if (vIndex > 0)
                {
                    title = title.Substring(0, vIndex);
                }
                displayName = title;
            }
            else if (parts.Length >= 2)
            {
                displayName = parts[parts.Length - 1];
            }
            else
            {
                displayName = parts[0].Replace(".exe", "");
            }

            string[] suffixesToRemove = new[] 
            { 
                " - Visual Studio Code", 
                " - Microsoft Visual Studio", 
                " - Visual Studio", 
                " - Antigravity",
                " - Kick", 
                " | Kick",
                " - Google AI Studio",
                "● "
            };

            foreach (var s in suffixesToRemove)
            {
                if (displayName.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                {
                    displayName = displayName.Substring(0, displayName.Length - s.Length);
                }
                else if (s == "● " && displayName.StartsWith(s))      
                {
                     displayName = displayName.Substring(s.Length);
                }
            }

            return displayName.Trim();
        }

        private Point CalculatePopupPosition(Control trackBar, int mouseX)
        {
            Point trackBarScreenPos = trackBar.PointToScreen(Point.Empty);
            int screenX = trackBarScreenPos.X + mouseX - (PreviewWidth / 2);
            int screenY = trackBarScreenPos.Y - _previewPopup.Height - 5;

            var screen = Screen.FromControl(trackBar);

            if (screenX < screen.WorkingArea.Left) screenX = screen.WorkingArea.Left;
            if (screenX + PreviewWidth > screen.WorkingArea.Right) screenX = screen.WorkingArea.Right - PreviewWidth;

            if (screenY < screen.WorkingArea.Top)
            {
                screenY = trackBarScreenPos.Y + trackBar.Height + 5;
            }

            return new Point(screenX, screenY);
        }

        public void Dispose()
        {
            lock (_workerLock) { _nextFrameToLoad = null; }
            if (!_previewPopup.IsDisposed)
            {
                _previewPopup.Dispose();
            }
        }

        private class PreviewPopup : Form
        {
            private Image _previewImage;
            private Image _appIcon;
            private string _text;
            private string _timeStr;
            private readonly int _pWidth;
            private readonly int _pHeight;

            public PreviewPopup(int width, int height)
            {
                _pWidth = width;
                _pHeight = height;
                this.FormBorderStyle = FormBorderStyle.None;
                this.ShowInTaskbar = false;
                this.StartPosition = FormStartPosition.Manual;
                this.Size = new Size(width, height + 25);     
                this.BackColor = Color.FromArgb(40, 40, 40);
                this.DoubleBuffered = true;
                this.TopMost = true;
            }

            protected override bool ShowWithoutActivation => true;

            public void ShowInactive()
            {
                if (this.IsDisposed) return;
                if (!this.Visible) ShowWindow(this.Handle, 4);  
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            public void UpdateContent(Image image, Image icon, string text, string timeStr)
            {
                if (this.IsDisposed) return;

                if (_previewImage != null) _previewImage.Dispose();
                _previewImage = image;
                _appIcon = icon;
                _text = text;
                _timeStr = timeStr;
                this.Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;

                if (_previewImage != null)
                {
                    g.DrawImage(_previewImage, 0, 0, _pWidth, _pHeight);
                }
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(30, 30, 30)))
                        g.FillRectangle(b, 0, 0, _pWidth, _pHeight);
                }

                using (var p = new Pen(Color.FromArgb(80, 80, 80)))
                    g.DrawRectangle(p, 0, 0, _pWidth - 1, _pHeight - 1);

                int textY = _pHeight + 4;
                int leftMargin = 4;

                if (_appIcon != null)
                {
                    int iconW = (_appIcon.Width > _appIcon.Height) ? 22 : 16;
                    int iconH = 16;

                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_appIcon, leftMargin, textY, iconW, iconH);
                    g.InterpolationMode = InterpolationMode.Default;
                    
                    leftMargin += iconW + 4;
                }

                Size timeSize = Size.Empty;
                if (!string.IsNullOrEmpty(_timeStr))
                {
                    timeSize = TextRenderer.MeasureText(g, _timeStr, SystemFonts.DefaultFont);
                }

                if (!string.IsNullOrEmpty(_text))
                {
                    int availableWidth = this.Width - leftMargin - 2;
                    Rectangle textRect = new Rectangle(leftMargin, textY, availableWidth, 20);
                    
                    TextRenderer.DrawText(g, _text, SystemFonts.DefaultFont, textRect, Color.White, 
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                }

                if (!string.IsNullOrEmpty(_timeStr))
                {
                    int timeX = this.Width - timeSize.Width - 4;
                    Rectangle timeRect = new Rectangle(timeX, textY, timeSize.Width, 20);

                    using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    {
                        g.FillRectangle(bgBrush, timeX - 8, textY, timeSize.Width + 8, 20);
                    }

                    TextRenderer.DrawText(g, _timeStr, SystemFonts.DefaultFont, timeRect, Color.LightGray, 
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                using (var p = new Pen(Color.FromArgb(100, 100, 100)))
                    g.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
                else if (_previewImage != null) _previewImage.Dispose();
                base.OnFormClosing(e);
            }
        }
    }
}