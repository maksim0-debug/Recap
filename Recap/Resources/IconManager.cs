using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class IconManager : IDisposable
    {
        private readonly Dictionary<string, Image> _memoryCache = new Dictionary<string, Image>();
        private readonly HashSet<string> _currentlyLoading = new HashSet<string>();
        private readonly HashSet<string> _failedAttempts = new HashSet<string>();

        private readonly string _iconCachePath;
        private readonly string _thumbCachePath;

        private readonly Image _loadingIcon;
        private readonly Image _errorIcon;
        private readonly Image _allAppsIcon;
        private readonly Image _youTubeIcon;

        private readonly object _lock = new object();
        private bool _isDisposed = false;

        private enum IconType { Exe, Web, YouTubeThumb }

        public event Action<string> IconLoaded;

        public bool DisableVideoPreviews { get; set; }

        private readonly HashSet<string> _browserPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "New Tab",
            "Extensions",
            "Extensions Manager",
            "Extension Page",
            "Settings",
            "History",
            "Downloads"
        };

        public IconManager()
        {
            _iconCachePath = Path.Combine(Application.LocalUserAppDataPath, "IconCache");
            _thumbCachePath = Path.Combine(Application.LocalUserAppDataPath, "ThumbCache");

            Directory.CreateDirectory(_iconCachePath);
            Directory.CreateDirectory(_thumbCachePath);

            _loadingIcon = CreateSolidIcon(Color.FromArgb(80, 80, 80));
            _errorIcon = CreateErrorIcon();     
            _allAppsIcon = CreateAllAppsIcon();
            _youTubeIcon = CreateYouTubeFolderIcon();
        }

        public Image GetIcon(string rawAppName)
        {
            if (rawAppName == Localization.Get("allApps")) return _allAppsIcon;

            var parts = rawAppName.Split('|');
            string exeName = parts[0];
            string lowerExe = exeName.ToLower();

            bool isMessenger = lowerExe.Contains("telegram") ||
                               lowerExe.Contains("ayugram") ||
                               lowerExe.Contains("kotatogram");

            string cacheKey;
            IconType type = IconType.Exe;
            string payload = exeName;         

            if (isMessenger)
            {
                cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                type = IconType.Exe;
            }
            else if (parts.Length >= 3 && parts[1].Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            {
                string detailName = parts[2];

                if (detailName.Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    return _youTubeIcon;
                }

                string videoId = ExtractVideoId(detailName);

                if (!string.IsNullOrEmpty(videoId))
                {
                    cacheKey = "yt_" + videoId;
                    type = IconType.YouTubeThumb;
                    payload = videoId;

                    if (DisableVideoPreviews)
                    {
                        lock (_lock)
                        {
                            if (_memoryCache.TryGetValue(cacheKey, out var icon)) return icon;
                        }

                        string thumbPath = Path.Combine(_thumbCachePath, cacheKey + ".jpg");
                        if (!File.Exists(thumbPath))
                        {
                            return _youTubeIcon;
                        }
                    }
                }
                else
                {
                    cacheKey = "web_" + MakeSafeFilename(detailName).GetHashCode();
                    type = IconType.Web;
                    payload = detailName;
                }
            }
            else if (parts.Length >= 2)
            {
                string groupName = parts[1];

                if (groupName.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    return _youTubeIcon;
                }

                if (_browserPages.Contains(groupName))
                {
                    cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                    type = IconType.Exe;
                    payload = exeName;
                }
                else
                {
                    cacheKey = "web_" + MakeSafeFilename(groupName).ToLower();
                    type = IconType.Web;
                    payload = groupName;
                }
            }
            else
            {
                if (exeName == Localization.Get("legacyApp")) return _errorIcon;
                cacheKey = "exe_" + MakeSafeFilename(exeName).ToLower();
                type = IconType.Exe;
            }

            lock (_lock)
            {
                if (_isDisposed) return _errorIcon;
                if (_memoryCache.TryGetValue(cacheKey, out var icon)) return icon;
                if (_failedAttempts.Contains(cacheKey)) return _errorIcon;
                if (_currentlyLoading.Contains(cacheKey)) return _loadingIcon;

                _currentlyLoading.Add(cacheKey);
            }

            Task.Run(() =>
            {
                try
                {
                    switch (type)
                    {
                        case IconType.YouTubeThumb:
                            LoadYouTubeThumbnail(payload, cacheKey, rawAppName);
                            break;
                        case IconType.Web:
                            LoadWebIcon(payload, cacheKey, rawAppName);
                            break;
                        case IconType.Exe:
                            LoadExeIcon(payload, cacheKey, rawAppName);
                            break;
                    }
                }
                catch
                {
                    FinishLoading(cacheKey, null, rawAppName);
                }
            });

            return _loadingIcon;
        }

        private void LoadYouTubeThumbnail(string videoId, string cacheKey, string originalName)
        {
            string thumbPath = Path.Combine(_thumbCachePath, cacheKey + ".jpg");
            Image loadedImage = null;

            try
            {
                if (File.Exists(thumbPath))
                {
                    using (var temp = new Bitmap(thumbPath)) loadedImage = new Bitmap(temp);
                }
                else
                {
                    string url = $"https://img.youtube.com/vi/{videoId}/default.jpg";

                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", AdvancedSettings.Instance.UserAgent);
                        byte[] data = client.DownloadData(url);
                        using (var ms = new MemoryStream(data))
                        using (var original = new Bitmap(ms))
                        {
                            int w = 32;
                            int h = 24;
                            using (var resized = ResizeImage(original, w, h))
                            {
                                resized.Save(thumbPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                                loadedImage = new Bitmap(resized);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            FinishLoading(cacheKey, loadedImage, originalName);
        }

        private void LoadWebIcon(string domain, string cacheKey, string originalName)
        {
            string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");
            Image loadedIcon = null;

            try
            {
                if (File.Exists(iconPath))
                {
                    using (var temp = new Bitmap(iconPath)) loadedIcon = new Bitmap(temp);
                }
                else
                {
                    string url = $"https://www.google.com/s2/favicons?domain={domain}&sz=32";
                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", AdvancedSettings.Instance.UserAgent);
                        byte[] data = client.DownloadData(url);
                        using (var ms = new MemoryStream(data))
                        using (var bmp = new Bitmap(ms))
                        {
                            bmp.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                            loadedIcon = new Bitmap(bmp);
                        }
                    }
                }
            }
            catch { }

            FinishLoading(cacheKey, loadedIcon, originalName);
        }

        private void LoadExeIcon(string exeName, string cacheKey, string originalName)
        {
            string iconPath = Path.Combine(_iconCachePath, cacheKey + ".png");
            Image loadedIcon = null;

            try
            {
                if (File.Exists(iconPath))
                {
                    using (var temp = new Bitmap(iconPath)) loadedIcon = new Bitmap(temp);
                }
                else
                {
                    string exePath = FindExePath(exeName);
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        Icon extractedIcon = Icon.ExtractAssociatedIcon(exePath);
                        if (extractedIcon != null)
                        {
                            using (var iconBmp = extractedIcon.ToBitmap())
                            {
                                iconBmp.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                            }
                            loadedIcon = new Bitmap(iconPath);
                            extractedIcon.Dispose();
                        }
                    }
                }
            }
            catch { }

            FinishLoading(cacheKey, loadedIcon, originalName);
        }

        private void FinishLoading(string cacheKey, Image icon, string originalName)
        {
            lock (_lock)
            {
                if (_isDisposed) { icon?.Dispose(); return; }

                if (icon != null)
                {
                    _memoryCache[cacheKey] = icon;
                }
                else
                {
                    _memoryCache[cacheKey] = _errorIcon;
                    _failedAttempts.Add(cacheKey);
                }

                _currentlyLoading.Remove(cacheKey);
            }

            IconLoaded?.Invoke(originalName);
        }

        private string ExtractVideoId(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var regex = new Regex(@"(?:v=|\/)([0-9A-Za-z_-]{11})");
            var match = regex.Match(text);
            if (match.Success) return match.Groups[1].Value;
            return null;
        }

        private string MakeSafeFilename(string text)
        {
            if (string.IsNullOrEmpty(text)) return "unknown";
            if (text.Length > 50) return Math.Abs(text.GetHashCode()).ToString();
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
            return text;
        }

        private Bitmap ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);
            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.SmoothingMode = SmoothingMode.HighSpeed;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                using (var wrapMode = new System.Drawing.Imaging.ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }
            return destImage;
        }

        private Bitmap CreateYouTubeFolderIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(255, 0, 0))) g.FillRectangle(brush, 1, 3, 14, 10);
                using (var brush = new SolidBrush(Color.White)) g.FillPolygon(brush, new Point[] { new Point(6, 5), new Point(6, 11), new Point(11, 8) });
            }
            return bmp;
        }

        private Bitmap CreateSolidIcon(Color c)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp)) using (var b = new SolidBrush(c)) g.FillRectangle(b, 0, 0, 16, 16);
            return bmp;
        }

        private Bitmap CreateErrorIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(Color.Gray)) g.FillRectangle(b, 0, 0, 16, 16);

                using (var b = new SolidBrush(Color.White))
                {
                    var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
                    g.DrawString("?", font, b, 1, -1);
                }
            }
            return bmp;
        }

        private Bitmap CreateAllAppsIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(Color.Gray)) g.FillRectangle(b, 0, 0, 16, 16);
                using (var b = new SolidBrush(Color.White)) { g.FillRectangle(b, 2, 2, 5, 5); g.FillRectangle(b, 9, 2, 5, 5); g.FillRectangle(b, 2, 9, 5, 5); g.FillRectangle(b, 9, 9, 5, 5); }
            }
            return bmp;
        }

        #region WinAPI
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        private string FindExePath(string appName)
        {
            string processName = appName.Replace(".exe", "");
            
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var buffer = new StringBuilder(1024);
                        int size = buffer.Capacity;
                        IntPtr hProcess = OpenProcess(0x1000, false, process.Id);
                        if (hProcess != IntPtr.Zero)
                        {
                            if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                            {
                                string path = buffer.ToString();
                                CloseHandle(hProcess);
                                return path;
                            }
                            CloseHandle(hProcess);
                        }
                    }
                    catch { }
                }
            }

            if (appName.Equals("telegram.exe", StringComparison.OrdinalIgnoreCase))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string tPath = Path.Combine(appData, "Telegram Desktop", "Telegram.exe");
                if (File.Exists(tPath)) return tPath;
            }

            return null;
        }
        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                foreach (var img in _memoryCache.Values) img?.Dispose();
                _memoryCache.Clear();
            }
            _loadingIcon?.Dispose();
            _errorIcon?.Dispose();
            _allAppsIcon?.Dispose();
            _youTubeIcon?.Dispose();
        }
    }
}